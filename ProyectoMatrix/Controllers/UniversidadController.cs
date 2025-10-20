// =====================================================
// ARCHIVO: Controllers/UniversidadController.cs
// PROPÓSITO: Controlador principal Universidad NS (con bitácora)
// =====================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Seguridad;
using ProyectoMatrix.Servicios;
using static ProyectoMatrix.Servicios.UniversidadServices;

namespace ProyectoMatrix.Controllers
{
    public class UniversidadController : Controller
    {
        private readonly UniversidadServices _universidadServices;
        private readonly ILogger<UniversidadController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly BitacoraService _bitacora;
        private readonly IServicioAcceso _acceso;

        public UniversidadController(
            UniversidadServices universidadServices,
            ILogger<UniversidadController> logger,
            IWebHostEnvironment webHostEnvironment,
            BitacoraService bitacora,
            IServicioAcceso acceso)
        {
            _universidadServices = universidadServices;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _bitacora = bitacora;
            _acceso = acceso;
        }

        // Helpers de contexto para bitácora
        private (int? idUsuario, int? idEmpresa) LeerIdsSesion()
        {
            int? idUsuario = HttpContext.Session.GetInt32("UsuarioID");
            int? idEmpresa = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                            ?? HttpContext.Session.GetInt32("EmpresaID");
            return (idUsuario, idEmpresa);
        }

        private (string? solicitudId, string? ip, string? agente) LeerCtxMiddleware()
        {
            var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
            var ip = HttpContext.Items["DireccionIp"]?.ToString();
            var agente = HttpContext.Items["AgenteUsuario"]?.ToString();
            return (solicitudId, ip, agente);
        }

        // =====================================================
        // DASHBOARD / INDEX
        // =====================================================

