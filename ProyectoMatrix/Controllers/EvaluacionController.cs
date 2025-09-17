using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;

public class EvaluacionController : Controller
{
    private readonly UniversidadServices _universidadServices;
    private readonly ILogger<EvaluacionController> _logger;
    private readonly BitacoraService _bitacoraService;

    public EvaluacionController(
        UniversidadServices universidadServices,
        ILogger<EvaluacionController> logger,
        BitacoraService bitacoraService)
    {
        _universidadServices = universidadServices;
        _logger = logger;
        _bitacoraService = bitacoraService;
    }

    // =====================================================
    // CREAR/EDITAR EVALUACIÓN
    // =====================================================

    public async Task<IActionResult> CrearEvaluacion(int subCursoId)
    {
        try
        {
            var rolId = HttpContext.Session.GetInt32("RolID") ??
                       HttpContext.Session.GetInt32("RolId") ?? 4;

            if (!UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
            {
                TempData["Error"] = "No tiene permisos para crear evaluaciones.";
                return RedirectToAction("Index", "Universidad");
            }

            var viewModel = await _universidadServices.GetEvaluacionViewModelAsync(subCursoId);

            if (viewModel == null)
            {
                TempData["Error"] = "SubCurso no encontrado.";
                return RedirectToAction("GestionCursos", "Universidad");
            }

            viewModel.PuedeEditarEvaluacion = true;

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar evaluación para SubCurso {SubCursoId}", subCursoId);
            TempData["Error"] = "Error al cargar la evaluación.";
            return RedirectToAction("GestionCursos", "Universidad");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearEvaluacion(CrearEvaluacionRequest request)
    {
        try
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
            var rolId = HttpContext.Session.GetInt32("RolID") ??
                       HttpContext.Session.GetInt32("RolId") ?? 4;

            if (!usuarioId.HasValue || !UniversidadPermisosHelper.PermisosUniversidad.PuedeCrearCursos(rolId))
            {
                TempData["Error"] = "No tiene permisos para crear evaluaciones.";
                return RedirectToAction("Index", "Universidad");
            }

            if (!ModelState.IsValid || !request.Preguntas.Any())
            {
                TempData["Error"] = "Debe agregar al menos una pregunta a la evaluación.";
                var viewModel = await _universidadServices.GetEvaluacionViewModelAsync(request.SubCursoID);
                return View(viewModel);
            }

            var resultado = await _universidadServices.CrearEvaluacionAsync(request);

            if (resultado)
            {
                try
                {
                    var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                    var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                    var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                    int? idEmpresa = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                        ?? HttpContext.Session.GetInt32("EmpresaID");

                    await _bitacoraService.RegistrarAsync(
                        idUsuario: usuarioId,
                        idEmpresa: idEmpresa,
                        accion: "CREAR EVALUACION",
                        mensaje: $"Evaluacion creada en Subcurso {request.SubCursoID} con {request.Preguntas.Count} preguntas ",
                        modulo: "UNIVERSIDAD",
                        entidad: "Evaluación",
                        entidadId: request.SubCursoID.ToString(),
                        resultado: "OK",
                        severidad: 4,
                        solicitudId: solicitudId,
                        ip: direccionIp,
                        AgenteUsuario: agenteUsuario
                        );
                }
                
                catch { }



                TempData["Success"] = "Evaluación creada exitosamente.";
                return RedirectToAction("CrearEvaluacion", new { subCursoId = request.SubCursoID });
            }
            else
            {
                TempData["Error"] = "Error al crear la evaluación.";
                var vm = await _universidadServices.GetEvaluacionViewModelAsync(request.SubCursoID);
                return View(vm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear evaluación");

            // 📝 Bitácora: ERROR
            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                int? idUsuario = HttpContext.Session.GetInt32("UsuarioID");
                int? idEmpresa = HttpContext.Session.GetInt32("EmpresaSeleccionada")
                                 ?? HttpContext.Session.GetInt32("EmpresaID");

                await _bitacoraService.RegistrarAsync(
                    idUsuario: idUsuario,
                    idEmpresa: idEmpresa,
                    accion: "CREAR",
                    mensaje: ex.Message,
                    modulo: "UNIVERSIDAD",
                    entidad: "Evaluación",
                    entidadId: request.SubCursoID.ToString(),
                    resultado: "ERROR",
                    severidad: 3,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                );
            }
            catch { }

            var vm = await _universidadServices.GetEvaluacionViewModelAsync(request.SubCursoID);
            TempData["Error"] = "Error al crear la evaluación.";
            return View(vm);
        }
    }

    // =====================================================
    // TOMAR EVALUACIÓN (ESTUDIANTES)
    // =====================================================

    public async Task<IActionResult> TomarEvaluacion(int subCursoId)
    {
        try
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaSeleccionada") ??
                           HttpContext.Session.GetInt32("EmpresaID") ?? 1;

            if (!usuarioId.HasValue)
                return RedirectToAction("Index", "Universidad");

            var viewModel = await _universidadServices.GetTomarEvaluacionViewModelAsync(
                subCursoId, usuarioId.Value, empresaId);

            if (viewModel == null)
            {
                TempData["Error"] = "Evaluación no disponible.";
                return RedirectToAction("MisCursos", "Universidad");
            }

            try
            {
                var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
                var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
                var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

                await _bitacoraService.RegistrarAsync(
                    idUsuario: usuarioId,
                    idEmpresa: empresaId,
                    accion: "TOMAR",           // o "VER_DETALLE"
                    mensaje: $"Alumno inició evaluación del SubCurso {subCursoId}",
                    modulo: "UNIVERSIDAD",
                    entidad: "Evaluación",
                    entidadId: subCursoId.ToString(),
                    resultado: "OK",
                    severidad: 1,                  // info
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                );
            }
            catch { }



            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar evaluación para tomar");
            TempData["Error"] = "Error al cargar la evaluación.";
            return RedirectToAction("MisCursos", "Universidad");
        }
    }
}