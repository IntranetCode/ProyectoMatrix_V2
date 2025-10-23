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
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='UTF-8'>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f4f9; margin: 0; padding: 20px; }}
                .container {{ max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }}
                .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px 20px; text-align: center; }}
                .header h1 {{ margin: 0; font-size: 24px; font-weight: 600; }}
                .content {{ padding: 30px 25px; }}
                .greeting {{ font-size: 18px; color: #333; margin-bottom: 15px; }}
                .message {{ font-size: 16px; color: #555; line-height: 1.6; margin-bottom: 20px; }}
                .course-box {{ background-color: #f8f9fa; border-left: 4px solid #667eea; padding: 15px; margin: 20px 0; border-radius: 4px; }}
                .course-name {{ font-size: 18px; font-weight: 600; color: #333; margin-bottom: 5px; }}
                .course-id {{ font-size: 14px; color: #888; }}
                .cta {{ text-align: center; margin: 30px 0; }}
                .button {{ display: inline-block; padding: 14px 32px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; text-decoration: none; border-radius: 6px; font-weight: 600; transition: transform 0.2s; }}
                .button:hover {{ transform: translateY(-2px); }}
                .footer {{ background-color: #f8f9fa; padding: 20px; text-align: center; font-size: 13px; color: #888; }}
                .emoji {{ font-size: 22px; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1><span class='emoji'>🎓</span> Nuevo Curso Asignado</h1>
                </div>
                <div class='content'>
                    <p class='greeting'>¡Hola!</p>
                    <p class='message'>
                        Esperamos que te encuentres muy bien. Nos complace informarte que se te ha asignado un nuevo curso 
                        en nuestra plataforma de capacitación.
                    </p>
                    <div class='course-box'>
                        <div class='course-name'>{System.Net.WebUtility.HtmlEncode(nombreCurso)}</div>
                        <div class='course-id'>ID: {request.IdCurso}</div>
                    </div>
                    <p class='message'>
                        Este curso ha sido seleccionado especialmente para fortalecer tus habilidades y conocimientos. 
                        Te invitamos a comenzar cuanto antes para aprovechar al máximo esta oportunidad de aprendizaje.
                    </p>
                    {(request.FechaLimite.HasValue ? $@"
                    <p class='message' style='color: #e74c3c;'>
                        <strong>📅 Fecha límite:</strong> {request.FechaLimite.Value:dd/MM/yyyy}
                    </p>" : "")}
                </div>
                <div class='footer'>
                    <p>Asignado el {DateTime.Now:dd/MM/yyyy} a las {DateTime.Now:HH:mm}</p>
                    <p>Este es un mensaje automático. Por favor no responder a este correo.</p>
                </div>
            </div>
        </body>
        </html>";


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