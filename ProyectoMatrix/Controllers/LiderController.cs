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



        // metodo que decide que vista mostrarle al usuario segun sus permisos
        [HttpGet]
        public async Task<IActionResult> Entrada()
        {
            var userIdStr = User.FindFirst("UsuarioID")?.Value;
            if (!int.TryParse(userIdStr, out var userId)) return RedirectToAction("Login", "Login");

            // ¿Tiene alguna acción de gestor?
            var esGestor =
                await _acceso.TienePermisoAsync(userId, "Crear webinar", "Crear") ||
                await _acceso.TienePermisoAsync(userId, "Editar webinar", "Editar") ||
                await _acceso.TienePermisoAsync(userId, "Eliminar Proyectos", "Eliminar");

            // Seguridad real: el controlador de Index/Lista debe tener su propia autorización.
            return esGestor
                ? RedirectToAction("Index", "Lider")
                : RedirectToAction("Lista", "Lider");
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


        //Metodo que extrae el SRC de un iframer

        private static string ExtractIframeSrcOrReturn(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input?.Trim();

            var trimmed = input.Trim();

            // Si ya parece URL absoluta, se deculve tal cual
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
                return trimmed;

            // src="..." cuando esta entre comillas dobles se estrae
            var m = System.Text.RegularExpressions.Regex.Match(
                trimmed, "src\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            // src='...' cuando esta en comillas simples se extrae
            m = System.Text.RegularExpressions.Regex.Match(
                trimmed, "src\\s*=\\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            return trimmed;
        }


        private static bool IsAllowedLiveEmbedHost(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var h = uri.Host.ToLowerInvariant();
            // Town hall / convene y dominios Microsoft 365
            return h.EndsWith(".microsoft.com") || h.EndsWith(".office.com") || h.EndsWith(".office365.com");
        }

        [AutorizarAccion ("Ver Webinars", "Ver")]
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [AutorizarAccion("Ver Webinars", "Ver")]
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
                    UrlEnVivoEmbed = w.UrlEnVivoEmbed,
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



        [AutorizarAccion("Ver Webinars", "Ver")]
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
                    UrlEnVivoEmbed = w.UrlEnVivoEmbed,
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
        [AutorizarAccion("Ver Webinars", "Ver")]
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
        [AutorizarAccion("Crear webinar", "Crear")]
        public IActionResult CrearWebinar()
        {
            ViewBag.Empresas = GetEmpresasSelect();
            return View("~/Views/Lider/CrearWebinar.cshtml");
        }



        [AutorizarAccion("Crear webinar", "Crear")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearWebinar(Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile)
        {
            // Validación de fecha
            if (model.FechaFin <= model.FechaInicio)
                ModelState.AddModelError(nameof(model.FechaFin), "La fecha de fin debe ser mayor que la fecha de inicio.");

            // Normalización de los inputs
            model.UrlEnVivoEmbed = ExtractIframeSrcOrReturn(model.UrlEnVivoEmbed);
            model.UrlRegistro = model.UrlRegistro?.Trim();
            model.UrlTeams = model.UrlTeams?.Trim();
            model.UrlGrabacion = null; // siempre nula al crear

            // Validaciones comunes opcionales
            if (!string.IsNullOrWhiteSpace(model.UrlRegistro) && !EsRegistroUrl(model.UrlRegistro))
                ModelState.AddModelError(nameof(model.UrlRegistro), "La URL de registro debe ser de events.teams.microsoft.com.");

            if (!string.IsNullOrWhiteSpace(model.UrlGrabacion) && !EsGrabacionUrl(model.UrlGrabacion))
                ModelState.AddModelError(nameof(model.UrlGrabacion), "La URL de grabación debe ser de SharePoint/OneDrive/YouTube/Vimeo.");

            // Reglas según formato
            if (model.EsAsamblea)
            {
                // Asamblea: se requiere el url embed. Join es opcional (presentadores).
                if (string.IsNullOrWhiteSpace(model.UrlEnVivoEmbed))
                    ModelState.AddModelError(nameof(model.UrlEnVivoEmbed), "En Asamblea, el SRC de inserción es obligatorio.");

                if (!string.IsNullOrWhiteSpace(model.UrlEnVivoEmbed) && !IsAllowedLiveEmbedHost(model.UrlEnVivoEmbed))
                    ModelState.AddModelError(nameof(model.UrlEnVivoEmbed), "El SRC debe ser de Microsoft (microsoft.com / office.com / office365.com).");

                //  UrlTeams aquí se usa solo para presentadores
                if (!string.IsNullOrWhiteSpace(model.UrlTeams) && !EsJoinUrl(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "El enlace de unirse debe ser teams.microsoft.com/l/meetup-join/...");
            }
            else
            {
                // Webinar  se requiere JOIN. 
                if (string.IsNullOrWhiteSpace(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "En Webinar, debes proporcionar el enlace para unirse a Teams (meetup-join).");
                else if (!EsJoinUrl(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "La URL de unirse debe ser de teams.microsoft.com/l/meetup-join/...");

                // Limpia el embed si alguien lo pegó por error
                model.UrlEnVivoEmbed = null;
            }

            //  Resultado de validaciones
            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
                return View("~/Views/Lider/CrearWebinar.cshtml", model);
            }

            //  Imagen
            var rutaRel = await GuardarImagenAsync(imagenFile);
            if (!string.IsNullOrWhiteSpace(rutaRel))
                model.Imagen = rutaRel;

            // Auditoría mínima
            model.UsuarioCreadorID = GetUsuarioId();

            //Persistencia
            _db.Webinars.Add(model);
            await _db.SaveChangesAsync();

            //  Relaciones empresa ↔ webinar (si no es público)
            if (!model.EsPublico && (empresasSeleccionadas?.Length > 0))
            {
                var relaciones = empresasSeleccionadas.Distinct().Select(eid => new WebinarEmpresa
                {
                    WebinarID = model.WebinarID,
                    EmpresaID = eid
                });
                _db.WebinarsEmpresas.AddRange(relaciones);
                await _db.SaveChangesAsync();
            }

            // srvicio de Notificaciones
            var tipoNotif = "WebinarAsignado";
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
            else if (empresasSeleccionadas?.Any() == true)
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

            // servicio de logs
            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                await _bitacoraService.RegistrarAsync(
                    idUsuario: GetUsuarioId(),
                    idEmpresa: GetEmpresaId(),
                    accion: "WEBINAR_CREAR",
                    mensaje: model.EsPublico
                             ? "Webinar público creado"
                             : $"Webinar privado creado para {(empresasSeleccionadas?.Distinct().Count() ?? 0)} empresas",
                    modulo: "LIDER",
                    entidad: "Webinar",
                    entidadId: model.WebinarID.ToString(),
                    resultado: "OK",
                    severidad: 4,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                );
            }
            catch {  }

            return RedirectToAction(nameof(MisWebinars));
        }





        [AutorizarAccion("Editar webinar", "Editar")]
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





        [AutorizarAccion("Editar webinar", "Editar")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(
            int id, Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile, string? returnUrl)
        {
            if (id != model.WebinarID) return NotFound();

            // Normalizaciones
            model.UrlRegistro = model.UrlRegistro?.Trim();
            model.UrlTeams = model.UrlTeams?.Trim();

            model.UrlEnVivoEmbed = model.EsAsamblea
                ? ExtractIframeSrcOrReturn(model.UrlEnVivoEmbed)
                : null;

            // Grabación, si se pega todo el iframe manda a llamar al metodo para solo extraer el sscr
            model.UrlGrabacion = string.IsNullOrWhiteSpace(model.UrlGrabacion)
                ? null
                : ExtractIframeSrcOrReturn(model.UrlGrabacion);

            // Validaciones
            if (model.FechaFin < model.FechaInicio)
                ModelState.AddModelError(nameof(model.FechaFin), "La fecha de fin no puede ser menor que la fecha de inicio.");

            if (!string.IsNullOrWhiteSpace(model.UrlRegistro) && !EsRegistroUrl(model.UrlRegistro))
                ModelState.AddModelError(nameof(model.UrlRegistro), "La URL de registro debe ser de events.teams.microsoft.com.");

            if (!string.IsNullOrWhiteSpace(model.UrlGrabacion) && !EsGrabacionUrl(model.UrlGrabacion))
                ModelState.AddModelError(nameof(model.UrlGrabacion), "La URL/iframe de grabación debe ser de SharePoint/OneDrive/YouTube/Vimeo.");

            if (model.EsAsamblea)
            {
                if (string.IsNullOrWhiteSpace(model.UrlEnVivoEmbed))
                    ModelState.AddModelError(nameof(model.UrlEnVivoEmbed), "En Asamblea, el SRC de inserción es obligatorio.");

                if (!string.IsNullOrWhiteSpace(model.UrlEnVivoEmbed) && !IsAllowedLiveEmbedHost(model.UrlEnVivoEmbed))
                    ModelState.AddModelError(nameof(model.UrlEnVivoEmbed), "El SRC debe ser de Microsoft (microsoft.com / office.com / office365.com).");

                if (!string.IsNullOrWhiteSpace(model.UrlTeams) && !EsJoinUrl(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "El enlace de unirse debe ser teams.microsoft.com/l/meetup-join/...");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "En Webinar, debes proporcionar el enlace para unirse (meetup-join).");
                else if (!EsJoinUrl(model.UrlTeams))
                    ModelState.AddModelError(nameof(model.UrlTeams), "La URL de unirse debe ser de teams.microsoft.com/l/meetup-join/...");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
                ViewBag.ReturnUrl = returnUrl ?? Url.Action("GestionarWebinar", "Lider");
                return View("~/Views/Lider/EditarWebinar.cshtml", model);
            }

            // Recupera original para conservar campos no editables
            var original = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id);
            if (original == null) return NotFound();

            // Preservar metadatos
            model.UsuarioCreadorID = original.UsuarioCreadorID;
            model.FechaCreacion = original.FechaCreacion;

            // Imagen 
            var nuevaRuta = await GuardarImagenAsync(imagenFile);
            model.Imagen = !string.IsNullOrWhiteSpace(nuevaRuta) ? nuevaRuta : original.Imagen;

            // Persistir cambios del webinar
            _db.Update(model);
            _db.Entry(model).Property(x => x.UsuarioCreadorID).IsModified = false;
            _db.Entry(model).Property(x => x.FechaCreacion).IsModified = false;
            await _db.SaveChangesAsync();

            // Reasignar empresas
            var actuales = _db.WebinarsEmpresas.Where(we => we.WebinarID == id);
            _db.WebinarsEmpresas.RemoveRange(actuales);
            if (!model.EsPublico && empresasSeleccionadas is { Length: > 0 })
            {
                var relaciones = empresasSeleccionadas.Distinct().Select(eid => new WebinarEmpresa
                {
                    WebinarID = id,
                    EmpresaID = eid
                });
                await _db.WebinarsEmpresas.AddRangeAsync(relaciones);
            }
            await _db.SaveChangesAsync();

            // Bitácora
            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                await _bitacoraService.RegistrarAsync(
                    idUsuario: GetUsuarioId(),
                    idEmpresa: GetEmpresaId(),
                    accion: "WEBINAR_EDITADO",
                    mensaje: model.EsPublico
                        ? "Webinar público editado"
                        : $"Webinar privado editado para {(empresasSeleccionadas?.Distinct().Count() ?? 0)} empresas",
                    modulo: "LIDER",
                    entidad: "Webinar",
                    entidadId: model.WebinarID.ToString(),
                    resultado: "OK",
                    severidad: 4,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                );
            }
            catch {  }

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


        [AutorizarAccion("Crear webinar", "Crear")]
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

        [AutorizarAccion("Ver Webinars", "Ver")]
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


        [AutorizarAccion("Eliminar webinar", "Eliminar")]
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

        [AutorizarAccion("Eliminar webinar", "Eliminar")]
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

   

   






}