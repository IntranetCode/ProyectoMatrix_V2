using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.ViewModels.Formularios;

namespace ProyectoMatrix.Controllers
{
    [Route("Formularios")]
    [Route("Logistica/Formularios")]
    public class FormulariosController : Controller
    {
        private readonly FormulariosSqlService _formulariosSqlService;

        public FormulariosController(FormulariosSqlService formulariosSqlService)
        {
            _formulariosSqlService = formulariosSqlService;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(string? origenModulo = null)
        {
            var formularios = await _formulariosSqlService.ObtenerPlantillasAsync("Logistica");
            return View("~/Views/Logistica/Formularios/Index.cshtml", formularios);
        }

        [HttpGet("Crear")]
        public IActionResult Crear(string? origenModulo = null)
        {
            var categoria = string.IsNullOrWhiteSpace(origenModulo)
                ? "General"
                : origenModulo;

            var model = new FormularioPlantillaViewModel
            {
                Modulo = "Logistica",
                Categoria = categoria,
                Activo = true,
                EsPlantillaBase = false,
                DatosFijosPdfJson = null,
                Campos = new List<FormularioCampoViewModel>()
            };

            return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
        }

        [HttpPost("Crear")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(FormularioPlantillaViewModel model, string? origenModulo = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    ModelState.AddModelError(nameof(model.Nombre), "El nombre del formulario es obligatorio.");
                }

                model.Campos = (model.Campos ?? new List<FormularioCampoViewModel>())
                    .Where(c => !string.IsNullOrWhiteSpace(c.Etiqueta) || !string.IsNullOrWhiteSpace(c.Clave))
                    .ToList();

                if (!model.Campos.Any())
                {
                    ModelState.AddModelError(string.Empty, "Debes agregar al menos un campo al formulario.");
                }

                model.Modulo = "Logistica";
                model.Activo = true;
                model.EsPlantillaBase = false;
                model.DatosFijosPdfJson = null;

                if (!ModelState.IsValid)
                {
                    return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
                }

                await _formulariosSqlService.CrearPlantillaAsync(model);

                TempData["Success"] = "Formulario creado correctamente.";
                return RedirectToFormularioIndex(origenModulo ?? model.Categoria);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"No se pudo crear el formulario: {ex.Message}");
                return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
            }
        }

        [HttpGet("Llenar/{id:int}")]
        [HttpGet("Llenar")]
        public async Task<IActionResult> Llenar(int id, string? origenModulo = null, string? origenTipo = null, int? origenID = null)
        {
            var model = await _formulariosSqlService.PrepararLlenadoAsync(id);

            if (model == null)
            {
                TempData["Error"] = "No se encontró el formulario solicitado.";
                return RedirectToFormularioIndex(origenModulo);
            }

            return View("~/Views/Logistica/Formularios/Llenar.cshtml", model);
        }

        [HttpPost("GuardarRespuesta")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarRespuesta(FormularioRespuestaViewModel model, string? origenModulo = null)
        {
            try
            {
                var usuarioId = ObtenerUsuarioId();

                if (usuarioId <= 0)
                {
                    TempData["Error"] = "No se pudo identificar al usuario en sesión.";
                    return RedirectToFormularioIndex(origenModulo);
                }

                model.UsuarioID = usuarioId;
                model.Estado = string.IsNullOrWhiteSpace(model.Estado)
                    ? "Registrado"
                    : model.Estado;

                await _formulariosSqlService.GuardarRespuestaAsync(model);

                TempData["Success"] = "Respuesta guardada correctamente.";
                return RedirectToFormularioIndex(origenModulo);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"No se pudo guardar la respuesta: {ex.Message}";
                return RedirectToFormularioIndex(origenModulo);
            }
        }

        [HttpGet("Respuestas")]
        public async Task<IActionResult> Respuestas(string? origenModulo = null)
        {
            var respuestas = await _formulariosSqlService.ObtenerRespuestasResumenAsync();
            return View("~/Views/Logistica/Formularios/Respuestas.cshtml", respuestas);
        }

        [HttpGet("CrearDesdeRespuesta")]
        public async Task<IActionResult> CrearDesdeRespuesta(int idFormularioDestino, int idRespuestaOrigen, string? origenModulo = null)
        {
            var model = await _formulariosSqlService.PrepararLlenadoDesdeRespuestaAsync(
                idFormularioDestino,
                idRespuestaOrigen
            );

            if (model == null)
            {
                TempData["Error"] = "No se pudo preparar el formulario relacionado.";
                return RedirectToFormularioIndex(origenModulo);
            }

            return View("~/Views/Logistica/Formularios/Llenar.cshtml", model);
        }

        [HttpGet("PlantillasJson")]
        public async Task<IActionResult> PlantillasJson(string? categoria = null)
        {
            var plantillas = await _formulariosSqlService.ObtenerPlantillasAsync("Logistica");

            if (!string.IsNullOrWhiteSpace(categoria))
            {
                plantillas = plantillas
                    .Where(x => string.Equals(x.Categoria, categoria, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var resultado = plantillas.Select(p => new
            {
                idFormulario = p.IdFormulario,
                nombre = p.Nombre,
                descripcion = p.Descripcion,
                categoria = p.Categoria,
                campos = (p.Campos ?? new List<FormularioCampoViewModel>()).Select(c => new
                {
                    clave = c.Clave,
                    etiqueta = c.Etiqueta,
                    tipo = c.Tipo,
                    obligatorio = c.Obligatorio,
                    copiable = c.Copiable,
                    placeholder = c.Placeholder,
                    opciones = c.Opciones
                }).ToList()
            });

            return Json(resultado);
        }

        private IActionResult RedirectToFormularioIndex(string? origenModulo)
        {
            if (string.IsNullOrWhiteSpace(origenModulo))
            {
                return Redirect("/Logistica/Formularios");
            }

            return Redirect($"/Logistica/Formularios?origenModulo={Uri.EscapeDataString(origenModulo)}");
        }

        private int ObtenerUsuarioId()
        {
            return HttpContext.Session.GetInt32("UsuarioID")
                ?? HttpContext.Session.GetInt32("UsuarioId")
                ?? HttpContext.Session.GetInt32("IdUsuario")
                ?? 0;
        }
    }
}
