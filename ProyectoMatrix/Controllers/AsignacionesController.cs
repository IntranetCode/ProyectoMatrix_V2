// =====================================================
// ARCHIVO: Controllers/AsignacionesController.cs
// PROPÓSITO: Controlador para asignación masiva de cursos
// =====================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Seguridad;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class AsignacionesController : Controller
    {
        private readonly UniversidadServices _universidadServices;
        private readonly ILogger<AsignacionesController> _logger;
        private readonly ServicioNotificaciones _notif;
        private readonly IServicioAcceso _acceso;

        public AsignacionesController(
            UniversidadServices universidadServices,
            ILogger<AsignacionesController> logger,
            ServicioNotificaciones notif,
            IServicioAcceso acceso)
        {
            _universidadServices = universidadServices;
            _logger = logger;
            _notif = notif;
            _acceso = acceso;
        }

        // =====================================================
        // ASIGNACIÓN MASIVA - GET
        // =====================================================
        [HttpGet]
        [AutorizarAccion("Ver cursos", "Ver")] // puerta neutral
        public async Task<IActionResult> AsignacionMasiva()
        {
            try
            {
                var uid = HttpContext.Session.GetInt32("UsuarioID") ?? 0;

                // OR de gestión desde BD (como acordamos: si puede gestionar, puede asignar)
                var tCrear = _acceso.TienePermisoAsync(uid, "Crear curso", "Crear");
                var tEditar = _acceso.TienePermisoAsync(uid, "Editar curso", "Editar");
                var tEliminar = _acceso.TienePermisoAsync(uid, "Eliminar curso", "Eliminar");
                await Task.WhenAll(tCrear, tEditar, tEliminar);

                var puedeGestionar = tCrear.Result || tEditar.Result || tEliminar.Result;
                if (!puedeGestionar)
                {
                    TempData["Error"] = "No tiene permisos para asignar cursos.";
                    return RedirectToAction("Index", "Universidad");
                }

                var viewModel = new AsignacionMasivaViewModel
                {
                    Cursos = await _universidadServices.GetCursosActivosParaAsignacionAsync(),
                    Empresas = await _universidadServices.GetEmpresasActivasAsync()
                };

                return View("~/Views/Universidad/AsignacionMasiva.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar vista de asignación masiva");
                TempData["Error"] = "Error al cargar la página de asignación.";
                return RedirectToAction("Index", "Universidad");
            }
        }


        // =====================================================
        // OBTENER DEPARTAMENTOS POR EMPRESA - AJAX
        // =====================================================
        [HttpGet]
        public async Task<JsonResult> ObtenerDepartamentosPorEmpresa(int idEmpresa)
        {
            try
            {
                var departamentos = await _universidadServices.GetDepartamentosPorEmpresaAsync(idEmpresa);
                return Json(new { success = true, departamentos = departamentos });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener departamentos de empresa {EmpresaId}", idEmpresa);
                return Json(new { success = false, message = "Error al obtener departamentos" });
            }
        }

        // =====================================================
        // OBTENER USUARIOS FILTRADOS - AJAX
        // =====================================================
        [HttpPost]
        public async Task<JsonResult> ObtenerUsuariosPorCriterio([FromBody] FiltroUsuariosRequest request)
        {
            try
            {
                var usuarios = new List<UsuarioAsignacionViewModel>();

                switch (request.TipoFiltro?.ToLower())
                {
                    case "todos":
                        usuarios = await _universidadServices.GetTodosLosUsuariosActivosAsync(request.IdCurso);
                        break;
                    case "empresa":
                        if (request.IdEmpresa.HasValue)
                            usuarios = await _universidadServices.GetUsuariosPorEmpresaAsync(request.IdEmpresa.Value, request.IdCurso);
                        break;
                    case "departamento":
                        if (request.IdDepartamento.HasValue)
                            usuarios = await _universidadServices.GetUsuariosPorDepartamentoAsync(request.IdDepartamento.Value, request.IdCurso);
                        break;
                }

                return Json(new { success = true, usuarios = usuarios });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener usuarios por criterio");
                return Json(new { success = false, message = "Error al obtener usuarios" });
            }
        }

        // =====================================================
        // PROCESAR ASIGNACIÓN MASIVA - AJAX
        // =====================================================
        [HttpPost]
        public async Task<JsonResult> ProcesarAsignacionMasiva([FromBody] AsignacionMasivaRequest request)
        {
            try
            {
                var usuarioCreador = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                               HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                if (!usuarioCreador.HasValue)
                {
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                if (request.UsuariosSeleccionados == null || !request.UsuariosSeleccionados.Any())
                {
                    return Json(new { success = false, message = "Debe seleccionar al menos un usuario" });
                }

                var resultado = await _universidadServices.AsignarCursoMasivoAsync(
                    request.IdCurso,
                    request.UsuariosSeleccionados,
                    usuarioCreador.Value,
                    request.FechaLimite,
                    request.Observaciones
                );


                if (resultado.Exito)
                {

                    var nombreCurso = "Curso";
                    foreach (var uid in request.UsuariosSeleccionados.Distinct())
                    {
                        await _notif.EmitirUsuario(
                            "CursoAsignado",
                            nombreCurso,
                            "Se te asigno un nuevo curso",
                            request.IdCurso,
                            "Cursos",
                            uid
                            );
                    }


                    return Json(new
                    {
                        success = true,
                        message = $"Curso asignado exitosamente a {resultado.UsuariosAsignados} usuarios.",
                        usuariosAsignados = resultado.UsuariosAsignados,
                        usuariosOmitidos = resultado.UsuariosOmitidos
                    });
                }
                else
                {
                    return Json(new { success = false, message = resultado.Mensaje });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar asignación masiva");
                return Json(new { success = false, message = "Error interno del servidor" });
            }
        }

        // =====================================================
        // VER ASIGNACIONES RECIENTES
        // =====================================================
        [HttpGet]
        [AutorizarAccion("Ver cursos", "Ver")]
        public async Task<IActionResult> VerAsignaciones()
        {
            try
            {
                var uid = HttpContext.Session.GetInt32("UsuarioID") ?? 0;

                var tCrear = _acceso.TienePermisoAsync(uid, "Crear curso", "Crear");
                var tEditar = _acceso.TienePermisoAsync(uid, "Editar curso", "Editar");
                var tEliminar = _acceso.TienePermisoAsync(uid, "Eliminar curso", "Eliminar");
                await Task.WhenAll(tCrear, tEditar, tEliminar);

                var puedeGestionar = tCrear.Result || tEditar.Result || tEliminar.Result;
                if (!puedeGestionar)
                {
                    TempData["Error"] = "No tiene permisos para ver asignaciones.";
                    return RedirectToAction("Index", "Universidad");
                }

                var asignaciones = await _universidadServices.GetAsignacionesRecientesAsync();
                return View(asignaciones);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar asignaciones recientes");
                TempData["Error"] = "Error al cargar las asignaciones.";
                return RedirectToAction("Index", "Universidad");
            }
        }

    }

    // =====================================================
    // MODELOS DE REQUEST
    // =====================================================
    public class FiltroUsuariosRequest
    {
        public string? TipoFiltro { get; set; } // "todos", "empresa", "departamento"
        public int? IdEmpresa { get; set; }
        public int? IdDepartamento { get; set; }
        public int IdCurso { get; set; } // Para verificar si ya lo tienen asignado
    }

    public class AsignacionMasivaRequest
    {
        public int IdCurso { get; set; }
        public List<int> UsuariosSeleccionados { get; set; } = new List<int>();
        public DateTime? FechaLimite { get; set; }
        public string? Observaciones { get; set; }
    }
}