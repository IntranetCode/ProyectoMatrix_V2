using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using Microsoft.AspNetCore.Mvc.Filters;



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

        public async Task<IActionResult> Index()
        {
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
                    DirigidoA = c.EsPublico
                        ? "Todos"
                        : string.Join(", ", c.ComunicadosEmpresas.Select(ce => ce.Empresa.Nombre)),
                    Imagen = c.Imagen
                })
                .ToListAsync();

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
            else idUsuario = HttpContext.Session.GetInt32("UsuarioID"); // último recurso


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



        [RequestSizeLimit(104_857_600)]
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

        [RequestSizeLimit(104_857_600)]
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
    }
}