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
using System.Linq;
using System.Collections.Generic;


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
                        {
                            // Trae TODAS las empresas activas
                            var todasEmpresas = await _universidadServices.GetEmpresasActivasAsync();

                            var acumulado = new List<UsuarioAsignacionViewModel>();

                            foreach (var emp in todasEmpresas)
                            {
                                // En tu vista, la empresa tiene propiedad "Id" (no EmpresaID)
                                var lote = await _universidadServices.GetUsuariosPorEmpresaAsync(emp.Id, request.IdCurso);
                                if (lote != null && lote.Count > 0)
                                    acumulado.AddRange(lote);
                            }

                            // Evita duplicados por "Id" (no UsuarioId)
                            usuarios = acumulado
                                .GroupBy(u => u.Id)
                                .Select(g => g.First())
                                .ToList();

                            break;
                        }


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
        // MÉTODO: ProcesarAsignacionMasiva
        // ✅ CON ENVÍO DE CORREO GARANTIZADO Y LOGGING EXHAUSTIVO
        // =====================================================

        [HttpPost]
        public async Task<JsonResult> ProcesarAsignacionMasiva([FromBody] AsignacionMasivaRequest request)
        {
            try
            {
                var usuarioCreador = HttpContext.Session.GetInt32("UsuarioID");
                var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                                HttpContext.Session.GetInt32("EmpresaID") ?? 1;

                _logger.LogInformation("=== INICIO ProcesarAsignacionMasiva ===");
                _logger.LogInformation("UsuarioCreador: {Uid}, CursoID: {CursoId}, Usuarios: {Count}",
                    usuarioCreador, request.IdCurso, request.UsuariosSeleccionados?.Count() ?? 0);

                if (!usuarioCreador.HasValue)
                {
                    _logger.LogWarning("Sesión expirada - UsuarioID no encontrado");
                    return Json(new { success = false, message = "Sesión expirada" });
                }

                if (request.UsuariosSeleccionados == null || !request.UsuariosSeleccionados.Any())
                {
                    _logger.LogWarning("No se seleccionaron usuarios");
                    return Json(new { success = false, message = "Debe seleccionar al menos un usuario" });
                }

                // ✅ ASIGNACIÓN EN BD
                var resultado = await _universidadServices.AsignarCursoMasivoAsync(
                    request.IdCurso,
                    request.UsuariosSeleccionados,
                    usuarioCreador.Value,
                    request.FechaLimite,
                    request.Observaciones
                );

                if (!resultado.Exito)
                {
                    _logger.LogError("AsignarCursoMasivoAsync falló: {Mensaje}", resultado.Mensaje);
                    return Json(new { success = false, message = resultado.Mensaje });
                }

                _logger.LogInformation("✅ Asignación BD exitosa: {Asignados} usuarios, {Omitidos} omitidos",
                    resultado.UsuariosAsignados, resultado.UsuariosOmitidos);

                // ✅ NOTIFICACIÓN IN-APP
                var nombreCurso = "Curso"; // TODO: obtener nombre real desde BD si lo necesitas
                foreach (var uid in request.UsuariosSeleccionados.Distinct())
                {
                    try
                    {
                        await _notif.EmitirUsuario(
                            "CursoAsignado",
                            nombreCurso,
                            "Se te asignó un nuevo curso",
                            request.IdCurso,
                            "Cursos",
                            uid
                        );
                    }
                    catch (Exception exNotif)
                    {
                        _logger.LogError(exNotif, "Error al emitir notificación in-app para UsuarioID={Uid}", uid);
                    }
                }

                _logger.LogInformation("✅ Notificaciones in-app emitidas");

                // ✅ ENVÍO DE CORREO (GARANTIZADO)
                var resultadoCorreo = new ServicioNotificaciones.ResultadoEnvio();
                try
                {
                    var asunto = $"[Nuevo curso asignado] {nombreCurso}";
                    var html = $@"
                <h2>{System.Net.WebUtility.HtmlEncode(nombreCurso)}</h2>
                <p>Se te ha asignado un nuevo curso.</p>
                <p style='color:#666'>ID Curso: {request.IdCurso} • {DateTime.Now:dd/MM/yyyy HH:mm}</p>";

                    _logger.LogInformation("Iniciando envío de correo a {Count} usuarios (UsuarioIDs)",
                        request.UsuariosSeleccionados.Distinct().Count());

                    resultadoCorreo = await _notif.EnviarCursosAUsuariosAsync(
                        request.UsuariosSeleccionados.Distinct(),
                        asunto,
                        html,
                        batchSize: 40
                    );

                    _logger.LogInformation("📧 Resultado correo: Encontrados={Enc}, Enviados={Env}, Filtrados={Filt}, Errores={Err}",
                        resultadoCorreo.Encontrados,
                        resultadoCorreo.Enviados,
                        resultadoCorreo.FiltradosPorCandados,
                        resultadoCorreo.Errores);

                    if (resultadoCorreo.Mensajes.Any())
                    {
                        foreach (var msg in resultadoCorreo.Mensajes)
                            _logger.LogWarning("Correo mensaje: {Msg}", msg);
                    }

                    if (resultadoCorreo.Enviados == 0 && resultadoCorreo.Errores == 0)
                    {
                        _logger.LogWarning("⚠️ NO SE ENVIÓ NINGÚN CORREO - Revisa candados: SoloPruebas/ListaBlanca/Habilitado");
                    }
                }
                catch (Exception exCorreo)
                {
                    _logger.LogError(exCorreo, "❌ ERROR CRÍTICO al enviar correos de asignación masiva (CursoId={IdCurso})", request.IdCurso);
                    // ⚠️ NO interrumpimos la respuesta si el correo falla - la asignación ya se hizo
                }

                _logger.LogInformation("=== FIN ProcesarAsignacionMasiva ===");

                // ✅ RESPUESTA CON INFO DE CORREO
                return Json(new
                {
                    success = true,
                    message = $"Curso asignado exitosamente a {resultado.UsuariosAsignados} usuarios.",
                    usuariosAsignados = resultado.UsuariosAsignados,
                    usuariosOmitidos = resultado.UsuariosOmitidos,
                    correo = new
                    {
                        enviados = resultadoCorreo.Enviados,
                        filtrados = resultadoCorreo.FiltradosPorCandados,
                        errores = resultadoCorreo.Errores,
                        mensajes = resultadoCorreo.Mensajes
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR CRÍTICO en ProcesarAsignacionMasiva");
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