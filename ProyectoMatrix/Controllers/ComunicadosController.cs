using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System.Security.Claims;
using ProyectoMatrix.Seguridad;



namespace ProyectoMatrix.Controllers
{
    [AuditarAccion(Modulo = "COMUNICADOS", Entidad = "Comunicado", OmitirListas = true)]
    public class ComunicadosController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ServicioNotificaciones _notif;
        private readonly BitacoraService _bitacora;
        private readonly IServicioAcceso _acceso;


        public ComunicadosController(ApplicationDbContext db, IWebHostEnvironment env, ServicioNotificaciones notif, BitacoraService bitacora, IServicioAcceso acceso)
        {
            _db = db;
            _env = env;
            _notif = notif;
            _bitacora = bitacora;
            _acceso = acceso;
        }


        [AutorizarAccion("Ver Comunicados|Gestionar Comunicados", "Ver")]
        public async Task<IActionResult> Index(string? categoria = null, string? estado = null)
        {
            var userId = int.Parse(User.FindFirst("UsuarioID").Value);
            var puedeGestionar =
                   await _acceso.TienePermisoAsync(userId, "Comunicados", "Crear")
                || await _acceso.TienePermisoAsync(userId, "Comunicados", "Editar")
                || await _acceso.TienePermisoAsync(userId, "Comunicados", "Eliminar");


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
                .Where (c => c.Activo)
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
                query = query.Where(c => c.Categoria == categoria);

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

                    using (var cmd = new SqlCommand(sqlNoLeidos, conn))
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
        [AutorizarAccion("Ver Comunicados|Gestionar Comunicados", "Ver")]
        public async Task<IActionResult> Lista()
        {


            // userId desde el claim (es lo que usa AutorizarAccion)
            var userIdStr = User.FindFirst("UsuarioID")?.Value;
            if (!int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Login", "Login");

            // Empresa del usuario (sesión o claim como respaldo)
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (!empresaId.HasValue && int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid))
                empresaId = eid;

            // ¿Puede gestionar? (si tiene Crear o Editar o Eliminar sobre "Comunicados")
            var puedeGestionar =
                   await _acceso.TienePermisoAsync(userId, "Comunicados", "Crear")
                || await _acceso.TienePermisoAsync(userId, "Comunicados", "Editar")
                || await _acceso.TienePermisoAsync(userId, "Comunicados", "Eliminar");

            // Query base (solo lectura)
            var q = _db.Comunicados
                .Include(c => c.ComunicadosEmpresas).ThenInclude(ce => ce.Empresa)
                .AsNoTracking()
                .Where (c => c.Activo)
                .AsQueryable();

            // Si NO puede gestionar, filtra por públicos o asignados a su empresa
            if (!puedeGestionar)
            {
                if (empresaId.HasValue)
                    q = q.Where(c => c.EsPublico || c.ComunicadosEmpresas.Any(ce => ce.EmpresaID == empresaId.Value));
                else
                    q = q.Where(c => c.EsPublico);
            }

            // Proyecta primero (EF-friendly) y arma DirigidoA en memoria
            var datos = await q
                .OrderByDescending(c => c.FechaCreacion)
                .ThenByDescending(c => c.ComunicadoID)
                .Select(c => new {
                    c.ComunicadoID,
                    c.NombreComunicado,
                    c.Descripcion,
                    c.FechaCreacion,
                    c.Categoria,
                    c.EsPublico,
                    Empresas = c.ComunicadosEmpresas.Select(ce => ce.Empresa.Nombre).ToList(),
                    c.Imagen
                })
                .ToListAsync();

            var vm = datos.Select(c => new ComunicadoListItemVM
            {
                ComunicadoID = c.ComunicadoID,
                NombreComunicado = c.NombreComunicado,
                Descripcion = c.Descripcion,
                FechaCreacion = c.FechaCreacion,
                Categoria = c.Categoria,
                DirigidoA = c.EsPublico ? "Todos" : string.Join(", ", c.Empresas),
                Imagen = c.Imagen
            }).ToList();

            // (Opcional) para botones en la vista
            ViewBag.PuedeGestionar = puedeGestionar;

            return View(vm);
        }



       
       
        [HttpGet]
        [AutorizarAccion("Gestionar Comunicados", "Crear")]
        public async Task<IActionResult> Crear()
        {
            var vm = new ComunicadoCreateVM
            {
                Empresas = await _db.Empresas.OrderBy(e => e.Nombre).ToListAsync()
            };
            return View(vm);
        }








  
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Gestionar Comunicados", "Crear")]
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






        
        [HttpGet]
        [AutorizarAccion("Gestionar Comunicados", "Crear")]
        public async Task<IActionResult> Gestionar(string? q = null, int page = 1, int pageSize = 15)
        {
            //Si el numero de la pagina es menor que uno lo ponemos en 1 
            //asi nuna hacemos skip con valores negativos
            if (page < 1) page = 1;

            //Si el tamaño de la pagina es muy chico o muy grande lo coloca en 15 por defecto
            //de esta manera la lista de comunicados al momento de gestionar no es muy larga 
            if (pageSize < 5 || pageSize > 100) pageSize = 15;


            var query = _db.Comunicados
                .Include(c => c.ComunicadosEmpresas).ThenInclude(ce => ce.Empresa)
                .Where (c => c.Activo)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => c.NombreComunicado.Contains(q));

            var total = await query.CountAsync();




            var pageItems = await query
      .OrderByDescending(c => c.FechaCreacion)
      .ThenByDescending(c => c.ComunicadoID)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
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

            // 🔹 Lectores por comunicado (para estas filas) con ADO.NET, una sola query IN (...)
            var ids = pageItems.Select(x => x.ComunicadoID).ToArray();
            var lectores = new Dictionary<int, int>();
            if (ids.Length > 0)
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_db.Database.GetConnectionString());
                await conn.OpenAsync();
                // construimos un IN @p0,@p1,...
                var pars = string.Join(",", ids.Select((_, i) => $"@p{i}"));
                var sql = $@"
            SELECT cl.ComunicadoID, COUNT(DISTINCT cl.UsuarioID) AS Lectores
            FROM dbo.ComunicadoLecturas cl
            WHERE cl.ComunicadoID IN ({pars})
            GROUP BY cl.ComunicadoID;";
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
                for (int i = 0; i < ids.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", ids[i]);
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    lectores[rd.GetInt32(0)] = rd.GetInt32(1);
            }

