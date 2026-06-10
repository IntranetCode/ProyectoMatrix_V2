using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProyectoMatrix.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using ProyectoMatrix.Hubs;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.ViewModels.Formularios;
using System.Security.Claims;
using System.Data;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProyectoMatrix.Controllers
{
    // Este ViewModel se conserva por compatibilidad si tienes reportes generales anteriores.
    public class ReportePDFViewModel
    {
        public List<Transporte> Transportes { get; set; } = new();
        public Dictionary<int, string> Usuarios { get; set; } = new();
    }

    public class LogisticaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<LogisticaHub> _hubContext;
        private readonly FormulariosSqlService _formulariosSqlService;

        public LogisticaController(
            ApplicationDbContext context,
            IHubContext<LogisticaHub> hubContext,
            FormulariosSqlService formulariosSqlService)
        {
            _context = context;
            _hubContext = hubContext;
            _formulariosSqlService = formulariosSqlService;
        }

        private int ObtenerUsuarioIDActual()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(userIdClaim) ? 1 : int.Parse(userIdClaim);
        }

        private string ObtenerNombreUsuarioActual(int usuarioId)
        {
            return ObtenerNombreCompletoUsuario(usuarioId);
        }

        private string ObtenerNombreCompletoUsuario(int usuarioId)
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                bool cerrarConexion = connection.State != ConnectionState.Open;

                if (cerrarConexion)
                    connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT TOP 1
                        NULLIF(LTRIM(RTRIM(
                            ISNULL(p.Nombre, '') + ' ' +
                            ISNULL(p.ApellidoPaterno, '') + ' ' +
                            ISNULL(p.ApellidoMaterno, '')
                        )), '') AS NombreCompleto
                    FROM dbo.Usuarios u
                    LEFT JOIN dbo.Persona p ON p.PersonaID = u.PersonaID
                    WHERE u.UsuarioID = @UsuarioID";

                var parameter = command.CreateParameter();
                parameter.ParameterName = "@UsuarioID";
                parameter.Value = usuarioId;
                command.Parameters.Add(parameter);

                string? nombreCompleto = command.ExecuteScalar()?.ToString();

                if (cerrarConexion)
                    connection.Close();

                if (!string.IsNullOrWhiteSpace(nombreCompleto))
                    return nombreCompleto.Trim();
            }
            catch
            {
                // Si la tabla Persona o la relación no existe en algún ambiente, usa el Username.
            }

            return _context.Usuarios
                .Where(u => u.UsuarioID == usuarioId)
                .Select(u => u.Username)
                .FirstOrDefault()
                ?? User.Identity?.Name
                ?? "Usuario";
        }

        private string ObtenerAreaUsuarioActual(int usuarioId)
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

        private bool ValidarSiEsDepartamentoLogistica(int usuarioId)
        {
            return _context.EmpleadoDepartamentos
                .Join(_context.Departamentos,
                      ed => ed.DepartamentoID,
                      d => d.DepartamentoID,
                      (ed, d) => new { ed, d })
                .Any(x => x.ed.UsuarioID == usuarioId && x.d.NombreDepartamento == "LOGISTICA");
        }

        private string CrearFirmaPanelTransporte()
        {
            var transportes = _context.Transporte.AsNoTracking().ToList();

            int pendientes = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Pendiente");
            int autorizadas = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Autorizada");
            int finalizadas = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Finalizada");
            int eliminadas = transportes.Count(x => x.EstaBorrado);
            int ultimoId = transportes.Any() ? transportes.Max(x => x.IdTransporte) : 0;
            long ultimoMovimiento = transportes.Any()
                ? transportes.Max(x => (x.FechaActualizacion ?? x.FechaRegistro).Ticks)
                : 0;

            return $"{pendientes}-{autorizadas}-{finalizadas}-{eliminadas}-{ultimoId}-{ultimoMovimiento}";
        }

        // ==========================================
        // MÓDULO: TRANSPORTE
        // ==========================================
        
        private List<SelectListItem> ObtenerEmpresasSelectList()
        {
            var empresas = new List<SelectListItem>();

            try
            {
                var connection = _context.Database.GetDbConnection();
                bool cerrarConexion = connection.State != ConnectionState.Open;

                if (cerrarConexion)
                    connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT EmpresaID, Nombre
                    FROM Empresas
                    WHERE ISNULL(Activa, 1) = 1
                    ORDER BY Nombre";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    empresas.Add(new SelectListItem
                    {
                        Value = reader["Nombre"]?.ToString() ?? "",
                        Text = reader["Nombre"]?.ToString() ?? ""
                    });
                }

                if (cerrarConexion)
                    connection.Close();
            }
            catch
            {
                // Si la tabla Empresas no está disponible en algún ambiente,
                // se dejan opciones fijas como respaldo para que el formulario no falle.
            }

            if (!empresas.Any())
            {
                empresas.Add(new SelectListItem { Value = "NS EQUIPO E IMPLEMENTOS", Text = "NS EQUIPO E IMPLEMENTOS" });
                empresas.Add(new SelectListItem { Value = "CORPORATIVO", Text = "CORPORATIVO" });
            }

            return empresas;
        }

        public IActionResult Transporte()
        {
            int usuarioId = ObtenerUsuarioIDActual();
            bool esLogistica = ValidarSiEsDepartamentoLogistica(usuarioId);

            ViewBag.EsLogistica = esLogistica;
            ViewBag.UsuarioActualId = usuarioId;
            ViewBag.UsuarioActualNombre = ObtenerNombreUsuarioActual(usuarioId);
            ViewBag.UsuarioActualArea = ObtenerAreaUsuarioActual(usuarioId);
            ViewBag.Empresas = ObtenerEmpresasSelectList();

            // Carga de diccionario de usuarios
            var usuarios = _context.Usuarios
                .Include(u => u.Persona)
                .AsNoTracking()
                .AsEnumerable();

            ViewBag.UsuariosIntranet = usuarios.ToDictionary(
                u => u.UsuarioID,
                u =>
                {
                    var nombreCompleto = string.Concat(u.Persona?.Nombre, " ", u.Persona?.ApellidoPaterno, " ", u.Persona?.ApellidoMaterno).Trim();
                    return string.IsNullOrWhiteSpace(nombreCompleto)
                        ? u.Username ?? "Usuario"
                        : nombreCompleto;
                });

            List<Transporte> lista;
            if (esLogistica)
            {
                lista = _context.Transporte.Include(t => t.Destinos).Include(t => t.PlanEmbarque).ToList();
            }
            else
            {
                lista = _context.Transporte
                    .Include(t => t.Destinos)
                    .Include(t => t.PlanEmbarque)
                    .Where(t => t.UsuarioID == usuarioId
                             && !t.EstaBorrado
                             && (t.EstadoSolicitud == "Autorizada" || t.EstadoSolicitud == "Finalizada" || t.EstadoSolicitud == "Activo"))
                    .ToList();
            }

            return View(lista);
        }

        [HttpGet]
        public IActionResult EstadoPanelTransporte()
        {
            int usuarioId = ObtenerUsuarioIDActual();
            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var transportes = _context.Transporte.AsNoTracking().ToList();

            return Json(new
            {
                firma = CrearFirmaPanelTransporte(),
                pendientes = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Pendiente"),
                autorizadas = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Autorizada"),
                finalizadas = transportes.Count(x => !x.EstaBorrado && x.EstadoSolicitud == "Finalizada"),
                eliminadas = transportes.Count(x => x.EstaBorrado)
            });
        }

        [HttpGet]
        public IActionResult ObtenerTransporte(int id)
        {
            var transporte = _context.Transporte
                .Include(x => x.Destinos)
                .Include(x => x.PlanEmbarque)
                .Include(x => x.HistorialEstados)
                .FirstOrDefault(x => x.IdTransporte == id);

            if (transporte == null)
            {
                return NotFound();
            }

            return Json(new
            {
                transporte.IdTransporte,
                transporte.Folio,
                transporte.Area,
                ElaboradoPor = ObtenerNombreCompletoUsuario(transporte.UsuarioID),
                transporte.FechaEmision,
                transporte.CodigoFormato,
                transporte.FechaCarga,
                transporte.NumeroFactura,
                transporte.HorarioCarga,
                transporte.HorarioLlegadaDestino,
                transporte.DuracionAproxFlete,
                transporte.Cliente,
                transporte.Proyecto,
                NombreSolicitante = ObtenerNombreCompletoUsuario(transporte.UsuarioID),
                transporte.Departamento,
                transporte.CompaniaSolicitante,
                transporte.CentroCosto,
                transporte.AutorizadoPresupuesto,
                transporte.TipoRuta,
                transporte.DireccionRecoleccion,
                transporte.Volumetria,
                transporte.TipoUnidad,
                transporte.ComentariosUnidad,
                transporte.Fletero,
                transporte.CostoFlete,
                transporte.EstadoSolicitud,
                destinos = transporte.Destinos
                    .OrderBy(d => d.NumeroDestino)
                    .Select(d => new
                    {
                        d.IdDestino,
                        d.NumeroDestino,
                        d.NombreRecibe,
                        d.ContactoRecibe,
                        d.DireccionDestino
                    }),
                planEmbarque = transporte.PlanEmbarque
                    .OrderBy(p => p.IdPlanEmbarque)
                    .Select(p => new
                    {
                        p.IdPlanEmbarque,
                        p.ClaveSAT,
                        p.Descripcion,
                        p.Cantidad,
                        p.UnidadMedida,
                        p.Peso,
                        p.Valor,
                        p.ValeSalidaFactura
                    }),
                historialEstados = transporte.HistorialEstados
                    .OrderByDescending(h => h.FechaMovimiento)
                    .Select(h => new
                    {
                        h.IdHistorial,
                        h.EstadoAnterior,
                        h.EstadoNuevo,
                        h.UsuarioID,
                        Usuario = ObtenerNombreCompletoUsuario(h.UsuarioID),
                        h.Comentario,
                        h.FechaMovimiento
                    })
            });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarTransporteAjax(Transporte modelo, int? FormularioAdicionalId = null, string? FormularioAdicionalDatosJson = null)
        {
            try
            {
                int idUsuarioActual = ObtenerUsuarioIDActual();
                bool esLogistica = ValidarSiEsDepartamentoLogistica(idUsuarioActual);

                var destinosValidos = (modelo.Destinos ?? new List<TransporteDestino>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.NombreRecibe)
                             || !string.IsNullOrWhiteSpace(x.ContactoRecibe)
                             || !string.IsNullOrWhiteSpace(x.DireccionDestino))
                    .ToList();

                var planValido = (modelo.PlanEmbarque ?? new List<TransportePlanEmbarque>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.Descripcion)
                             || x.Cantidad.HasValue
                             || !string.IsNullOrWhiteSpace(x.UnidadMedida)
                             || !string.IsNullOrWhiteSpace(x.ClaveSAT)
                             || x.Peso.HasValue
                             || x.Valor.HasValue
                             || !string.IsNullOrWhiteSpace(x.ValeSalidaFactura))
                    .ToList();

                if (!destinosValidos.Any())
                {
                    return Json(new { success = false, message = "Agrega al menos un destino." });
                }

                if (!planValido.Any())
                {
                    return Json(new { success = false, message = "Agrega al menos una partida en el plan de embarque." });
                }

                if (modelo.IdTransporte == 0)
                {
                    string nombreUsuario = ObtenerNombreUsuarioActual(idUsuarioActual);
                    string areaUsuario = ObtenerAreaUsuarioActual(idUsuarioActual);

                    modelo.UsuarioID = idUsuarioActual;
                    modelo.Area = areaUsuario;
                    modelo.ElaboradoPor = nombreUsuario;
                    modelo.NombreSolicitante = nombreUsuario;
                    modelo.Departamento = areaUsuario;
                    modelo.FechaEmision = new DateTime(2025, 9, 17);
                    modelo.CodigoFormato = "F-19-06";
                    modelo.EstadoSolicitud = esLogistica ? "Autorizada" : "Pendiente";
                    modelo.EstaBorrado = false;
                    modelo.NotificacionLeida = esLogistica;
                    modelo.FechaRegistro = DateTime.Now;
                    modelo.FechaActualizacion = DateTime.Now;

                    modelo.Destinos = new List<TransporteDestino>();
                    for (int i = 0; i < destinosValidos.Count; i++)
                    {
                        destinosValidos[i].NumeroDestino = i + 1;
                        modelo.Destinos.Add(destinosValidos[i]);
                    }

                    modelo.PlanEmbarque = planValido;

                    _context.Transporte.Add(modelo);
                    await _context.SaveChangesAsync();

                    modelo.Folio = $"TR-{modelo.IdTransporte}";

                    _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
                    {
                        IdTransporte = modelo.IdTransporte,
                        EstadoAnterior = null,
                        EstadoNuevo = modelo.EstadoSolicitud,
                        UsuarioID = idUsuarioActual,
                        Comentario = esLogistica ? "Solicitud creada por Logística" : "Solicitud creada y enviada a autorización",
                        FechaMovimiento = DateTime.Now
                    });

                    await _context.SaveChangesAsync();
                }
                else
                {
                    var registro = await _context.Transporte
                        .Include(x => x.Destinos)
                        .Include(x => x.PlanEmbarque)
                        .FirstOrDefaultAsync(x => x.IdTransporte == modelo.IdTransporte);

                    if (registro == null)
                    {
                        return Json(new { success = false, message = "El registro no existe." });
                    }

                    string? estadoAnterior = registro.EstadoSolicitud;

                    registro.FechaCarga = modelo.FechaCarga;
                    registro.NumeroFactura = modelo.NumeroFactura;
                    registro.HorarioCarga = modelo.HorarioCarga;
                    registro.HorarioLlegadaDestino = modelo.HorarioLlegadaDestino;
                    registro.DuracionAproxFlete = modelo.DuracionAproxFlete;
                    registro.Cliente = modelo.Cliente;
                    registro.Proyecto = modelo.Proyecto;

                    // Estos datos se controlan desde sesión/BD; no deben depender de inputs del formulario.
                    if (string.IsNullOrWhiteSpace(registro.NombreSolicitante))
                    {
                        registro.NombreSolicitante = ObtenerNombreUsuarioActual(registro.UsuarioID);
                    }
                    if (string.IsNullOrWhiteSpace(registro.Departamento))
                    {
                        registro.Departamento = ObtenerAreaUsuarioActual(registro.UsuarioID);
                    }
                    if (string.IsNullOrWhiteSpace(registro.Area))
                    {
                        registro.Area = registro.Departamento;
                    }
                    if (string.IsNullOrWhiteSpace(registro.ElaboradoPor))
                    {
                        registro.ElaboradoPor = registro.NombreSolicitante;
                    }
                    if (!registro.FechaEmision.HasValue)
                    {
                        registro.FechaEmision = new DateTime(2025, 9, 17);
                    }
                    if (string.IsNullOrWhiteSpace(registro.CodigoFormato))
                    {
                        registro.CodigoFormato = "F-19-06";
                    }

                    registro.CompaniaSolicitante = modelo.CompaniaSolicitante;
                    registro.CentroCosto = modelo.CentroCosto;
                    registro.AutorizadoPresupuesto = modelo.AutorizadoPresupuesto;
                    registro.TipoRuta = modelo.TipoRuta;
                    registro.DireccionRecoleccion = modelo.DireccionRecoleccion;
                    registro.Volumetria = modelo.Volumetria;
                    registro.TipoUnidad = modelo.TipoUnidad;
                    registro.ComentariosUnidad = modelo.ComentariosUnidad;
                    registro.Fletero = modelo.Fletero;
                    registro.CostoFlete = modelo.CostoFlete;
                    registro.FechaActualizacion = DateTime.Now;

                    if (!esLogistica)
                    {
                        registro.EstadoSolicitud = "Pendiente";
                        registro.NotificacionLeida = false;
                        registro.MensajeEdicion = "El usuario modificó la solicitud. Requiere nueva revisión de Logística.";
                    }
                    else if (registro.EstadoSolicitud != "Pendiente")
                    {
                        registro.EstadoSolicitud = "Autorizada";
                        registro.NotificacionLeida = true;
                    }

                    _context.TransporteDestinos.RemoveRange(registro.Destinos);
                    _context.TransportePlanEmbarque.RemoveRange(registro.PlanEmbarque);

                    registro.Destinos = new List<TransporteDestino>();
                    for (int i = 0; i < destinosValidos.Count; i++)
                    {
                        registro.Destinos.Add(new TransporteDestino
                        {
                            IdTransporte = registro.IdTransporte,
                            NumeroDestino = i + 1,
                            NombreRecibe = destinosValidos[i].NombreRecibe,
                            ContactoRecibe = destinosValidos[i].ContactoRecibe,
                            DireccionDestino = destinosValidos[i].DireccionDestino
                        });
                    }

                    registro.PlanEmbarque = planValido.Select(p => new TransportePlanEmbarque
                    {
                        IdTransporte = registro.IdTransporte,
                        ClaveSAT = p.ClaveSAT,
                        Descripcion = p.Descripcion,
                        Cantidad = p.Cantidad,
                        UnidadMedida = p.UnidadMedida,
                        Peso = p.Peso,
                        Valor = p.Valor,
                        ValeSalidaFactura = p.ValeSalidaFactura
                    }).ToList();

                    _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
                    {
                        IdTransporte = registro.IdTransporte,
                        EstadoAnterior = estadoAnterior,
                        EstadoNuevo = registro.EstadoSolicitud,
                        UsuarioID = idUsuarioActual,
                        Comentario = esLogistica ? "Solicitud actualizada por Logística" : "Solicitud editada por usuario y enviada a autorización",
                        FechaMovimiento = DateTime.Now
                    });

                    await _context.SaveChangesAsync();
                }

                await GuardarFormularioAdicionalAsync(
                    FormularioAdicionalId,
                    FormularioAdicionalDatosJson,
                    "Transporte",
                    modelo.IdTransporte,
                    idUsuarioActual);

                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                string mensajeError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = "Error: " + mensajeError });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResolverTransporte(int id, string resolucion, string? motivo)
        {
            int usuarioId = ObtenerUsuarioIDActual();
            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var registro = await _context.Transporte.FindAsync(id);
            if (registro == null)
            {
                return Json(new { success = false, message = "No se encontró la solicitud." });
            }

            string? estadoAnterior = registro.EstadoSolicitud;

            if (resolucion == "Aceptada")
            {
                registro.EstadoSolicitud = "Autorizada";
                registro.EstaBorrado = false;
                registro.NotificacionLeida = true;
                registro.MensajeEdicion = null;
            }
            else if (resolucion == "Rechazada")
            {
                registro.EstadoSolicitud = "Rechazada";
                registro.EstaBorrado = true;
                registro.MotivoBorrado = string.IsNullOrWhiteSpace(motivo) ? "Solicitud rechazada por Logística" : motivo;
                registro.FechaBorrado = DateTime.Now;
                registro.BorradoPor = usuarioId;
                registro.NotificacionLeida = true;
            }
            else
            {
                return Json(new { success = false, message = "Resolución no válida." });
            }

            registro.FechaActualizacion = DateTime.Now;

            _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
            {
                IdTransporte = registro.IdTransporte,
                EstadoAnterior = estadoAnterior,
                EstadoNuevo = registro.EstadoSolicitud,
                UsuarioID = usuarioId,
                Comentario = motivo,
                FechaMovimiento = DateTime.Now
            });

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> FinalizarTransporte(int id, string? comentario)
        {
            int usuarioId = ObtenerUsuarioIDActual();

            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var registro = await _context.Transporte.FindAsync(id);

            if (registro == null)
            {
                return Json(new { success = false, message = "No se encontró la solicitud." });
            }

            if (registro.EstaBorrado)
            {
                return Json(new { success = false, message = "No se puede finalizar una solicitud cancelada o eliminada." });
            }

            if (registro.EstadoSolicitud != "Autorizada")
            {
                return Json(new { success = false, message = "Solo se pueden finalizar solicitudes autorizadas." });
            }

            string? estadoAnterior = registro.EstadoSolicitud;

            registro.EstadoSolicitud = "Finalizada";
            registro.FechaActualizacion = DateTime.Now;
            registro.NotificacionLeida = true;
            registro.MensajeEdicion = null;

            _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
            {
                IdTransporte = registro.IdTransporte,
                EstadoAnterior = estadoAnterior,
                EstadoNuevo = "Finalizada",
                UsuarioID = usuarioId,
                Comentario = string.IsNullOrWhiteSpace(comentario) ? "Transporte finalizado por Logística." : comentario,
                FechaMovimiento = DateTime.Now
            });

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");

            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult MarcarAvisoLeido(int id)
        {
            var registro = _context.Transporte.Find(id);
            if (registro != null)
            {
                registro.NotificacionLeida = true;
                _context.SaveChanges();
            }
            return Ok();
        }

        // ==========================================
        // MÓDULO: GUÍAS (PAQUETERÍA EXTERNA)
        // ==========================================
