using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.Seguridad;

namespace ProyectoMatrix.Controllers
{
    [AuditarAccion(Modulo = "LIDER", Entidad = "Webinars", OmitirListas = true)]
    public class LiderController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ServicioNotificaciones _notif;
        private readonly BitacoraService _bitacoraService;
        private readonly IServicioAcceso _acceso;

        public LiderController(ApplicationDbContext db, IWebHostEnvironment env, ServicioNotificaciones notif, BitacoraService bitacoraService, IServicioAcceso acceso)
        {
            _db = db;
            _env = env;
            _notif = notif;
            _bitacoraService = bitacoraService;
            _acceso = acceso;
        }


        private int? GetUsuarioId()
            => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

        private int? GetEmpresaId()
            => int.TryParse(User.FindFirst("EmpresaID")?.Value, out var id) ? id : (int?)null;

        // --- Helpers de validación de URLs (predefinimos las url que se pueden aceptar) ---
        private static bool EsRegistroUrl(string? url)
            => !string.IsNullOrWhiteSpace(url)
               && url.Contains("events.teams.microsoft.com", StringComparison.OrdinalIgnoreCase);

        private static bool EsJoinUrl(string? url)
            => !string.IsNullOrWhiteSpace(url)
               && url.Contains("/l/meetup-join/", StringComparison.OrdinalIgnoreCase);