        [AutorizarAccion("Ver cursos", "Ver")]
        public async Task<IActionResult> Index()
        {
            try
            {
                // DEBUG sesión
                _logger.LogInformation("=== DEBUG SESIÓN UNIVERSIDAD ===");
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID");
                var rolId2 = HttpContext.Session.GetInt32("RolId");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");
                var empresaId2 = HttpContext.Session.GetInt32("EmpresaId");
                var nombreUsuario = HttpContext.Session.GetString("Username");
                var nombreEmpresa = HttpContext.Session.GetString("EmpresaNombre");
                var rol = HttpContext.Session.GetString("Rol");

                _logger.LogInformation("UsuarioID: {UsuarioId}", usuarioId);
                _logger.LogInformation("RolID: {RolId}", rolId);
                _logger.LogInformation("RolId: {RolId2}", rolId2);
                _logger.LogInformation("EmpresaSeleccionada: {EmpresaId}", empresaId);
                _logger.LogInformation("EmpresaId: {EmpresaId2}", empresaId2);
                _logger.LogInformation("Username: {Username}", nombreUsuario);
                _logger.LogInformation("EmpresaNombre: {EmpresaNombre}", nombreEmpresa);
                _logger.LogInformation("Rol: {Rol}", rol);

                if (!usuarioId.HasValue)
                {
                    _logger.LogWarning("UsuarioID no encontrado en sesión - Redirigiendo a login");
                    TempData["Error"] = "Sesión expirada. Por favor, inicia sesión nuevamente.";
                    return RedirectToAction("Login", "Login");
                }

                var rolIdFinal = rolId ?? rolId2 ?? 4;
                var empresaIdFinal = empresaId ?? empresaId2 ?? 1;

                _logger.LogInformation("Valores finales - RolId: {RolId}, EmpresaId: {EmpresaId}", rolIdFinal, empresaIdFinal);

                // Calcula permisos de gestión desde BD (usa EXACTAMENTE los nombres de SubMenú)
                var tCrear = _acceso.TienePermisoAsync(usuarioId.Value, "Crear curso", "Crear");
                var tEditar = _acceso.TienePermisoAsync(usuarioId.Value, "Editar curso", "Editar");
                var tEliminar = _acceso.TienePermisoAsync(usuarioId.Value, "Eliminar curso", "Eliminar");
                await Task.WhenAll(tCrear, tEditar, tEliminar);
                var puedeGestionarCursos = tCrear.Result || tEditar.Result || tEliminar.Result;
                var viewModel = new UniversidadDashboardViewModel
                {
                    UsuarioId = usuarioId.Value,
                    RolId = rolIdFinal,
                    EmpresaId = empresaIdFinal,
                    NombreUsuario = nombreUsuario ?? "Usuario",
                    NombreEmpresa = nombreEmpresa ?? "NS Group",
                    PuedeCrearCursos = puedeGestionarCursos,   // <- ya lo tenías
                    PuedeAsignarCursos = puedeGestionarCursos,   // <- CAMBIO: quita helper
                    PuedeVerReportes = UniversidadPermisosHelper.PermisosUniversidad.PuedeVerReportes(rolIdFinal), // o cámbialo después
                    PuedeConfiguracion = puedeGestionarCursos    // <- CAMBIO: quita helper
                };



                await CargarDatosDashboard(viewModel);
                viewModel.MenuItems = GenerarMenuItems(viewModel);

                _logger.LogInformation("Dashboard cargado exitosamente para usuario {UsuarioId}", usuarioId);

                // Bitácora
                try
                {
                    var (idUsuarioBit, idEmpresaBit) = (usuarioId, empresaIdFinal);
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuarioBit,
                        idEmpresa: idEmpresaBit,
                        accion: "VER",
                        mensaje: "Usuario abrió dashboard de Universidad",
                        modulo: "UNIVERSIDAD",
                        entidad: "Dashboard",
                        entidadId: null,
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar dashboard de Universidad NS");
                TempData["Error"] = "Error al cargar el dashboard. Intente nuevamente.";
                return RedirectToAction("Index", "Menu");
            }
        }


        private async Task CargarDatosDashboard(UniversidadDashboardViewModel viewModel)
        {
            try
            {
                _logger.LogInformation("=== INICIO CargarDatosDashboard ===");
                _logger.LogInformation("Usuario: {UsuarioId}, Empresa: {EmpresaId}, Rol: {RolId}",
                    viewModel.UsuarioId, viewModel.EmpresaId, viewModel.RolId);

                viewModel.MisCursos = await _universidadServices.GetCursosAsignadosUsuarioViewModelAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                _logger.LogInformation("Cursos cargados: {Count}", viewModel.MisCursos?.Count ?? 0);

                viewModel.MisCertificados = await _universidadServices.GetCertificadosUsuarioViewModelAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                int tiempoEstudio = await _universidadServices.GetTiempoEstudioUsuarioAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                viewModel.Estadisticas = new EstadisticasUniversidadViewModel
                {
                    TotalCursosAsignados = viewModel.MisCursos?.Count ?? 0,
                    CursosCompletados = viewModel.MisCursos?.Count(c => c.Estado == "Completado") ?? 0,
                    CursosEnProgreso = viewModel.MisCursos?.Count(c => c.Estado == "En Progreso") ?? 0,
                    CertificadosObtenidos = viewModel.MisCertificados?.Count(c => c.Estado == "Vigente") ?? 0,
                    PromedioProgreso = viewModel.MisCursos?.Any() == true ?
                        (decimal)viewModel.MisCursos.Average(c => (double)c.PorcentajeProgreso) : 0m,
                    TotalSubCursos = viewModel.MisCursos?.Sum(c => c.TotalSubCursos) ?? 0,
                    SubCursosCompletados = viewModel.MisCursos?.Sum(c => c.SubCursosCompletados) ?? 0,
                    TiempoTotalEstudio = tiempoEstudio
                };

                _logger.LogInformation("Estadísticas básicas calculadas - Cursos: {Total}, SubCursos: {SubCursos}, Completados: {Completados}, Progreso: {Progreso}%",
                    viewModel.Estadisticas.TotalCursosAsignados,
                    viewModel.Estadisticas.TotalSubCursos,
                    viewModel.Estadisticas.SubCursosCompletados,
                    viewModel.Estadisticas.PromedioProgreso);

                if (viewModel.PuedeCrearCursos || viewModel.PuedeVerReportes)
                {
                    _logger.LogInformation("🎯 Cargando estadísticas administrativas...");
                    try
                    {
                        var estadisticasAdmin = await _universidadServices.GetEstadisticasAdministrativasAsync();
                        if (estadisticasAdmin != null)
                        {
                            viewModel.Estadisticas.TotalUsuariosActivos = estadisticasAdmin.TotalUsuariosActivos;
                            viewModel.Estadisticas.TotalCursosCreados = estadisticasAdmin.TotalCursosCreados;
                            viewModel.Estadisticas.CertificadosEmitidosMes = estadisticasAdmin.CertificadosEmitidosMes;
                            _logger.LogInformation("✅ Estadísticas administrativas cargadas");
                        }
                        else
                        {
                            _logger.LogWarning("❌ GetEstadisticasAdministrativasAsync devolvió NULL");
                        }
                    }
                    catch (Exception adminEx)
                    {
                        _logger.LogError(adminEx, "❌ ERROR al cargar estadísticas administrativas");
                        // Fallback de ejemplo
                        viewModel.Estadisticas.TotalUsuariosActivos = 5;
                        viewModel.Estadisticas.TotalCursosCreados = 2;
                        viewModel.Estadisticas.CertificadosEmitidosMes = 1;
                    }
                }
                else
                {
                    _logger.LogInformation("Usuario sin permisos admin - no se cargan estadísticas del sistema");
                }

                _logger.LogInformation("=== FIN CargarDatosDashboard ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR GENERAL en CargarDatosDashboard");
                viewModel.MisCursos = new List<CursoAsignadoViewModel>();
                viewModel.MisCertificados = new List<CertificadoUsuarioViewModel>();
                viewModel.Estadisticas = new EstadisticasUniversidadViewModel();
            }
        }

        private List<MenuItemUniversidad> GenerarMenuItems(UniversidadDashboardViewModel viewModel)
        {
            var items = new List<MenuItemUniversidad>
            {
                new MenuItemUniversidad { Titulo = "Dashboard", Url = "/Universidad", Icono = "fas fa-home", EsActivo = true }
            };

            items.Add(new MenuItemUniversidad
            {
                Titulo = "Mis Cursos",
                Url = "/Universidad/MisCursos",
                Icono = "fas fa-graduation-cap",
                Badge = viewModel.MisCursos.Count(c => c.Estado == "En Progreso")
            });

            items.Add(new MenuItemUniversidad
            {
                Titulo = "Mis Certificados",
                Url = "/Universidad/MisCertificados",
                Icono = "fas fa-certificate",
                Badge = viewModel.MisCertificados.Count
            });

            if (viewModel.PuedeCrearCursos)
            {
                items.Add(new MenuItemUniversidad
                {
                    Titulo = "Gestionar Cursos",
                    Url = "/Universidad/GestionCursos",
                    Icono = "fas fa-book",
                    SubItems = new List<MenuItemUniversidad>
                    {
                        new MenuItemUniversidad { Titulo = "Crear Curso", Url = "/Universidad/CrearCurso", Icono = "fas fa-plus" },
                        new MenuItemUniversidad { Titulo = "Mis Cursos Creados", Url = "/Universidad/MisCursosCreados", Icono = "fas fa-list" }
                    }
                });
            }

            if (viewModel.PuedeAsignarCursos)
            {
                items.Add(new MenuItemUniversidad
                {
                    Titulo = "Asignar Cursos",
                    Url = "/Asignaciones/AsignacionMasiva",
                    Icono = "fas fa-users-cog",
                    SubItems = new List<MenuItemUniversidad>
                    {
                        new MenuItemUniversidad { Titulo = "Asignación Masiva", Url = "/Asignaciones/AsignacionMasiva", Icono = "fas fa-users-cog" },
                        new MenuItemUniversidad { Titulo = "Ver Asignaciones", Url = "/Asignaciones/VerAsignaciones", Icono = "fas fa-list-check" }
                    }
                });
            }

            if (viewModel.PuedeVerReportes)
            {
                items.Add(new MenuItemUniversidad
                {
                    Titulo = "Reportes",
                    Url = "/Universidad/Reportes",
                    Icono = "fas fa-chart-bar",
                    SubItems = new List<MenuItemUniversidad>
                    {
                        new MenuItemUniversidad { Titulo = "Progreso General", Url = "/Universidad/ReporteProgreso", Icono = "fas fa-chart-line" },
                        new MenuItemUniversidad { Titulo = "Certificados Emitidos", Url = "/Universidad/ReporteCertificados", Icono = "fas fa-award" },
                        new MenuItemUniversidad { Titulo = "Actividad Usuarios", Url = "/Universidad/ReporteActividad", Icono = "fas fa-user-clock" }
                    }
                });
            }

            if (viewModel.PuedeConfiguracion)
            {
                items.Add(new MenuItemUniversidad
                {
                    Titulo = "Configuración",
                    Url = "/Universidad/Configuracion",
                    Icono = "fas fa-cog"
                });
            }

            return items;
        }

        // =====================================================
        // MIS CURSOS
        // =====================================================

        [AutorizarAccion("Ver cursos", "Ver")]
        [HttpGet]
        public async Task<IActionResult> MisCursos()
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                _logger.LogInformation("🔍 CONTROLLER DEBUG - UsuarioID: {UsuarioId}, EmpresaID: {EmpresaId}", usuarioId, empresaId);

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    _logger.LogWarning("❌ Sesión inválida - redirigiendo a login");
                    return RedirectToAction("Login", "Account");
                }

