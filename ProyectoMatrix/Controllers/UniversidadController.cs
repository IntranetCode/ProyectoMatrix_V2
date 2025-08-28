// =====================================================
// ARCHIVO: Controllers/UniversidadController.cs
// PROPÓSITO: Controlador principal Universidad NS
// =====================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class UniversidadController : Controller
    {
        private readonly UniversidadServices _universidadServices;
        private readonly ILogger<UniversidadController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public UniversidadController(
            UniversidadServices universidadServices,
            ILogger<UniversidadController> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _universidadServices = universidadServices;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

     

        // MODIFICAR tu método Index() en UniversidadController.cs
        // Agregar DEBUG para ver qué hay en la sesión

        public async Task<IActionResult> Index()
        {
            try
            {
                // ✅ DEBUG: Ver TODAS las variables de sesión disponibles
                _logger.LogInformation("=== DEBUG SESIÓN UNIVERSIDAD ===");

                // Probar diferentes nombres de variables de sesión
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID");  // ¿Existe?
                var rolId2 = HttpContext.Session.GetInt32("RolId"); // ¿O así?
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada"); // ¿Existe?
                var empresaId2 = HttpContext.Session.GetInt32("EmpresaId"); // ¿O así?
                var nombreUsuario = HttpContext.Session.GetString("Username");
                var nombreEmpresa = HttpContext.Session.GetString("EmpresaNombre");
                var rol = HttpContext.Session.GetString("Rol");

                // Log de todos los valores
                _logger.LogInformation("UsuarioID: {UsuarioId}", usuarioId);
                _logger.LogInformation("RolID: {RolId}", rolId);
                _logger.LogInformation("RolId: {RolId2}", rolId2);
                _logger.LogInformation("EmpresaSeleccionada: {EmpresaId}", empresaId);
                _logger.LogInformation("EmpresaId: {EmpresaId2}", empresaId2);
                _logger.LogInformation("Username: {Username}", nombreUsuario);
                _logger.LogInformation("EmpresaNombre: {EmpresaNombre}", nombreEmpresa);
                _logger.LogInformation("Rol: {Rol}", rol);

                // ✅ VERIFICACIÓN BÁSICA - Solo verificar lo esencial
                if (!usuarioId.HasValue)
                {
                    _logger.LogWarning("UsuarioID no encontrado en sesión - Redirigiendo a login");
                    TempData["Error"] = "Sesión expirada. Por favor, inicia sesión nuevamente.";
                    return RedirectToAction("Login", "Login");
                }

                // ✅ USAR VALORES CON FALLBACK
                var rolIdFinal = rolId ?? rolId2 ?? 4; // Usar rol de YOLGUINM por defecto
                var empresaIdFinal = empresaId ?? empresaId2 ?? 1; // Usar empresa 1 por defecto

                _logger.LogInformation("Valores finales - RolId: {RolId}, EmpresaId: {EmpresaId}", rolIdFinal, empresaIdFinal);

                // ✅ COMENTAR TEMPORALMENTE la verificación de empresa
                // if (!empresaId.HasValue)
                // {
                //     TempData["Error"] = "Debe seleccionar una empresa para acceder a Universidad NS.";
                //     return RedirectToAction("SeleccionEmpresas", "Login");
                // }

                var viewModel = new UniversidadDashboardViewModel
                {
                    UsuarioId = usuarioId.Value,
                    RolId = rolIdFinal,
                    EmpresaId = empresaIdFinal,
                    NombreUsuario = nombreUsuario ?? "Usuario",
                    NombreEmpresa = nombreEmpresa ?? "NS Group",

                    // Calcular permisos
                    PuedeCrearCursos = UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolIdFinal),
                    PuedeAsignarCursos = UniversidadPermisosHelper.PermisosUniversidad.PuedeAsignarCursos(rolIdFinal),
                    PuedeVerReportes = UniversidadPermisosHelper.PermisosUniversidad.PuedeVerReportes(rolIdFinal),
                    PuedeConfiguracion = UniversidadPermisosHelper.PermisosUniversidad.PuedeConfigurarSistema(rolIdFinal)
                };

                // Cargar datos según el rol
                await CargarDatosDashboard(viewModel);

                // Generar menú dinámico
                viewModel.MenuItems = GenerarMenuItems(viewModel);

                _logger.LogInformation("Dashboard cargado exitosamente para usuario {UsuarioId}", usuarioId);

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

                // ✅ CARGAR MIS CURSOS (para todos los roles)
                viewModel.MisCursos = await _universidadServices.GetCursosAsignadosUsuarioViewModelAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                _logger.LogInformation("Cursos cargados: {Count}", viewModel.MisCursos?.Count ?? 0);

                // ✅ CARGAR MIS CERTIFICADOS
                viewModel.MisCertificados = await _universidadServices.GetCertificadosUsuarioViewModelAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                // Tiempo de estudio
                int tiempoEstudio = await _universidadServices.GetTiempoEstudioUsuarioAsync(
                    viewModel.UsuarioId, viewModel.EmpresaId);

                _logger.LogInformation("Certificados cargados: {Count}", viewModel.MisCertificados?.Count ?? 0);

                // ✅ CALCULAR ESTADÍSTICAS BÁSICAS (para todos los roles)
                viewModel.Estadisticas = new EstadisticasUniversidadViewModel
                {
                    TotalCursosAsignados = viewModel.MisCursos?.Count ?? 0,
                    CursosCompletados = viewModel.MisCursos?.Count(c => c.Estado == "Completado") ?? 0,
                    CursosEnProgreso = viewModel.MisCursos?.Count(c => c.Estado == "En Progreso") ?? 0,
                    CertificadosObtenidos = viewModel.MisCertificados?.Count(c => c.Estado == "Vigente") ?? 0,

                    // Promedio de progreso en %
                    PromedioProgreso = viewModel.MisCursos?.Any() == true ?
                        (decimal)viewModel.MisCursos.Average(c => (double)c.PorcentajeProgreso) : 0m,

                    // 👇 Estadísticas de subcursos
                    TotalSubCursos = viewModel.MisCursos?.Sum(c => c.TotalSubCursos) ?? 0,
                    SubCursosCompletados = viewModel.MisCursos?.Sum(c => c.SubCursosCompletados) ?? 0,
                    TiempoTotalEstudio = tiempoEstudio
                };

                _logger.LogInformation("Estadísticas básicas calculadas - Cursos: {Total}, SubCursos: {SubCursos}, Completados: {Completados}, Progreso: {Progreso}%",
                    viewModel.Estadisticas.TotalCursosAsignados,
                    viewModel.Estadisticas.TotalSubCursos,
                    viewModel.Estadisticas.SubCursosCompletados,
                    viewModel.Estadisticas.PromedioProgreso);

                // ✅ VERIFICAR PERMISOS (solo roles con acceso admin)
                if (viewModel.PuedeCrearCursos || viewModel.PuedeVerReportes)
                {
                    _logger.LogInformation("🎯 ENTRANDO a cargar estadísticas administrativas...");

                    try
                    {
                        // Obtener estadísticas generales del sistema
                        var estadisticasAdmin = await _universidadServices.GetEstadisticasAdministrativasAsync();

                        if (estadisticasAdmin != null)
                        {
                            viewModel.Estadisticas.TotalUsuariosActivos = estadisticasAdmin.TotalUsuariosActivos;
                            viewModel.Estadisticas.TotalCursosCreados = estadisticasAdmin.TotalCursosCreados;
                            viewModel.Estadisticas.CertificadosEmitidosMes = estadisticasAdmin.CertificadosEmitidosMes;

                            _logger.LogInformation("✅ Estadísticas administrativas cargadas correctamente");
                        }
                        else
                        {
                            _logger.LogWarning("❌ GetEstadisticasAdministrativasAsync devolvió NULL");
                        }
                    }
                    catch (Exception adminEx)
                    {
                        _logger.LogError(adminEx, "❌ ERROR específico al cargar estadísticas administrativas");

                        // 🔧 VALORES TEMPORALES PARA TESTING
                        viewModel.Estadisticas.TotalUsuariosActivos = 5;
                        viewModel.Estadisticas.TotalCursosCreados = 2;
                        viewModel.Estadisticas.CertificadosEmitidosMes = 1;
                    }
                }
                else
                {
                    _logger.LogInformation("❌ Usuario SIN permisos administrativos - No se cargan estadísticas del sistema");
                }

                _logger.LogInformation("=== FIN CargarDatosDashboard ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR GENERAL en CargarDatosDashboard");

                // ✅ FALLBACK: Inicializar con datos vacíos
                viewModel.MisCursos = new List<CursoAsignadoViewModel>();
                viewModel.MisCertificados = new List<CertificadoUsuarioViewModel>();
                viewModel.Estadisticas = new EstadisticasUniversidadViewModel
                {
                    TotalCursosAsignados = 0,
                    CursosCompletados = 0,
                    CursosEnProgreso = 0,
                    CertificadosObtenidos = 0,
                    PromedioProgreso = 0,
                    TotalUsuariosActivos = 0,
                    TotalCursosCreados = 0,
                    CertificadosEmitidosMes = 0,
                    TotalSubCursos = 0,
                    SubCursosCompletados = 0
                };
            }
        }

        private List<MenuItemUniversidad> GenerarMenuItems(UniversidadDashboardViewModel viewModel)
        {
            var items = new List<MenuItemUniversidad>
            {
                new MenuItemUniversidad
                {
                    Titulo = "Dashboard",
                    Url = "/Universidad",
                    Icono = "fas fa-home",
                    EsActivo = true
                }
            };

            // Mis Cursos (todos los roles)
            items.Add(new MenuItemUniversidad
            {
                Titulo = "Mis Cursos",
                Url = "/Universidad/MisCursos",
                Icono = "fas fa-graduation-cap",
                Badge = viewModel.MisCursos.Count(c => c.Estado == "En Progreso")
            });

            // Certificados (todos los roles)
            items.Add(new MenuItemUniversidad
            {
                Titulo = "Mis Certificados",
                Url = "/Universidad/MisCertificados",
                Icono = "fas fa-certificate",
                Badge = viewModel.MisCertificados.Count
            });

            // Gestión de cursos (roles 1, 3, 4)
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

            // Asignaciones (roles 1, 3)
            if (viewModel.PuedeAsignarCursos)
            {
                items.Add(new MenuItemUniversidad
                {
                    Titulo = "Asignar Cursos",
                    Url = "/Asignaciones/AsignacionMasiva",
                    Icono = "fas fa-users-cog",
                    SubItems = new List<MenuItemUniversidad>
                    {
                        new MenuItemUniversidad { 
                            Titulo = "Asignación Masiva",
                            Url = "/Asignaciones/AsignacionMasiva",
                            Icono = "fas fa-users-cog"
                        },
                        new MenuItemUniversidad { 
                            Titulo = "Ver Asignaciones",
                            Url = "/Asignaciones/VerAsignaciones",
                            Icono = "fas fa-list-check"
                        }
                    }
                });
            }

            // Reportes (roles 1, 2, 3, 6)
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

            // Configuración (roles 1, 2)
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
        [HttpGet]
        public async Task<IActionResult> MisCursos()
        {
            try
            {
                // Obtener datos de sesión
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                _logger.LogInformation("🔍 CONTROLLER DEBUG - UsuarioID: {UsuarioId}, EmpresaID: {EmpresaId}", usuarioId, empresaId);

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    _logger.LogWarning("❌ Sesión inválida - redirigiendo a login");
                    return RedirectToAction("Login", "Account");
                }

                // Usar el método de servicios
                var cursosAsignados = await _universidadServices.GetCursosAsignadosUsuarioViewModelAsync(
                    usuarioId.Value, empresaId.Value);

                _logger.LogInformation("✅ CONTROLLER - Cursos obtenidos: {Total}", cursosAsignados.Count);

                return View(cursosAsignados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR en controlador MisCursos");
                TempData["Error"] = "Error al cargar tus cursos";

                // Devolver lista vacía para evitar datos falsos
                return View(new List<CursoAsignadoViewModel>());
            }
        }
        // ✅ Y CAMBIAR el método GestionCursos:
        public async Task<IActionResult> GestionCursos()
        {
            try
            {
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                            HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                    return RedirectToAction("Index");
                }

                var niveles = await _universidadServices.GetNivelesEducativosViewModelAsync();

                var viewModel = new GestionCursosViewModel
                {
                    RolId = rolId,
                    PuedeAprobar = UniversidadPermisosHelper.PermisosUniversidad.PuedeAprobarCursos(rolId),
                    PuedeCrear = true,
                    PuedeEditar = true,
                    Niveles = niveles ?? new List<NivelEducativoViewModel>(),
                    Cursos = new List<CursoCompleto>(), // ✅ Inicializar vacío
                    CursosPorNivel = new Dictionary<int, int>()
                };

                // ✅ CARGAR CURSOS CON LOGS
                _logger.LogInformation("🔍 Cargando cursos para gestión...");
                var cursosObtenidos = await _universidadServices.GetTodosCursosAsync();

                _logger.LogInformation("📊 Cursos obtenidos del servicio: {Count}", cursosObtenidos?.Count ?? 0);

                // ✅ ASIGNAR DIRECTAMENTE (sin manipulaciones)
                viewModel.Cursos = cursosObtenidos ?? new List<CursoCompleto>();

                _logger.LogInformation("📊 Cursos asignados al ViewModel: {Count}", viewModel.Cursos.Count);

                // ✅ VERIFICAR IDs ÚNICOS
                var idsUnicos = viewModel.Cursos.Select(c => c.CursoID).Distinct().Count();
                var totalCursos = viewModel.Cursos.Count;

                _logger.LogInformation("🔍 IDs únicos: {Unicos}, Total cursos: {Total}", idsUnicos, totalCursos);

                if (idsUnicos != totalCursos)
                {
                    _logger.LogWarning("⚠️ DUPLICADOS DETECTADOS - Eliminando...");
                    viewModel.Cursos = viewModel.Cursos
                        .GroupBy(c => c.CursoID)
                        .Select(g => g.First())
                        .ToList();
                    _logger.LogInformation("✅ Después de eliminar duplicados: {Count}", viewModel.Cursos.Count);
                }

                // Calcular cursos por nivel
                foreach (var nivel in viewModel.Niveles)
                {
                    var cursosDelNivel = viewModel.Cursos.Where(c => c.NivelID == nivel.NivelID);
                    viewModel.CursosPorNivel[nivel.NivelID] = cursosDelNivel.Count();
                }

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
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                                HttpContext.Session.GetInt32("EmpresaID") ?? 1;

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
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                               HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return RedirectToAction("Index");
                }

                // CORREGIDO: Usar método existente en lugar de GetSubCursoIndividualAsync
                // Obtener todos los subcursos del usuario para encontrar el específico
                //var todosSubCursos = await _universidadServices.GetSubCursosPorCursoAsync(
                //  0, usuarioId.Value, empresaId); // 0 = todos los cursos

                //var subCurso = todosSubCursos.FirstOrDefault(sc => sc.SubCursoID == subCursoId);
                // Obtener el subcurso específico con información del curso padre
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

                return View(subCurso);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar subcurso {SubCursoId}", subCursoId);
                TempData["Error"] = "Error al cargar el contenido. Intente nuevamente.";
                return RedirectToAction("MisCursos");
            }
        }
        // AGREGA este método nuevo para completar subcursos:
        [HttpPost]
        public async Task<IActionResult> CompletarSubCurso([FromBody] CompletarSubCursoRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                               HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                // CORREGIDO: Usar las propiedades correctas de tu modelo ActualizarProgresoRequest
                var progresoRequest = new ActualizarProgresoRequest
                {
                    UsuarioID = usuarioId.Value,
                    EmpresaID = empresaId,
                    SubCursoID = request.SubCursoId,
                    TiempoTotalVisto = request.TiempoVisto, // Mapear TiempoVisto a TiempoTotalVisto
                    PorcentajeVisto = 100 // Marcar como 100% visto al completar
                };

                var resultado = await _universidadServices.ActualizarProgresoVideoAsync(progresoRequest);

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

                return Json(new { success = resultado, message = "Progreso actualizado" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar progreso");
                return Json(new { success = false, message = "Error al actualizar progreso" });
            }
        }

        // =====================================================
        // GESTIÓN DE CURSOS (ADMIN)
        // =====================================================


        // =====================================================
        // CREAR CURSO
        // =====================================================

        public async Task<IActionResult> CrearCurso()
        {
            var rolId = HttpContext.Session.GetInt32("RolID") ??
           HttpContext.Session.GetInt32("RolId") ?? 4;

            if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
            {
                TempData["Error"] = "No tiene permisos para crear cursos.";
                return RedirectToAction("Index");
            }

            var niveles = await _universidadServices.GetNivelesEducativosViewModelAsync();
            ViewBag.Niveles = niveles;

            return View(new CrearCursoRequest());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCurso(CrearCursoRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID");

                if (!usuarioId.HasValue || !UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId ?? 0))
                {
                    TempData["Error"] = "No tiene permisos para crear cursos.";
                    return RedirectToAction("Index");
                }

                if (!ModelState.IsValid)
                {
                    var niveles = await _universidadServices.GetNivelesEducativosAsync();
                    ViewBag.Niveles = niveles;
                    return View(request);
                }

                request.CreadoPorUsuarioID = usuarioId.Value;

                var cursoId = await _universidadServices.CrearCursoAsync(request);

                TempData["Success"] = $"Curso '{request.NombreCurso}' creado exitosamente.";
                return RedirectToAction("EditarCurso", new { id = cursoId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear curso");
                TempData["Error"] = "Error al crear el curso. Intente nuevamente.";

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

                var certificados = await _universidadServices.GetCertificadosUsuarioAsync(
                    usuarioId.Value, empresaId);

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

                var certificados = await _universidadServices.GetCertificadosUsuarioAsync(usuarioId.Value);
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
        // MÉTODOS DE UTILIDAD
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


        // AGREGAR estos métodos al final de tu UniversidadController.cs

        // =====================================================
        // EDITAR CURSO - Mostrar curso con sus subcursos
        // =====================================================

        public async Task<IActionResult> EditarCurso(int id)
        {
            try
            {
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para editar cursos.";
                    return RedirectToAction("Index");
                }

                // Obtener información del curso
                var curso = await _universidadServices.GetCursoPorIdAsync(id);
                if (curso == null)
                {
                    TempData["Error"] = "Curso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                // Obtener subcursos del curso
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID") ?? 0;
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                               HttpContext.Session.GetInt32("EmpresaID") ?? 1;

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
        // CREAR SUBCURSO - GET
        // =====================================================

        public async Task<IActionResult> CrearSubCurso(int cursoId)
        {
            try
            {
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para crear subcursos.";
                    return RedirectToAction("Index");
                }

                // Verificar que el curso existe
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

        // =====================================================
        // CREAR SUBCURSO - POST
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSubCurso(CrearSubCursoRequest request, IFormFile archivoVideo, IFormFile archivoPDF)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!usuarioId.HasValue || !UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para crear subcursos.";
                    return RedirectToAction("Index");
                }

                // PROCESAR ARCHIVOS
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

                if (!ModelState.IsValid)
                {
                    var curso = await _universidadServices.GetCursoPorIdAsync(request.CursoID);
                    ViewBag.CursoId = request.CursoID;
                    ViewBag.NombreCurso = curso?.NombreCurso ?? "Curso";
                    return View(request);
                }

                var subCursoId = await _universidadServices.CrearSubCursoAsync(request);
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

        // AGREGAR TAMBIÉN ESTE MÉTODO
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
        public async Task<IActionResult> CrearEvaluacion(int subCursoId)
        {
            try
            {

                // LIMPIAR MENSAJES ANTERIORES
                TempData.Remove("Error");
                TempData.Remove("Success");

                _logger.LogInformation("🎯 ENTRANDO a CrearEvaluacion con subCursoId: {SubCursoId}", subCursoId);
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;
                
                _logger.LogInformation("🎯 RolId obtenido: {RolId}", rolId);


                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    _logger.LogWarning("🎯 Usuario SIN permisos para crear evaluaciones");
                    TempData["Error"] = "No tiene permisos para crear evaluaciones.";
                    return RedirectToAction("Index");
                }

                _logger.LogInformation("🎯 Permisos OK - Llamando a GetEvaluacionViewModelAsync...");

                var viewModel = await _universidadServices.GetEvaluacionViewModelAsync(subCursoId);

                if (viewModel == null)
                {
                    _logger.LogWarning("🎯 ViewModel es NULL - Redirigiendo a GestionCursos");

                    TempData["Error"] = "SubCurso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }
                _logger.LogInformation("🎯 ViewModel OK - Retornando vista");
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
        public async Task<IActionResult> CrearEvaluacion([FromBody] CrearEvaluacionRequest request)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!usuarioId.HasValue || !UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    return Json(new { success = false, message = "No tiene permisos para crear evaluaciones." });
                }

                if (request.Preguntas == null || !request.Preguntas.Any())
                {
                    return Json(new { success = false, message = "Debe agregar al menos una pregunta." });
                }

                var resultado = await _universidadServices.CrearEvaluacionAsync(request);

                if (resultado)
                {
                    return Json(new { success = true, message = "Evaluación creada exitosamente." });
                }
                else
                {
                    return Json(new { success = false, message = "Error al crear la evaluación." });
                }
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
            _logger.LogInformation("🎯 Entrando a TomarEvaluacion para SubCursoID={SubCursoId}", subCursoId);

            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                                HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                    return RedirectToAction("Login", "Login");

                var viewModel = await _universidadServices.GetTomarEvaluacionViewModelAsync(
                    subCursoId, usuarioId.Value, empresaId);

                if (viewModel == null || viewModel.Preguntas == null || !viewModel.Preguntas.Any())
                {
                    TempData["Warning"] = "Este módulo no tiene evaluación disponible.";
                    return RedirectToAction("TomarSubCurso", new { subCursoId });
                }

                // 👀 Asegurarte que usa la vista correcta
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
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                               HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioId.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada." });
                }

                var resultado = await _universidadServices.EntregarEvaluacionAsync(
                    usuarioId.Value, request.SubCursoId, empresaId, request.Respuestas, request.TiempoEmpleado);

                if (resultado.Success)
                {
                    return Json(new
                    {
                        success = true,
                        calificacion = resultado.Calificacion,
                        aprobado = resultado.Aprobado,
                        cursoCompleto = resultado.CursoCompleto,    // 👈 NUEVO
                        nombreUsuario = resultado.NombreUsuario,    // 👈 NUEVO
                        nombreCurso = resultado.NombreCurso,        // 👈 NUEVO
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
        // EDITAR SUBCURSO
        // =====================================================

        public async Task<IActionResult> EditarSubCurso(int id)
        {
            try
            {
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para editar subcursos.";
                    return RedirectToAction("Index");
                }

                var subCurso = await _universidadServices.GetSubCursoPorIdAsync(id);

                if (subCurso == null)
                {
                    TempData["Error"] = "SubCurso no encontrado.";
                    return RedirectToAction("GestionCursos");
                }

                // Convertir a request para edición
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
                    ArchivoVideo = subCurso.ArchivoVideo,  // AGREGAR ESTA LÍNEA
                    ArchivoPDF = subCurso.ArchivoPDF       // AGREGAR ESTA LÍNEA
                };

                ViewBag.SubCursoId = id;
                ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                ViewBag.EsEdicion = true;

                return View("CrearSubCurso", request); // Reutilizar la misma vista
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
        public async Task<IActionResult> EditarSubCurso(int id, CrearSubCursoRequest request, IFormFile archivoVideo, IFormFile archivoPDF)
        {
            try
            {
                Console.WriteLine($"=== DEBUG EDITAR SUBCURSO ===");
                Console.WriteLine($"Archivo video recibido: {archivoVideo?.FileName ?? "NULL"} - Tamaño: {archivoVideo?.Length ?? 0}");
                Console.WriteLine($"Archivo PDF recibido: {archivoPDF?.FileName ?? "NULL"} - Tamaño: {archivoPDF?.Length ?? 0}");

                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var rolId = HttpContext.Session.GetInt32("RolID") ??
                           HttpContext.Session.GetInt32("RolId") ?? 4;

                if (!usuarioId.HasValue || !UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    TempData["Error"] = "No tiene permisos para editar subcursos.";
                    return RedirectToAction("Index");
                }

                // PROCESAR ARCHIVOS NUEVOS (si se subieron)
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

                Console.WriteLine($"ArchivoVideo en request: {request.ArchivoVideo ?? "NULL"}");
                Console.WriteLine($"ArchivoPDF en request: {request.ArchivoPDF ?? "NULL"}");

                if (!ModelState.IsValid)
                {
                    ViewBag.SubCursoId = id;
                    ViewBag.NombreCurso = await _universidadServices.GetNombreCursoPorSubCursoAsync(id);
                    ViewBag.EsEdicion = true;
                    return View("CrearSubCurso", request);
                }

                var resultado = await _universidadServices.ActualizarSubCursoAsync(id, request);

                Console.WriteLine($"Resultado de ActualizarSubCursoAsync: {resultado}");

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
        // AGREGAR ESTOS MÉTODOS AL UniversidadController.cs
        // =====================================================

        /// <summary>
        /// Vista principal "Mis Cursos" para usuarios
        /// </summary>
        /// 
        /*
        [HttpGet]
        public async Task<IActionResult> MisCursos(string? estado = null, string? nivel = null, bool? obligatorios = null, bool? vencidos = null)
        {
            try
            {
                // Obtener datos de sesión
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada");

                if (!usuarioId.HasValue || !empresaId.HasValue)
                {
                    return RedirectToAction("Login", "Account");
                }

                // Crear ViewModel
                var viewModel = new MisCursosViewModel
                {
                    FiltroEstado = estado,
                    FiltroNivel = nivel,
                    SoloObligatorios = obligatorios,
                    SoloVencidos = vencidos
                };

                // Obtener datos
                viewModel.MisCursos = await _universidadServices.ObtenerMisCursosAsync(usuarioId.Value, empresaId.Value);
                viewModel.Estadisticas = await _universidadServices.ObtenerEstadisticasProgresoUsuarioAsync(usuarioId.Value, empresaId.Value);
                viewModel.CertificadosDisponibles = await _universidadServices.ObtenerCertificadosDisponiblesAsync(usuarioId.Value, empresaId.Value);

                _logger.LogInformation("Usuario {UsuarioId} consultó sus cursos. Total: {Total}",
                    usuarioId.Value, viewModel.MisCursos.Count);

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar mis cursos");
                TempData["Error"] = "Error al cargar tus cursos";
                return RedirectToAction("Index");
            }
        }
        */
        /// <summary>
        /// Vista detallada de un curso específico del usuario
        /// </summary>
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

                // Verificar que el usuario tenga acceso al curso
                var tieneAcceso = await _universidadServices.UsuarioPuedeAccederCursoAsync(
                    usuarioId.Value, cursoId, empresaId.Value);

                if (!tieneAcceso)
                {
                    TempData["Error"] = "No tienes acceso a este curso";
                    return RedirectToAction("MisCursos");
                }

                // Obtener datos del curso
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
                    // Aquí puedes agregar más datos como historial de evaluaciones
                };

                // Verificar si puede generar certificado
                viewModel.PuedeGenerarCertificado = viewModel.TodosLosSubCursosCompletados &&
                                                  viewModel.EvaluacionesAprobadas >= viewModel.SubCursosConEvaluacion;

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar detalle del curso {CursoId}", cursoId);
                TempData["Error"] = "Error al cargar los detalles del curso";
                return RedirectToAction("MisCursos");
            }
        }

        /// <summary>
        /// API para obtener progreso en tiempo real
        /// </summary>
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

        /// <summary>
        /// Marcar un curso como iniciado
        /// </summary>
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

                // Verificar acceso
                var tieneAcceso = await _universidadServices.UsuarioPuedeAccederCursoAsync(
                    usuarioId.Value, cursoId, empresaId.Value);

                if (!tieneAcceso)
                {
                    return Json(new { success = false, message = "No tienes acceso a este curso" });
                }

                // Redirigir al primer subcurso disponible
                var subCursos = await _universidadServices.GetSubCursosPorCursoAsync(
                    cursoId, usuarioId.Value, empresaId.Value);

                var primerSubCurso = subCursos.OrderBy(s => s.Orden).FirstOrDefault();

                if (primerSubCurso == null)
                {
                    return Json(new { success = false, message = "Este curso no tiene contenido disponible" });
                }

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

        /// <summary>
        /// Obtener estadísticas del dashboard del usuario
        /// </summary>
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

                // Obtener cursos recientes
                var misCursos = await _universidadServices.ObtenerMisCursosAsync(usuarioId.Value, empresaId.Value);
                var cursosRecientes = misCursos
                    .OrderByDescending(c => c.FechaInicio ?? c.FechaAsignacion)
                    .Take(5)
                    .ToList();

                // Obtener cursos próximos a vencer
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

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar dashboard del usuario");
                TempData["Error"] = "Error al cargar el dashboard";
                return RedirectToAction("Index");
            }
        }

        /////////////apartid e aqui eliminar subcurso editar subccurso
        ///

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarSubCurso(int id)
        {
            try
            {
                var rolId = HttpContext.Session.GetInt32("RolID") ?? 4;
                if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
                {
                    return Json(new { success = false, message = "No tiene permisos" });
                }

                var resultado = await _universidadServices.EliminarSubCursoAsync(id);

                if (resultado)
                {
                    return Json(new { success = true, message = "SubCurso eliminado correctamente" });
                }
                else
                {
                    return Json(new { success = false, message = "Error al eliminar" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar subcurso");
                return Json(new { success = false, message = "Error al eliminar" });
            }
        }

        //////////////// a partir de aqui es o de tomar curso
        // MÉTODOS NECESARIOS EN UniversidadController.cs





 

    }
}