        private static bool EsGrabacionUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var u = url.ToLowerInvariant();
            return u.Contains(".sharepoint.com") || u.Contains("1drv.ms")
                || u.Contains("youtube.com") || u.Contains("youtu.be")
                || u.Contains("vimeo.com") || u.Contains("player.vimeo.com");
        }




        private List<SelectListItem> GetEmpresasSelect()
            => _db.Empresas
                 .Select(e => new SelectListItem { Value = e.EmpresaID.ToString(), Text = e.Nombre })
                 .ToList();

        private async Task<string?> GuardarImagenAsync(IFormFile? archivo)
        {
            if (archivo == null || archivo.Length == 0) return null;

            var carpeta = Path.Combine(_env.WebRootPath, "uploads", "webinars");
            Directory.CreateDirectory(carpeta);

            var nombre = $"{Guid.NewGuid()}{Path.GetExtension(archivo.FileName)}";
            var rutaFisica = Path.Combine(carpeta, nombre);

            using (var fs = new FileStream(rutaFisica, FileMode.Create))
                await archivo.CopyToAsync(fs);

            // ruta relativa para BD
            return $"/uploads/webinars/{nombre}";
        }

        private void EliminarImagenFisica(string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa)) return;
            var ruta = Path.Combine(_env.WebRootPath, rutaRelativa.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(ruta))
                System.IO.File.Delete(ruta);
        }

        private async Task<string> GetDirigidoAAsync(int webinarId, bool esPublico)
        {
            if (esPublico) return "Todas";
            var nombres = await _db.WebinarsEmpresas
                .Where(we => we.WebinarID == webinarId)
                .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => e.Nombre)
                .OrderBy(n => n)
                .ToListAsync();
            return nombres.Count == 0 ? "-" : string.Join(", ", nombres);
        }



        [AutorizarAccion ("Videos de Líderes", "Ver")]
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [AutorizarAccion("Videos de Líderes", "Ver")]
        [HttpGet]
        [AllowAnonymous] // opcional
        public async Task<IActionResult> Lista()
        {
            var empresaId = GetEmpresaId();
            var ahora = DateTime.Now;

            var baseQ = _db.Webinars.AsNoTracking().Where(w =>
                w.EsPublico ||
                (empresaId.HasValue &&
                 _db.WebinarsEmpresas.Any(we => we.WebinarID == w.WebinarID && we.EmpresaID == empresaId.Value))
            );
            // --- DESTACADO: próximo o en curso ---
            var proximo = await baseQ
                .Where(w => w.FechaFin >= ahora)   // aún no ha terminado
                .OrderBy(w => w.FechaInicio)       // el que sigue más pronto o el que ya está en curso primero
                .FirstOrDefaultAsync();

            if (proximo != null)
            {
                var destacadoVm = new WebinarListItemVm
                {
                    WebinarID = proximo.WebinarID,
                    Titulo = proximo.Titulo,
                    Descripcion = proximo.Descripcion,
                    FechaInicio = proximo.FechaInicio,
                    FechaFin = proximo.FechaFin,
                    UrlTeams = proximo.UrlTeams,      
                    UrlGrabacion = proximo.UrlGrabacion, 
                    UrlRegistro = proximo.UrlRegistro,   
                    Imagen = proximo.Imagen,
                    DirigidoA = proximo.EsPublico ? "Todas" : await GetDirigidoAAsync(proximo.WebinarID, proximo.EsPublico)
                };
                ViewBag.Destacado = destacadoVm;
            }
            else
            {
                ViewBag.Destacado = null;
            }


            // --- VIDEO DESTACADO: solo si NO hay webinar destacado ---
            if (ViewBag.Destacado == null)
            {
                ViewBag.FeaturedVideo = new FeaturedVideoVm
                {
                    Titulo = "Visión Estratégica para CEOs",
                    Descripcion = "Cómo desarrollar una visión a largo plazo y comunicarla efectivamente.",
                    VideoIdYoutube = "WRgDEFqrYl0",
                    Thumbnail = "", // opcional
                    Categoria = "Liderazgo Estratégico"
                };
            }
            else
            {
                ViewBag.FeaturedVideo = null;
            }

            // --- Lista ---
            var lista = await baseQ
                .OrderByDescending(w => w.FechaInicio)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID = w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    UrlGrabacion = w.UrlGrabacion,
                    UrlRegistro = w.UrlRegistro,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico ? "Todas" : ""
                })
                .ToListAsync();

            var idsNoPublicos = lista.Where(x => x.DirigidoA == "").Select(x => x.WebinarID).ToList();
            if (idsNoPublicos.Count > 0)
            {
                var empresasPorWebinar = await _db.WebinarsEmpresas
                    .Where(we => idsNoPublicos.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new { we.WebinarID, e.Nombre })
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g => g.Key, g => string.Join(", ", g.Select(x => x.Nombre).OrderBy(n => n)));

                foreach (var item in lista.Where(x => x.DirigidoA == ""))
                    item.DirigidoA = empresasPorWebinar.TryGetValue(item.WebinarID, out var nombres) ? nombres : "-";
            }

            return View("~/Views/Lider/LiderLista.cshtml", lista);
        }



        [AutorizarAccion("Videos de Líderes", "Ver")]
        [HttpGet]
        public async Task<IActionResult> MisWebinars()
        {
            var userId = GetUsuarioId();
            if (!userId.HasValue) return Forbid();

            var lista = await _db.Webinars
                .AsNoTracking()
                .Where(w => w.UsuarioCreadorID == userId.Value)
                .OrderByDescending(w => w.FechaCreacion)
                .ThenByDescending(w => w.WebinarID)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID = w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    UrlRegistro = w.UrlRegistro,
                    UrlGrabacion = w.UrlGrabacion,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico ? "Todas" : ""
                })
                .ToListAsync();
            var ids = lista.Where(x => x.DirigidoA == "").Select(x => x.WebinarID).ToList();
            if (ids.Count > 0)
            {
                var map = await _db.WebinarsEmpresas
                    .Where(we => ids.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new { we.WebinarID, e.Nombre })
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g => g.Key, g => string.Join(", ", g.Select(x => x.Nombre).OrderBy(n => n)));
                foreach (var x in lista.Where(x => x.DirigidoA == ""))
                    x.DirigidoA = map.TryGetValue(x.WebinarID, out var noms) ? noms : "-";
            }

            return View("~/Views/Lider/LiderLista.cshtml", lista); // si prefieres, crea otra vista
        }


        [HttpGet]
        [AutorizarAccion("Videos de Líderes", "Ver")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Webinars
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id);
            if (w == null) return NotFound();

            // Si NO es público, aplicamos alcance por permisos/empresa
            if (!w.EsPublico)
            {
                // 1) ¿Tiene permisos de gestión? (cualquiera de Crear/Editar/Eliminar)
                var userIdStr = User.FindFirst("UsuarioID")?.Value;
                if (!int.TryParse(userIdStr, out var userId)) return Challenge(); // a Login si no autenticado

                var puedeGestionar =
                       await _acceso.TienePermisoAsync(userId, "Videos de Líderes", "Crear")
                    || await _acceso.TienePermisoAsync(userId, "Videos de Líderes", "Editar")
                    || await _acceso.TienePermisoAsync(userId, "Videos de Líderes", "Eliminar");

                // 2) Si NO puede gestionar, entonces debe estar asignado a su empresa
                if (!puedeGestionar)
                {
                    int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
                    if (!empresaId.HasValue && int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid))
                        empresaId = eid;

                    if (!empresaId.HasValue) return Forbid();

                    var asignado = await _db.WebinarsEmpresas
                        .AsNoTracking()
                        .AnyAsync(we => we.WebinarID == w.WebinarID && we.EmpresaID == empresaId.Value);

                    if (!asignado) return Forbid();
                }
            }

            return View(w);
        }

        
        [HttpGet]
        [AutorizarAccion("Videos de Líderes", "Crear")]
        public IActionResult CrearWebinar()
        {
            ViewBag.Empresas = GetEmpresasSelect();
            return View("~/Views/Lider/CrearWebinar.cshtml");
        }



        [AutorizarAccion("Videos de Líderes", "Crear")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearWebinar(Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile)
        {


            // Validación de coherencia de fechas
            if (model.FechaFin <= model.FechaInicio)
                ModelState.AddModelError(nameof(model.FechaFin), "La fecha de fin debe ser mayor que la fecha de inicio.");

            // Validación de URLs (solo si vienen con valor)
            // UrlRegistro (opcional, pero si viene debe ser válida)
            if (!string.IsNullOrWhiteSpace(model.UrlRegistro) && !EsRegistroUrl(model.UrlRegistro))
                ModelState.AddModelError(nameof(model.UrlRegistro), "La URL de registro debe ser de events.teams.microsoft.com.");

            // UrlTeams (REQUERIDA y con formato válido)
            if (string.IsNullOrWhiteSpace(model.UrlTeams))
            {
                ModelState.AddModelError(nameof(model.UrlTeams), "Debes proporcionar el enlace para unirse a Teams.");
            }
            else if (!EsJoinUrl(model.UrlTeams))
            {
                ModelState.AddModelError(nameof(model.UrlTeams), "La URL de unirse debe ser de teams.microsoft.com/l/meetup-join/...");
            }

            // UrlGrabacion (opcional, pero si viene debe ser válida)
            if (!string.IsNullOrWhiteSpace(model.UrlGrabacion) && !EsGrabacionUrl(model.UrlGrabacion))
                ModelState.AddModelError(nameof(model.UrlGrabacion), "La URL de grabación debe ser de SharePoint/OneDrive/YouTube/Vimeo.");


            model.UrlGrabacion = null;

            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
                return View("~/Views/Lider/CrearWebinar.cshtml", model);
            }


            //Codigo para guardar una imagen si se subió
            var rutaRel = await GuardarImagenAsync(imagenFile);
            if (!string.IsNullOrWhiteSpace(rutaRel))
                model.Imagen = rutaRel;

            model.UsuarioCreadorID = GetUsuarioId();

            _db.Webinars.Add(model);
            await _db.SaveChangesAsync();

            // Relaciones empresa ↔ webinar (si no es público)
            if (!model.EsPublico && (empresasSeleccionadas?.Length > 0))
            {
                var relaciones = empresasSeleccionadas
                    .Distinct()
                    .Select(eid => new WebinarEmpresa
                    {
                        WebinarID = model.WebinarID,
                        EmpresaID = eid
                    });
                _db.WebinarsEmpresas.AddRange(relaciones);
                await _db.SaveChangesAsync();
            }

            var tipoNotif = "WebinarAsignado"; // unifica con tu JS

            if (model.EsPublico)
            {
                await _notif.EmitirGlobal(
                    tipoNotif,
                    model.Titulo,
                    $"Webinar programado: {model.Titulo} - {model.FechaInicio:dd/MM/yyyy HH:mm}",
                    model.WebinarID,
                    "Webinars"
                );
            }
            else if (empresasSeleccionadas?.Any() == true) // <- null-safe
            {
                await _notif.EmitirParaEmpresas(
                    tipoNotif,
                    model.Titulo,
                    $"Webinar programado: {model.Titulo} - {model.FechaInicio:dd/MM/yyyy HH:mm}",
                    model.WebinarID,
                    "Webinars",
                    empresasSeleccionadas.Distinct()
                );
            }

            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario =  HttpContext.Items["AgenteUsuario"]?.ToString();

                int? idUsuario = GetUsuarioId();
                int? idEmpresa = GetEmpresaId();

                await _bitacoraService.RegistrarAsync(

                       idUsuario: idUsuario,
                idEmpresa: idEmpresa,   
                accion: "WEBINAR_CREAR",
                mensaje: model.EsPublico  
                 ? "Webinar público creado"
                 : $"Webinar privado creado para {(empresasSeleccionadas?.Distinct().Count() ?? 0)} empresas",
             modulo: "LIDER",
    entidad: "Webinar",
    entidadId: model.WebinarID.ToString(),
    resultado: "OK",
    severidad: 4,               // sugerencia: 4 = auditoría
    solicitudId: solicitudId,
    ip: direccionIp,
    AgenteUsuario: agenteUsuario

                    );
            }
            catch
            {

            }



            return RedirectToAction(nameof(MisWebinars));
        }


        [AutorizarAccion("Videos de Líderes", "Editar")]
        [HttpGet]
        public async Task<IActionResult> Editar(int? id, string? returnUrl)
        {
            if (id == null) return NotFound();
            var w = await _db.Webinars.FindAsync(id.Value);
            if (w == null) return NotFound();

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }

            var seleccionadas = await _db.WebinarsEmpresas
                .Where(we => we.WebinarID == w.WebinarID)
                .Select(we => we.EmpresaID)
                .ToListAsync();


            var empresas = await _db.Empresas
                .OrderBy(e => e.Nombre)
               .Select(e => new { e.EmpresaID, e.Nombre })
               .ToListAsync();

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                ? Url.Action("GestionarWebinar", "Lider")
                : returnUrl;

            ViewBag.Empresas = await _db.Empresas
    .OrderBy(e => e.Nombre)
    .Select(e => new SelectListItem { Value = e.EmpresaID.ToString(), Text = e.Nombre })
    .ToListAsync();

            ViewBag.EmpresasSeleccionadas = seleccionadas; // List<int>



            return View("~/Views/Lider/EditarWebinar.cshtml", w);
        }






        [AutorizarAccion("Videos de Líderes", "Editar")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(
    int id, Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile, string? returnUrl)
        {
            if (id != model.WebinarID) return NotFound();

            // Validaciones...
            if (model.FechaFin < model.FechaInicio)
         ModelState.AddModelError("FechaFin", "La fecha de fin no puede ser menor que la fecha de inicio.");
            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
         ViewBag.ReturnUrl = returnUrl ?? Url.Action("GestionarWebinar", "Lider");
                return View("~/Views/Lider/EditarWebinar.cshtml", model);
            }

            if (!string.IsNullOrWhiteSpace(model.UrlRegistro) && !EsRegistroUrl(model.UrlRegistro))
                ModelState.AddModelError(nameof(model.UrlRegistro), "La URL de registro debe ser de events.teams.microsoft.com.");

            if (!string.IsNullOrWhiteSpace(model.UrlTeams) && !EsJoinUrl(model.UrlTeams))
                ModelState.AddModelError(nameof(model.UrlTeams), "La URL de unirse debe ser de teams.microsoft.com/l/meetup-join/...");

            if (!string.IsNullOrWhiteSpace(model.UrlGrabacion) && !EsGrabacionUrl(model.UrlGrabacion))
                ModelState.AddModelError(nameof(model.UrlGrabacion), "La URL de grabación debe ser de SharePoint/OneDrive/YouTube/Vimeo.");




            //  Recupera el original para conservar campos
            var original = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id);
            if (original == null) return NotFound();

            // Preserva estos campos
            model.UsuarioCreadorID = original.UsuarioCreadorID;
            model.FechaCreacion = original.FechaCreacion;

            // Imagen
            var nuevaRuta = await GuardarImagenAsync(imagenFile);
            model.Imagen = !string.IsNullOrWhiteSpace(nuevaRuta) ? nuevaRuta : original.Imagen;

            

            
            _db.Update(model);
            _db.Entry(model).Property(x => x.UsuarioCreadorID).IsModified = false;
            _db.Entry(model).Property(x => x.FechaCreacion).IsModified = false;
            await _db.SaveChangesAsync();

            // Reasignar empresas 
            var actuales = _db.WebinarsEmpresas.Where(we => we.WebinarID == id);
            _db.WebinarsEmpresas.RemoveRange(actuales);
            if (!model.EsPublico && empresasSeleccionadas is { Length: > 0 })
            {
                var relaciones = empresasSeleccionadas.Select(eid => new WebinarEmpresa
                {
                    WebinarID = id,
                    EmpresaID = eid
                });
                await _db.WebinarsEmpresas.AddRangeAsync(relaciones);
            }
            await _db.SaveChangesAsync();


            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                int? idUsuario = GetUsuarioId();
                int? idEmpresa = GetEmpresaId();

                await _bitacoraService.RegistrarAsync(

                       idUsuario: idUsuario,
                idEmpresa: idEmpresa,
                accion: "WEBINAR_EDITADO",
                mensaje: model.EsPublico
                 ? "Webinar público editado"
                 : $"Webinar privado editado para {(empresasSeleccionadas?.Distinct().Count() ?? 0)} empresas",
             modulo: "LIDER",
    entidad: "Webinar",
    entidadId: model.WebinarID.ToString(),
    resultado: "OK",
    severidad: 4,               // sugerencia: 4 = auditoría
    solicitudId: solicitudId,
    ip: direccionIp,
    AgenteUsuario: agenteUsuario

                    );
            }
            catch
            {

            }

            return Redirect(returnUrl ?? Url.Action("GestionarWebinar", "Lider")!);
        }

        [HttpGet]
        public async Task<IActionResult> Entrar()
        {
            var usuarioId = GetUsuarioId();
            if (!usuarioId.HasValue) return Forbid();

            const string modulo = "Videos de Líderes";
            var puedeVer = await _acceso.TienePermisoAsync(usuarioId.Value, modulo, "Ver");
            var puedeCrear = await _acceso.TienePermisoAsync(usuarioId.Value, modulo, "Crear");
            var puedeEditar = await _acceso.TienePermisoAsync(usuarioId.Value, modulo, "Editar");
            var puedeEliminar = await _acceso.TienePermisoAsync(usuarioId.Value, modulo, "Eliminar");

            var esGestor = puedeCrear || puedeEditar || puedeEliminar;

            if (esGestor) return RedirectToAction(nameof(Index));
            if (puedeVer) return RedirectToAction(nameof(LiderLista));

            return Forbid();
        }


        [AutorizarAccion("Videos de Líderes", "Crear")]
        [HttpGet]
        public async Task<IActionResult> GestionarWebinar()
        {
             var userId = GetUsuarioId();
            if (!userId.HasValue) return Forbid();

           var lista = await _db.Webinars
                .AsNoTracking()
                .Where(w => w.UsuarioCreadorID== userId.Value )
                .OrderByDescending(w => w.FechaCreacion)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID= w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico? "Todas" : ""
                })
                .ToListAsync();
            var  idsNoPublicos = lista.Where(x => x.DirigidoA == "").Select(  x => x.WebinarID).ToList();
            if (idsNoPublicos.Count > 0)
            {
                var empresasPorWebinar = await _db.WebinarsEmpresas
                    .Where(we => idsNoPublicos.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new {we.WebinarID, e.Nombre})
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g  => g .Key, g => string.Join(",", g.Select(x => x.Nombre).OrderBy(n  => n)));
                foreach (var item in lista.Where(x => x.DirigidoA == ""))
                    item.DirigidoA = empresasPorWebinar.TryGetValue(item.WebinarID, out var nombres) ? nombres : "-";
            }
            return View("~/Views/Lider/GestionarWebinar.cshtml",lista);
        }

        [AutorizarAccion("Videos de Líderes", "Ver")]
        [HttpGet]
        public async Task<IActionResult> LiderLista()
        {
            // arma el mismo ViewModel que tu vista espera
            var lista = await _db.Webinars
                .AsNoTracking()
                .OrderByDescending(w => w.FechaInicio)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID = w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    UrlGrabacion = w.UrlGrabacion,
                    UrlRegistro = w.UrlRegistro,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico ? "Todas" : ""
                })
                .ToListAsync();

            // si usas destacado/featured:
            ViewBag.Destacado = await _db.Webinars
                .AsNoTracking()
                .OrderByDescending(w => w.FechaInicio)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID = w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    UrlGrabacion = w.UrlGrabacion,
                    UrlRegistro = w.UrlRegistro,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico ? "Todas" : ""
                })
                .FirstOrDefaultAsync();

            // ViewBag.FeaturedVideo si lo ocupas
            // ViewBag.FeaturedVideo = ...

            return View("~/Views/Lider/LiderLista.cshtml", lista);
        }


        [AutorizarAccion("Videos de Líderes", "Eliminar")]
        [HttpGet]
        public async Task<IActionResult> Eliminar(int? id, string? returnUrl)
        {
            if (id == null) return NotFound();

            var w = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id.Value);
            if (w == null) return NotFound();

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                ? Url.Action("GestionarWebinar","Lider")
                : returnUrl;

            return View("~/Views/Lider/GestionarWebinar.cshtml",w); // Views/Webinars/Delete.cshtml (o tu ruta)
        }

        [AutorizarAccion("Videos de Líderes", "Eliminar")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id, string? returnUrl)
        {
            var w = await _db.Webinars.FindAsync(id);
            if (w == null) 
                return Redirect( returnUrl ?? Url.Action("GestionarWebinar","Lider")!);

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }


            w.Activo = false;
            await _db.SaveChangesAsync();



            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                int? idUsuario = GetUsuarioId();
                int? idEmpresa = GetEmpresaId();

                await _bitacoraService.RegistrarAsync(

                       idUsuario: idUsuario,
                idEmpresa: idEmpresa,
                accion: "WEBINAR_ELIMINADO",
                mensaje: w.EsPublico
                 ? "Webinar eliminado"
                 : $"Webinar privado eliminado para {w.Titulo} empresas",
             modulo: "LIDER",
    entidad: "Webinar",
    entidadId: w.WebinarID.ToString(),
    resultado: "OK",
    severidad: 4,               // sugerencia: 4 = auditoría
    solicitudId: solicitudId,
    ip: direccionIp,
    AgenteUsuario: agenteUsuario

                    );
            }
            catch
            {

            }



            return Redirect (returnUrl ?? Url.Action("GestionarWebinar", "Lider")!);
        }
    }

   

    public class WebinarGestionVm
    {
        public Webinar Webinar { get; set; } = default!;
        public string DirigidoA { get; set; } = "-";
    }







}