                var cursosAsignados = await _universidadServices.GetCursosAsignadosUsuarioViewModelAsync(
                    usuarioId.Value, empresaId.Value);

                _logger.LogInformation("✅ CONTROLLER - Cursos obtenidos: {Total}", cursosAsignados.Count);

                // Bitácora: ver lista (opcional, info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_LISTA",
                        mensaje: "Usuario abrió Mis Cursos",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: null,
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(cursosAsignados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR en controlador MisCursos");
                TempData["Error"] = "Error al cargar tus cursos";
                return View(new List<CursoAsignadoViewModel>());
            }
        }

        [AutorizarAccion("Ver cursos", "Ver")]
        public async Task<IActionResult> GestionCursos()
        {
            try
            {
                var uid = HttpContext.Session.GetInt32("UsuarioID") ?? 0;
                var rolId = HttpContext.Session.GetInt32("RolID")
                           ?? HttpContext.Session.GetInt32("RolId") ?? 4;

                // ---- Permisos desde BD (OR de Crear/Editar/Eliminar) ----
                var tCrear = _acceso.TienePermisoAsync(uid, "Crear curso", "Crear");
                var tEditar = _acceso.TienePermisoAsync(uid, "Editar curso", "Editar");
                var tEliminar = _acceso.TienePermisoAsync(uid, "Eliminar curso", "Eliminar");
                await Task.WhenAll(tCrear, tEditar, tEliminar);

                var puedeCrear = tCrear.Result;
                var puedeEditar = tEditar.Result;
                var puedeEliminar = tEliminar.Result;
                var puedeGestionar = puedeCrear || puedeEditar || puedeEliminar;

                if (!puedeGestionar)
                {
                    TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                    return RedirectToAction("Index");
                }
                // ----------------------------------------------------------

                var niveles = await _universidadServices.GetNivelesEducativosViewModelAsync();

                var viewModel = new GestionCursosViewModel
                {
                    RolId = rolId,
                    // Si quieres que “aprobar” también dependa de BD, usa: PuedeAprobar = puedeGestionar;
                    PuedeAprobar = UniversidadPermisosHelper.PermisosUniversidad.PuedeAprobarCursos(rolId),
                    PuedeCrear = puedeCrear,
                    PuedeEditar = puedeEditar,
                   // PuedeEliminar = puedeEliminar,
                   // PuedeGestionar = puedeGestionar,

                    Niveles = niveles ?? new List<NivelEducativoViewModel>(),
                    Cursos = new List<CursoCompleto>(),
                    CursosPorNivel = new Dictionary<int, int>()
                };

                _logger.LogInformation("🔍 Cargando cursos para gestión...");
                var cursosObtenidos = await _universidadServices.GetTodosCursosAsync();
                _logger.LogInformation("📊 Cursos obtenidos del servicio: {Count}", cursosObtenidos?.Count ?? 0);

                viewModel.Cursos = (cursosObtenidos ?? new List<CursoCompleto>())
                    .GroupBy(c => c.CursoID)            // evita duplicados
                    .Select(g => g.First())
                    .ToList();

                foreach (var nivel in viewModel.Niveles)
                    viewModel.CursosPorNivel[nivel.NivelID] = viewModel.Cursos.Count(c => c.NivelID == nivel.NivelID);

                // Bitácora
                try
                {
                    var (idUsuario, idEmpresa) = LeerIdsSesion();
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuario,
                        idEmpresa: idEmpresa,
                        accion: "VER",
                        mensaje: "Usuario abrió Gestión de Cursos",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: null,
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar gestión de cursos");
                TempData["Error"] = "Error al cargar la gestión de cursos.";
                return RedirectToAction("Index");
            }
        }




        // =====================================================
        // TOMAR CURSO - VER SUBCURSOS
        // =====================================================

        [HttpGet("Universidad/TomarCurso/{cursoId:int}")]
        public async Task<IActionResult> TomarCurso([FromRoute] int cursoId)
        {
            _logger.LogInformation("🔍 TomarCurso ejecutándose para curso {CursoId}", cursoId);

            if (cursoId <= 0)
            {
                TempData["Error"] = "Curso inválido.";
                return RedirectToAction("MisCursos");
            }

            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                                ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                    return RedirectToAction("Index");

                var subCursos = await _universidadServices.GetSubCursosPorCursoAsync(
                    cursoId, usuarioId.Value, empresaId);

                if (!subCursos.Any())
                {
                    TempData["Warning"] = "Este curso no tiene contenido disponible aún.";
                    return RedirectToAction("MisCursos");
                }

                var cursoInfo = subCursos.FirstOrDefault();

                var viewModel = new CursoDetalleViewModel
                {
                    Curso = new CursoCompleto
                    {
                        CursoID = cursoId,
                        NombreCurso = cursoInfo?.NombreSubCurso ?? "Curso",
                        TotalSubCursos = subCursos.Count
                    },
                    SubCursos = subCursos,
                    PuedeEditarCurso = false,
                    PuedeEliminarCurso = false,
                    PuedeAgregarSubCursos = false
                };

                // Bitácora: ver detalle de curso (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_DETALLE",
                        mensaje: $"Usuario abrió curso {cursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: cursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar curso {CursoId}", cursoId);
                TempData["Error"] = "Error al cargar el curso. Intente nuevamente.";
                return RedirectToAction("MisCursos");
            }
        }

        // =====================================================
        // VER VIDEO/SUBCURSO
        // =====================================================

        [HttpGet]
        public async Task<IActionResult> TomarSubCurso(int subCursoId)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                               ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return RedirectToAction("Index");
                }

                var subCurso = await _universidadServices.ObtenerSubCursoConCursoAsync(subCursoId, usuarioId.Value, empresaId);

                if (subCurso == null)
                {
                    TempData["Error"] = "Subcurso no encontrado.";
                    return RedirectToAction("MisCursos");
                }

                if (!subCurso.PuedeAcceder)
                {
                    TempData["Warning"] = "Debe completar los prerrequisitos antes de acceder a este contenido.";
                    return RedirectToAction("TomarCurso", new { cursoId = subCurso.CursoID });
                }

                ViewBag.UsuarioId = usuarioId.Value;
                ViewBag.EmpresaId = empresaId;
                ViewBag.SubCursoId = subCursoId;

                // Bitácora: ver subcurso (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_DETALLE",
                        mensaje: $"Usuario abrió subcurso {subCursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: subCursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(subCurso);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar subcurso {SubCursoId}", subCursoId);
                TempData["Error"] = "Error al cargar el contenido. Intente nuevamente.";
                return RedirectToAction("MisCursos");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CompletarSubCurso([FromBody] CompletarSubCursoRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                               ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                var progresoRequest = new ActualizarProgresoRequest
                {
                    UsuarioID = usuarioId.Value,
                    EmpresaID = empresaId,
                    SubCursoID = request.SubCursoId,
                    TiempoTotalVisto = request.TiempoVisto,
                    PorcentajeVisto = 100
                };

                var resultado = await _universidadServices.ActualizarProgresoVideoAsync(progresoRequest);

                // Bitácora: completar subcurso (auditoría si OK, error si no)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "COMPLETAR_SUBCURSO",
                        mensaje: resultado ? $"SubCurso {request.SubCursoId} completado" : "No se pudo completar subcurso",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: request.SubCursoId.ToString(),
                        resultado: resultado ? "OK" : "ERROR",
                        severidad: resultado ? (byte)4 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return Json(new
                {
                    success = resultado,
                    message = resultado ? "Módulo completado exitosamente" : "Error al completar el módulo"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al completar subcurso {SubCursoId}", request?.SubCursoId);
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }

        // =====================================================
        // ACTUALIZAR PROGRESO (AJAX)
        // =====================================================

        [HttpPost]
        public async Task<IActionResult> ActualizarProgreso([FromBody] ActualizarProgresoRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                    return Json(new { success = false, message = "Sesión expirada" });

                request.UsuarioID = usuarioId.Value;
                request.EmpresaID = empresaId.Value;

                var resultado = await _universidadServices.ActualizarProgresoVideoAsync(request);

                // Bitácora: actualizar progreso (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "ACTUALIZAR_PROGRESO",
                        mensaje: $"Actualizó progreso en SubCurso {request.SubCursoID} a {request.PorcentajeVisto}%",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: request.SubCursoID.ToString(),
                        resultado: resultado ? "OK" : "ERROR",
                        severidad: resultado ? (byte)1 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return Json(new { success = resultado, message = "Progreso actualizado" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar progreso");
                return Json(new { success = false, message = "Error al actualizar progreso" });
            }
        }

        // =====================================================
        // CREAR CURSO
        [HttpGet]
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearCurso()
        {
            var niveles = await _universidadServices.GetNivelesEducativosViewModelAsync();
            ViewBag.Niveles = niveles;
            return View(new CrearCursoRequest());
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearCurso(CrearCursoRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
               

              

                if (!ModelState.IsValid)
                {
                    var niveles = await _universidadServices.GetNivelesEducativosAsync();
                    ViewBag.Niveles = niveles;
                    return View(request);
                }

                request.CreadoPorUsuarioID = usuarioId.Value;
                var cursoId = await _universidadServices.CrearCursoAsync(request);

                // Bitácora: crear curso
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    var (_, idEmpresa) = LeerIdsSesion();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: idEmpresa,
                        accion: "CREAR",
                        mensaje: $"Curso '{request.NombreCurso}' creado",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: cursoId.ToString(),
                        resultado: "OK",
                        severidad: 4,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                TempData["Success"] = $"Curso '{request.NombreCurso}' creado exitosamente.";
                return RedirectToAction("EditarCurso", new { id = cursoId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear curso");
                TempData["Error"] = "Error al crear el curso. Intente nuevamente.";

                // Bitácora: error al crear
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    var (idUsuario, idEmpresa) = LeerIdsSesion();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuario,
                        idEmpresa: idEmpresa,
                        accion: "CREAR",
                        mensaje: ex.Message,
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: null,
                        resultado: "ERROR",
                        severidad: 3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                var niveles = await _universidadServices.GetNivelesEducativosAsync();
                ViewBag.Niveles = niveles;
                return View(request);
            }
        }

        // =====================================================
        // MIS CERTIFICADOS
        // =====================================================

        public async Task<IActionResult> MisCertificados()
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue)
                    return RedirectToAction("Index");

                var certificados = await _universidadServices.GetCertificadosUsuarioViewModelAsync(
                    usuarioId.Value, empresaId);

                // Bitácora: ver certificados (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_LISTA",
                        mensaje: "Usuario abrió Mis Certificados",
                        modulo: "UNIVERSIDAD",
                        entidad: "Certificado",
                        entidadId: null,
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(certificados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar certificados");
                TempData["Error"] = "Error al cargar los certificados.";
                return RedirectToAction("Index");
            }
        }

        // =====================================================
        // DESCARGAR CERTIFICADO
        // =====================================================

        public async Task<IActionResult> DescargarCertificado(int id)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

                if (!usuarioId.HasValue)
                    return RedirectToAction("Index");

                var certificados = await _universidadServices.GetCertificadosUsuarioViewModelAsync(usuarioId.Value);
                var certificado = certificados.FirstOrDefault(c => c.CertificadoID == id);

                if (certificado == null || !certificado.TieneArchivo)
                {
                    TempData["Error"] = "Certificado no encontrado o no disponible para descarga.";
                    return RedirectToAction("MisCertificados");
                }

                var filePath = Path.Combine("wwwroot", "certificados", certificado.ArchivoPDF!);

                if (!System.IO.File.Exists(filePath))
                {
                    TempData["Error"] = "Archivo de certificado no encontrado.";
                    return RedirectToAction("MisCertificados");
                }

                // Bitácora: descargar (auditoría leve, puedes dejar 1 o 4)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    int? empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                                   ?? HttpContext.Session.GetInt32("EmpresaID");
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "DESCARGAR",
                        mensaje: $"Descargó certificado {id}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Certificado",
                        entidadId: id.ToString(),
                        resultado: "OK",
                        severidad: 4, // si prefieres Info, cambia a 1
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var fileName = $"Certificado_{certificado.CodigoCertificado}.pdf";

                return File(fileBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar certificado {CertificadoId}", id);
                TempData["Error"] = "Error al descargar el certificado.";
                return RedirectToAction("MisCertificados");
            }
        }

        // =====================================================
        // EDITAR CURSO
        // =====================================================
        [AutorizarAccion("Editar curso", "Editar")]
        public async Task<IActionResult> EditarCurso(int id)
        {
            try
            {
             

                var curso = await _universidadServices.GetCursoPorIdAsync(id);
                if (curso == null)
                {
                    TempData["Error"] = "Curso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                var usuarioId = HttpContext.Session.GetInt32("UsuarioID") ?? 0;
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                               ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                var subCursos = await _universidadServices.GetSubCursosPorCursoAsync(
                    id, usuarioId, empresaId);

                var viewModel = new CursoDetalleViewModel
                {
                    Curso = curso,
                    SubCursos = subCursos,
                    PuedeEditarCurso = true,
                    PuedeEliminarCurso = true,
                    PuedeAgregarSubCursos = true
                };

                // Bitácora: ver detalle curso para edición (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_DETALLE",
                        mensaje: $"Abrió edición de curso {id}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: id.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar curso para edición {CursoId}", id);
                TempData["Error"] = "Error al cargar el curso.";
                return RedirectToAction("GestionCursos");
            }
        }

        // =====================================================
        // CREAR SUBCURSO
        // =====================================================
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearSubCurso(int cursoId)
        {
            try
            {
               

                var curso = await _universidadServices.GetCursoPorIdAsync(cursoId);
                if (curso == null)
                {
                    TempData["Error"] = "Curso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                ViewBag.CursoId = cursoId;
                ViewBag.NombreCurso = curso.NombreCurso;

                var model = new CrearSubCursoRequest
                {
                    CursoID = cursoId,
                    EsObligatorio = true,
                    RequiereEvaluacion = true,
                    PuntajeMinimo = 70.00m
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar formulario de crear subcurso");
                TempData["Error"] = "Error al cargar el formulario.";
                return RedirectToAction("GestionCursos");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearSubCurso(CrearSubCursoRequest request, IFormFile archivoVideo, IFormFile archivoPDF)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
             

                // Mapear archivos si vienen
                if (archivoVideo != null && archivoVideo.Length > 0)
                {
                    var rutaVideo = await GuardarArchivoAsync(archivoVideo, "videos");
                    request.ArchivoVideo = rutaVideo;
                }

                if (archivoPDF != null && archivoPDF.Length > 0)
                {
                    var rutaPDF = await GuardarArchivoAsync(archivoPDF, "documentos");
                    request.ArchivoPDF = rutaPDF;
                }

                // ✅ Regla: al menos uno (video o PDF)
                if (string.IsNullOrWhiteSpace(request.ArchivoVideo) &&
                    string.IsNullOrWhiteSpace(request.ArchivoPDF))
                {
                    ModelState.AddModelError(nameof(request.ArchivoVideo), "Sube al menos un Video o un PDF.");
                    ModelState.AddModelError(nameof(request.ArchivoPDF), "Sube al menos un Video o un PDF.");
                }

                if (!ModelState.IsValid)
                {
                    var curso = await _universidadServices.GetCursoPorIdAsync(request.CursoID);
                    ViewBag.CursoId = request.CursoID;
                    ViewBag.NombreCurso = curso?.NombreCurso ?? "Curso";
                    return View(request);
                }

                var subCursoId = await _universidadServices.CrearSubCursoAsync(request);

                // Bitácora: crear subcurso (silenciar errores)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    var (_, idEmpresa) = LeerIdsSesion();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: idEmpresa,
                        accion: "CREAR",
                        mensaje: $"SubCurso '{request.NombreSubCurso}' creado para Curso {request.CursoID}",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: subCursoId.ToString(),
                        resultado: "OK",
                        severidad: 4,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                TempData["Success"] = $"SubCurso '{request.NombreSubCurso}' creado exitosamente.";
                return RedirectToAction("EditarCurso", new { id = request.CursoID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear subcurso");
                TempData["Error"] = "Error al crear el subcurso. Intente nuevamente.";
                var curso = await _universidadServices.GetCursoPorIdAsync(request.CursoID);
                ViewBag.CursoId = request.CursoID;
                ViewBag.NombreCurso = curso?.NombreCurso ?? "Curso";
                return View(request);
            }
        }


        // =====================================================
        // EDITAR SUBCURSO
        // =====================================================
        [AutorizarAccion("Editar curso", "Editar")]
        public async Task<IActionResult> EditarSubCurso(int id)
        {
            try
            {

                var subCurso = await _universidadServices.GetSubCursoPorIdAsync(id);

                if (subCurso == null)
                {
                    TempData["Error"] = "SubCurso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                var request = new CrearSubCursoRequest
                {
                    CursoID = subCurso.CursoID,
                    NombreSubCurso = subCurso.NombreSubCurso,
                    Descripcion = subCurso.Descripcion,
                    Orden = subCurso.Orden,
                    DuracionVideo = subCurso.DuracionVideo,
                    EsObligatorio = subCurso.EsObligatorio,
                    RequiereEvaluacion = subCurso.RequiereEvaluacion,
                    PuntajeMinimo = subCurso.PuntajeMinimo,
                    ArchivoVideo = subCurso.ArchivoVideo,
                    ArchivoPDF = subCurso.ArchivoPDF
                };

                ViewBag.SubCursoId = id;
                ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                ViewBag.EsEdicion = true;

                // Bitácora: ver edición subcurso (info)
                try
                {
                    var (idUsuario, idEmpresa) = LeerIdsSesion();
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuario,
                        idEmpresa: idEmpresa,
                        accion: "VER_DETALLE",
                        mensaje: $"Abrió edición de subcurso {id}",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: id.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View("CrearSubCurso", request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar subcurso para edición {SubCursoId}", id);
                TempData["Error"] = "Error al cargar el subcurso.";
                return RedirectToAction("GestionCursos");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Editar curso", "Editar")]
        public async Task<IActionResult> EditarSubCurso(int id, CrearSubCursoRequest request, IFormFile archivoVideo, IFormFile archivoPDF)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

                // Traer existente para preservar rutas si no suben nuevas
                var existente = await _universidadServices.GetSubCursoPorIdAsync(id);

                // Video
                if (archivoVideo != null && archivoVideo.Length > 0)
                {
                    var rutaVideo = await GuardarArchivoAsync(archivoVideo, "videos");
                    request.ArchivoVideo = rutaVideo;
                }
                else
                {
                    // Conservar la ruta previa si el form no mandó nada
                    if (string.IsNullOrWhiteSpace(request.ArchivoVideo) && !string.IsNullOrWhiteSpace(existente?.ArchivoVideo))
                        request.ArchivoVideo = existente.ArchivoVideo;
                }

                // PDF
                if (archivoPDF != null && archivoPDF.Length > 0)
                {
                    var rutaPDF = await GuardarArchivoAsync(archivoPDF, "documentos");
                    request.ArchivoPDF = rutaPDF;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(request.ArchivoPDF) && !string.IsNullOrWhiteSpace(existente?.ArchivoPDF))
                        request.ArchivoPDF = existente.ArchivoPDF;
                }

                // ✅ Regla: al menos uno (video o PDF) después de preservar existentes
                if (string.IsNullOrWhiteSpace(request.ArchivoVideo) &&
                    string.IsNullOrWhiteSpace(request.ArchivoPDF))
                {
                    ModelState.AddModelError(nameof(request.ArchivoVideo), "Sube al menos un Video o un PDF.");
                    ModelState.AddModelError(nameof(request.ArchivoPDF), "Sube al menos un Video o un PDF.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.SubCursoId = id;
                    ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                    ViewBag.EsEdicion = true;
                    return View("CrearSubCurso", request);
                }

                var resultado = await _universidadServices.ActualizarSubCursoAsync(id, request);

                // Bitácora: editar subcurso (silenciar errores)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    var (_, idEmpresa) = LeerIdsSesion();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: idEmpresa,
                        accion: "EDITAR",
                        mensaje: resultado ? $"SubCurso {id} actualizado" : "No se pudo actualizar subcurso",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: id.ToString(),
                        resultado: resultado ? "OK" : "ERROR",
                        severidad: resultado ? (byte)4 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                if (resultado)
                {
                    TempData["Success"] = $"SubCurso '{request.NombreSubCurso}' actualizado exitosamente.";
                    return RedirectToAction("EditarCurso", new { id = request.CursoID });
                }
                else
                {
                    TempData["Error"] = "Error al actualizar el subcurso.";
                    ViewBag.SubCursoId = id;
                    ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                    ViewBag.EsEdicion = true;
                    return View("CrearSubCurso", request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar subcurso");
                TempData["Error"] = "Error al actualizar el subcurso.";
                ViewBag.SubCursoId = id;
                ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                ViewBag.EsEdicion = true;
                return View("CrearSubCurso", request);
            }
        }


        // =====================================================
        // EVALUACIONES
        // =====================================================
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearEvaluacion(int subCursoId)
        {
            try
            {
                TempData.Remove("Error");
                TempData.Remove("Success");

              
                var viewModel = await _universidadServices.GetEvaluacionViewModelAsync(subCursoId);

                if (viewModel == null)
                {
                    TempData["Error"] = "SubCurso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                // Bitácora: ver crear evaluación (info)
                try
                {
                    var (idUsuario, idEmpresa) = LeerIdsSesion();
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuario,
                        idEmpresa: idEmpresa,
                        accion: "VER",
                        mensaje: $"Abrió crear evaluación para SubCurso {subCursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Evaluación",
                        entidadId: subCursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                viewModel.PuedeEditarEvaluacion = true;
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar evaluación para SubCurso {SubCursoId}", subCursoId);
                TempData["Error"] = "Error al cargar la evaluación.";
                return RedirectToAction("GestionCursos");
            }
        }

        [HttpPost]
        [AutorizarAccion("Crear curso", "Crear")]
        public async Task<IActionResult> CrearEvaluacion([FromBody] CrearEvaluacionRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
               

                if (request.Preguntas == null || !request.Preguntas.Any())
                {
                    return Json(new { success = false, message = "Debe agregar al menos una pregunta." });
                }

                var ok = await _universidadServices.CrearEvaluacionAsync(request);

                // Bitácora: crear evaluación
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    int? idEmpresa = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                                   ?? HttpContext.Session.GetInt32("EmpresaID");
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: idEmpresa,
                        accion: "CREAR",
                        mensaje: ok
                            ? $"Evaluación creada en SubCurso {request.SubCursoID} con {request.Preguntas.Count} preguntas"
                            : "Error al crear evaluación",
                        modulo: "UNIVERSIDAD",
                        entidad: "Evaluación",
                        entidadId: request.SubCursoID.ToString(),
                        resultado: ok ? "OK" : "ERROR",
                        severidad: ok ? (byte)4 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                if (ok)
                    return Json(new { success = true, message = "Evaluación creada exitosamente." });
                else
                    return Json(new { success = false, message = "Error al crear la evaluación." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear evaluación");
                return Json(new { success = false, message = "Error interno del servidor." });
            }
        }

        [HttpGet("Universidad/TomarEvaluacion/{subCursoId:int}")]
        public async Task<IActionResult> TomarEvaluacion([FromRoute] int subCursoId)
        {
            _logger.LogInformation("Entrando a TomarEvaluacion para SubCursoID={SubCursoId}", subCursoId);

            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                                ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                    return RedirectToAction("Login", "Login");

                var viewModel = await _universidadServices.GetTomarEvaluacionViewModelAsync(
                    subCursoId, usuarioId.Value, empresaId);

                if (viewModel == null || viewModel.Preguntas == null || !viewModel.Preguntas.Any())
                {
                    TempData["Warning"] = "Este módulo no tiene evaluación disponible.";
                    return RedirectToAction("TomarSubCurso", new { subCursoId });
                }

                // Bitácora: iniciar evaluación (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "TOMAR",
                        mensaje: $"Alumno inició evaluación del SubCurso {subCursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Evaluación",
                        entidadId: subCursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View("TomarEvaluacion", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al cargar evaluación para SubCursoID={SubCursoId}", subCursoId);
                TempData["Error"] = "Error al cargar la evaluación.";
                return RedirectToAction("MisCursos");
            }
        }

        [HttpPost]
        public async Task<IActionResult> EntregarEvaluacion([FromBody] EntregarEvaluacionRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                               ?? HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada." });
                }

                var resultado = await _universidadServices.EntregarEvaluacionAsync(
                    usuarioId.Value, request.SubCursoId, empresaId, request.Respuestas, request.TiempoEmpleado);

                // Bitácora: entrega evaluación (auditoría)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "ENTREGAR",
                        mensaje: resultado.Success
                            ? $"Evaluación entregada. Calificación {resultado.Calificacion}, Aprobado: {resultado.Aprobado}"
                            : $"Entrega fallida: {resultado.Message}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Evaluación",
                        entidadId: request.SubCursoId.ToString(),
                        resultado: resultado.Success ? "OK" : "ERROR",
                        severidad: resultado.Success ? (byte)4 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                if (resultado.Success)
                {
                    return Json(new
                    {
                        success = true,
                        calificacion = resultado.Calificacion,
                        aprobado = resultado.Aprobado,
                        cursoCompleto = resultado.CursoCompleto,
                        nombreUsuario = resultado.NombreUsuario,
                        nombreCurso = resultado.NombreCurso,
                        message = "Evaluación entregada exitosamente."
                    });
                }
                else
                {
                    return Json(new { success = false, message = resultado.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al entregar evaluación");
                return Json(new { success = false, message = "Error interno del servidor." });
            }
        }

        // =====================================================
        // UTILIDADES / DESCARGAS / AJAX
        // =====================================================

        private bool ValidarSesionUsuario()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");
            return usuarioId.HasValue && empresaId.HasValue;
        }

        private void LogActividad(string accion, int? cursoId = null, int? subCursoId = null)
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

            _logger.LogInformation(
                "Universidad NS - Usuario: {UsuarioId}, Empresa: {EmpresaId}, Acción: {Accion}, Curso: {CursoId}, SubCurso: {SubCursoId}",
                usuarioId, empresaId, accion, cursoId, subCursoId);
        }

        private async Task<string> GuardarArchivoAsync(IFormFile archivo, string carpeta)
        {
            var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "contenidos", carpeta);
            Directory.CreateDirectory(uploadsPath);

            var fileName = $"{Guid.NewGuid()}_{archivo.FileName}";
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            return Path.Combine(carpeta, fileName).Replace("\\", "/");
        }

        // =====================================================
        // MIS CURSOS (DETALLE / PROGRESO / INICIO / DASHBOARD)
        // =====================================================

        [HttpGet]
        public async Task<IActionResult> DetalleMiCurso(int cursoId)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    return RedirectToAction("Login", "Account");
                }

                var tieneAcceso = await _universidadServices.UsuarioPuedeAccederCursoAsync(
                    usuarioId.Value, cursoId, empresaId.Value);

                if (!tieneAcceso)
                {
                    TempData["Error"] = "No tienes acceso a este curso";
                    return RedirectToAction("MisCursos");
                }

                var misCursos = await _universidadServices.ObtenerMisCursosAsync(usuarioId.Value, empresaId.Value);
                var miCurso = misCursos.FirstOrDefault(c => c.CursoID == cursoId);

                if (miCurso == null)
                {
                    return NotFound();
                }

                var viewModel = new DetalleMiCursoViewModel
                {
                    Curso = miCurso,
                    SubCursos = await _universidadServices.GetSubCursosPorCursoAsync(cursoId, usuarioId.Value, empresaId.Value),
                };

                viewModel.PuedeGenerarCertificado = viewModel.TodosLosSubCursosCompletados &&
                                                    viewModel.EvaluacionesAprobadas >= viewModel.SubCursosConEvaluacion;

                // Bitácora: ver mi curso (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER_DETALLE",
                        mensaje: $"Usuario abrió DetalleMiCurso {cursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: cursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar detalle del curso {CursoId}", cursoId);
                TempData["Error"] = "Error al cargar los detalles del curso";
                return RedirectToAction("MisCursos");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerProgresoCurso(int cursoId)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                var misCursos = await _universidadServices.ObtenerMisCursosAsync(usuarioId.Value, empresaId.Value);
                var curso = misCursos.FirstOrDefault(c => c.CursoID == cursoId);

                if (curso == null)
                {
                    return Json(new { success = false, message = "Curso no encontrado" });
                }

                return Json(new
                {
                    success = true,
                    progreso = curso.Progreso,
                    estado = curso.Estado,
                    subcursosCompletados = curso.SubCursosCompletados,
                    totalSubcursos = curso.TotalSubCursos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener progreso del curso {CursoId}", cursoId);
                return Json(new { success = false, message = "Error interno" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> IniciarCurso(int cursoId)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                var tieneAcceso = await _universidadServices.UsuarioPuedeAccederCursoAsync(
                    usuarioId.Value, cursoId, empresaId.Value);

                if (!tieneAcceso)
                {
                    return Json(new { success = false, message = "No tienes acceso a este curso" });
                }

                var subCursos = await _universidadServices.GetSubCursosPorCursoAsync(
                    cursoId, usuarioId.Value, empresaId.Value);

                var primerSubCurso = subCursos.OrderBy(s => s.Orden).FirstOrDefault();

                if (primerSubCurso == null)
                {
                    return Json(new { success = false, message = "Este curso no tiene contenido disponible" });
                }

                // Bitácora: iniciar curso (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "INICIAR_CURSO",
                        mensaje: $"Usuario inició curso {cursoId}",
                        modulo: "UNIVERSIDAD",
                        entidad: "Curso",
                        entidadId: cursoId.ToString(),
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return Json(new
                {
                    success = true,
                    redirectUrl = Url.Action("VerVideo", "Universidad", new { subCursoId = primerSubCurso.SubCursoID })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al iniciar curso {CursoId}", cursoId);
                return Json(new { success = false, message = "Error al iniciar el curso" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DashboardUsuario()
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    return RedirectToAction("Login", "Account");
                }

                var estadisticas = await _universidadServices.ObtenerEstadisticasProgresoUsuarioAsync(
                    usuarioId.Value, empresaId.Value);

                var misCursos = await _universidadServices.ObtenerMisCursosAsync(usuarioId.Value, empresaId.Value);
                var cursosRecientes = misCursos
                    .OrderByDescending(c => c.FechaInicio ?? c.FechaAsignacion)
                    .Take(5)
                    .ToList();

                var cursosProximosVencer = misCursos
                    .Where(c => c.FechaLimite.HasValue && c.DiasRestantes <= 7 && c.DiasRestantes > 0)
                    .OrderBy(c => c.DiasRestantes)
                    .Take(5)
                    .ToList();

                var viewModel = new DashboardUsuarioViewModel
                {
                    Estadisticas = estadisticas,
                    CursosRecientes = cursosRecientes,
                    CursosProximosVencer = cursosProximosVencer,
                    CertificadosDisponibles = await _universidadServices.ObtenerCertificadosDisponiblesAsync(
                        usuarioId.Value, empresaId.Value)
                };

                // Bitácora: ver dashboard usuario (info)
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: empresaId,
                        accion: "VER",
                        mensaje: "Usuario abrió DashboardUsuario",
                        modulo: "UNIVERSIDAD",
                        entidad: "Dashboard",
                        entidadId: null,
                        resultado: "OK",
                        severidad: 1,
                        solicitudId: sol,
                        ip: ip,
                       AgenteUsuario: ag
                    );
                }
                catch { }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar dashboard del usuario");
                TempData["Error"] = "Error al cargar el dashboard";
                return RedirectToAction("Index");
            }
        }

        // =====================================================
        // ELIMINAR SUBCURSO
        // =====================================================
        [HttpPost]
       // [ValidateAntiForgeryToken]
        [AutorizarAccion("Eliminar curso", "Eliminar")]
        public async Task<IActionResult> EliminarSubCurso(int id, string? motivo = null)
        {
            try
            {
                var (idUsuario, idEmpresa) = LeerIdsSesion();

                var result = await _universidadServices.EliminarSubCursoAsync(
                    id, idUsuario ?? 0, motivo
                );

                bool ok;
                string msg;
                switch (result)
                {
                    case SoftDeleteResult.Success:
                        ok = true; msg = "Subcurso eliminado correctamente."; break;
                    case SoftDeleteResult.AlreadyInactive:
                        ok = true; msg = "El subcurso ya estaba inactivo."; break;
                    case SoftDeleteResult.NotFound:
                        ok = false; msg = "Subcurso no encontrado."; break;
                    default:
                        ok = false; msg = "Error al eliminar el subcurso."; break;
                }

                // Bitácora
                try
                {
                    var (sol, ip, ag) = LeerCtxMiddleware();
                    await _bitacora.RegistrarAsync(
                        idUsuario: idUsuario,
                        idEmpresa: idEmpresa,
                        accion: "ELIMINAR",
                        mensaje: $"{msg} Motivo: {motivo ?? "N/D"}",
                        modulo: "UNIVERSIDAD",
                        entidad: "SubCurso",
                        entidadId: id.ToString(),
                        resultado: ok ? "OK" : "ERROR",
                        severidad: ok ? (byte)4 : (byte)3,
                        solicitudId: sol,
                        ip: ip,
                        AgenteUsuario: ag
                    );
                }
                catch { }

                return Json(new { success = ok, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar subcurso {SubCursoId}", id);
                return Json(new { success = false, message = "Error al eliminar" });
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Eliminar curso", "Eliminar")]
        public async Task<IActionResult> EliminarCurso(int id, string? motivo = null)
        {
            var (idUsuario, idEmpresa) = LeerIdsSesion();

            var result = await _universidadServices.EliminarCursoAsync(id, idUsuario ?? 0, motivo);

            bool ok; string msg;
            switch (result)
            {
                case SoftDeleteResult.Success: ok = true; msg = "Curso eliminado correctamente."; break;
                case SoftDeleteResult.AlreadyInactive: ok = true; msg = "El curso ya estaba inactivo."; break;
                case SoftDeleteResult.NotFound: ok = false; msg = "Curso no encontrado."; break;
                default: ok = false; msg = "Error al eliminar el curso."; break;
            }

            // Agregar bitacora
            return Json(new { success = ok, message = msg });
        }


    }
}
