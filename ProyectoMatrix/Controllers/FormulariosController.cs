using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Hubs;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.ViewModels.Formularios;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace ProyectoMatrix.Controllers
{
    [Route("Formularios")]
    [Route("Logistica/Formularios")]
    public class FormulariosController : Controller
    {
        private readonly FormulariosSqlService _formulariosSqlService;
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<LogisticaHub> _hubContext;

        public FormulariosController(
            FormulariosSqlService formulariosSqlService,
            ApplicationDbContext context,
            IHubContext<LogisticaHub> hubContext)
        {
            _formulariosSqlService = formulariosSqlService;
            _context = context;
            _hubContext = hubContext;
        }


        [HttpGet("Centro")]
        [HttpGet("CentroFormularios")]
        public IActionResult CentroFormularios(string? origenModulo = null)
        {
            var moduloOperacion = NormalizarModuloOperacion(origenModulo) ?? "Transporte";

            ViewData["OrigenModulo"] = moduloOperacion;
            ViewData["EsLogistica"] = EsUsuarioLogistica(ObtenerUsuarioId());
            ViewData["Title"] = "Centro de formularios logísticos";

            return View("~/Views/Logistica/Formularios/CentroFormularios.cshtml");
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(string? origenModulo = null)
        {
            var formularios = await _formulariosSqlService.ObtenerPlantillasAsync("Logistica");
            var moduloOperacion = NormalizarModuloOperacion(origenModulo);

            if (!string.IsNullOrWhiteSpace(moduloOperacion))
            {
                formularios = formularios
                    .Where(f => PlantillaPerteneceAModulo(f, moduloOperacion))
                    .ToList();
            }

            ViewData["OrigenModulo"] = moduloOperacion ?? origenModulo;
            ViewData["EsLogistica"] = EsUsuarioLogistica(ObtenerUsuarioId());

            return View("~/Views/Logistica/Formularios/Index.cshtml", formularios);
        }

        [HttpGet("Crear")]
        public IActionResult Crear()
        {
            var model = new FormularioPlantillaViewModel
            {
                Modulo = "Logistica",
                Categoria = "General",
                Campos = new List<FormularioCampoViewModel>
                {
                    new FormularioCampoViewModel
                    {
                        Clave = "cliente",
                        Etiqueta = "Cliente",
                        Tipo = "texto",
                        Obligatorio = true,
                        Copiable = true
                    },
                    new FormularioCampoViewModel
                    {
                        Clave = "proyecto",
                        Etiqueta = "Proyecto",
                        Tipo = "texto",
                        Obligatorio = false,
                        Copiable = true
                    },
                    new FormularioCampoViewModel
                    {
                        Clave = "solicitante",
                        Etiqueta = "Solicitante",
                        Tipo = "texto",
                        Obligatorio = true,
                        Copiable = true
                    }
                }
            };

            return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
        }

        [HttpPost("Crear")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(FormularioPlantillaViewModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    ModelState.AddModelError(nameof(model.Nombre), "El nombre del formulario es obligatorio.");
                }

                model.Campos = model.Campos
                    .Where(c => !string.IsNullOrWhiteSpace(c.Etiqueta) || !string.IsNullOrWhiteSpace(c.Clave))
                    .ToList();

                if (!model.Campos.Any())
                {
                    ModelState.AddModelError("", "Debes agregar al menos un campo al formulario.");
                }

                if (!ModelState.IsValid)
                {
                    return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
                }

                await _formulariosSqlService.CrearPlantillaAsync(model);

                TempData["Success"] = "Formulario creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"No se pudo crear el formulario: {ex.Message}");
                return View("~/Views/Logistica/Formularios/Crear.cshtml", model);
            }
        }

        [HttpGet("Usar/{id:int}")]
        [HttpGet("Llenar")]
        [HttpGet("Llenar/{id:int}")]
        public async Task<IActionResult> Llenar(int id, string? origenModulo = null, bool modal = false)
        {
            var model = await _formulariosSqlService.PrepararLlenadoAsync(id);

            if (model == null)
            {
                TempData["Error"] = "No se encontró el formulario solicitado.";
                return RedirectToAction(nameof(Index), new { origenModulo });
            }

            var moduloOperacion = NormalizarModuloOperacion(origenModulo)
                ?? NormalizarModuloOperacion(model.Categoria);

            model.OrigenTipo = moduloOperacion;
            ViewData["OrigenModulo"] = moduloOperacion ?? origenModulo;
            ViewData["EsModal"] = modal;

            return View("~/Views/Logistica/Formularios/Llenar.cshtml", model);
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
                    return ResponderGuardado(false, "No se pudo identificar al usuario en sesión.", origenModulo, null, modal);
                }

                var plantilla = await _formulariosSqlService.ObtenerPlantillaPorIdAsync(model.IdFormulario);

                if (plantilla == null)
                {
                    return ResponderGuardado(false, "No se encontró la plantilla del formulario.", origenModulo, null, modal);
                }

                var moduloOperacion = NormalizarModuloOperacion(origenModulo)
                    ?? NormalizarModuloOperacion(model.OrigenTipo)
                    ?? NormalizarModuloOperacion(plantilla.Categoria)
                    ?? NormalizarModuloOperacion(plantilla.Nombre);

                if (string.IsNullOrWhiteSpace(moduloOperacion))
                {
                    return ResponderGuardado(false, "No se pudo identificar si la petición corresponde a Guías o Transporte.", origenModulo, null, modal);
                }

                var esLogistica = EsUsuarioLogistica(usuarioId);

                model.UsuarioID = usuarioId;
                model.Estado = "Registrado";
                model.OrigenTipo = moduloOperacion;

                int origenId;

                if (moduloOperacion == "Transporte")
                {
                    origenId = await CrearSolicitudTransporteDesdeFormularioAsync(model, plantilla, usuarioId, esLogistica);
                    model.OrigenID = origenId;
                    model.OrigenTipo = "Transporte";
                }
                else
                {
                    origenId = await CrearSolicitudGuiaDesdeFormularioAsync(model, usuarioId, esLogistica);
                    model.OrigenID = origenId;
                    model.OrigenTipo = "Guias";
                }

                await _formulariosSqlService.GuardarRespuestaAsync(model);
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");

                var redirectUrl = moduloOperacion == "Transporte"
                    ? Url.Action("Transporte", "Logistica")
                    : Url.Action("Guias", "Logistica");

                return ResponderGuardado(true, "Petición enviada correctamente a Logística.", origenModulo ?? moduloOperacion, redirectUrl, modal);
            }
            catch (Exception ex)
            {
                var mensaje = ex.InnerException?.Message ?? ex.Message;
                return ResponderGuardado(false, $"No se pudo guardar la petición: {mensaje}", origenModulo, null, modal);
            }
        }

        [HttpGet("Respuestas")]
        public async Task<IActionResult> Respuestas()
        {
            var respuestas = await _formulariosSqlService.ObtenerRespuestasResumenAsync();
            return View("~/Views/Logistica/Formularios/Respuestas.cshtml", respuestas);
        }

        [HttpGet("CrearDesdeRespuesta")]
        public async Task<IActionResult> CrearDesdeRespuesta(int idFormularioDestino, int idRespuestaOrigen)
        {
            var model = await _formulariosSqlService.PrepararLlenadoDesdeRespuestaAsync(
                idFormularioDestino,
                idRespuestaOrigen
            );

            if (model == null)
            {
                TempData["Error"] = "No se pudo preparar el formulario relacionado.";
                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/Logistica/Formularios/Llenar.cshtml", model);
        }

        private async Task<int> CrearSolicitudTransporteDesdeFormularioAsync(
            FormularioRespuestaViewModel model,
            FormularioPlantillaViewModel plantilla,
            int usuarioId,
            bool esLogistica)
        {
            var valores = model.Valores ?? new Dictionary<string, string?>();
            var nombreUsuario = ObtenerNombreUsuario(usuarioId);
            var areaUsuario = ObtenerAreaUsuario(usuarioId);
            var estado = esLogistica ? "Autorizada" : "Pendiente";

            var transporte = new Transporte
            {
                UsuarioID = usuarioId,
                Area = areaUsuario,
                ElaboradoPor = nombreUsuario,
                NombreSolicitante = Valor(valores, "nombre_solicitante", "solicitante", "NombreSolicitante") ?? nombreUsuario,
                Departamento = Valor(valores, "departamento", "Departamento") ?? areaUsuario,
                FechaEmision = LeerFechaDesdeDatosOficiales(plantilla.DatosFijosPdfJson) ?? new DateTime(2025, 9, 17),
                CodigoFormato = LeerDatoOficial(plantilla.DatosFijosPdfJson, "codigo", "Codigo") ?? "F-19-06",
                FechaCarga = LeerFecha(valores, "fecha_carga", "FechaCarga"),
                NumeroFactura = Valor(valores, "factura", "numero_factura", "NumeroFactura"),
                HorarioCarga = Valor(valores, "horario_carga", "HorarioCarga"),
                HorarioLlegadaDestino = Valor(valores, "horario_llegada_destino", "HorarioLlegadaDestino"),
                DuracionAproxFlete = Valor(valores, "duracion_aprox_flete", "DuracionAproxFlete"),
                Cliente = Valor(valores, "cliente", "Cliente"),
                Proyecto = Valor(valores, "proyecto", "Proyecto"),
                CompaniaSolicitante = Valor(valores, "compania_solicitante", "CompaniaSolicitante"),
                CentroCosto = Valor(valores, "centro_costo", "CentroCosto"),
                AutorizadoPresupuesto = Valor(valores, "autorizado_presupuesto", "AutorizadoPresupuesto"),
                TipoRuta = Valor(valores, "tipo_ruta", "TipoRuta"),
                DireccionRecoleccion = Valor(valores, "direccion_recoleccion", "DireccionRecoleccion"),
                Volumetria = Valor(valores, "volumetria", "Volumetria"),
                TipoUnidad = Valor(valores, "tipo_unidad", "TipoUnidad"),
                ComentariosUnidad = Valor(valores, "comentarios_unidad", "ComentariosUnidad", "observaciones"),
                Fletero = Valor(valores, "fletero", "Fletero"),
                CostoFlete = LeerDecimal(valores, "costo_flete", "presupuesto_flete", "CostoFlete"),
                EstadoSolicitud = estado,
                EstaBorrado = false,
                NotificacionLeida = esLogistica,
                FechaRegistro = DateTime.Now,
                FechaActualizacion = DateTime.Now,
                MensajeEdicion = esLogistica ? null : "Solicitud creada desde formulario. Requiere autorización de Logística.",
                Destinos = CrearDestinosTransporte(valores),
                PlanEmbarque = CrearPlanEmbarqueTransporte(valores)
            };

            _context.Transporte.Add(transporte);
            await _context.SaveChangesAsync();

            transporte.Folio = $"TR-{transporte.IdTransporte}";

            _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
            {
                IdTransporte = transporte.IdTransporte,
                EstadoAnterior = null,
                EstadoNuevo = estado,
                UsuarioID = usuarioId,
                Comentario = esLogistica
                    ? "Solicitud creada desde formulario por Logística."
                    : "Solicitud creada desde formulario y enviada a autorización.",
                FechaMovimiento = DateTime.Now
            });

            await _context.SaveChangesAsync();
            return transporte.IdTransporte;
        }

        private async Task<int> CrearSolicitudGuiaDesdeFormularioAsync(
            FormularioRespuestaViewModel model,
            int usuarioId,
            bool esLogistica)
        {
            var valores = model.Valores ?? new Dictionary<string, string?>();
            var estado = esLogistica ? "Activo" : "Pendiente";

            var guia = new Guia
            {
                UsuarioID = usuarioId,
                Departamento = Valor(valores, "departamento", "Departamento") ?? ObtenerAreaUsuario(usuarioId),
                ClienteProyecto = Valor(valores, "cliente_proyecto", "ClienteProyecto", "proyecto", "cliente"),
                QuienGestiona = Valor(valores, "quien_gestiona", "QuienGestiona"),
                TipoRequerimiento = Valor(valores, "tipo_requerimiento", "TipoRequerimiento"),
                FechaEnvio = LeerFecha(valores, "fecha_envio", "FechaEnvio"),
                TipoEntrega = Valor(valores, "tipo_entrega", "TipoEntrega"),
                DireccionRemitenteTipo = Valor(valores, "direccion_remitente_tipo", "DireccionRemitenteTipo"),
                DestinatarioCorreo = Valor(valores, "destinatario_correo", "DestinatarioCorreo"),
                InformacionDimensionesPeso = Valor(valores, "informacion_dimensiones_peso", "InformacionDimensionesPeso"),
                Empresa = Valor(valores, "empresa", "Empresa"),
                RemitenteNombre = Valor(valores, "remitente_nombre", "RemitenteNombre"),
                RemitenteTelefono = Valor(valores, "remitente_telefono", "RemitenteTelefono"),
                Origen = Valor(valores, "origen", "Origen") ?? "No especificado",
                CodigoPostalOrigen = Valor(valores, "codigo_postal_origen", "CodigoPostalOrigen"),
                DestinatarioNombre = Valor(valores, "destinatario_nombre", "DestinatarioNombre"),
                DestinatarioTelefono = Valor(valores, "destinatario_telefono", "DestinatarioTelefono"),
                Destino = Valor(valores, "destino", "Destino") ?? "No especificado",
                CodigoPostalDestino = Valor(valores, "codigo_postal_destino", "CodigoPostalDestino"),
                PesoKg = LeerDecimal(valores, "peso_kg", "PesoKg"),
                LargoCm = LeerDecimal(valores, "largo_cm", "LargoCm"),
                AnchoCm = LeerDecimal(valores, "ancho_cm", "AnchoCm"),
                AltoCm = LeerDecimal(valores, "alto_cm", "AltoCm"),
                ContenidoDeclarado = Valor(valores, "contenido_declarado", "ContenidoDeclarado"),
                TipoEnvio = Valor(valores, "tipo_envio", "TipoEnvio"),
                RequiereCadenaFrio = LeerBool(valores, "requiere_cadena_frio", "RequiereCadenaFrio"),
                Costo = LeerDecimal(valores, "costo", "Costo") ?? 0,
                Observaciones = Valor(valores, "observaciones", "Observaciones"),
                FechaSolicitud = DateTime.Now,
                EstadoEdicion = estado,
                MensajeEdicion = esLogistica ? null : "Solicitud creada desde formulario. Requiere validación de Logística.",
                NotificacionLeida = esLogistica,
                EstaBorrado = false,
                DatosAntiguos = null
            };

            _context.Guias.Add(guia);
            await _context.SaveChangesAsync();

            return guia.IdGuia;
        }

        private List<TransporteDestino> CrearDestinosTransporte(Dictionary<string, string?> valores)
        {
            var destinos = new List<TransporteDestino>();
            var json = Valor(valores, "destinos", "Destinos");
            var items = ParseJsonArray(json);

            foreach (var item in items)
            {
                var nombre = LeerPropiedad(item, "nombre_recibe", "NombreRecibe");
                var contacto = LeerPropiedad(item, "contacto", "ContactoRecibe");
                var direccion = LeerPropiedad(item, "direccion", "DireccionDestino");

                if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(contacto) && string.IsNullOrWhiteSpace(direccion))
                    continue;

                destinos.Add(new TransporteDestino
                {
                    NumeroDestino = destinos.Count + 1,
                    NombreRecibe = nombre,
                    ContactoRecibe = contacto,
                    DireccionDestino = direccion
                });
            }

            if (!destinos.Any())
            {
                destinos.Add(new TransporteDestino
                {
                    NumeroDestino = 1,
                    NombreRecibe = Valor(valores, "destinatario_nombre", "nombre_recibe"),
                    ContactoRecibe = Valor(valores, "destinatario_telefono", "contacto"),
                    DireccionDestino = Valor(valores, "destino", "direccion_destino") ?? "No especificado"
                });
            }

            return destinos;
        }

        private List<TransportePlanEmbarque> CrearPlanEmbarqueTransporte(Dictionary<string, string?> valores)
        {
            var plan = new List<TransportePlanEmbarque>();
            var json = Valor(valores, "plan_embarque", "PlanEmbarque");
            var items = ParseJsonArray(json);

            foreach (var item in items)
            {
                var descripcion = LeerPropiedad(item, "descripcion", "Descripcion");
                var claveSat = LeerPropiedad(item, "clave_sat", "ClaveSAT");
                var um = LeerPropiedad(item, "um", "UnidadMedida");
                var vale = LeerPropiedad(item, "vale_salida_factura", "ValeSalidaFactura");

                if (string.IsNullOrWhiteSpace(descripcion) && string.IsNullOrWhiteSpace(claveSat) && string.IsNullOrWhiteSpace(um) && string.IsNullOrWhiteSpace(vale))
                    continue;

                plan.Add(new TransportePlanEmbarque
                {
                    ClaveSAT = claveSat,
                    Descripcion = descripcion,
                    Cantidad = ParseDecimal(LeerPropiedad(item, "cantidad", "Cantidad")),
                    UnidadMedida = um,
                    Peso = ParseDecimal(LeerPropiedad(item, "peso", "Peso")),
                    Valor = ParseDecimal(LeerPropiedad(item, "valor", "Valor")),
                    ValeSalidaFactura = vale
                });
            }

            if (!plan.Any())
            {
                plan.Add(new TransportePlanEmbarque
                {
                    Descripcion = Valor(valores, "descripcion", "contenido_declarado", "observaciones") ?? "Registro creado desde formulario",
                    Cantidad = 1,
                    UnidadMedida = "PZA"
                });
            }

            return plan;
        }

        private IActionResult ResponderGuardado(bool success, string message, string? origenModulo, string? redirectUrl, bool modal = false)
        {
            var aceptaJson = Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
            var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || aceptaJson;

            var destino = redirectUrl ?? Url.Action(nameof(Index), new { origenModulo });

            if (ajax)
            {
                var payload = new
                {
                    success,
                    message,
                    redirectUrl = destino
                };

                if (success)
                {
                    return Json(payload);
                }

                Response.StatusCode = StatusCodes.Status400BadRequest;
                return Json(payload);
            }

            // Si por cualquier razón el JavaScript del modal no intercepta el submit,
            // evitamos que el iframe cargue la vista completa de Transporte/Guías dentro del modal.
            if (modal)
            {
                var mensajeJs = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(message ?? string.Empty);
                var destinoJs = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(destino ?? string.Empty);
                var funcion = success ? "formularioPeticionGuardada" : "formularioPeticionError";

                return Content($@"<!doctype html>
<html><head><meta charset='utf-8'></head><body>
<script>
(function() {{
    if (window.parent && typeof window.parent.{funcion} === 'function') {{
        window.parent.{funcion}('{mensajeJs}', '{destinoJs}');
    }} else if ('{destinoJs}') {{
        window.top.location.href = '{destinoJs}';
    }}
}})();
</script>
</body></html>", "text/html");
            }

            if (success)
                TempData["Success"] = message;
            else
                TempData["Error"] = message;

            if (success && !string.IsNullOrWhiteSpace(redirectUrl))
                return Redirect(redirectUrl);

            return RedirectToAction(nameof(Index), new { origenModulo });
        }

        private bool PlantillaPerteneceAModulo(FormularioPlantillaViewModel plantilla, string moduloOperacion)
        {
            var categoria = NormalizarModuloOperacion(plantilla.Categoria);
            var nombre = NormalizarModuloOperacion(plantilla.Nombre);

            if (categoria == "Ambos")
                return true;

            return categoria == moduloOperacion || nombre == moduloOperacion;
        }

        private string? NormalizarModuloOperacion(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var limpio = valor.Trim();

            if (limpio.Equals("Transporte", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Transportes", StringComparison.OrdinalIgnoreCase)
                || limpio.Contains("transporte", StringComparison.OrdinalIgnoreCase))
                return "Transporte";

            if (limpio.Equals("Guias", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guías", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guia", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guía", StringComparison.OrdinalIgnoreCase)
                || limpio.Contains("guia", StringComparison.OrdinalIgnoreCase)
                || limpio.Contains("guía", StringComparison.OrdinalIgnoreCase))
                return "Guias";

            if (limpio.Equals("Ambos", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("General", StringComparison.OrdinalIgnoreCase))
                return "Ambos";

            return null;
        }

        private bool EsUsuarioLogistica(int usuarioId)
        {
            if (usuarioId <= 0)
                return false;

            return _context.EmpleadoDepartamentos
                .Join(_context.Departamentos,
                      ed => ed.DepartamentoID,
                      d => d.DepartamentoID,
                      (ed, d) => new { ed, d })
                .Any(x => x.ed.UsuarioID == usuarioId
                       && x.d.NombreDepartamento != null
                       && x.d.NombreDepartamento.ToUpper() == "LOGISTICA");
        }

        private string ObtenerNombreUsuario(int usuarioId)
        {
            var usuario = _context.Usuarios
                .Include(u => u.Persona)
                .AsNoTracking()
                .FirstOrDefault(u => u.UsuarioID == usuarioId);

            if (usuario?.Persona != null)
            {
                var nombreCompleto = string.Join(" ", new[]
                {
                    usuario.Persona.Nombre,
                    usuario.Persona.ApellidoPaterno,
                    usuario.Persona.ApellidoMaterno
                }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

                if (!string.IsNullOrWhiteSpace(nombreCompleto))
                    return nombreCompleto;
            }

            return usuario?.Username ?? User.Identity?.Name ?? "Usuario";
        }

        private string ObtenerAreaUsuario(int usuarioId)
        {
            var area = _context.EmpleadoDepartamentos
                .Join(_context.Departamentos,
                      ed => ed.DepartamentoID,
                      d => d.DepartamentoID,
                      (ed, d) => new { ed, d })
                .Where(x => x.ed.UsuarioID == usuarioId)
                .Select(x => x.d.NombreDepartamento)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(area) ? "Logística" : area;
        }

        private string? Valor(Dictionary<string, string?> valores, params string[] claves)
        {
            foreach (var clave in claves)
            {
                if (valores.TryGetValue(clave, out var valor) && !string.IsNullOrWhiteSpace(valor))
                    return valor;

                var encontrado = valores.FirstOrDefault(x => x.Key.Equals(clave, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(encontrado.Value))
                    return encontrado.Value;
            }

            return null;
        }

        private DateTime? LeerFecha(Dictionary<string, string?> valores, params string[] claves)
        {
            return ParseFecha(Valor(valores, claves));
        }

        private DateTime? LeerFechaDesdeDatosOficiales(string? datosFijosJson)
        {
            return ParseFecha(LeerDatoOficial(datosFijosJson, "fechaEmision", "fecha_emision", "FechaEmision"));
        }

        private string? LeerDatoOficial(string? datosFijosJson, params string[] claves)
        {
            if (string.IsNullOrWhiteSpace(datosFijosJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(datosFijosJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                foreach (var clave in claves)
                {
                    if (doc.RootElement.TryGetProperty(clave, out var prop))
                    {
                        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
                    }
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (claves.Any(c => prop.Name.Equals(c, StringComparison.OrdinalIgnoreCase)))
                    {
                        return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private DateTime? ParseFecha(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            if (DateTime.TryParse(valor, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out var fechaMx))
                return fechaMx;

            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaInvariant))
                return fechaInvariant;

            return null;
        }

        private decimal? LeerDecimal(Dictionary<string, string?> valores, params string[] claves)
        {
            return ParseDecimal(Valor(valores, claves));
        }

        private decimal? ParseDecimal(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var limpio = valor
                .Replace("$", string.Empty)
                .Replace("MXN", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("+ IVA", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out var decimalMx))
                return decimalMx;

            if (decimal.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalInvariant))
                return decimalInvariant;

            return null;
        }

        private bool LeerBool(Dictionary<string, string?> valores, params string[] claves)
        {
            var valor = Valor(valores, claves);

            if (string.IsNullOrWhiteSpace(valor))
                return false;

            return valor.Equals("true", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("1", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private List<JsonElement> ParseJsonArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<JsonElement>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<JsonElement>();

                return doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
            }
            catch
            {
                return new List<JsonElement>();
            }
        }

        private string? LeerPropiedad(JsonElement item, params string[] claves)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var clave in claves)
            {
                if (item.TryGetProperty(clave, out var prop))
                {
                    return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
                }
            }

            foreach (var prop in item.EnumerateObject())
            {
                if (claves.Any(c => prop.Name.Equals(c, StringComparison.OrdinalIgnoreCase)))
                {
                    return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                }
            }

            return null;
        }

        private int ObtenerUsuarioId()
        {
            var desdeSesion = HttpContext.Session.GetInt32("UsuarioID")
                ?? HttpContext.Session.GetInt32("UsuarioId")
                ?? HttpContext.Session.GetInt32("IdUsuario");

            if (desdeSesion.HasValue && desdeSesion.Value > 0)
                return desdeSesion.Value;

            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("UsuarioID")?.Value
                ?? User.FindFirst("UsuarioId")?.Value
                ?? User.FindFirst("IdUsuario")?.Value;

            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}