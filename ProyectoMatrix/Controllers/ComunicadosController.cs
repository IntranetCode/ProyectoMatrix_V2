using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System.Linq;
using System.Security.Claims;




namespace ProyectoMatrix.Controllers
{
    [AuditarAccion(Modulo = "COMUNICADOS", Entidad = "Comunicado", OmitirListas = true)]
    public class ComunicadosController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ServicioNotificaciones _notif;
        private readonly BitacoraService _bitacora;


        public ComunicadosController(ApplicationDbContext db, IWebHostEnvironment env, ServicioNotificaciones notif, BitacoraService bitacora)
        {
            _db = db;
            _env = env;
            _notif = notif;
            _bitacora = bitacora;
        }

        public async Task<IActionResult> Index( string? categoria = null, string? estado = null )
        {
            var puedeGestionar = EsGestor(User);

            // Ids desde claims (ajusta si usas otros)
            int? idUsuario = null;
            if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid)) idUsuario = uid;

            else if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid)) idUsuario = uid;
            else idUsuario = HttpContext.Session.GetInt32("UsuarioID"); 

            int? empresaId = null;
            if (!puedeGestionar && int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid))
                empresaId = eid;

            var query = _db.Comunicados
                .Include(c => c.ComunicadosEmpresas).ThenInclude(ce => ce.Empresa)
                .AsQueryable();
           

            //Este apartado es para el alcance por empresa/p{ublico

            if (!puedeGestionar)
            {
                if (empresaId.HasValue)
                    query = query.Where(c => c.EsPublico || c.ComunicadosEmpresas.Any(ce => ce.EmpresaID == empresaId.Value));
                else
                    query = query.Where(c => c.EsPublico);
            }


            //Se ha agregado el filtro por categoria
            if (!string.IsNullOrWhiteSpace(categoria))
              query   = query.Where(c => c.Categoria == categoria);

            //Se agregara un nuevo filtro (manejo de estado) para filtrar comunicados pendientes y leidos 

            if (!string.IsNullOrWhiteSpace(estado) && idUsuario.HasValue)
            {
                using var conn = new SqlConnection(_db.Database.GetConnectionString());
                await conn.OpenAsync();

                var ids = new List<int>();

                if (estado == "NoLeidos")
                {
                    // IDs pendientes (visibles al usuario) = NOT IN lecturas
                    using var cmd = new SqlCommand(@"
            SELECT c.ComunicadoID
            FROM dbo.Comunicados c
            LEFT JOIN dbo.ComunicadosEmpresas ce ON ce.ComunicadoID = c.ComunicadoID
            LEFT JOIN dbo.UsuariosEmpresas ue ON ue.EmpresaID = ce.EmpresaID AND ue.UsuarioID = @UsuarioID
            WHERE (c.EsPublico = 1 OR ue.UsuarioID IS NOT NULL)
            AND NOT EXISTS (
                SELECT 1 FROM dbo.ComunicadoLecturas cl 
                WHERE cl.ComunicadoID = c.ComunicadoID AND cl.UsuarioID = @UsuarioID
            )
        ", conn);
                    cmd.Parameters.AddWithValue("@UsuarioID", idUsuario.Value);
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
                }
                else if (estado == "Leidos")
                {
                    using var cmd = new SqlCommand(@"
            SELECT cl.ComunicadoID
            FROM dbo.ComunicadoLecturas cl
            WHERE cl.UsuarioID = @UsuarioID
        ", conn);
                    cmd.Parameters.AddWithValue("@UsuarioID", idUsuario.Value);
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
                }

                // filtra la query principal por los IDs obtenidos
                query = query.Where(c => ids.Contains(c.ComunicadoID));
            }

            ViewBag.Estado = estado; // para que el JS sepa si está en modo "Pendientes"


            bool esHome = string.IsNullOrEmpty(estado) && string.IsNullOrEmpty(categoria);
            ViewBag.EsHome = esHome;

            IQueryable<Comunicado> qOrdered = query
                .OrderByDescending(c => c.FechaCreacion)
                .ThenByDescending(c => c.ComunicadoID);

            if (esHome)
                qOrdered = qOrdered.Take(3);

            var vm = await qOrdered
    .Select(c => new ComunicadoListItemVM
    {
        ComunicadoID = c.ComunicadoID,
        NombreComunicado = c.NombreComunicado,
        Descripcion = c.Descripcion,
        FechaCreacion = c.FechaCreacion,
        Categoria = c.Categoria,
        DirigidoA = c.EsPublico
            ? "Todos"
            : string.Join(", ", c.ComunicadosEmpresas.Select(ce => ce.Empresa.Nombre)),
        Imagen = c.Imagen
    })
    .ToListAsync();


            //AGREGANDO bloque para obtener contadores

            if (idUsuario.HasValue)
            {
                using (var conn = new SqlConnection(_db.Database.GetConnectionString()))
                {
                    await conn.OpenAsync();

                    //Recuperar kis leídos
                    var sqlLeidos = "SELECT COUNT (*) FROM ComunicadoLecturas WHERE UsuarioID=@UsuarioID";
                    using (var cmd = new SqlCommand(sqlLeidos, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioID", idUsuario.Value);
                        ViewBag.Leidos = (int)await cmd.ExecuteScalarAsync();
                    }

                    //Recuperar comunicados no leidos 
                    var sqlNoLeidos = @"
                     SELECT COUNT (*)
                     FROM Comunicados c
                      WHERE (c.EsPublico = 1 OR EXISTS (
                       SELECT 1 FROM ComunicadosEmpresas ce 
                       JOIN UsuariosEmpresas ue ON ce.EmpresaID = ue.EmpresaID
                       WHERE ce.ComunicadoID = c.ComunicadoID AND ue.UsuarioID = @UsuarioID
                      ))
                      AND NOT EXISTS (
                      SELECT 1 FROM ComunicadoLecturas cl
                     WHERE cl.ComunicadoID=c.ComunicadoID AND cl.UsuarioID= @UsuarioID
                         );";

                    using (var cmd = new SqlCommand (sqlNoLeidos, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioID", idUsuario.Value);
                        ViewBag.NoLeidos = (int)await cmd.ExecuteScalarAsync();
                    }
                }
            }
            else
            {
                ViewBag.Leidos = 0;
                ViewBag.NoLeidos = 0;
            }


            return View(vm);
        }



        [HttpGet]
        public async Task<IActionResult> Lista()
        {
            // Usa la MISMA lógica de la policy para saber si puede gestionar
            var puedeGestionar = EsGestor(User);

            int? empresaId = null;
            if (!puedeGestionar && int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid))
                empresaId = eid;

            var query = _db.Comunicados
                .Include(c => c.ComunicadosEmpresas).ThenInclude(ce => ce.Empresa)
                .AsQueryable();

            if (!puedeGestionar)
            {
                if (empresaId.HasValue)
                    query = query.Where(c => c.EsPublico || c.ComunicadosEmpresas.Any(ce => ce.EmpresaID == empresaId.Value));
                else
                    query = query.Where(c => c.EsPublico);
            }

            var vm = await query
                .OrderByDescending(c => c.FechaCreacion)
                .ThenByDescending(c => c.ComunicadoID)
                .Select(c => new ComunicadoListItemVM
                {
                    ComunicadoID = c.ComunicadoID,
                    NombreComunicado = c.NombreComunicado,
                    Descripcion = c.Descripcion,
                    FechaCreacion = c.FechaCreacion,
                    Categoria = c.Categoria,
                    DirigidoA = c.EsPublico
                        ? "Todos"
                        : string.Join(", ", c.ComunicadosEmpresas.Select(ce => ce.Empresa.Nombre)),
                    Imagen = c.Imagen
                })
                .ToListAsync();

            return View(vm);
        }




        [Authorize(Policy = "GestionComunicados")]
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new ComunicadoCreateVM
            {
                Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync()
            };
            return View(vm);
        }








        [Authorize(Policy = "GestionComunicados")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(ComunicadoCreateVM vm, [FromServices] BitacoraService bitacora)
        {
            // Ids desde claims (ajusta si usas otros)
            int? idUsuario = null;
            if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid)) idUsuario = uid;

            else if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid)) idUsuario = uid;
            else idUsuario = HttpContext.Session.GetInt32("UsuarioID"); 


            int? idEmpresaContexto = int.TryParse(User.FindFirstValue("EmpresaID"), out var eid)
            ? eid : (int?)null;

            var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
            var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
            var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();


            if (!ModelState.IsValid)
            {
                vm.Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync();
                return View(vm);
            }

            try
            {
                var comunicado = new Comunicado
                {
                    NombreComunicado = vm.NombreComunicado,
                    Descripcion = vm.Descripcion,
                    FechaCreacion = vm.FechaCreacion,
                    Categoria = vm.Categoria,
                    EsPublico = vm.EsPublico,
                    UsuarioCreadorID = idUsuario
                };

                // Media opcional
                if (vm.ImagenFile != null && vm.ImagenFile.Length > 0)
                {
                    var ext = Path.GetExtension(vm.ImagenFile.FileName).ToLowerInvariant();
                    var permitidos = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".pdf" };
                    if (!permitidos.Contains(ext))
                    {
                        ModelState.AddModelError("MediaFile", "Formato no soportado");
                        vm.Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync();
                        return View(vm);
                    }

                    var uploads = Path.Combine(_env.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploads);
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var path = Path.Combine(uploads, fileName);
                    using var fs = new FileStream(path, FileMode.Create);
                    await vm.ImagenFile.CopyToAsync(fs);
                    comunicado.Imagen = $"/uploads/{fileName}";
                }

                // Empresas destino
                if (!vm.EsPublico && vm.EmpresasSeleccionadas.Any())
                {
                    foreach (var empId in vm.EmpresasSeleccionadas.Distinct())
                        comunicado.ComunicadosEmpresas.Add(new ComunicadoEmpresa { EmpresaID = empId });
                }

                // Guardado
                _db.Comunicados.Add(comunicado);
                await _db.SaveChangesAsync();


                if (vm.EsPublico)
                {
                    await _notif.EmitirGlobal(
                        "Comunicado_Nuevo",
                        comunicado.NombreComunicado,
                        comunicado.Descripcion,
                        comunicado.ComunicadoID,
                        "Comunicados");
                }
                else if (vm.EmpresasSeleccionadas.Any())
                {
                    await _notif.EmitirParaEmpresas(
                        "Comunicado_Nuevo",
                        comunicado.NombreComunicado,
                        comunicado.Descripcion,
                        comunicado.ComunicadoID,
                        "Comunicados",
                        vm.EmpresasSeleccionadas);
                }

                await bitacora.RegistrarAsync(
                    idUsuario: idUsuario,
                    idEmpresa: idEmpresaContexto,   // o null si no aplica
                    accion: "COMUNICADO_CREAR",
                    mensaje: vm.EsPublico
                        ? "Comunicado público creado"
                        : $"Comunicado privado creado para {vm.EmpresasSeleccionadas.Distinct().Count()} empresas",
                 modulo: "COMUNICADOS",
        entidad: "Comunicado",
        entidadId: comunicado.ComunicadoID.ToString(),
        resultado: "OK",
        severidad: 4,               // sugerencia: 4 = auditoría
        solicitudId: solicitudId,
        ip: direccionIp,
        AgenteUsuario: agenteUsuario
                );

                return RedirectToAction(nameof(Gestionar));
            }
            catch (Exception ex)
            {
                // 📝 Bitácora: error
                await bitacora.RegistrarAsync(
         idUsuario: idUsuario,
         idEmpresa: idEmpresaContexto,
         accion: "CREAR",
         mensaje: ex.Message,
         modulo: "COMUNICADOS",
         entidad: "Comunicado",
         entidadId: null,            // si falló antes de tener ID
         resultado: "ERROR",
         severidad: 3,               // 3 = error
         solicitudId: solicitudId,
         ip: direccionIp,
         AgenteUsuario: agenteUsuario
     );

                // UI
                ModelState.AddModelError(string.Empty, "No se pudo crear el comunicado.");
                vm.Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync();
                return View(vm);
            }
        }






        [Authorize(Policy = "GestionComunicados")]
        [HttpGet]
        public async Task<IActionResult> Gestionar(string? q = null)
        {
            var query = _db.Comunicados
                .Include(c => c.ComunicadosEmpresas).ThenInclude(ce => ce.Empresa)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => c.NombreComunicado.Contains(q));

            var lista = await query
                .OrderByDescending(c => c.FechaCreacion)
                .ThenByDescending(c => c.ComunicadoID)
                .Select(c => new ComunicadoListItemVM
                {
                    ComunicadoID = c.ComunicadoID,
                    NombreComunicado = c.NombreComunicado,
                    Descripcion = c.Descripcion,
                    FechaCreacion = c.FechaCreacion,
                    Categoria = c.Categoria,
                    DirigidoA = c.EsPublico ? "Todos" : string.Join(", ", c.ComunicadosEmpresas.Select(ce => ce.Empresa.Nombre)),
                    Imagen = c.Imagen
                })
                .ToListAsync();

            return View(lista);
        }

        [Authorize(Policy = "GestionComunicados")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            var comunicado = await _db.Comunicados
                .Include(c => c.ComunicadosEmpresas)
                .FirstOrDefaultAsync(c => c.ComunicadoID == id);

            if (comunicado == null) return NotFound();

            DeleteOldImage(comunicado.Imagen);

            if (comunicado.ComunicadosEmpresas.Any())
                _db.ComunicadoEmpresas.RemoveRange(comunicado.ComunicadosEmpresas); // Asegúrate que tu DbSet se llama así

            _db.Comunicados.Remove(comunicado);
            await _db.SaveChangesAsync();

            //SERVICO DE LOGS

            try
            {

                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();


                await _bitacora.RegistrarAsync(
                    idUsuario: int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null,
                    idEmpresa: int.TryParse(User.FindFirstValue("EmpresaID"), out var eid) ? eid : (int?)null,
                    accion: "ELIMINAR",
                    mensaje: $"Comunicado eliminado: {comunicado.NombreComunicado}",
                    modulo: "COMUNICADOS",
                    entidad: "Comunicado",
                    entidadId: id.ToString(),
                    resultado: "OK",
                    severidad: 4,
                    solicitudId: solicitudId,
        ip: direccionIp,
        AgenteUsuario: agenteUsuario
                );
            }
            catch { /*  */ }


            return RedirectToAction(nameof(Gestionar));
        }



     
        [Authorize(Policy = "GestionComunicados")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var comunicado = await _db.Comunicados
                .Include(c => c.ComunicadosEmpresas)
                .FirstOrDefaultAsync(c => c.ComunicadoID == id);

            if (comunicado == null) return NotFound();

            var vm = new ComunicadoCreateVM
            {
                ComunicadoID = comunicado.ComunicadoID,
                NombreComunicado = comunicado.NombreComunicado,
                Descripcion = comunicado.Descripcion,
                FechaCreacion = comunicado.FechaCreacion,
                Categoria = comunicado.Categoria,
                EsPublico = comunicado.EsPublico,
                EmpresasSeleccionadas = comunicado.ComunicadosEmpresas.Select(ce => ce.EmpresaID).ToArray(),
                Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync()
            };

            ViewBag.ComunicadoID = id;
            ViewBag.ImagenActual = comunicado.Imagen;
            return View(vm);
        }


        [Authorize(Policy = "GestionComunicados")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, ComunicadoCreateVM vm)
        {
            var comunicado = await _db.Comunicados
                .Include(c => c.ComunicadosEmpresas)
                .FirstOrDefaultAsync(c => c.ComunicadoID == id);



            if (comunicado == null) return NotFound();

            if (!ModelState.IsValid)
            {
                vm.Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync();
                ViewBag.ComunicadoID = id;
                ViewBag.ImagenActual = comunicado.Imagen;
                return View(vm);
            }

            comunicado.NombreComunicado = vm.NombreComunicado;
            comunicado.Descripcion = vm.Descripcion;
            comunicado.FechaCreacion = vm.FechaCreacion;
            comunicado.Categoria = vm.Categoria;
            comunicado.EsPublico = vm.EsPublico;

            if (vm.ImagenFile != null && vm.ImagenFile.Length > 0)
            {
                var ext = Path.GetExtension(vm.ImagenFile.FileName).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".pdf" };
                if (!allowed.Contains(ext))
                {
                    ModelState.AddModelError("MediaFile", "Formato no soportado");
                    vm.Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync();
                    return View(vm);

                }
                var uploads = Path.Combine(_env.WebRootPath, "uploads");
                Directory.CreateDirectory(uploads);
                var fileName = $"{Guid.NewGuid()}{ext}";
                var path = Path.Combine(uploads, fileName);
                using var fs = new FileStream(path, FileMode.Create);
                await vm.ImagenFile.CopyToAsync(fs);


                comunicado.Imagen = $"/uploads/{fileName}";
            }

            comunicado.ComunicadosEmpresas.Clear();
            if (!vm.EsPublico && vm.EmpresasSeleccionadas.Any())
            {
                foreach (var empId in vm.EmpresasSeleccionadas.Distinct())
                    comunicado.ComunicadosEmpresas.Add(new ComunicadoEmpresa { EmpresaID = empId });
            }

            await _db.SaveChangesAsync();

            try
            {

                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                await _bitacora.RegistrarAsync(
                    idUsuario: int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null,
                    idEmpresa: int.TryParse(User.FindFirstValue("IDEmpresa"), out var eid) ? eid : (int?)null,
                    accion: "EDITAR",
                    mensaje: $"Comunicado editado: {comunicado.NombreComunicado}",
                    modulo: "COMUNICADOS",
                    entidad: "Comunicado",
                    entidadId: id.ToString(),
                    resultado: "OK",
                    severidad: 4,
                     solicitudId: solicitudId,
         ip: direccionIp,
         AgenteUsuario: agenteUsuario
                );
            }
            catch { }




            return RedirectToAction(nameof(Gestionar));
        }


        
        private static bool EsGestor(ClaimsPrincipal user)
        {
            var nombreRol = user.FindFirst(ClaimTypes.Role)?.Value;
            if (nombreRol == "Administrador de Intranet" ||
                nombreRol == "Propietario de Contenido" ||
                nombreRol == "Autor/Editor de Contenido")
                return true;

            var rolId = user.FindFirst("RolID")?.Value;
            return rolId == "1" || rolId == "3" || rolId == "4";
        }

  
        private void DeleteOldImage(string? imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath)) return;
                var relative = imagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(_env.WebRootPath, relative);
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch { /* log opcional */ }
        }

        //SE ESTA AÑADIENDO UN CONTROLADOR PARA MARCAR LEIDO UN COMUNICADO,
        //HACIENDO CONSULRAS CON SQL

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> MarcarLeido(int id)
        {
            int? usuarioId = null;
            if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid)) usuarioId = uid;
            else if (int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out uid)) usuarioId = uid;
            else usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            if (!usuarioId.HasValue)
                return Unauthorized();

            using (var conn = new SqlConnection(_db.Database.GetConnectionString()))
            {
                await conn.OpenAsync();
                var sql = @"
            MERGE dbo.ComunicadoLecturas AS t
            USING (SELECT @ComunicadoID AS ComunicadoID, @UsuarioID AS UsuarioID) AS s
            ON (t.ComunicadoID = s.ComunicadoID AND t.UsuarioID = s.UsuarioID)
            WHEN NOT MATCHED THEN
              INSERT (ComunicadoID, UsuarioID) VALUES (s.ComunicadoID, s.UsuarioID);";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ComunicadoID", id);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return Json(new { ok = true });
        }
 

    }
}