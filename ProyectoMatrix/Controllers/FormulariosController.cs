using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.ViewModels.Formularios;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    [Route("Formularios")]
    [Route("Logistica/Formularios")]
    public class FormulariosController : Controller
    {
        private const string VistaIndex = "~/Views/Logistica/Formularios/Index.cshtml";
        private const string VistaCrear = "~/Views/Logistica/Formularios/Crear.cshtml";
        private const string VistaLlenar = "~/Views/Logistica/Formularios/Llenar.cshtml";
        private const string VistaRespuestas = "~/Views/Logistica/Formularios/Respuestas.cshtml";

        private readonly FormulariosSqlService _formulariosSqlService;

        public FormulariosController(FormulariosSqlService formulariosSqlService)
        {
            _formulariosSqlService = formulariosSqlService;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(string? origenModulo = null, string? estado = null)
        {
            var usuarioId = ObtenerUsuarioId();
            var esLogistica = await EsUsuarioLogisticaAsync(usuarioId);
            var verDesactivadas = esLogistica && EstadoEsDesactivadas(estado);

            await _formulariosSqlService.GarantizarPlantillaBaseGuiasAsync();

            ViewData["OrigenModulo"] = origenModulo;
            ViewData["EsLogistica"] = esLogistica;
            ViewData["EstadoPlantillas"] = verDesactivadas ? "desactivadas" : "activas";

            var formularios = await _formulariosSqlService.ObtenerPlantillasAsync(
                modulo: "Logistica",
                origenModulo: origenModulo,
                incluirInactivos: verDesactivadas
            );

            formularios = verDesactivadas
                ? formularios.Where(p => !p.Activo).ToList()
                : formularios.Where(p => p.Activo).ToList();

            return View(VistaIndex, formularios);
        }

        [HttpGet("PlantillasJson")]
        public async Task<IActionResult> PlantillasJson(string? categoria = null, string? origenModulo = null)
        {
            var filtro = string.IsNullOrWhiteSpace(categoria) ? origenModulo : categoria;

            await _formulariosSqlService.GarantizarPlantillaBaseGuiasAsync();

            var plantillas = await _formulariosSqlService.ObtenerPlantillasAsync(
                modulo: "Logistica",
                origenModulo: filtro,
                incluirInactivos: false
            );

            var resultado = plantillas.Select(p => new
            {
                idFormulario = p.IdFormulario,
                nombre = p.Nombre,
                descripcion = p.Descripcion,
                categoria = p.Categoria,
                modulo = p.Modulo,
                activo = p.Activo,
                datosOficiales = p.DatosOficiales,
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

        [HttpGet("Crear")]
        public async Task<IActionResult> Crear(string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para crear plantillas de formularios.";
                return RedirectToFormularioIndex(origenModulo);
            }

            var categoria = NormalizarCategoria(origenModulo) ?? "Ambos";

            var model = new FormularioPlantillaViewModel
            {
                Modulo = "Logistica",
                Categoria = categoria,
                Activo = true,
                EsPlantillaBase = false,
                DatosOficiales = new FormularioDatosOficialesViewModel
                {
                    Area = "Logística",
                    Pagina = "1 de 1"
                },
                Campos = new List<FormularioCampoViewModel>
                {
                    new FormularioCampoViewModel
                    {
                        Clave = string.Empty,
                        Etiqueta = string.Empty,
                        Tipo = "texto",
                        Obligatorio = false,
                        Copiable = true
                    }
                }
            };

            return View(VistaCrear, model);
        }

        [HttpPost("Crear")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(FormularioPlantillaViewModel model, string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para crear plantillas de formularios.";
                return RedirectToFormularioIndex(origenModulo);
            }

            PrepararModeloParaGuardar(model, origenModulo);

            if (!ValidarPlantilla(model))
            {
                return View(VistaCrear, model);
            }

            try
            {
                await _formulariosSqlService.CrearPlantillaAsync(model);

                TempData["Success"] = "Plantilla creada correctamente.";
                return RedirectToFormularioIndex(origenModulo ?? model.Categoria);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"No se pudo crear la plantilla: {ex.Message}");
                return View(VistaCrear, model);
            }
        }

        [HttpGet("Editar/{id:int}")]
        public async Task<IActionResult> Editar(int id, string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para editar plantillas.";
                return RedirectToFormularioIndex(origenModulo);
            }

            var model = await _formulariosSqlService.ObtenerPlantillaPorIdAsync(id, incluirInactiva: true);

            if (model == null)
            {
                TempData["Error"] = "No se encontró la plantilla solicitada.";
                return RedirectToFormularioIndex(origenModulo);
            }

            return View(VistaCrear, model);
        }

        [HttpPost("Editar/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, FormularioPlantillaViewModel model, string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para editar plantillas.";
                return RedirectToFormularioIndex(origenModulo);
            }

            if (id != model.IdFormulario)
            {
                TempData["Error"] = "La plantilla enviada no coincide con la URL.";
                return RedirectToFormularioIndex(origenModulo);
            }

            PrepararModeloParaGuardar(model, origenModulo);

            if (!ValidarPlantilla(model))
            {
                return View(VistaCrear, model);
            }

            try
            {
                await _formulariosSqlService.ActualizarPlantillaAsync(model);

                TempData["Success"] = "Plantilla actualizada correctamente.";
                return RedirectToFormularioIndex(origenModulo ?? model.Categoria);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"No se pudo actualizar la plantilla: {ex.Message}");
                return View(VistaCrear, model);
            }
        }

        [HttpPost("Eliminar/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id, string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para desactivar plantillas.";
                return RedirectToFormularioIndex(origenModulo);
            }

            try
            {
                await _formulariosSqlService.DesactivarPlantillaAsync(id);

                TempData["Success"] = "Plantilla desactivada correctamente.";
                return RedirectToFormularioIndex(origenModulo);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"No se pudo desactivar la plantilla: {ex.Message}";
                return RedirectToFormularioIndex(origenModulo);
            }
        }

        [HttpPost("Reactivar/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivar(int id, string? origenModulo = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para reactivar plantillas.";
                return RedirectToFormularioIndex(origenModulo);
            }

            try
            {
                await _formulariosSqlService.ReactivarPlantillaAsync(id);

                TempData["Success"] = "Plantilla reactivada correctamente.";
                return Redirect($"/Logistica/Formularios?origenModulo={Uri.EscapeDataString(origenModulo ?? string.Empty)}&estado=desactivadas");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"No se pudo reactivar la plantilla: {ex.Message}";
                return Redirect($"/Logistica/Formularios?origenModulo={Uri.EscapeDataString(origenModulo ?? string.Empty)}&estado=desactivadas");
            }
        }

        [HttpGet("Llenar/{id:int}")]
        [HttpGet("Usar/{id:int}")]
        public async Task<IActionResult> Llenar(int id, string? origenModulo = null, string? origenTipo = null, int? origenId = null)
        {
            var model = await _formulariosSqlService.PrepararLlenadoAsync(id);

            if (model == null)
            {
                TempData["Error"] = "No se encontró el formulario solicitado.";
                return RedirectToFormularioIndex(origenModulo);
            }

            model.OrigenTipo = origenTipo;
            model.OrigenID = origenId;

            ViewData["OrigenModulo"] = origenModulo;

            return View(VistaLlenar, model);
        }

        [HttpPost("GuardarRespuesta")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarRespuesta(FormularioRespuestaViewModel model, string? origenModulo = null, bool modal = false)
        {
            try
            {
                var usuarioId = ObtenerUsuarioId();

                if (usuarioId <= 0)
                {
                    const string mensajeSesion = "No se pudo identificar al usuario en sesión.";

                    if (modal)
                        return RespuestaModalFormulario(false, mensajeSesion, origenModulo);

                    TempData["Error"] = mensajeSesion;
                    return RedirectToFormularioIndex(origenModulo);
                }

                model.UsuarioID = usuarioId;
                model.Estado = string.IsNullOrWhiteSpace(model.Estado)
                    ? "Registrado"
                    : model.Estado;

                await _formulariosSqlService.GuardarRespuestaAsync(model);

                const string mensajeExito = "Petición registrada correctamente.";

                if (modal)
                    return RespuestaModalFormulario(true, mensajeExito, origenModulo);

                TempData["Success"] = mensajeExito;
                return RedirectToFormularioIndex(origenModulo);
            }
            catch (Exception ex)
            {
                var mensajeError = $"No se pudo guardar la petición: {ex.Message}";

                if (modal)
                    return RespuestaModalFormulario(false, mensajeError, origenModulo);

                TempData["Error"] = mensajeError;
                return RedirectToFormularioIndex(origenModulo);
            }
        }

        [HttpGet("Respuestas")]
        public async Task<IActionResult> Respuestas()
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                TempData["Error"] = "No tienes permisos para consultar respuestas.";
                return RedirectToFormularioIndex(null);
            }

            var respuestas = await _formulariosSqlService.ObtenerRespuestasResumenAsync();
            return View(VistaRespuestas, respuestas);
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

            ViewData["OrigenModulo"] = origenModulo;

            return View(VistaLlenar, model);
        }

        [HttpGet("ContenidoJson/{id:int}")]
        public async Task<IActionResult> ContenidoJson(int id)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!await EsUsuarioLogisticaAsync(usuarioId))
            {
                return Forbid();
            }

            var plantilla = await _formulariosSqlService.ObtenerPlantillaPorIdAsync(id, incluirInactiva: true);

            if (plantilla == null)
                return NotFound();

            return Json(new
            {
                idFormulario = plantilla.IdFormulario,
                nombre = plantilla.Nombre,
                descripcion = plantilla.Descripcion,
                categoria = plantilla.Categoria,
                datosOficiales = plantilla.DatosOficiales,
                campos = plantilla.Campos.Select(c => new
                {
                    clave = c.Clave,
                    etiqueta = c.Etiqueta,
                    tipo = c.Tipo,
                    obligatorio = c.Obligatorio,
                    copiable = c.Copiable,
                    opciones = c.Opciones
                }).ToList()
            });
        }

        private bool ValidarPlantilla(FormularioPlantillaViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                ModelState.AddModelError(nameof(model.Nombre), "El nombre de la plantilla es obligatorio.");
            }

            model.Campos = (model.Campos ?? new List<FormularioCampoViewModel>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Etiqueta) || !string.IsNullOrWhiteSpace(c.Clave))
                .ToList();

            if (!model.Campos.Any())
            {
                ModelState.AddModelError("", "Debes agregar al menos un campo a la plantilla.");
            }

            if (string.IsNullOrWhiteSpace(model.Categoria))
            {
                ModelState.AddModelError(nameof(model.Categoria), "Debes indicar dónde se usará la plantilla.");
            }

            return ModelState.IsValid;
        }

        private void PrepararModeloParaGuardar(FormularioPlantillaViewModel model, string? origenModulo)
        {
            model.Modulo = "Logistica";
            model.Categoria = NormalizarCategoria(model.Categoria)
                ?? NormalizarCategoria(origenModulo)
                ?? "Ambos";
            model.EsPlantillaBase = false;
            model.Activo = true;
            model.DatosOficiales ??= new FormularioDatosOficialesViewModel();

            if (model.Campos != null)
            {
                foreach (var campo in model.Campos)
                {
                    campo.OpcionesTexto ??= campo.Opciones == null || campo.Opciones.Count == 0
                        ? string.Empty
                        : string.Join(Environment.NewLine, campo.Opciones);
                }
            }
        }

        private string? NormalizarCategoria(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var limpio = valor.Trim();

            if (limpio.Equals("Transportes", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Transporte", StringComparison.OrdinalIgnoreCase))
                return "Transporte";

            if (limpio.Equals("Guias", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guías", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guia", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guía", StringComparison.OrdinalIgnoreCase))
                return "Guias";

            if (limpio.Equals("Ambos", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("General", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Logistica", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Logística", StringComparison.OrdinalIgnoreCase))
                return "Ambos";

            return limpio;
        }

        private bool EstadoEsDesactivadas(string? estado)
        {
            if (string.IsNullOrWhiteSpace(estado))
                return false;

            return estado.Equals("desactivadas", StringComparison.OrdinalIgnoreCase)
                || estado.Equals("inactivas", StringComparison.OrdinalIgnoreCase)
                || estado.Equals("inactive", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> EsUsuarioLogisticaAsync(int usuarioId)
        {
            var rolSesion = HttpContext.Session.GetString("Rol") ?? string.Empty;
            var rolClaim = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            if (ContieneRolLogistica(rolSesion) || ContieneRolLogistica(rolClaim))
                return true;

            return await _formulariosSqlService.UsuarioPerteneceALogisticaAsync(usuarioId);
        }

        private bool ContieneRolLogistica(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return false;

            return valor.Contains("ADMIN", StringComparison.OrdinalIgnoreCase)
                || valor.Contains("LOGISTICA", StringComparison.OrdinalIgnoreCase)
                || valor.Contains("LOGÍSTICA", StringComparison.OrdinalIgnoreCase);
        }



        private ContentResult RespuestaModalFormulario(bool exito, string mensaje, string? origenModulo)
        {
            var callback = exito ? "formularioPeticionGuardada" : "formularioPeticionError";
            var mensajeJson = System.Text.Json.JsonSerializer.Serialize(mensaje ?? string.Empty);
            var origenJson = System.Text.Json.JsonSerializer.Serialize(origenModulo ?? string.Empty);

            var html = $@"<!doctype html>
<html lang='es'>
<head><meta charset='utf-8'></head>
<body>
<script>
(function() {{
    if (window.parent && window.parent.{callback}) {{
        window.parent.{callback}({mensajeJson}, {origenJson});
        return;
    }}
    window.location.href = '/Logistica/Formularios?origenModulo=' + encodeURIComponent({origenJson});
}})();
</script>
</body>
</html>";

            return Content(html, "text/html; charset=utf-8");
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
            var sesion = HttpContext.Session.GetInt32("UsuarioID")
                ?? HttpContext.Session.GetInt32("UsuarioId")
                ?? HttpContext.Session.GetInt32("IdUsuario");

            if (sesion.HasValue)
                return sesion.Value;

            var claim =
                User.FindFirst("UsuarioID")?.Value
                ?? User.FindFirst("UsuarioId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(claim, out var usuarioId)
                ? usuarioId
                : 0;
        }
    }
}