            // adjunta lectores al VM (si tu VM no lo tiene, agrega una prop int Lectores)
            foreach (var item in pageItems)
                item.Lectores = lectores.TryGetValue(item.ComunicadoID, out var n) ? n : 0;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Query = q;


            return View(pageItems);
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Gestionar Comunicados", "Eliminar")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var comunicado = await _db.Comunicados
                .FirstOrDefaultAsync(c => c.ComunicadoID == id);

            if (comunicado == null) return NotFound();
            if (!comunicado.Activo)
            {
                TempData["Warn"] = "El comunicado ya estaba eliminado.";
                return RedirectToAction(nameof(Gestionar));
            }


            //Agregadno soft delete para los comunicados

            var usuarioId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

            comunicado.Activo = false;
            

            await _db.SaveChangesAsync();


            //SERVICO DE LOGS

            try
            {

                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();


                await _bitacora.RegistrarAsync(
                    idUsuario: usuarioId,
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




     
        [HttpGet]
        [AutorizarAccion("Gestionar Comunicados", "Editar")]
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


        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Gestionar Comunicados", "Editar")]
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

        //Agregare un controlador para ver estadisticas para la vista gestor 

        [HttpGet]
        public async Task<IActionResult> Estadisticas(int id)
        {
            using var conn = new SqlConnection(_db.Database.GetConnectionString());
            await conn.OpenAsync();

            // Lectores únicos
            int lectores;
            using (var cmd = new SqlCommand(@"
        SELECT COUNT(DISTINCT UsuarioID)
        FROM dbo.ComunicadoLecturas
        WHERE ComunicadoID = @id;", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                lectores = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            // Destinatarios: público vs empresas 
            int destinatarios;
            using (var cmd = new SqlCommand(@"
        SELECT CASE 
            WHEN c.EsPublico = 1 THEN
                -- si usas Activo, cuenta activos; si no, quita el WHERE
                (SELECT COUNT(*) FROM dbo.Usuarios WHERE Activo = 1)
            ELSE
                (SELECT COUNT(DISTINCT ue.UsuarioID)
                 FROM dbo.ComunicadosEmpresas ce
                 JOIN dbo.UsuariosEmpresas ue ON ue.EmpresaID = ce.EmpresaID
                 WHERE ce.ComunicadoID = c.ComunicadoID)
        END AS Destinatarios
        FROM dbo.Comunicados c
        WHERE c.ComunicadoID = @id;", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                destinatarios = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }

            // Si la tabla usuarios no maneja activo delvera 0
            if (destinatarios == 0)
            {
                // solo aplica si el comunicado es público y no se maneja activo
                using var cmd = new SqlCommand(@"
            SELECT CASE WHEN EsPublico = 1 
                        THEN (SELECT COUNT(*) FROM dbo.Usuarios)
                        ELSE 0 END
            FROM dbo.Comunicados WHERE ComunicadoID = @id;", conn);
                cmd.Parameters.AddWithValue("@id", id);
                destinatarios = Math.Max(destinatarios, Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0));
            }

            var sinDestinatarios = destinatarios == 0;
            var porcentaje = sinDestinatarios ? 0.0 : (lectores * 100.0 / destinatarios);

            return Json(new { lectores, destinatarios, porcentaje, sinDestinatarios });
        }



    }
}