public IActionResult Guias()
{
    int usuarioId = ObtenerUsuarioIDActual();
    bool esLogistica = ValidarSiEsDepartamentoLogistica(usuarioId);

    ViewBag.EsLogistica = esLogistica;
    ViewBag.UsuarioActualId = usuarioId;
    ViewBag.EmpresasGuias = ObtenerEmpresasSelectList();

    ViewBag.UsuariosIntranet = _context.Usuarios
        .Include(u => u.Persona)
        .AsNoTracking()
        .ToDictionary(
            u => u.UsuarioID,
            u =>
            {
                var nombreCompleto = string.Concat(u.Persona?.Nombre, " ", u.Persona?.ApellidoPaterno, " ", u.Persona?.ApellidoMaterno).Trim();
                return string.IsNullOrWhiteSpace(nombreCompleto)
                    ? u.Username ?? "Usuario"
                    : nombreCompleto;
            });

    List<Guia> lista;
    if (esLogistica)
    {
        // Logística ve todas las guías
        lista = _context.Guias.ToList();
    }
    else
    {
        // USUARIO FINAL: Solo sus guías, no borradas y ya autorizadas/finalizadas/activas
        lista = _context.Guias
            .Where(g => g.UsuarioID == usuarioId 
                     && !g.EstaBorrado 
                     && (g.EstadoEdicion == "Autorizada" || g.EstadoEdicion == "Finalizada" || g.EstadoEdicion == "Activa"))
            .ToList();
    }

    return View(lista);
}

        [HttpGet]
        public IActionResult ObtenerGuia(int id)
        {
            var guia = _context.Guias.Find(id);
            return guia == null ? NotFound() : Json(guia);
        }

        [HttpPost]
        public async Task<IActionResult> GuardarGuiaAjax(Guia modelo, int? FormularioAdicionalId = null, string? FormularioAdicionalDatosJson = null)
        {
            try
            {
                int idUsuarioActual = ObtenerUsuarioIDActual();
                bool esDeLogistica = ValidarSiEsDepartamentoLogistica(idUsuarioActual);

                if (modelo.IdGuia == 0)
                {
                    modelo.UsuarioID = idUsuarioActual;
                    modelo.EstadoEdicion = "Activo";
                    modelo.NotificacionLeida = true;
                    modelo.EstaBorrado = false;
                    modelo.FechaSolicitud = DateTime.Now;

                    _context.Guias.Add(modelo);
                }
                else
                {
                    var registroExistente = _context.Guias.Find(modelo.IdGuia);
                    if (registroExistente != null)
                    {
                        modelo.UsuarioID = registroExistente.UsuarioID;
                        modelo.FechaSolicitud = registroExistente.FechaSolicitud;
                        modelo.EstaBorrado = registroExistente.EstaBorrado;

                        if (!esDeLogistica)
                        {
                            var opcionesJson = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };
                            modelo.DatosAntiguos = JsonSerializer.Serialize(registroExistente, opcionesJson);
                            modelo.EstadoEdicion = "Pendiente";
                            modelo.MensajeEdicion = null;
                            modelo.NotificacionLeida = false;
                        }
                        else
                        {
                            modelo.EstadoEdicion = "Activo";
                            modelo.NotificacionLeida = true;
                            modelo.DatosAntiguos = null;
                        }
                        _context.Entry(registroExistente).CurrentValues.SetValues(modelo);
                    }
                }

                await _context.SaveChangesAsync();

                await GuardarFormularioAdicionalAsync(
                    FormularioAdicionalId,
                    FormularioAdicionalDatosJson,
                    "Guias",
                    modelo.IdGuia,
                    idUsuarioActual);

                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult ObtenerDiferenciasGuia(int id)
        {
            var registro = _context.Guias.Find(id);
            if (registro != null && !string.IsNullOrEmpty(registro.DatosAntiguos))
            {
                var viejo = JsonSerializer.Deserialize<Guia>(registro.DatosAntiguos);
                return Json(new { success = true, viejo = viejo, nuevo = registro });
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> ResolverEdicionGuia(int id, string resolucion, string motivo)
        {
            var registro = _context.Guias.Find(id);
            if (registro != null)
            {
                if (resolucion == "Rechazada" && !string.IsNullOrEmpty(registro.DatosAntiguos))
                {
                    var datosViejos = JsonSerializer.Deserialize<Guia>(registro.DatosAntiguos);
                    if (datosViejos != null)
                    {
                        _context.Entry(registro).CurrentValues.SetValues(datosViejos);
                    }
                }

                registro.EstadoEdicion = resolucion;
                registro.MensajeEdicion = motivo;
                registro.NotificacionLeida = false;
                registro.DatosAntiguos = null;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public IActionResult MarcarAvisoGuiaLeido(int id)
        {
            var reg = _context.Guias.Find(id);
            if (reg != null)
            {
                reg.NotificacionLeida = true;
                _context.SaveChanges();
            }
            return Ok();
        }


        private async Task GuardarFormularioAdicionalAsync(
            int? idFormulario,
            string? datosJson,
            string origenTipo,
            int origenId,
            int usuarioId)
        {
            if (!idFormulario.HasValue || idFormulario.Value <= 0)
                return;

            if (origenId <= 0 || usuarioId <= 0)
                return;

            if (string.IsNullOrWhiteSpace(datosJson))
                return;

            var valores = ConvertirJsonFormularioAdicionalADiccionario(datosJson);

            if (valores.Count == 0)
                return;

            var respuesta = new FormularioRespuestaViewModel
            {
                IdFormulario = idFormulario.Value,
                UsuarioID = usuarioId,
                Estado = "Registrado",
                Valores = valores,
                OrigenTipo = origenTipo,
                OrigenID = origenId
            };

            await _formulariosSqlService.GuardarRespuestaAsync(respuesta);
        }

        private Dictionary<string, string?> ConvertirJsonFormularioAdicionalADiccionario(string datosJson)
        {
            var valores = new Dictionary<string, string?>();

            if (string.IsNullOrWhiteSpace(datosJson))
                return valores;

            try
            {
                using var document = JsonDocument.Parse(datosJson);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return valores;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    valores[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Array => property.Value.GetRawText(),
                        JsonValueKind.Object => property.Value.GetRawText(),
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch
            {
                return new Dictionary<string, string?>();
            }

            return valores;
        }


        // ==========================================
        // MÉTODOS COMPARTIDOS
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SolicitarBorrado(int id, string tipo, string motivo)
        {
            if (tipo == "Transporte")
            {
                var registro = _context.Transporte.Find(id);
                if (registro != null)
                {
                    string? estadoAnterior = registro.EstadoSolicitud;
                    registro.EstaBorrado = true;
                    registro.EstadoSolicitud = "Cancelada";
                    registro.MotivoBorrado = motivo;
                    registro.FechaBorrado = DateTime.Now;
                    registro.BorradoPor = ObtenerUsuarioIDActual();
                    registro.FechaActualizacion = DateTime.Now;

                    _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
                    {
                        IdTransporte = registro.IdTransporte,
                        EstadoAnterior = estadoAnterior,
                        EstadoNuevo = "Cancelada",
                        UsuarioID = ObtenerUsuarioIDActual(),
                        Comentario = motivo,
                        FechaMovimiento = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return RedirectToAction("Transporte");
            }
            else if (tipo == "Guia")
            {
                var registro = _context.Guias.Find(id);
                if (registro != null)
                {
                    registro.EstaBorrado = true;
                    registro.MotivoBorrado = motivo;
                    registro.FechaBorrado = DateTime.Now;
                    registro.BorradoPor = ObtenerUsuarioIDActual();
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return RedirectToAction("Guias");
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> RestaurarBorradoAjax(int id, string tipo)
        {
            try
            {
                if (tipo == "Transporte")
                {
                    var registro = _context.Transporte.Find(id);
                    if (registro != null)
                    {
                        string? estadoAnterior = registro.EstadoSolicitud;
                        registro.EstaBorrado = false;
                        registro.EstadoSolicitud = "Pendiente";
                        registro.MotivoBorrado = null;
                        registro.FechaBorrado = null;
                        registro.BorradoPor = null;
                        registro.NotificacionLeida = false;
                        registro.FechaActualizacion = DateTime.Now;

                        _context.TransporteHistorialEstados.Add(new TransporteHistorialEstado
                        {
                            IdTransporte = registro.IdTransporte,
                            EstadoAnterior = estadoAnterior,
                            EstadoNuevo = "Pendiente",
                            UsuarioID = ObtenerUsuarioIDActual(),
                            Comentario = "Solicitud restaurada y enviada nuevamente a revisión",
                            FechaMovimiento = DateTime.Now
                        });
                    }
                }
                else if (tipo == "Guia")
                {
                    var registro = _context.Guias.Find(id);
                    if (registro != null) registro.EstaBorrado = false;
                }
                else
                {
                    return Json(new { success = false, message = "Tipo no válido." });
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public async Task<IActionResult> EliminarTransporteFinalizadoDefinitivo(int id)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIDActual();
                if (!ValidarSiEsDepartamentoLogistica(usuarioId))
                {
                    return Json(new { success = false, message = "No tienes permisos para eliminar transportes finalizados." });
                }

                var registro = await _context.Transporte
                    .Include(t => t.Destinos)
                    .Include(t => t.PlanEmbarque)
                    .Include(t => t.HistorialEstados)
                    .FirstOrDefaultAsync(t => t.IdTransporte == id);

                if (registro == null)
                {
                    return Json(new { success = false, message = "No se encontró el transporte." });
                }

                if (registro.EstaBorrado || registro.EstadoSolicitud != "Finalizada")
                {
                    return Json(new { success = false, message = "Solo se pueden eliminar definitivamente transportes con estado Finalizada." });
                }

                if (registro.Destinos != null && registro.Destinos.Any())
                    _context.TransporteDestinos.RemoveRange(registro.Destinos);

                if (registro.PlanEmbarque != null && registro.PlanEmbarque.Any())
                    _context.TransportePlanEmbarque.RemoveRange(registro.PlanEmbarque);

                if (registro.HistorialEstados != null && registro.HistorialEstados.Any())
                    _context.TransporteHistorialEstados.RemoveRange(registro.HistorialEstados);

                _context.Transporte.Remove(registro);

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EliminarTransporteDefinitivo(int id)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIDActual();
                if (!ValidarSiEsDepartamentoLogistica(usuarioId))
                {
                    return Json(new { success = false, message = "No tienes permisos para eliminar definitivamente solicitudes de transporte." });
                }

                var registro = await _context.Transporte
                    .Include(t => t.Destinos)
                    .Include(t => t.PlanEmbarque)
                    .Include(t => t.HistorialEstados)
                    .FirstOrDefaultAsync(t => t.IdTransporte == id);

                if (registro == null)
                {
                    return Json(new { success = false, message = "No se encontró la solicitud." });
                }

                if (!registro.EstaBorrado)
                {
                    return Json(new { success = false, message = "Solo se pueden eliminar definitivamente solicitudes que ya están canceladas o eliminadas." });
                }

                if (registro.Destinos != null && registro.Destinos.Any())
                    _context.TransporteDestinos.RemoveRange(registro.Destinos);

                if (registro.PlanEmbarque != null && registro.PlanEmbarque.Any())
                    _context.TransportePlanEmbarque.RemoveRange(registro.PlanEmbarque);

                if (registro.HistorialEstados != null && registro.HistorialEstados.Any())
                    _context.TransporteHistorialEstados.RemoveRange(registro.HistorialEstados);

                _context.Transporte.Remove(registro);

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ActualizacionLogistica");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult ObtenerDatosFinancieros(string rango)
        {
            int usuarioId = ObtenerUsuarioIDActual();
            if (!ValidarSiEsDepartamentoLogistica(usuarioId)) return Forbid();

            DateTime hoy = DateTime.Now;
            string[] etiquetas;
            decimal[] ingresos;
            decimal[] gastos;

            if (rango == "Semana")
            {
                etiquetas = new string[5];
                ingresos = new decimal[5];
                gastos = new decimal[5];

                for (int i = 0; i < 5; i++)
                {
                    DateTime dia = hoy.AddDays(-4 + i);
                    etiquetas[i] = dia.ToString("ddd dd");

                    ingresos[i] = _context.Transporte
                        .Where(x => !x.EstaBorrado && x.FechaCarga.HasValue && x.FechaCarga.Value.Date == dia.Date)
                        .Sum(x => (decimal?)x.CostoFlete) ?? 0;

                    gastos[i] = _context.Guias
                        .Where(x => !x.EstaBorrado && x.FechaSolicitud.HasValue && x.FechaSolicitud.Value.Date == dia.Date)
                        .Sum(x => (decimal?)x.Costo) ?? 0;
                }
            }
            else if (rango == "Mes")
            {
                etiquetas = new[] { "Semana 1", "Semana 2", "Semana 3", "Semana 4" };
                ingresos = new decimal[4];
                gastos = new decimal[4];

                var transportesMes = _context.Transporte
                    .Where(x => !x.EstaBorrado && x.FechaCarga.HasValue && x.FechaCarga.Value.Month == hoy.Month && x.FechaCarga.Value.Year == hoy.Year)
                    .ToList();

                var guiasMes = _context.Guias
                    .Where(x => !x.EstaBorrado && x.FechaSolicitud.HasValue && x.FechaSolicitud.Value.Month == hoy.Month && x.FechaSolicitud.Value.Year == hoy.Year)
                    .ToList();

                for (int i = 0; i < 4; i++)
                {
                    int diaInicio = (i * 7) + 1;
                    int diaFin = (i == 3) ? 31 : diaInicio + 6;

                    ingresos[i] = transportesMes
                        .Where(x => x.FechaCarga!.Value.Day >= diaInicio && x.FechaCarga!.Value.Day <= diaFin)
                        .Sum(x => x.CostoFlete ?? 0);

                    gastos[i] = guiasMes
                        .Where(x => x.FechaSolicitud!.Value.Day >= diaInicio && x.FechaSolicitud!.Value.Day <= diaFin)
                        .Sum(x => x.Costo);
                }
            }
            else
            {
                etiquetas = new[] { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
                ingresos = new decimal[12];
                gastos = new decimal[12];

                for (int i = 0; i < 12; i++)
                {
                    ingresos[i] = _context.Transporte
                        .Where(x => !x.EstaBorrado && x.FechaCarga.HasValue && x.FechaCarga.Value.Year == hoy.Year && x.FechaCarga.Value.Month == i + 1)
                        .Sum(x => (decimal?)x.CostoFlete) ?? 0;

                    gastos[i] = _context.Guias
                        .Where(x => !x.EstaBorrado && x.FechaSolicitud.HasValue && x.FechaSolicitud.Value.Year == hoy.Year && x.FechaSolicitud.Value.Month == i + 1)
                        .Sum(x => (decimal?)x.Costo) ?? 0;
                }
            }
            return Json(new { etiquetas, ingresos, gastos });
        }

        [HttpGet]
        public async Task<IActionResult> DescargarTransportePDF(int id)
        {
            var transporte = await _context.Transporte
                .Include(x => x.Destinos)
                .Include(x => x.PlanEmbarque)
                .FirstOrDefaultAsync(x => x.IdTransporte == id);

            if (transporte == null)
            {
                return NotFound();
            }

            string nombreCompleto = ObtenerNombreCompletoUsuario(transporte.UsuarioID);
            transporte.ElaboradoPor = nombreCompleto;
            transporte.NombreSolicitante = nombreCompleto;

            var formulariosAdicionales = await ObtenerFormulariosAdicionalesPdfAsync("Transporte", transporte.IdTransporte);

            var pdfBytes = GenerarPdfTransporte(transporte, formulariosAdicionales);
            var nombreArchivo = $"Solicitud_Transporte_{transporte.Folio ?? transporte.IdTransporte.ToString()}.pdf";

            return File(pdfBytes, "application/pdf", nombreArchivo);
        }

        [HttpGet]
        public async Task<IActionResult> DescargarReportePDF(int id)
        {
            return await DescargarTransportePDF(id);
        }


        [HttpGet]
        public IActionResult DescargarReporteGuiasPDF()
        {
            int usuarioId = ObtenerUsuarioIDActual();

            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var guias = _context.Guias
                .AsNoTracking()
                .Where(g => !g.EstaBorrado)
                .OrderByDescending(g => g.FechaSolicitud)
                .ToList();

            var pdfBytes = GenerarPdfReporteGuias(guias);
            var nombreArchivo = $"Auditoria_Guias_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            return File(pdfBytes, "application/pdf", nombreArchivo);
        }

        [HttpGet]
        public IActionResult DescargarReporteGuiasExcel()
        {
            int usuarioId = ObtenerUsuarioIDActual();

            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var guias = _context.Guias
                .AsNoTracking()
                .OrderByDescending(g => g.FechaSolicitud)
                .ToList();

            using var workbook = new XLWorkbook();

            var ws = workbook.Worksheets.Add("Guías");

            string[] encabezados =
            {
                "Folio",
                "Estado",
                "Fecha solicitud",
                "Fecha envío",
                "Empresa",
                "Cliente / Proyecto",
                "Quién gestiona",
                "Tipo requerimiento",
                "Tipo entrega",
                "Remitente",
                "Teléfono remitente",
                "Origen",
                "CP origen",
                "Destinatario",
                "Teléfono destinatario",
                "Correo destinatario",
                "Destino",
                "CP destino",
                "Tipo envío",
                "Contenido declarado",
                "Información peso/dimensiones",
                "Peso kg",
                "Largo cm",
                "Ancho cm",
                "Alto cm",
                "Cadena frío",
                "Costo",
                "Observaciones",
                "Cancelada / eliminada"
            };

            for (int i = 0; i < encabezados.Length; i++)
            {
                ws.Cell(1, i + 1).Value = encabezados[i];
            }

            int fila = 2;

            foreach (var g in guias)
            {
                ws.Cell(fila, 1).Value = $"GU-{g.IdGuia}";
                ws.Cell(fila, 2).Value = g.EstaBorrado ? "Cancelada / Eliminada" : "Activa";
                ws.Cell(fila, 3).Value = g.FechaSolicitud;
                ws.Cell(fila, 4).Value = g.FechaEnvio;
                ws.Cell(fila, 5).Value = g.Empresa ?? "";
                ws.Cell(fila, 6).Value = g.ClienteProyecto ?? "";
                ws.Cell(fila, 7).Value = g.QuienGestiona ?? "";
                ws.Cell(fila, 8).Value = g.TipoRequerimiento ?? "";
                ws.Cell(fila, 9).Value = g.TipoEntrega ?? "";
                ws.Cell(fila, 10).Value = g.RemitenteNombre ?? "";
                ws.Cell(fila, 11).Value = g.RemitenteTelefono ?? "";
                ws.Cell(fila, 12).Value = g.Origen ?? "";
                ws.Cell(fila, 13).Value = g.CodigoPostalOrigen ?? "";
                ws.Cell(fila, 14).Value = g.DestinatarioNombre ?? "";
                ws.Cell(fila, 15).Value = g.DestinatarioTelefono ?? "";
                ws.Cell(fila, 16).Value = g.DestinatarioCorreo ?? "";
                ws.Cell(fila, 17).Value = g.Destino ?? "";
                ws.Cell(fila, 18).Value = g.CodigoPostalDestino ?? "";
                ws.Cell(fila, 19).Value = g.TipoEnvio ?? "";
                ws.Cell(fila, 20).Value = g.ContenidoDeclarado ?? "";
                ws.Cell(fila, 21).Value = g.InformacionDimensionesPeso ?? "";
                ws.Cell(fila, 22).Value = g.PesoKg;
                ws.Cell(fila, 23).Value = g.LargoCm;
                ws.Cell(fila, 24).Value = g.AnchoCm;
                ws.Cell(fila, 25).Value = g.AltoCm;
                ws.Cell(fila, 26).Value = g.RequiereCadenaFrio == true ? "Sí" : "No";
                ws.Cell(fila, 27).Value = g.Costo;
                ws.Cell(fila, 28).Value = g.Observaciones ?? "";
                ws.Cell(fila, 29).Value = g.EstaBorrado ? "Sí" : "No";

                fila++;
            }

            AplicarEstiloReporteExcel(ws, encabezados.Length, fila - 1);

            ws.Column(3).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Column(4).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Column(22).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(23).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(24).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(25).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(27).Style.NumberFormat.Format = "$ #,##0.00";

            var resumen = workbook.Worksheets.Add("Resumen");
            resumen.Cell("A1").Value = "Reporte de Auditoría de Guías";
            resumen.Cell("A2").Value = "Fecha de generación";
            resumen.Cell("B2").Value = DateTime.Now;
            resumen.Cell("A3").Value = "Total de guías";
            resumen.Cell("B3").Value = guias.Count;
            resumen.Cell("A4").Value = "Guías activas";
            resumen.Cell("B4").Value = guias.Count(g => !g.EstaBorrado);
            resumen.Cell("A5").Value = "Guías canceladas / eliminadas";
            resumen.Cell("B5").Value = guias.Count(g => g.EstaBorrado);
            resumen.Cell("A6").Value = "Cadena de frío";
            resumen.Cell("B6").Value = guias.Count(g => g.RequiereCadenaFrio == true);
            resumen.Cell("A7").Value = "Costo total";
            resumen.Cell("B7").Value = guias.Sum(g => g.Costo);

            resumen.Range("A1:B1").Merge();
            resumen.Range("A1:B1").Style.Font.Bold = true;
            resumen.Range("A1:B1").Style.Font.FontSize = 16;
            resumen.Range("A1:B1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            resumen.Range("A2:A7").Style.Font.Bold = true;
            resumen.Cell("B2").Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            resumen.Cell("B7").Style.NumberFormat.Format = "$ #,##0.00";
            resumen.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            string nombreArchivo = $"Auditoria_Guias_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombreArchivo
            );
        }

        [HttpGet]
        public IActionResult DescargarReporteTransportesExcel()
        {
            int usuarioId = ObtenerUsuarioIDActual();

            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var transportes = _context.Transporte
                .AsNoTracking()
                .Include(t => t.Destinos)
                .Include(t => t.PlanEmbarque)
                .Include(t => t.HistorialEstados)
                .OrderByDescending(t => t.FechaEmision)
                .ToList();

            using var workbook = new XLWorkbook();

            // ------------------------------------------------------------------------
            // Hoja 1: Transportes
            // ------------------------------------------------------------------------
            var ws = workbook.Worksheets.Add("Transportes");

            string[] encabezadosTransportes =
            {
                "Folio",
                "Estado",
                "Fecha emisión",
                "Fecha carga",
                "# Factura",
                "Horario carga",
                "Horario llegada destino",
                "Duración aprox. flete",
                "Cliente",
                "Proyecto",
                "Solicitante",
                "Departamento",
                "Compañía solicitante",
                "Centro costo",
                "Autorizado presupuesto",
                "Tipo ruta",
                "Dirección recolección",
                "Volumetría",
                "Tipo unidad",
                "Comentarios unidad",
                "Fletero",
                "Costo flete",
                "Cancelado / eliminado"
            };

            for (int i = 0; i < encabezadosTransportes.Length; i++)
            {
                ws.Cell(1, i + 1).Value = encabezadosTransportes[i];
            }

            int fila = 2;

            foreach (var t in transportes)
            {
                string solicitante = !string.IsNullOrWhiteSpace(t.NombreSolicitante)
                    ? t.NombreSolicitante
                    : ObtenerNombreCompletoUsuario(t.UsuarioID);

                ws.Cell(fila, 1).Value = t.Folio ?? $"TR-{t.IdTransporte}";
                ws.Cell(fila, 2).Value = t.EstaBorrado ? "Cancelada / Eliminada" : (t.EstadoSolicitud ?? "");
                ws.Cell(fila, 3).Value = t.FechaEmision;
                ws.Cell(fila, 4).Value = t.FechaCarga;
                ws.Cell(fila, 5).Value = t.NumeroFactura ?? "";
                ws.Cell(fila, 6).Value = t.HorarioCarga ?? "";
                ws.Cell(fila, 7).Value = t.HorarioLlegadaDestino ?? "";
                ws.Cell(fila, 8).Value = t.DuracionAproxFlete ?? "";
                ws.Cell(fila, 9).Value = t.Cliente ?? "";
                ws.Cell(fila, 10).Value = t.Proyecto ?? "";
                ws.Cell(fila, 11).Value = solicitante;
                ws.Cell(fila, 12).Value = t.Departamento ?? "";
                ws.Cell(fila, 13).Value = t.CompaniaSolicitante ?? "";
                ws.Cell(fila, 14).Value = t.CentroCosto ?? "";
                ws.Cell(fila, 15).Value = t.AutorizadoPresupuesto ?? "";
                ws.Cell(fila, 16).Value = t.TipoRuta ?? "";
                ws.Cell(fila, 17).Value = t.DireccionRecoleccion ?? "";
                ws.Cell(fila, 18).Value = t.Volumetria ?? "";
                ws.Cell(fila, 19).Value = t.TipoUnidad ?? "";
                ws.Cell(fila, 20).Value = t.ComentariosUnidad ?? "";
                ws.Cell(fila, 21).Value = t.Fletero ?? "";
                ws.Cell(fila, 22).Value = t.CostoFlete;
                ws.Cell(fila, 23).Value = t.EstaBorrado ? "Sí" : "No";

                fila++;
            }

            AplicarEstiloReporteExcel(ws, encabezadosTransportes.Length, fila - 1);
            ws.Column(3).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            ws.Column(4).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Column(22).Style.NumberFormat.Format = "$ #,##0.00";

            // ------------------------------------------------------------------------
            // Hoja 2: Destinos
            // ------------------------------------------------------------------------
            var wsDestinos = workbook.Worksheets.Add("Destinos");

            string[] encabezadosDestinos =
            {
                "Folio transporte",
                "# Destino",
                "Nombre recibe",
                "Contacto recibe",
                "Dirección destino"
            };

            for (int i = 0; i < encabezadosDestinos.Length; i++)
            {
                wsDestinos.Cell(1, i + 1).Value = encabezadosDestinos[i];
            }

            fila = 2;

            foreach (var t in transportes)
            {
                string folio = t.Folio ?? $"TR-{t.IdTransporte}";

                foreach (var d in t.Destinos.OrderBy(d => d.NumeroDestino))
                {
                    wsDestinos.Cell(fila, 1).Value = folio;
                    wsDestinos.Cell(fila, 2).Value = d.NumeroDestino;
                    wsDestinos.Cell(fila, 3).Value = d.NombreRecibe ?? "";
                    wsDestinos.Cell(fila, 4).Value = d.ContactoRecibe ?? "";
                    wsDestinos.Cell(fila, 5).Value = d.DireccionDestino ?? "";
                    fila++;
                }
            }

            AplicarEstiloReporteExcel(wsDestinos, encabezadosDestinos.Length, fila - 1);

            // ------------------------------------------------------------------------
            // Hoja 3: Plan de Embarque
            // ------------------------------------------------------------------------
            var wsPlan = workbook.Worksheets.Add("Plan de Embarque");

            string[] encabezadosPlan =
            {
                "Folio transporte",
                "Clave SAT",
                "Descripción",
                "Cantidad",
                "UM",
                "Peso",
                "Valor",
                "Vale salida / factura"
            };

            for (int i = 0; i < encabezadosPlan.Length; i++)
            {
                wsPlan.Cell(1, i + 1).Value = encabezadosPlan[i];
            }

            fila = 2;

            foreach (var t in transportes)
            {
                string folio = t.Folio ?? $"TR-{t.IdTransporte}";

                foreach (var p in t.PlanEmbarque)
                {
                    wsPlan.Cell(fila, 1).Value = folio;
                    wsPlan.Cell(fila, 2).Value = p.ClaveSAT ?? "";
                    wsPlan.Cell(fila, 3).Value = p.Descripcion ?? "";
                    wsPlan.Cell(fila, 4).Value = p.Cantidad;
                    wsPlan.Cell(fila, 5).Value = p.UnidadMedida ?? "";
                    wsPlan.Cell(fila, 6).Value = p.Peso;
                    wsPlan.Cell(fila, 7).Value = p.Valor;
                    wsPlan.Cell(fila, 8).Value = p.ValeSalidaFactura ?? "";
                    fila++;
                }
            }

            AplicarEstiloReporteExcel(wsPlan, encabezadosPlan.Length, fila - 1);
            wsPlan.Column(4).Style.NumberFormat.Format = "#,##0.00";
            wsPlan.Column(6).Style.NumberFormat.Format = "#,##0.00";
            wsPlan.Column(7).Style.NumberFormat.Format = "$ #,##0.00";

            // ------------------------------------------------------------------------
            // Hoja 4: Historial
            // ------------------------------------------------------------------------
            var wsHistorial = workbook.Worksheets.Add("Historial");

            string[] encabezadosHistorial =
            {
                "Folio transporte",
                "Fecha movimiento",
                "Estado anterior",
                "Estado nuevo",
                "Usuario",
                "Comentario"
            };

            for (int i = 0; i < encabezadosHistorial.Length; i++)
            {
                wsHistorial.Cell(1, i + 1).Value = encabezadosHistorial[i];
            }

            fila = 2;

            foreach (var t in transportes)
            {
                string folio = t.Folio ?? $"TR-{t.IdTransporte}";

                foreach (var h in t.HistorialEstados.OrderBy(h => h.FechaMovimiento))
                {
                    wsHistorial.Cell(fila, 1).Value = folio;
                    wsHistorial.Cell(fila, 2).Value = h.FechaMovimiento;
                    wsHistorial.Cell(fila, 3).Value = h.EstadoAnterior ?? "";
                    wsHistorial.Cell(fila, 4).Value = h.EstadoNuevo ?? "";
                    wsHistorial.Cell(fila, 5).Value = ObtenerNombreCompletoUsuario(h.UsuarioID);
                    wsHistorial.Cell(fila, 6).Value = h.Comentario ?? "";
                    fila++;
                }
            }

            AplicarEstiloReporteExcel(wsHistorial, encabezadosHistorial.Length, fila - 1);
            wsHistorial.Column(2).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";

            // ------------------------------------------------------------------------
            // Hoja 5: Resumen
            // ------------------------------------------------------------------------
            var resumen = workbook.Worksheets.Add("Resumen");

            resumen.Cell("A1").Value = "Reporte de Auditoría de Transportes";
            resumen.Cell("A2").Value = "Fecha generación";
            resumen.Cell("B2").Value = DateTime.Now;
            resumen.Cell("A3").Value = "Total transportes";
            resumen.Cell("B3").Value = transportes.Count;
            resumen.Cell("A4").Value = "Pendientes";
            resumen.Cell("B4").Value = transportes.Count(t => !t.EstaBorrado && t.EstadoSolicitud == "Pendiente");
            resumen.Cell("A5").Value = "Autorizadas";
            resumen.Cell("B5").Value = transportes.Count(t => !t.EstaBorrado && t.EstadoSolicitud == "Autorizada");
            resumen.Cell("A6").Value = "Finalizadas";
            resumen.Cell("B6").Value = transportes.Count(t => !t.EstaBorrado && t.EstadoSolicitud == "Finalizada");
            resumen.Cell("A7").Value = "Canceladas / eliminadas";
            resumen.Cell("B7").Value = transportes.Count(t => t.EstaBorrado);
            resumen.Cell("A8").Value = "Costo total fletes";
            resumen.Cell("B8").Value = transportes.Sum(t => t.CostoFlete ?? 0);

            resumen.Range("A1:B1").Merge();
            resumen.Range("A1:B1").Style.Font.Bold = true;
            resumen.Range("A1:B1").Style.Font.FontSize = 16;
            resumen.Range("A1:B1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            resumen.Range("A2:A8").Style.Font.Bold = true;
            resumen.Cell("B2").Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            resumen.Cell("B8").Style.NumberFormat.Format = "$ #,##0.00";
            resumen.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            string nombreArchivo = $"Auditoria_Transportes_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombreArchivo
            );
        }

        private void AplicarEstiloReporteExcel(IXLWorksheet ws, int totalColumnas, int ultimaFila)
        {
            if (ultimaFila < 1)
            {
                ultimaFila = 1;
            }

            var rangoEncabezado = ws.Range(1, 1, 1, totalColumnas);
            rangoEncabezado.Style.Font.Bold = true;
            rangoEncabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B1F3A");
            rangoEncabezado.Style.Font.FontColor = XLColor.White;
            rangoEncabezado.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangoEncabezado.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var rangoDatos = ws.Range(1, 1, ultimaFila, totalColumnas);
            rangoDatos.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, ultimaFila, totalColumnas).SetAutoFilter();
            ws.Columns().AdjustToContents();

            foreach (var column in ws.ColumnsUsed())
            {
                if (column.Width > 45)
                {
                    column.Width = 45;
                }
            }
        }
        [HttpGet]
        public IActionResult DescargarReporteTransportesPDF()
        {
            int usuarioId = ObtenerUsuarioIDActual();

            if (!ValidarSiEsDepartamentoLogistica(usuarioId))
            {
                return Forbid();
            }

            var transportes = _context.Transporte
                .AsNoTracking()
                .Include(t => t.Destinos)
                .Include(t => t.PlanEmbarque)
                .OrderByDescending(t => t.FechaEmision)
                .ToList();

            var pdfBytes = GenerarPdfReporteTransportes(transportes);
            var nombreArchivo = $"Auditoria_Transportes_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            return File(pdfBytes, "application/pdf", nombreArchivo);
        }
        private class PdfFormularioAdicional
        {
            public int IdRespuesta { get; set; }
            public int IdFormulario { get; set; }
            public string NombreFormulario { get; set; } = string.Empty;
            public string? Categoria { get; set; }
            public DateTime FechaRegistro { get; set; }
            public List<PdfFormularioCampo> Campos { get; set; } = new();
        }

        private class PdfFormularioCampo
        {
            public string Clave { get; set; } = string.Empty;
            public string Etiqueta { get; set; } = string.Empty;
            public string Tipo { get; set; } = "texto";
            public string? Valor { get; set; }
        }

        private async Task<List<PdfFormularioAdicional>> ObtenerFormulariosAdicionalesPdfAsync(string origenTipo, int origenId)
        {
            var lista = new List<PdfFormularioAdicional>();

            if (string.IsNullOrWhiteSpace(origenTipo) || origenId <= 0)
            {
                return lista;
            }

            var connection = _context.Database.GetDbConnection();
            var cerrarConexion = connection.State != ConnectionState.Open;

            if (cerrarConexion)
            {
                await connection.OpenAsync();
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT
                        r.IdRespuesta,
                        r.IdFormulario,
                        p.Nombre AS NombreFormulario,
                        p.Categoria,
                        p.EstructuraJson,
                        r.DatosJson,
                        r.FechaRegistro
                    FROM dbo.FormularioRespuestas r
                    INNER JOIN dbo.FormularioPlantillas p
                        ON p.IdFormulario = r.IdFormulario
                    WHERE r.EstaBorrado = 0
                      AND r.OrigenID = @OrigenID
                      AND (
                            r.OrigenTipo = @OrigenTipo
                            OR (@OrigenTipo = 'Transporte' AND r.OrigenTipo IN ('Transportes', 'SolicitudTransporte', 'Solicitud de Transporte'))
                            OR (@OrigenTipo = 'Guias' AND r.OrigenTipo IN ('Guia', 'Guías', 'Guias'))
                          )
                    ORDER BY r.FechaRegistro DESC;";

                var pOrigenId = command.CreateParameter();
                pOrigenId.ParameterName = "@OrigenID";
                pOrigenId.Value = origenId;
                command.Parameters.Add(pOrigenId);

                var pOrigenTipo = command.CreateParameter();
                pOrigenTipo.ParameterName = "@OrigenTipo";
                pOrigenTipo.Value = origenTipo;
                command.Parameters.Add(pOrigenTipo);

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var estructuraJson = reader["EstructuraJson"]?.ToString() ?? "[]";
                    var datosJson = reader["DatosJson"]?.ToString() ?? "{}";

                    var campos = ConvertirFormularioPdfCampos(estructuraJson, datosJson);

                    lista.Add(new PdfFormularioAdicional
                    {
                        IdRespuesta = Convert.ToInt32(reader["IdRespuesta"]),
                        IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                        NombreFormulario = reader["NombreFormulario"]?.ToString() ?? "Formulario adicional",
                        Categoria = reader["Categoria"] == DBNull.Value ? null : reader["Categoria"]?.ToString(),
                        FechaRegistro = reader["FechaRegistro"] == DBNull.Value ? DateTime.Now : Convert.ToDateTime(reader["FechaRegistro"]),
                        Campos = campos
                    });
                }
            }
            finally
            {
                if (cerrarConexion)
                {
                    await connection.CloseAsync();
                }
            }

            return lista;
        }

        private static List<PdfFormularioCampo> ConvertirFormularioPdfCampos(string estructuraJson, string datosJson)
        {
            var campos = new List<PdfFormularioCampo>();
            var valores = ConvertirJsonPlanoADiccionario(datosJson);

            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(estructuraJson) ? "[]" : estructuraJson);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var clave = ObtenerPropiedadJsonTexto(item, "clave") ?? ObtenerPropiedadJsonTexto(item, "Clave") ?? string.Empty;
                        var etiqueta = ObtenerPropiedadJsonTexto(item, "etiqueta") ?? ObtenerPropiedadJsonTexto(item, "Etiqueta") ?? clave;
                        var tipo = ObtenerPropiedadJsonTexto(item, "tipo") ?? ObtenerPropiedadJsonTexto(item, "Tipo") ?? "texto";

                        if (string.IsNullOrWhiteSpace(clave))
                        {
                            continue;
                        }

                        valores.TryGetValue(clave, out var valor);

                        campos.Add(new PdfFormularioCampo
                        {
                            Clave = clave,
                            Etiqueta = string.IsNullOrWhiteSpace(etiqueta) ? clave : etiqueta,
                            Tipo = tipo,
                            Valor = valor
                        });
                    }
                }
            }
            catch
            {
                // Si la estructura no se puede leer, abajo se imprimen los valores encontrados en DatosJson.
            }

            var clavesYaAgregadas = campos
                .Select(c => c.Clave)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var valor in valores)
            {
                if (clavesYaAgregadas.Contains(valor.Key))
                {
                    continue;
                }

                campos.Add(new PdfFormularioCampo
                {
                    Clave = valor.Key,
                    Etiqueta = ConvertirClaveAEtiqueta(valor.Key),
                    Tipo = "texto",
                    Valor = valor.Value
                });
            }

            return campos;
        }

        private static Dictionary<string, string?> ConvertirJsonPlanoADiccionario(string datosJson)
        {
            var resultado = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(datosJson))
            {
                return resultado;
            }

            try
            {
                using var doc = JsonDocument.Parse(datosJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return resultado;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    resultado[prop.Name] = ConvertirValorJsonATexto(prop.Value);
                }
            }
            catch
            {
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            return resultado;
        }

        private static string? ObtenerPropiedadJsonTexto(JsonElement element, string nombre)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (element.TryGetProperty(nombre, out var prop))
            {
                return ConvertirValorJsonATexto(prop);
            }

            foreach (var item in element.EnumerateObject())
            {
                if (string.Equals(item.Name, nombre, StringComparison.OrdinalIgnoreCase))
                {
                    return ConvertirValorJsonATexto(item.Value);
                }
            }

            return null;
        }

        private static string? ConvertirValorJsonATexto(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "Sí",
                JsonValueKind.False => "No",
                JsonValueKind.Null => null,
                JsonValueKind.Array => value.GetRawText(),
                JsonValueKind.Object => value.GetRawText(),
                _ => value.GetRawText()
            };
        }

        private static string ConvertirClaveAEtiqueta(string clave)
        {
            if (string.IsNullOrWhiteSpace(clave))
            {
                return "Campo";
            }

            var texto = clave.Replace("_", " ").Trim();
            return char.ToUpper(texto[0]) + texto.Substring(1);
        }

        private byte[] GenerarPdfTransporte(Transporte transporte, List<PdfFormularioAdicional> formulariosAdicionales)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            formulariosAdicionales ??= new List<PdfFormularioAdicional>();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(7).FontFamily("Arial"));

                    page.Content().Column(column =>
                    {
                        column.Spacing(5);

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                for (int i = 0; i < 12; i++)
                                {
                                    columns.RelativeColumn();
                                }
                            });

                            table.Cell().RowSpan(3).ColumnSpan(2).Element(CeldaValorCentro).Column(c =>
                            {
                                c.Item().AlignCenter().Text("NS").FontSize(16).Bold();
                                c.Item().AlignCenter().Text("GROUP").FontSize(5).Bold();
                            });

                            table.Cell().ColumnSpan(8).Element(CeldaEtiqueta).Text("NS GROUP").Bold();
                            table.Cell().ColumnSpan(2).Element(CeldaValorCentro).Text(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));

                            table.Cell().ColumnSpan(10).Element(CeldaEtiqueta).Text("PROCESOS Y MEJORA CONTINUA").Bold();

                            table.Cell().ColumnSpan(10).Element(CeldaTituloPrincipal).Text("Formato de Solicitud de Transporte");

                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Área:");
                            table.Cell().ColumnSpan(1).Element(CeldaValorCentro).Text("Logística");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Elaborado Por:");
                            table.Cell().ColumnSpan(2).Element(CeldaValorCentro).Text("Ing. Axel Delgado");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Liberado Por:");
                            table.Cell().ColumnSpan(2).Element(CeldaValorCentro).Text("Lic. Pedro Bello");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Versión:");
                            table.Cell().ColumnSpan(1).Element(CeldaValorCentro).Text("03-2025");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Código:");
                            table.Cell().ColumnSpan(1).Element(CeldaValorCentro).Text("F-19-06");

                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Fecha de Emisión:");
                            table.Cell().ColumnSpan(2).Element(CeldaValorCentro).Text("17/09/2025");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("Página:");
                            table.Cell().ColumnSpan(2).Element(CeldaValorCentro).Text("1 de 1");
                            table.Cell().ColumnSpan(1).Element(CeldaEtiqueta).Text("# ID Folio");
                            table.Cell().ColumnSpan(5).Element(CeldaValorCentro).Text(Texto(transporte.Folio ?? $"TR-{transporte.IdTransporte}"));
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.4f);
                                columns.RelativeColumn(2.2f);
                                columns.RelativeColumn(1.4f);
                                columns.RelativeColumn(2.2f);
                            });

                            void Fila(string etiqueta1, string valor1, string etiqueta2, string valor2)
                            {
                                table.Cell().Element(CeldaEtiqueta).Text(etiqueta1);
                                table.Cell().Element(CeldaValorCentro).Text(valor1);
                                table.Cell().Element(CeldaEtiqueta).Text(etiqueta2);
                                table.Cell().Element(CeldaValorCentro).Text(valor2);
                            }

                            Fila("# Factura", Texto(transporte.NumeroFactura), "Duración aprox. De Flete", Texto(transporte.DuracionAproxFlete));
                            Fila("Fecha de Carga", Fecha(transporte.FechaCarga), "Horario De Llegada a Destino", Texto(transporte.HorarioLlegadaDestino));
                            Fila("Horario de Carga", Texto(transporte.HorarioCarga), "Proyecto", Texto(transporte.Proyecto));
                            Fila("Cliente", Texto(transporte.Cliente), "Departamento", Texto(transporte.Departamento));
                            Fila("Nombre del Solicitante", Texto(transporte.NombreSolicitante), "Autorizado Presupuesto", Texto(transporte.AutorizadoPresupuesto));
                            Fila("Compañía Solicitante", Texto(transporte.CompaniaSolicitante), "Presupuesto de Flete", "");
                            Fila("Centro de Costo", Texto(transporte.CentroCosto), "Tipo de Unidad", Texto(transporte.TipoUnidad));
                            Fila("Tipo de Ruta", Texto(transporte.TipoRuta), "Comentarios de Unidad", Texto(transporte.ComentariosUnidad));

                            table.Cell().Element(CeldaEtiqueta).Text("Dirección de Recolección");
                            table.Cell().ColumnSpan(3).Element(CeldaValorCentro).Text(Texto(transporte.DireccionRecoleccion));

                            Fila("Volumetría\n(Ancho y Largo - mts)", Texto(transporte.Volumetria), "Fletero", Texto(transporte.Fletero));
                            table.Cell().Element(CeldaEtiqueta).Text("Costo de Flete");
                            table.Cell().ColumnSpan(3).Element(CeldaValorCentro).Text($"{Dinero(transporte.CostoFlete)} MXN + IVA");
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(25);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(4);
                            });

                            table.Cell().ColumnSpan(4).Element(CeldaSeccion).Text("Destinos");
                            table.Cell().Element(CeldaEncabezado).Text("#");
                            table.Cell().Element(CeldaEncabezado).Text("Nombre de quien recibe carga");
                            table.Cell().Element(CeldaEncabezado).Text("Contacto");
                            table.Cell().Element(CeldaEncabezado).Text("Dirección de destino");

                            var destinos = transporte.Destinos?.OrderBy(x => x.NumeroDestino).ToList() ?? new List<TransporteDestino>();

                            if (destinos.Any())
                            {
                                foreach (var destino in destinos)
                                {
                                    table.Cell().Element(CeldaValorCentro).Text(destino.NumeroDestino.ToString());
                                    table.Cell().Element(CeldaValor).Text(Texto(destino.NombreRecibe));
                                    table.Cell().Element(CeldaValor).Text(Texto(destino.ContactoRecibe));
                                    table.Cell().Element(CeldaValor).Text(Texto(destino.DireccionDestino));
                                }
                            }
                            else
                            {
                                table.Cell().ColumnSpan(4).Element(CeldaValorCentro).Text("Sin destinos registrados.");
                            }
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.1f);
                                columns.RelativeColumn(3.2f);
                                columns.RelativeColumn(.8f);
                                columns.RelativeColumn(.7f);
                                columns.RelativeColumn(.8f);
                                columns.RelativeColumn(1.1f);
                                columns.RelativeColumn(1.2f);
                            });

                            table.Cell().ColumnSpan(7).Element(CeldaSeccion).Text("Plan de Embarque");
                            table.Cell().Element(CeldaEncabezado).Text("Clave SAT");
                            table.Cell().Element(CeldaEncabezado).Text("Descripción");
                            table.Cell().Element(CeldaEncabezado).Text("Cantidad");
                            table.Cell().Element(CeldaEncabezado).Text("UM");
                            table.Cell().Element(CeldaEncabezado).Text("Peso");
                            table.Cell().Element(CeldaEncabezado).Text("Valor");
                            table.Cell().Element(CeldaEncabezado).Text("Vale de salida / Factura");

                            var partidas = transporte.PlanEmbarque?.OrderBy(x => x.IdPlanEmbarque).ToList() ?? new List<TransportePlanEmbarque>();

                            if (partidas.Any())
                            {
                                foreach (var item in partidas)
                                {
                                    table.Cell().Element(CeldaValor).Text(Texto(item.ClaveSAT));
                                    table.Cell().Element(CeldaValor).Text(Texto(item.Descripcion));
                                    table.Cell().Element(CeldaValorCentro).Text(Numero(item.Cantidad));
                                    table.Cell().Element(CeldaValorCentro).Text(Texto(item.UnidadMedida));
                                    table.Cell().Element(CeldaValorCentro).Text(Numero(item.Peso));
                                    table.Cell().Element(CeldaValorDerecha).Text(Dinero(item.Valor));
                                    table.Cell().Element(CeldaValor).Text(Texto(item.ValeSalidaFactura));
                                }

                                table.Cell().ColumnSpan(4).Element(CeldaValorDerecha).Text("Totales").Bold();
                                table.Cell().Element(CeldaValorCentro).Text(Numero(partidas.Sum(x => x.Peso ?? 0)));
                                table.Cell().Element(CeldaValorDerecha).Text(Dinero(partidas.Sum(x => x.Valor ?? 0)));
                                table.Cell().Element(CeldaValorCentro).Text("MXN").Bold();
                            }
                            else
                            {
                                table.Cell().ColumnSpan(7).Element(CeldaValorCentro).Text("Sin partidas registradas.");
                            }
                        });

                        if (formulariosAdicionales.Any())
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.4f);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(1.4f);
                                    columns.RelativeColumn(2.2f);
                                });

                                table.Cell().ColumnSpan(4).Element(CeldaSeccion).Text("Formularios adicionales");

                                foreach (var formulario in formulariosAdicionales)
                                {
                                    table.Cell().ColumnSpan(4).Element(CeldaSubSeccion).Text(Texto(formulario.NombreFormulario));

                                    var campos = formulario.Campos ?? new List<PdfFormularioCampo>();

                                    if (!campos.Any())
                                    {
                                        table.Cell().ColumnSpan(4).Element(CeldaValorCentro).Text("Sin datos capturados.");
                                        continue;
                                    }

                                    for (int i = 0; i < campos.Count; i += 2)
                                    {
                                        var campo1 = campos[i];
                                        var campo2 = i + 1 < campos.Count ? campos[i + 1] : null;

                                        table.Cell().Element(CeldaEtiqueta).Text(Texto(campo1.Etiqueta));
                                        table.Cell().Element(CeldaValorCentro).Text(Texto(campo1.Valor));

                                        if (campo2 != null)
                                        {
                                            table.Cell().Element(CeldaEtiqueta).Text(Texto(campo2.Etiqueta));
                                            table.Cell().Element(CeldaValorCentro).Text(Texto(campo2.Valor));
                                        }
                                        else
                                        {
                                            table.Cell().Element(CeldaEtiqueta).Text("");
                                            table.Cell().Element(CeldaValorCentro).Text("");
                                        }
                                    }
                                }
                            });
                        }

                        column.Item().Row(row =>
                        {
                            row.RelativeItem(1.25f).Column(firmas =>
                            {
                                firmas.Item().PaddingTop(18).Text("__________________________________________").AlignCenter();
                                firmas.Item().Text("Nombre y Firma del solicitante").FontSize(7).AlignCenter();
                                firmas.Item().PaddingTop(12).Text("__________________________________________").AlignCenter();
                                firmas.Item().Text("Nombre y firma de Logística").FontSize(7).AlignCenter();
                                firmas.Item().PaddingTop(12).Text("__________________________________________").AlignCenter();
                                firmas.Item().Text("Nombre y Firma de Conformidad de Costo\nDirector del Solicitante").FontSize(7).AlignCenter();
                            });

                            row.RelativeItem(.85f).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(4);
                                    columns.RelativeColumn(1);
                                });

                                table.Cell().ColumnSpan(2).Element(CeldaSeccion).Text("Checklist");
                                table.Cell().Element(CeldaValor).Text("1. Formato de Solicitud de Transporte");
                                table.Cell().Element(CeldaValor).Text("");
                                table.Cell().Element(CeldaValor).Text("2. Carta Porte");
                                table.Cell().Element(CeldaValor).Text("");
                                table.Cell().Element(CeldaValor).Text("3. Factura");
                                table.Cell().Element(CeldaValor).Text("");
                                table.Cell().Element(CeldaValor).Text("4. Evidencia (Documentos Firmados)");
                                table.Cell().Element(CeldaValor).Text("");
                                table.Cell().Element(CeldaValor).Text("5. Fotografías");
                                table.Cell().Element(CeldaValor).Text("");
                            });
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private byte[] GenerarPdfReporteGuias(List<Guia> guias)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(7));

                    page.Header()
                        .Column(column =>
                        {
                            column.Item().Text("Auditoría de Guías").FontSize(16).SemiBold();
                            column.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm} | Total de registros: {guias.Count}");
                            column.Item().LineHorizontal(1);
                        });

                    page.Content()
                        .PaddingVertical(8)
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(35);
                                columns.ConstantColumn(58);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.ConstantColumn(55);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CeldaEncabezado).Text("ID");
                                header.Cell().Element(CeldaEncabezado).Text("Fecha");
                                header.Cell().Element(CeldaEncabezado).Text("Depto.");
                                header.Cell().Element(CeldaEncabezado).Text("Cliente/Proyecto");
                                header.Cell().Element(CeldaEncabezado).Text("Remitente");
                                header.Cell().Element(CeldaEncabezado).Text("Origen");
                                header.Cell().Element(CeldaEncabezado).Text("Destinatario");
                                header.Cell().Element(CeldaEncabezado).Text("Destino");
                                header.Cell().Element(CeldaEncabezado).Text("Tipo envío");
                                header.Cell().Element(CeldaEncabezado).Text("Costo");
                                header.Cell().Element(CeldaEncabezado).Text("Estado edición");
                            });

                            foreach (var guia in guias)
                            {
                                table.Cell().Element(CeldaValor).Text(guia.IdGuia.ToString());
                                table.Cell().Element(CeldaValor).Text(Fecha(guia.FechaSolicitud));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.Departamento));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.ClienteProyecto));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.RemitenteNombre));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.Origen));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.DestinatarioNombre));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.Destino));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.TipoEnvio));
                                table.Cell().Element(CeldaValor).Text(Dinero(guia.Costo));
                                table.Cell().Element(CeldaValor).Text(Texto(guia.EstadoEdicion));
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("Página ");
                            text.CurrentPageNumber();
                            text.Span(" de ");
                            text.TotalPages();
                        });
                });
            }).GeneratePdf();
        }

        private byte[] GenerarPdfReporteTransportes(List<Transporte> transportes)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(7));

                    page.Header()
                        .Column(column =>
                        {
                            column.Item().Text("Auditoría de Transportes").FontSize(16).SemiBold();
                            column.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm} | Total de registros: {transportes.Count}");
                            column.Item().LineHorizontal(1);
                        });

                    page.Content()
                        .PaddingVertical(8)
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(45);
                                columns.ConstantColumn(58);
                                columns.ConstantColumn(58);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(45);
                                columns.ConstantColumn(45);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CeldaEncabezado).Text("Folio");
                                header.Cell().Element(CeldaEncabezado).Text("Emisión");
                                header.Cell().Element(CeldaEncabezado).Text("Carga");
                                header.Cell().Element(CeldaEncabezado).Text("Estado");
                                header.Cell().Element(CeldaEncabezado).Text("Cliente");
                                header.Cell().Element(CeldaEncabezado).Text("Proyecto");
                                header.Cell().Element(CeldaEncabezado).Text("Solicitante");
                                header.Cell().Element(CeldaEncabezado).Text("Unidad");
                                header.Cell().Element(CeldaEncabezado).Text("Fletero");
                                header.Cell().Element(CeldaEncabezado).Text("Costo");
                                header.Cell().Element(CeldaEncabezado).Text("Dest.");
                                header.Cell().Element(CeldaEncabezado).Text("Part.");
                            });

                            foreach (var transporte in transportes)
                            {
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.Folio ?? transporte.IdTransporte.ToString()));
                                table.Cell().Element(CeldaValor).Text(Fecha(transporte.FechaEmision));
                                table.Cell().Element(CeldaValor).Text(Fecha(transporte.FechaCarga));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.EstadoSolicitud));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.Cliente));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.Proyecto));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.NombreSolicitante));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.TipoUnidad));
                                table.Cell().Element(CeldaValor).Text(Texto(transporte.Fletero));
                                table.Cell().Element(CeldaValor).Text(Dinero(transporte.CostoFlete));
                                table.Cell().Element(CeldaValor).Text((transporte.Destinos?.Count ?? 0).ToString());
                                table.Cell().Element(CeldaValor).Text((transporte.PlanEmbarque?.Count ?? 0).ToString());
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("Página ");
                            text.CurrentPageNumber();
                            text.Span(" de ");
                            text.TotalPages();
                        });
                });
            }).GeneratePdf();
        }

        private static IContainer CeldaTituloPrincipal(IContainer container)
        {
            return container
                .Border(1)
                .Padding(3)
                .AlignCenter()
                .DefaultTextStyle(x => x.FontSize(10).SemiBold());
        }

        private static IContainer CeldaSeccion(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten2)
                .Padding(3)
                .AlignCenter()
                .DefaultTextStyle(x => x.FontSize(8).SemiBold());
        }

        private static IContainer CeldaSubSeccion(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten3)
                .Padding(3)
                .DefaultTextStyle(x => x.FontSize(8).SemiBold());
        }

        private static IContainer CeldaValorCentro(IContainer container)
        {
            return container
                .Border(1)
                .Padding(3)
                .AlignCenter()
                .AlignMiddle();
        }

        private static IContainer CeldaValorDerecha(IContainer container)
        {
            return container
                .Border(1)
                .Padding(3)
                .AlignRight()
                .AlignMiddle();
        }

        private static IContainer CeldaEncabezado(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten3)
                .Padding(3)
                .DefaultTextStyle(x => x.SemiBold());
        }

        private static IContainer CeldaEtiqueta(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten4)
                .Padding(4)
                .DefaultTextStyle(x => x.SemiBold());
        }

        private static IContainer CeldaValor(IContainer container)
        {
            return container
                .Border(1)
                .Padding(4);
        }

        private static string Texto(string? valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? "-" : valor;
        }

        private static string Fecha(DateTime? fecha)
        {
            return fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy") : "-";
        }

        private static string Numero(decimal? valor)
        {
            return valor.HasValue ? valor.Value.ToString("N2") : "-";
        }

        private static string Dinero(decimal? valor)
        {
            return valor.HasValue ? "$" + valor.Value.ToString("N2") : "$0.00";
        }


    }
}
