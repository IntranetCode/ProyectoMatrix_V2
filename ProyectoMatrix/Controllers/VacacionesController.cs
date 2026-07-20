using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    public class VacacionesController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<VacacionesController> _logger;
        private readonly IConfiguration _configuration;
        private readonly ServicioNotificaciones _notif;

        public VacacionesController(IConfiguration configuration,
                                    ILogger<VacacionesController> logger, ServicioNotificaciones notif)
        {

            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _notif = notif;
        }

        [HttpGet]
        public IActionResult MisVacaciones()
        {
            var vm = new MisVacacionesVm();

            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
            {
                return Unauthorized();
            }
            vm.UsuarioID = usuarioId;

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // personaID a partir del UsuarioID
                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                    {
                        ViewBag.Error = "No se encontró la persona asociada al usuario actual.";
                        return View(vm);
                    }

                    // --- NUEVA VALIDACIÓN: Permiso de Vacaciones Adelantadas ---
                    // Consultamos si existe un registro autorizado por RH y completado
                    bool tienePermisoEspecial = false;
                    string sqlValidarPermiso = @"
                SELECT COUNT(*) 
                FROM VacacionesHabilitacionesEspeciales 
                WHERE UsuarioID = @UsuarioID 
                  AND EstatusRH = 'Autorizado' 
                  AND Completada = 1";

                    using (var cmdP = new SqlCommand(sqlValidarPermiso, conn))
                    {
                        cmdP.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        tienePermisoEspecial = (int)cmdP.ExecuteScalar() > 0;
                    }
                    // Este ViewBag es el que usa tu vista MisVacaciones.cshtml para mostrar el botón azul
                    ViewBag.PermiteAdelantadas = tienePermisoEspecial;

                    // Saber si esta persona es jefe de alguien
                    bool esJefe = EsJefeInmediato(conn, personaId);
                    ViewBag.EsJefeInmediato = esJefe;

                    // Detectar si el usuario es de RRHH
                    bool esRH = EsUsuarioRecursosHumanos(conn, usuarioId);
                    ViewBag.EsUsuarioRH = esRH;

                    // Datos de la persona (nombre y número)
                    using (var cmd = new SqlCommand(@"
                SELECT NumeroEmpleado, (ApellidoPaterno + ' ' + ApellidoMaterno + ' ' + Nombre) AS NombreCompleto
                FROM Persona WHERE PersonaID = @PersonaID;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                vm.NumeroEmpleado = reader["NumeroEmpleado"]?.ToString();
                                vm.NombreCompleto = reader["NombreCompleto"]?.ToString();
                            }
                        }
                    }

                    // Resumen del año actual
                    using (var cmd = new SqlCommand(@"
                SELECT TOP 1 AnioSaldo, DiasCorrespondientes, DiasExtra,DiasDeuda, DiasTomados, DiasCaducados, DiasDisponibles
                FROM vw_VacacionesSaldoActual
                WHERE PersonaID = @PersonaID
                ORDER BY AnioSaldo DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                vm.ResumenActual = new VacacionesResumenAnualVm
                                {
                                    Anio = Convert.ToInt32(reader["AnioSaldo"]),
                                    DiasCorrespondientes = Convert.ToInt32(reader["DiasCorrespondientes"]),
                                    DiasExtra = Convert.ToDecimal(reader["DiasExtra"]),
                                    DiasDeuda = reader["DiasDeuda"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["DiasDeuda"]),
                                    DiasTomados = Convert.ToDecimal(reader["DiasTomados"]),
                                    DiasCaducados = Convert.ToDecimal(reader["DiasCaducados"]),
                                    DiasDisponibles = Convert.ToDecimal(reader["DiasDisponibles"])
                                };
                            }
                        }
                    }

                    // Historial de solicitudes
                    vm.Solicitudes = new List<VacacionesSolicitudItemVm>();
                    using (var cmd = new SqlCommand(@"
                SELECT SolicitudVacacionesID, FechaIngreso, DiasTomados, DiasDisponibles, FechaSolicitud, 
                       FechaInicio, FechaFin, FechaRegresoLabores, DiasSolicitados, EsAnticipada, 
                       EstadoAutorizacion, EstadoRecursosHumanos, Origen
                FROM vw_VacacionesSolicitudesDetalle
                WHERE PersonaID = @PersonaID
                ORDER BY FechaSolicitud DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                vm.Solicitudes.Add(new VacacionesSolicitudItemVm
                                {
                                    SolicitudVacacionesID = (int)reader["SolicitudVacacionesID"],
                                    FechaIngreso = (DateTime)reader["FechaIngreso"],
                                    DiasTomados = reader["DiasTomados"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["DiasTomados"]),
                                    DiasDisponibles = reader["DiasDisponibles"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["DiasDisponibles"]),
                                    FechaSolicitud = (DateTime)reader["FechaSolicitud"],
                                    FechaInicio = (DateTime)reader["FechaInicio"],
                                    FechaFin = (DateTime)reader["FechaFin"],
                                    FechaRegresoLabores = (DateTime)reader["FechaRegresoLabores"],
                                    DiasSolicitados = Convert.ToDecimal(reader["DiasSolicitados"]),
                                    EsAnticipada = (bool)reader["EsAnticipada"],
                                    EstadoAutorizacion = reader["EstadoAutorizacion"]?.ToString(),
                                    EstadoRecursosHumanos = reader["EstadoRecursosHumanos"]?.ToString(),
                                    Origen = reader["Origen"]?.ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando MisVacaciones");
                ViewBag.Error = $"Error al cargar la información de vacaciones: {ex.Message}";
            }

            return View(vm);
        }

        [HttpGet]
        public IActionResult CrearSolicitud()
        {
            var vm = new CrearSolicitudVacacionesVm();

            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
            {
                return Unauthorized();
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                    {
                        ViewBag.Error = "No se encontró la persona asociada al usuario actual.";
                        return View(vm);
                    }

                    // Traer el saldo MÁS RECIENTE (último año generado)


                    // Valores por defecto
                    vm.FechaInicio = DateTime.Today;
                    vm.FechaFin = DateTime.Today;
                    vm.DiasDisponiblesActuales = ObtenerDiasDisponibles(conn, personaId);
                    ViewBag.PermiteAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en GET CrearSolicitud");
                ViewBag.Error = $"Error al cargar el formulario: {ex.Message}";
            }

            return View(vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSolicitud(CrearSolicitudVacacionesVm model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
            {
                return Unauthorized();
            }

            // Validación de fechas
            if (model.FechaFin < model.FechaInicio)
            {
                ModelState.AddModelError(string.Empty, "La fecha de fin no puede ser menor que la fecha de inicio.");
            }



            try
            {
                int nuevaSolicitudId;

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                    {
                        ModelState.AddModelError(string.Empty, "No se encontró la persona asociada al usuario actual.");
                        return View(model);
                    }

                    decimal diasDisponibles = ObtenerDiasDisponibles(conn, personaId);
                    decimal diasSolicitados = ContarDiasHabiles(conn, model.FechaInicio, model.FechaFin);
                    bool tienePermisoAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId);

                    model.DiasDisponiblesActuales = diasDisponibles;
                    ViewBag.PermiteAdelantadas = tienePermisoAdelantadas;

                    if (!ModelState.IsValid)
                    {
                        return View(model);
                    }

                    if (diasSolicitados > diasDisponibles && !tienePermisoAdelantadas)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            $"No tienes saldo suficiente para solicitar estas vacaciones. " +
                            $"Días solicitados: {diasSolicitados}. Días disponibles: {diasDisponibles}. "
                        );

                        return View(model);
                    }

                    using (var cmd = new SqlCommand("sp_Vacaciones_CrearSolicitud", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        cmd.Parameters.AddWithValue("@UsuarioSolicitaID", usuarioId);
                        cmd.Parameters.AddWithValue("@FechaInicio", model.FechaInicio.Date);
                        cmd.Parameters.AddWithValue("@FechaFin", model.FechaFin.Date);
                        cmd.Parameters.AddWithValue("@Origen", "Colaborador");

                        var paramEsAnt = new SqlParameter("@EsAnticipada", SqlDbType.Bit)
                        {
                            Value = DBNull.Value  // que el SP calcule si es anticipada
                        };
                        cmd.Parameters.Add(paramEsAnt);

                        cmd.Parameters.AddWithValue(
                            "@Observaciones",
                            (object?)model.Observaciones ?? DBNull.Value
                        );

                        var paramIdOut = new SqlParameter("@SolicitudVacacionesID", SqlDbType.Int)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(paramIdOut);

                        cmd.ExecuteNonQuery();

                        nuevaSolicitudId = (int)paramIdOut.Value;
                    }
                }

                //  Aquí mandamos el correo al jefe (sin romper el flujo si falla)
                await NotificarJefeNuevaSolicitudAsync(nuevaSolicitudId);

                TempData["MensajeVacaciones"] =
                    $"Tu solicitud se creó correctamente (Folio {nuevaSolicitudId}).";

                return RedirectToAction("MisVacaciones");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear solicitud de vacaciones");

                try
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        conn.Open();

                        int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                        if (personaId != 0)
                        {
                            model.DiasDisponiblesActuales = ObtenerDiasDisponibles(conn, personaId);
                            ViewBag.PermiteAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId);
                        }
                    }
                }
                catch { }

                ViewBag.Error = $"Error al crear la solicitud: {ex.Message}";
                return View(model);
            }
        }






        [HttpGet]
        public IActionResult EditarSolicitud(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                        return Forbid();

                    using (var cmd = new SqlCommand(@"
SELECT TOP 1
    SolicitudVacacionesID,
    FechaInicio,
    FechaFin,
    DiasSolicitados,
    Observaciones,
    EstadoAutorizacion,
    EstadoRecursosHumanos,
    Origen
FROM VacacionesSolicitud
WHERE SolicitudVacacionesID = @SolicitudID
  AND PersonaID = @PersonaID;", conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", id);
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read())
                                return NotFound();

                            string estadoJefe = rd["EstadoAutorizacion"]?.ToString() ?? "";
                            string estadoRH = rd["EstadoRecursosHumanos"]?.ToString() ?? "";
                            string origen = rd["Origen"]?.ToString() ?? "";

                            if (!PuedeModificarSolicitud(estadoJefe, estadoRH, origen))
                            {
                                TempData["ErrorVacaciones"] =
                                    "La solicitud ya no puede editarse porque fue procesada, cancelada o no fue creada por el colaborador.";
                                return RedirectToAction("MisVacaciones");
                            }

                            var vm = new EditarSolicitudVacacionesVm
                            {
                                SolicitudVacacionesID = Convert.ToInt32(rd["SolicitudVacacionesID"]),
                                FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
                                FechaFin = Convert.ToDateTime(rd["FechaFin"]),
                                DiasSolicitadosAnteriores = Convert.ToDecimal(rd["DiasSolicitados"]),
                                Observaciones = rd["Observaciones"] == DBNull.Value
                                    ? null
                                    : rd["Observaciones"].ToString()
                            };

                            rd.Close();

                            vm.DiasDisponiblesActuales = ObtenerDiasDisponibles(conn, personaId);
                            ViewBag.PermiteAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId);

                            return View(vm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando edición de solicitud {SolicitudID}", id);
                TempData["ErrorVacaciones"] = "No fue posible cargar la solicitud para editarla.";
                return RedirectToAction("MisVacaciones");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarSolicitud(EditarSolicitudVacacionesVm model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            if (model.FechaFin.Date < model.FechaInicio.Date)
            {
                ModelState.AddModelError(string.Empty,
                    "La fecha de fin no puede ser menor que la fecha de inicio.");
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                        return Forbid();

                    model.DiasDisponiblesActuales = ObtenerDiasDisponibles(conn, personaId);
                    ViewBag.PermiteAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId);

                    if (!ModelState.IsValid)
                        return View(model);

                    DateTime fechaInicioAnterior;
                    DateTime fechaFinAnterior;
                    decimal diasAnteriores;
                    decimal diasNuevos;

                    using (var trans = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        string estadoJefe;
                        string estadoRH;
                        string origen;

                        using (var cmdActual = new SqlCommand(@"
SELECT
    FechaInicio,
    FechaFin,
    DiasSolicitados,
    EstadoAutorizacion,
    EstadoRecursosHumanos,
    Origen
FROM VacacionesSolicitud WITH (UPDLOCK, ROWLOCK)
WHERE SolicitudVacacionesID = @SolicitudID
  AND PersonaID = @PersonaID;", conn, trans))
                        {
                            cmdActual.Parameters.AddWithValue("@SolicitudID", model.SolicitudVacacionesID);
                            cmdActual.Parameters.AddWithValue("@PersonaID", personaId);

                            using (var rd = cmdActual.ExecuteReader())
                            {
                                if (!rd.Read())
                                {
                                    trans.Rollback();
                                    return NotFound();
                                }

                                fechaInicioAnterior = Convert.ToDateTime(rd["FechaInicio"]);
                                fechaFinAnterior = Convert.ToDateTime(rd["FechaFin"]);
                                diasAnteriores = Convert.ToDecimal(rd["DiasSolicitados"]);
                                estadoJefe = rd["EstadoAutorizacion"]?.ToString() ?? "";
                                estadoRH = rd["EstadoRecursosHumanos"]?.ToString() ?? "";
                                origen = rd["Origen"]?.ToString() ?? "";
                            }
                        }

                        if (!PuedeModificarSolicitud(estadoJefe, estadoRH, origen))
                        {
                            trans.Rollback();
                            TempData["ErrorVacaciones"] =
                                "La solicitud cambió de estado y ya no puede editarse.";
                            return RedirectToAction("MisVacaciones");
                        }

                        diasNuevos = ContarDiasHabiles(
                            conn,
                            model.FechaInicio.Date,
                            model.FechaFin.Date,
                            trans);

                        if (diasNuevos <= 0)
                        {
                            trans.Rollback();
                            ModelState.AddModelError(string.Empty,
                                "El periodo seleccionado no contiene días hábiles.");
                            return View(model);
                        }

                        decimal diasDisponibles = ObtenerDiasDisponibles(conn, personaId, trans);
                        bool permiteAdelantadas = TienePermisoVacacionesAdelantadas(conn, usuarioId, trans);

                        model.DiasDisponiblesActuales = diasDisponibles;
                        ViewBag.PermiteAdelantadas = permiteAdelantadas;

                        if (diasNuevos > diasDisponibles && !permiteAdelantadas)
                        {
                            trans.Rollback();
                            ModelState.AddModelError(string.Empty,
                                $"No tienes saldo suficiente. Días solicitados: {diasNuevos}. " +
                                $"Días disponibles: {diasDisponibles}.");
                            return View(model);
                        }

                        DateTime fechaRegreso = ObtenerSiguienteDiaHabil(
                            conn,
                            model.FechaFin.Date,
                            trans);

                        bool esAnticipada = diasNuevos > diasDisponibles;

                        using (var cmdUpdate = new SqlCommand(@"
UPDATE VacacionesSolicitud
SET FechaInicio = @FechaInicio,
    FechaFin = @FechaFin,
    FechaRegresoLabores = @FechaRegresoLabores,
    DiasSolicitados = @DiasSolicitados,
    EsAnticipada = @EsAnticipada,
    Observaciones = @Observaciones
WHERE SolicitudVacacionesID = @SolicitudID
  AND PersonaID = @PersonaID
  AND EstadoAutorizacion = 'Pendiente'
  AND ISNULL(EstadoRecursosHumanos, 'SinRegistrar') IN ('SinRegistrar', 'Pendiente')
  AND Origen = 'Colaborador';", conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue("@FechaInicio", model.FechaInicio.Date);
                            cmdUpdate.Parameters.AddWithValue("@FechaFin", model.FechaFin.Date);
                            cmdUpdate.Parameters.AddWithValue("@FechaRegresoLabores", fechaRegreso.Date);
                            cmdUpdate.Parameters.AddWithValue("@DiasSolicitados", diasNuevos);
                            cmdUpdate.Parameters.AddWithValue("@EsAnticipada", esAnticipada);
                            cmdUpdate.Parameters.AddWithValue("@Observaciones",
                                (object?)model.Observaciones?.Trim() ?? DBNull.Value);
                            cmdUpdate.Parameters.AddWithValue("@SolicitudID", model.SolicitudVacacionesID);
                            cmdUpdate.Parameters.AddWithValue("@PersonaID", personaId);

                            int filas = cmdUpdate.ExecuteNonQuery();
                            if (filas != 1)
                            {
                                trans.Rollback();
                                TempData["ErrorVacaciones"] =
                                    "No fue posible actualizar la solicitud porque su estado cambió.";
                                return RedirectToAction("MisVacaciones");
                            }
                        }

                        trans.Commit();
                    }

                    await NotificarJefeSolicitudModificadaAsync(
                        model.SolicitudVacacionesID,
                        fechaInicioAnterior,
                        fechaFinAnterior,
                        diasAnteriores);

                    TempData["MensajeVacaciones"] =
                        $"La solicitud {model.SolicitudVacacionesID} se actualizó correctamente y se notificó a tu jefe.";

                    return RedirectToAction("MisVacaciones");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error editando solicitud de vacaciones {SolicitudID}",
                    model.SolicitudVacacionesID);

                ViewBag.Error = "No fue posible actualizar la solicitud: " + ex.Message;
                return View(model);
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarSolicitud(
            int solicitudId,
            string? motivo)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            motivo = string.IsNullOrWhiteSpace(motivo)
                ? "Sin motivo proporcionado."
                : motivo.Trim();

            if (motivo.Length > 500)
                motivo = motivo.Substring(0, 500);

            try
            {
                DateTime fechaInicioAnterior;
                DateTime fechaFinAnterior;
                decimal diasAnteriores;

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaId = ObtenerPersonaIdPorUsuario(conn, usuarioId);
                    if (personaId == 0)
                        return Forbid();

                    using (var trans = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        string estadoJefe;
                        string estadoRH;
                        string origen;

                        using (var cmdActual = new SqlCommand(@"
SELECT
    FechaInicio,
    FechaFin,
    DiasSolicitados,
    EstadoAutorizacion,
    EstadoRecursosHumanos,
    Origen
FROM VacacionesSolicitud WITH (UPDLOCK, ROWLOCK)
WHERE SolicitudVacacionesID = @SolicitudID
  AND PersonaID = @PersonaID;", conn, trans))
                        {
                            cmdActual.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdActual.Parameters.AddWithValue("@PersonaID", personaId);

                            using (var rd = cmdActual.ExecuteReader())
                            {
                                if (!rd.Read())
                                {
                                    trans.Rollback();
                                    return NotFound();
                                }

                                fechaInicioAnterior = Convert.ToDateTime(rd["FechaInicio"]);
                                fechaFinAnterior = Convert.ToDateTime(rd["FechaFin"]);
                                diasAnteriores = Convert.ToDecimal(rd["DiasSolicitados"]);
                                estadoJefe = rd["EstadoAutorizacion"]?.ToString() ?? "";
                                estadoRH = rd["EstadoRecursosHumanos"]?.ToString() ?? "";
                                origen = rd["Origen"]?.ToString() ?? "";
                            }
                        }

                        if (!PuedeModificarSolicitud(estadoJefe, estadoRH, origen))
                        {
                            trans.Rollback();
                            TempData["ErrorVacaciones"] =
                                "La solicitud ya fue procesada y no puede cancelarse.";
                            return RedirectToAction("MisVacaciones");
                        }

                        string notaCancelacion =
                            $"[CANCELADA POR COLABORADOR {DateTime.Now:dd/MM/yyyy HH:mm}] Motivo: {motivo}";

                        using (var cmdUpdate = new SqlCommand(@"
UPDATE VacacionesSolicitud
SET EstadoAutorizacion = 'Rechazada',
    EstadoRecursosHumanos = 'Cancelada',
    Observaciones = CASE
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones, ''))), '') IS NULL
            THEN @NotaCancelacion
        ELSE Observaciones + CHAR(13) + CHAR(10) + @NotaCancelacion
    END
WHERE SolicitudVacacionesID = @SolicitudID
  AND PersonaID = @PersonaID
  AND EstadoAutorizacion = 'Pendiente'
  AND ISNULL(EstadoRecursosHumanos, 'SinRegistrar') IN ('SinRegistrar', 'Pendiente')
  AND Origen = 'Colaborador';", conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue("@NotaCancelacion", notaCancelacion);
                            cmdUpdate.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdUpdate.Parameters.AddWithValue("@PersonaID", personaId);

                            int filas = cmdUpdate.ExecuteNonQuery();
                            if (filas != 1)
                            {
                                trans.Rollback();
                                TempData["ErrorVacaciones"] =
                                    "No fue posible cancelar la solicitud porque su estado cambió.";
                                return RedirectToAction("MisVacaciones");
                            }
                        }

                        trans.Commit();
                    }
                }

                await NotificarJefeSolicitudCanceladaAsync(
                    solicitudId,
                    fechaInicioAnterior,
                    fechaFinAnterior,
                    diasAnteriores,
                    motivo);

                TempData["MensajeVacaciones"] =
                    $"La solicitud {solicitudId} fue cancelada y se notificó a tu jefe.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error cancelando solicitud de vacaciones {SolicitudID}",
                    solicitudId);

                TempData["ErrorVacaciones"] =
                    "No fue posible cancelar la solicitud: " + ex.Message;
            }

            return RedirectToAction("MisVacaciones");
        }

        //Metodo POST para BandejaRH que cancela las vacaciones registradas y regresa los dias disponibles

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarVacacionesRegistradasRH(
    int solicitudId,
    string motivo)
        {
            int usuarioRhId = ObtenerUsuarioIdActual();
            if (usuarioRhId == 0)
                return Unauthorized();

            motivo = (motivo ?? string.Empty).Trim();

            if (motivo.Length < 10)
            {
                TempData["ErrorVacacionesRH"] =
                    "El motivo de cancelación debe contener al menos 10 caracteres.";

                return RedirectToAction(nameof(BandejaRH), new { tab = "registradas" });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    if (!EsUsuarioRecursosHumanos(conn, usuarioRhId))
                        return Forbid();

                    using (var cmd = new SqlCommand(
                        "dbo.sp_Vacaciones_CancelarRegistroRH",
                        conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue(
                            "@SolicitudVacacionesID",
                            solicitudId);
                        cmd.Parameters.AddWithValue("@UsuarioRHID", usuarioRhId);
                        cmd.Parameters.AddWithValue("@Motivo", motivo);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                // La devolución de saldo ya quedó confirmada en BD.
                // El correo no debe romper el flujo si llegara a fallar.
                await NotificarCancelacionVacacionesRHAsync(solicitudId, motivo);

                TempData["MensajeVacacionesRH"] =
                    $"La solicitud {solicitudId} fue cancelada y los días se devolvieron al colaborador.";
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Error SQL cancelando vacaciones registradas por RH. SolicitudID={SolicitudID}",
                    solicitudId);

                TempData["ErrorVacacionesRH"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error cancelando vacaciones registradas por RH. SolicitudID={SolicitudID}",
                    solicitudId);

                TempData["ErrorVacacionesRH"] =
                    "No se pudo cancelar la solicitud: " + ex.Message;
            }

            return RedirectToAction(nameof(BandejaRH), new { tab = "registradas" });
        }

        private async Task NotificarCancelacionVacacionesRHAsync(
            int solicitudId,
            string motivo)
        {
            try
            {
                int personaColaboradorId = 0;
                int? personaJefeId = null;
                string nombreColaborador = "Colaborador";
                string nombreJefe = "Jefe inmediato";
                string numeroEmpleado = string.Empty;
                DateTime fechaInicio = DateTime.Today;
                DateTime fechaFin = DateTime.Today;
                decimal dias = 0;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    const string sql = @"
SELECT
    p.PersonaID AS PersonaColaboradorID,
    p.NumeroEmpleado,
    CONCAT(
        ISNULL(p.ApellidoPaterno, ''), ' ',
        ISNULL(p.ApellidoMaterno, ''), ' ',
        ISNULL(p.Nombre, '')
    ) AS NombreColaborador,
    pj.PersonaID AS PersonaJefeID,
    CONCAT(
        ISNULL(pj.ApellidoPaterno, ''), ' ',
        ISNULL(pj.ApellidoMaterno, ''), ' ',
        ISNULL(pj.Nombre, '')
    ) AS NombreJefe,
    s.FechaInicio,
    s.FechaFin,
    s.DiasSolicitados
FROM dbo.VacacionesSolicitud s
INNER JOIN dbo.Persona p
    ON p.PersonaID = s.PersonaID
LEFT JOIN dbo.Persona pj
    ON pj.PersonaID = p.JefeInmediatoPersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (!await rd.ReadAsync())
                                return;

                            personaColaboradorId = Convert.ToInt32(
                                rd["PersonaColaboradorID"]);

                            personaJefeId = rd["PersonaJefeID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["PersonaJefeID"]);

                            numeroEmpleado = rd["NumeroEmpleado"]?.ToString() ?? "";
                            nombreColaborador =
                                rd["NombreColaborador"]?.ToString()?.Trim()
                                ?? "Colaborador";
                            nombreJefe =
                                rd["NombreJefe"]?.ToString()?.Trim()
                                ?? "Jefe inmediato";
                            fechaInicio = Convert.ToDateTime(rd["FechaInicio"]);
                            fechaFin = Convert.ToDateTime(rd["FechaFin"]);
                            dias = Convert.ToDecimal(rd["DiasSolicitados"]);
                        }
                    }
                }

                var destinatarios = new List<int> { personaColaboradorId };
                if (personaJefeId.HasValue)
                    destinatarios.Add(personaJefeId.Value);

                destinatarios = destinatarios.Distinct().ToList();

                var asunto =
                    $"Vacaciones canceladas por RH - Folio {solicitudId}";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#dc3545; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Vacaciones canceladas por RH</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>La solicitud de vacaciones del colaborador
         <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>
         ({System.Net.WebUtility.HtmlEncode(numeroEmpleado)}) fue cancelada por Recursos Humanos.</p>

      <div style='background:#f8f9fa; border-left:4px solid #dc3545; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {solicitudId}</p>
        <p style='margin:0 0 6px;'><strong>Periodo:</strong> {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
        <p style='margin:0 0 6px;'><strong>Días devueltos:</strong> {dias}</p>
        <p style='margin:0;'><strong>Motivo:</strong> {System.Net.WebUtility.HtmlEncode(motivo)}</p>
      </div>

      <p>Los días fueron devueltos al saldo disponible del colaborador.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    destinatarios,
                    asunto,
                    html);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error notificando cancelación RH. SolicitudID={SolicitudID}",
                    solicitudId);
            }
        }


        //Editar Vacaciones una vez Aprobadas

        [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> EditarVacacionesRegistradasRH(
    int solicitudId,
    DateTime fechaInicio,
    DateTime fechaFin,
    string motivo)
{
    int usuarioRhId = ObtenerUsuarioIdActual();
    if (usuarioRhId == 0)
        return Unauthorized();

    motivo = (motivo ?? string.Empty).Trim();

    if (fechaFin.Date < fechaInicio.Date)
    {
        TempData["ErrorVacacionesRH"] =
            "La fecha final no puede ser menor que la fecha inicial.";

        return RedirectToAction(nameof(BandejaRH), new { tab = "registradas" });
    }

    if (motivo.Length < 10)
    {
        TempData["ErrorVacacionesRH"] =
            "El motivo de la edición debe contener al menos 10 caracteres.";

        return RedirectToAction(nameof(BandejaRH), new { tab = "registradas" });
    }

    try
    {
        long edicionRhId;

        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            if (!EsUsuarioRecursosHumanos(conn, usuarioRhId))
                return Forbid();

            using (var cmd = new SqlCommand(
                "dbo.sp_Vacaciones_EditarRegistroRH",
                conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue(
                    "@SolicitudVacacionesID",
                    solicitudId);
                cmd.Parameters.AddWithValue("@UsuarioRHID", usuarioRhId);
                cmd.Parameters.AddWithValue("@NuevaFechaInicio", fechaInicio.Date);
                cmd.Parameters.AddWithValue("@NuevaFechaFin", fechaFin.Date);
                cmd.Parameters.AddWithValue("@Motivo", motivo);

                var outputEdicion = new SqlParameter(
                    "@EdicionRHID",
                    SqlDbType.BigInt)
                {
                    Direction = ParameterDirection.Output
                };

                cmd.Parameters.Add(outputEdicion);

                await cmd.ExecuteNonQueryAsync();

                edicionRhId = Convert.ToInt64(outputEdicion.Value);
            }
        }

        // El ajuste de saldo ya fue confirmado en la base de datos.
        // Si el correo falla, no se revierte la edición.
        await NotificarEdicionVacacionesRHAsync(edicionRhId);

        TempData["MensajeVacacionesRH"] =
            $"La solicitud {solicitudId} fue actualizada correctamente. " +
            "Los días no tomados se devolvieron al colaborador.";
    }
    catch (SqlException ex)
    {
        _logger.LogError(
            ex,
            "Error SQL editando vacaciones registradas por RH. SolicitudID={SolicitudID}",
            solicitudId);

        TempData["ErrorVacacionesRH"] = ex.Message;
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error editando vacaciones registradas por RH. SolicitudID={SolicitudID}",
            solicitudId);

        TempData["ErrorVacacionesRH"] =
            "No se pudo editar la solicitud: " + ex.Message;
    }

    return RedirectToAction(nameof(BandejaRH), new { tab = "registradas" });
}

private async Task NotificarEdicionVacacionesRHAsync(long edicionRhId)
{
    try
    {
        int solicitudId = 0;
        int personaColaboradorId = 0;
        int? personaJefeId = null;
        string numeroEmpleado = string.Empty;
        string nombreColaborador = "Colaborador";
        string nombreJefe = "Jefe inmediato";
        DateTime fechaInicioAnterior = DateTime.Today;
        DateTime fechaFinAnterior = DateTime.Today;
        DateTime fechaInicioNueva = DateTime.Today;
        DateTime fechaFinNueva = DateTime.Today;
        decimal diasAnteriores = 0;
        decimal diasNuevos = 0;
        decimal diasDevueltos = 0;
        string motivo = string.Empty;

        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            const string sql = @"
SELECT
    e.SolicitudVacacionesID,
    e.FechaInicioAnterior,
    e.FechaFinAnterior,
    e.DiasAnteriores,
    e.FechaInicioNueva,
    e.FechaFinNueva,
    e.DiasNuevos,
    e.DiasDevueltos,
    e.Motivo,
    p.PersonaID AS PersonaColaboradorID,
    p.NumeroEmpleado,
    CONCAT(
        ISNULL(p.ApellidoPaterno, ''), ' ',
        ISNULL(p.ApellidoMaterno, ''), ' ',
        ISNULL(p.Nombre, '')
    ) AS NombreColaborador,
    pj.PersonaID AS PersonaJefeID,
    CONCAT(
        ISNULL(pj.ApellidoPaterno, ''), ' ',
        ISNULL(pj.ApellidoMaterno, ''), ' ',
        ISNULL(pj.Nombre, '')
    ) AS NombreJefe
FROM dbo.VacacionesSolicitudEdicionRH e
INNER JOIN dbo.VacacionesSolicitud s
    ON s.SolicitudVacacionesID = e.SolicitudVacacionesID
INNER JOIN dbo.Persona p
    ON p.PersonaID = s.PersonaID
LEFT JOIN dbo.Persona pj
    ON pj.PersonaID = p.JefeInmediatoPersonaID
WHERE e.EdicionRHID = @EdicionRHID;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@EdicionRHID", edicionRhId);

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    if (!await rd.ReadAsync())
                        return;

                    solicitudId = Convert.ToInt32(rd["SolicitudVacacionesID"]);
                    personaColaboradorId = Convert.ToInt32(
                        rd["PersonaColaboradorID"]);
                    personaJefeId = rd["PersonaJefeID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["PersonaJefeID"]);

                    numeroEmpleado = rd["NumeroEmpleado"]?.ToString() ?? "";
                    nombreColaborador =
                        rd["NombreColaborador"]?.ToString()?.Trim()
                        ?? "Colaborador";
                    nombreJefe =
                        rd["NombreJefe"]?.ToString()?.Trim()
                        ?? "Jefe inmediato";

                    fechaInicioAnterior = Convert.ToDateTime(
                        rd["FechaInicioAnterior"]);
                    fechaFinAnterior = Convert.ToDateTime(
                        rd["FechaFinAnterior"]);
                    diasAnteriores = Convert.ToDecimal(rd["DiasAnteriores"]);
                    fechaInicioNueva = Convert.ToDateTime(
                        rd["FechaInicioNueva"]);
                    fechaFinNueva = Convert.ToDateTime(
                        rd["FechaFinNueva"]);
                    diasNuevos = Convert.ToDecimal(rd["DiasNuevos"]);
                    diasDevueltos = Convert.ToDecimal(rd["DiasDevueltos"]);
                    motivo = rd["Motivo"]?.ToString() ?? "";
                }
            }
        }

        var destinatarios = new List<int> { personaColaboradorId };
        if (personaJefeId.HasValue)
            destinatarios.Add(personaJefeId.Value);

        destinatarios = destinatarios.Distinct().ToList();

        var asunto =
            $"Vacaciones ajustadas por RH - Folio {solicitudId}";

        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:680px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Vacaciones ajustadas por Recursos Humanos</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>
        La solicitud del colaborador
        <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>
        ({System.Net.WebUtility.HtmlEncode(numeroEmpleado)}) fue actualizada por Recursos Humanos.
      </p>

      <div style='background:#fff3cd; border-left:4px solid #f0ad4e; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {solicitudId}</p>
        <p style='margin:0 0 6px;'><strong>Periodo anterior:</strong> {fechaInicioAnterior:dd/MM/yyyy} al {fechaFinAnterior:dd/MM/yyyy}</p>
        <p style='margin:0;'><strong>Días anteriores:</strong> {diasAnteriores}</p>
      </div>

      <div style='background:#eaf7ee; border-left:4px solid #198754; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Periodo corregido:</strong> {fechaInicioNueva:dd/MM/yyyy} al {fechaFinNueva:dd/MM/yyyy}</p>
        <p style='margin:0 0 6px;'><strong>Días realmente tomados:</strong> {diasNuevos}</p>
        <p style='margin:0;'><strong>Días devueltos al saldo:</strong> {diasDevueltos}</p>
      </div>

      <p><strong>Motivo:</strong> {System.Net.WebUtility.HtmlEncode(motivo)}</p>
      <p>El saldo disponible del colaborador ya fue actualizado.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

        await _notif.EnviarABccPersonasAsync(
            destinatarios,
            asunto,
            html);
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error notificando edición RH. EdicionRHID={EdicionRHID}",
            edicionRhId);
    }
}


        [HttpGet]
        public IActionResult SolicitudesPendientesJefe()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            var todas = new List<VacacionesSolicitudJefeVm>();
            // Inicializamos el VM que contendrá todas las listas
            var vm = new SolicitudesPendientesJefePantallaVm();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // --- BLOQUE 1: Solicitudes de Vacaciones Normales ---
                    var sqlSolicitudes = @"
                SELECT 
                    s.SolicitudVacacionesID,
                    p.NumeroEmpleado,
                    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador,
                    s.FechaSolicitud,
                    s.FechaInicio,
                    s.FechaFin,
                    s.DiasSolicitados,
                    s.EsAnticipada,
                    s.EstadoAutorizacion,
                    s.EstadoRecursosHumanos
                FROM VacacionesSolicitud s
                INNER JOIN Persona p ON s.PersonaID = p.PersonaID
                INNER JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                INNER JOIN Usuarios uj ON uj.PersonaID = pj.PersonaID
                WHERE uj.UsuarioID = @UsuarioJefeID
                ORDER BY s.FechaSolicitud DESC;";

                    using (var cmd = new SqlCommand(sqlSolicitudes, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioJefeID", usuarioId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                todas.Add(new VacacionesSolicitudJefeVm
                                {
                                    SolicitudVacacionesID = (int)reader["SolicitudVacacionesID"],
                                    NumeroEmpleado = reader["NumeroEmpleado"]?.ToString(),
                                    NombreColaborador = reader["NombreColaborador"]?.ToString(),
                                    FechaSolicitud = (DateTime)reader["FechaSolicitud"],
                                    FechaInicio = (DateTime)reader["FechaInicio"],
                                    FechaFin = (DateTime)reader["FechaFin"],
                                    DiasSolicitados = Convert.ToDecimal(reader["DiasSolicitados"]),
                                    EsAnticipada = (bool)reader["EsAnticipada"],
                                    EstadoAutorizacion = reader["EstadoAutorizacion"]?.ToString(),
                                    EstadoRecursosHumanos = reader["EstadoRecursosHumanos"]?.ToString()
                                });
                            }
                        }
                    }

                    // --- BLOQUE 2: Habilitaciones Especiales (Adelantadas) ---
                    var sqlHabilitaciones = @"
                SELECT 
                    h.HabilitacionID,
                    (p.ApellidoPaterno + ' ' + p.Nombre) AS NombreColaborador,
                    h.MotivoSolicitud,
                    h.FechaSolicitud
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                WHERE p.JefeInmediatoPersonaID = (SELECT PersonaID FROM Usuarios WHERE UsuarioID = @UsuarioJefeID)
                  AND h.EstatusJefe = 'Pendiente'
                  AND h.Completada = 0;";

                    using (var cmdH = new SqlCommand(sqlHabilitaciones, conn))
                    {
                        cmdH.Parameters.AddWithValue("@UsuarioJefeID", usuarioId);
                        using (var readerH = cmdH.ExecuteReader())
                        {
                            while (readerH.Read())
                            {
                                vm.HabilitacionesPendientes.Add(new HabilitacionPendienteJefeVm
                                {
                                    HabilitacionID = (int)readerH["HabilitacionID"],
                                    NombreColaborador = readerH["NombreColaborador"].ToString(),
                                    Motivo = readerH["MotivoSolicitud"].ToString(),
                                    FechaSolicitud = (DateTime)readerH["FechaSolicitud"]
                                });
                            }
                        }
                    }
                }

                // --- BLOQUE 3: Clasificación de listas 
                var hoy = DateTime.Today;
                var limite = hoy.AddDays(14);

                bool EsPendiente(VacacionesSolicitudJefeVm s) =>
                    string.Equals((s.EstadoAutorizacion ?? "").Trim(), "Pendiente", StringComparison.OrdinalIgnoreCase);

                bool EsAutorizadaJefe(VacacionesSolicitudJefeVm s) =>
                    string.Equals((s.EstadoAutorizacion ?? "").Trim(), "Autorizada", StringComparison.OrdinalIgnoreCase);

                bool EsRechazada(VacacionesSolicitudJefeVm s) =>
                    string.Equals((s.EstadoAutorizacion ?? "").Trim(), "Rechazada", StringComparison.OrdinalIgnoreCase)
                    || string.Equals((s.EstadoRecursosHumanos ?? "").Trim(), "Rechazada", StringComparison.OrdinalIgnoreCase);

                vm.Pendientes = todas.Where(EsPendiente).OrderByDescending(x => x.FechaSolicitud).ToList();
                vm.Proximas = todas.Where(s => !EsRechazada(s) && EsAutorizadaJefe(s) && s.FechaInicio.Date >= hoy && s.FechaInicio.Date <= limite)
                                   .OrderBy(s => s.FechaInicio).ToList();
                vm.Historial = todas.OrderByDescending(x => x.FechaSolicitud).ToList();

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando SolicitudesPendientesJefe");
                ViewBag.Error = $"Error al cargar solicitudes: {ex.Message}";
                return View(new SolicitudesPendientesJefePantallaVm());
            }
        }


        //Método que recibe la decición de un jefe sobre la solicitud de vacaciones


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarComoJefe(int solicitudId, bool autorizar, string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("sp_Vacaciones_AutorizarPorJefe", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@SolicitudVacacionesID", solicitudId);
                        cmd.Parameters.AddWithValue("@JefeUsuarioID", usuarioId);
                        cmd.Parameters.AddWithValue("@Autorizar", autorizar ? 1 : 0);
                        cmd.Parameters.AddWithValue("@ComentariosJefe", (object?)comentarios ?? DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }

                // Correo al SOLICITANTE: aprobada o rechazada
                await NotificarSolicitanteDecisionAsync(solicitudId, autorizar, comentarios);

                //  Correo a RRHH SOLO si fue aprobada
                if (autorizar)
                {
                    await NotificarRH_SolicitudAutorizadaAsync(solicitudId);
                }

                TempData["MensajeVacacionesJefe"] = autorizar
                    ? "La solicitud fue autorizada correctamente."
                    : "La solicitud fue rechazada correctamente.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al autorizar/rechazar solicitud de vacaciones como jefe");
                TempData["ErrorVacacionesJefe"] = $"Error al procesar la solicitud: {ex.Message}";
            }

            return RedirectToAction("SolicitudesPendientesJefe");
        }

        [HttpPost]
        public async Task<IActionResult> GuardarAjusteManual(int PersonaID, DateTime FechaInicio, DateTime FechaFin, string Observaciones)
        {
            int usuarioRhId = ObtenerUsuarioIdActual();
            if (usuarioRhId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            int nuevaSolicitudId = 0;

                            // 1. Crear la solicitud en el historial usando tu SP
                            using (var cmd = new SqlCommand("sp_Vacaciones_CrearSolicitud", conn, trans))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Parameters.AddWithValue("@PersonaID", PersonaID);
                                cmd.Parameters.AddWithValue("@UsuarioSolicitaID", usuarioRhId);
                                cmd.Parameters.AddWithValue("@FechaInicio", FechaInicio.Date);
                                cmd.Parameters.AddWithValue("@FechaFin", FechaFin.Date);
                                cmd.Parameters.AddWithValue("@Origen", "RecursosHumanos");
                                cmd.Parameters.AddWithValue("@Observaciones", "[AJUSTE DIRECTO RH] " + (Observaciones ?? ""));

                                SqlParameter outputId = new SqlParameter("@SolicitudVacacionesID", SqlDbType.Int) { Direction = ParameterDirection.Output };
                                cmd.Parameters.Add(outputId);

                                await cmd.ExecuteNonQueryAsync();
                                nuevaSolicitudId = (int)outputId.Value;
                            }

                            // 2. Autorizar la solicitud automáticamente para que sea oficial
                            string sqlAutorizar = @"UPDATE VacacionesSolicitud 
                        SET EstadoAutorizacion = 'Autorizada', 
                            EstadoRecursosHumanos = 'Registrado',
                            FechaAutorizacionJefe = GETDATE(),
                            FechaAutorizacionRH = GETDATE(),
                            UsuarioRecursosHumanosID = @RhID
                        WHERE SolicitudVacacionesID = @SolicitudID";

                            using (var cmdAut = new SqlCommand(sqlAutorizar, conn, trans))
                            {
                                cmdAut.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);
                                cmdAut.Parameters.AddWithValue("@RhID", usuarioRhId);
                                await cmdAut.ExecuteNonQueryAsync();
                            }

                            // 3. DESCUENTO REAL: Actualizar la columna DiasTomados en VacacionesSaldoAnual
                            // Usamos tu función fn_ContarDiasHabiles para que coincida con lo registrado en el historial
                            string sqlUpdateSaldo = @"UPDATE VacacionesSaldoAnual 
    SET DiasTomados = DiasTomados + dbo.fn_ContarDiasHabiles(@Inicio, @Fin)
    WHERE PersonaID = @PersonaID 
    AND Anio = (SELECT MAX(Anio) FROM VacacionesSaldoAnual WHERE PersonaID = @PersonaID)";

                            using (var cmdSaldo = new SqlCommand(sqlUpdateSaldo, conn, trans))
                            {
                                cmdSaldo.Parameters.AddWithValue("@Inicio", FechaInicio);
                                cmdSaldo.Parameters.AddWithValue("@Fin", FechaFin);
                                cmdSaldo.Parameters.AddWithValue("@PersonaID", PersonaID);

                                int filasAfectadas = await cmdSaldo.ExecuteNonQueryAsync();

                                if (filasAfectadas == 0)
                                {
                                    throw new Exception("El empleado no tiene ningún registro de saldo generado en VacacionesSaldoAnual.");
                                }
                            }

                            trans.Commit();
                        }
                        catch (Exception ex)
                        {
                            trans.Rollback();
                            throw;
                        }
                    }
                }
                return Json(new { success = true, message = "Días descontados y registrados correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en GuardarAjusteManual");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }


        private async Task NotificarJefeSolicitudModificadaAsync(
            int solicitudId,
            DateTime fechaInicioAnterior,
            DateTime fechaFinAnterior,
            decimal diasAnteriores)
        {
            try
            {
                int? personaJefeId = null;
                string nombreJefe = "Jefe inmediato";
                string nombreColaborador = "Colaborador";
                string numeroEmpleado = "";
                DateTime fechaInicioNueva = DateTime.Today;
                DateTime fechaFinNueva = DateTime.Today;
                decimal diasNuevos = 0m;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand(@"
SELECT
    s.FechaInicio,
    s.FechaFin,
    s.DiasSolicitados,
    p.NumeroEmpleado,
    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador,
    pj.PersonaID AS PersonaJefeID,
    (pj.ApellidoPaterno + ' ' + pj.ApellidoMaterno + ' ' + pj.Nombre) AS NombreJefe
FROM VacacionesSolicitud s
INNER JOIN Persona p ON p.PersonaID = s.PersonaID
LEFT JOIN Persona pj ON pj.PersonaID = p.JefeInmediatoPersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;", conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (!await rd.ReadAsync())
                                return;

                            if (rd["PersonaJefeID"] != DBNull.Value)
                                personaJefeId = Convert.ToInt32(rd["PersonaJefeID"]);

                            nombreJefe = rd["NombreJefe"]?.ToString() ?? nombreJefe;
                            nombreColaborador = rd["NombreColaborador"]?.ToString() ?? nombreColaborador;
                            numeroEmpleado = rd["NumeroEmpleado"]?.ToString() ?? "";
                            fechaInicioNueva = Convert.ToDateTime(rd["FechaInicio"]);
                            fechaFinNueva = Convert.ToDateTime(rd["FechaFin"]);
                            diasNuevos = Convert.ToDecimal(rd["DiasSolicitados"]);
                        }
                    }
                }

                if (!personaJefeId.HasValue)
                {
                    _logger.LogWarning(
                        "No se encontró jefe inmediato para notificar la modificación de la solicitud {SolicitudID}.",
                        solicitudId);
                    return;
                }

                var asunto =
                    $"Solicitud de vacaciones modificada - {nombreColaborador} (folio {solicitudId})";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:680px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud de vacaciones modificada</h2>
    </div>
    <div style='padding:22px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreJefe)}</strong>,</p>
      <p>
        El colaborador <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>
        ({System.Net.WebUtility.HtmlEncode(numeroEmpleado)}) modificó su solicitud de vacaciones
        con folio <strong>{solicitudId}</strong>.
      </p>

      <table style='width:100%; border-collapse:collapse; margin:16px 0;'>
        <thead>
          <tr>
            <th style='text-align:left; padding:10px; background:#f1f3f5;'>Dato</th>
            <th style='text-align:left; padding:10px; background:#f1f3f5;'>Antes</th>
            <th style='text-align:left; padding:10px; background:#f1f3f5;'>Ahora</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td style='padding:10px; border-bottom:1px solid #dee2e6;'><strong>Periodo</strong></td>
            <td style='padding:10px; border-bottom:1px solid #dee2e6;'>{fechaInicioAnterior:dd/MM/yyyy} al {fechaFinAnterior:dd/MM/yyyy}</td>
            <td style='padding:10px; border-bottom:1px solid #dee2e6;'>{fechaInicioNueva:dd/MM/yyyy} al {fechaFinNueva:dd/MM/yyyy}</td>
          </tr>
          <tr>
            <td style='padding:10px;'><strong>Días hábiles</strong></td>
            <td style='padding:10px;'>{diasAnteriores}</td>
            <td style='padding:10px;'>{diasNuevos}</td>
          </tr>
        </tbody>
      </table>

      <p>La solicitud continúa pendiente de tu autorización. Ingresa al módulo de <strong>Vacaciones</strong> para revisarla.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaJefeId.Value },
                    asunto,
                    html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error notificando al jefe la modificación de la solicitud {SolicitudID}.",
                    solicitudId);
            }
        }


        private async Task NotificarJefeSolicitudCanceladaAsync(
            int solicitudId,
            DateTime fechaInicio,
            DateTime fechaFin,
            decimal diasSolicitados,
            string motivo)
        {
            try
            {
                int? personaJefeId = null;
                string nombreJefe = "Jefe inmediato";
                string nombreColaborador = "Colaborador";
                string numeroEmpleado = "";

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand(@"
SELECT
    p.NumeroEmpleado,
    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador,
    pj.PersonaID AS PersonaJefeID,
    (pj.ApellidoPaterno + ' ' + pj.ApellidoMaterno + ' ' + pj.Nombre) AS NombreJefe
FROM VacacionesSolicitud s
INNER JOIN Persona p ON p.PersonaID = s.PersonaID
LEFT JOIN Persona pj ON pj.PersonaID = p.JefeInmediatoPersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;", conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (!await rd.ReadAsync())
                                return;

                            if (rd["PersonaJefeID"] != DBNull.Value)
                                personaJefeId = Convert.ToInt32(rd["PersonaJefeID"]);

                            nombreJefe = rd["NombreJefe"]?.ToString() ?? nombreJefe;
                            nombreColaborador = rd["NombreColaborador"]?.ToString() ?? nombreColaborador;
                            numeroEmpleado = rd["NumeroEmpleado"]?.ToString() ?? "";
                        }
                    }
                }

                if (!personaJefeId.HasValue)
                {
                    _logger.LogWarning(
                        "No se encontró jefe inmediato para notificar la cancelación de la solicitud {SolicitudID}.",
                        solicitudId);
                    return;
                }

                var asunto =
                    $"Solicitud de vacaciones cancelada - {nombreColaborador} (folio {solicitudId})";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#dc3545; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud de vacaciones cancelada</h2>
    </div>
    <div style='padding:22px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreJefe)}</strong>,</p>
      <p>
        El colaborador <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>
        ({System.Net.WebUtility.HtmlEncode(numeroEmpleado)}) canceló su solicitud de vacaciones.
      </p>

      <div style='background:#f8f9fa; border-left:4px solid #dc3545; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {solicitudId}</p>
        <p style='margin:0 0 6px;'><strong>Periodo:</strong> {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
        <p style='margin:0 0 6px;'><strong>Días hábiles:</strong> {diasSolicitados}</p>
        <p style='margin:0;'><strong>Motivo:</strong> {System.Net.WebUtility.HtmlEncode(motivo)}</p>
      </div>

      <p>La solicitud fue retirada de tus pendientes y ya no requiere autorización.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaJefeId.Value },
                    asunto,
                    html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error notificando al jefe la cancelación de la solicitud {SolicitudID}.",
                    solicitudId);
            }
        }


        private async Task NotificarSolicitanteDecisionAsync(int solicitudId, bool autorizar, string? comentarios)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // Traer datos básicos del solicitante y solicitud
                    var sql = @"
SELECT
    s.SolicitudVacacionesID,
    s.FechaInicio,
    s.FechaFin,
    s.DiasSolicitados,
    s.EsAnticipada,
    s.EstadoAutorizacion,
    s.EstadoRecursosHumanos,
    p.PersonaID,
    p.NumeroEmpleado,
    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador
FROM VacacionesSolicitud s
INNER JOIN Persona p ON s.PersonaID = p.PersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read()) return;

                            int personaSolicitanteId = (int)rd["PersonaID"];
                            var nombre = rd["NombreColaborador"]?.ToString() ?? "Colaborador";
                            var numEmp = rd["NumeroEmpleado"]?.ToString() ?? "";
                            var fechaIni = (DateTime)rd["FechaInicio"];
                            var fechaFin = (DateTime)rd["FechaFin"];
                            var dias = Convert.ToDecimal(rd["DiasSolicitados"]);
                            var esAnt = Convert.ToBoolean(rd["EsAnticipada"]);

                            var estadoTexto = autorizar ? "APROBADA" : "RECHAZADA";
                            var asunto = $"Solicitud de vacaciones {estadoTexto} (folio {solicitudId})";

                            var comentariosHtml = string.IsNullOrWhiteSpace(comentarios)
                                ? ""
                                : $"<p><strong>Comentarios del jefe:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>";

                            var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:{(autorizar ? "#198754" : "#dc3545")}; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud de vacaciones {estadoTexto}</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombre)}</strong> ({System.Net.WebUtility.HtmlEncode(numEmp)}),</p>
      <p>Tu solicitud de vacaciones con folio <strong>{solicitudId}</strong> ha sido <strong>{estadoTexto}</strong> por tu jefe inmediato.</p>

      <div style='background:#f8f9fa; border-left:4px solid {(autorizar ? "#198754" : "#dc3545")}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Periodo:</strong> {fechaIni:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
        <p style='margin:0 0 6px;'><strong>Días solicitados:</strong> {dias}</p>
        <p style='margin:0;'><strong>Anticipada:</strong> {(esAnt ? "Sí" : "No")}</p>
      </div>

      {comentariosHtml}

      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Vacaciones</strong> (sección <strong>Mis vacaciones</strong>).</p>
       <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                            var personaIds = new List<int> { personaSolicitanteId };
                            await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo al solicitante (SolicitudID={Id})", solicitudId);
            }
        }


        private async Task NotificarRH_SolicitudAutorizadaAsync(int solicitudId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    //  Obtener todos los PersonaID que pertenecen a RH
                    var rhPersonaIds = new List<int>();
                    var sqlRH = @"
SELECT DISTINCT u.PersonaID
FROM Usuarios u
INNER JOIN EmpleadoDepartamentos ed ON ed.UsuarioID = u.UsuarioID AND ed.Activo = 1
INNER JOIN Departamentos d ON d.DepartamentoID = ed.DepartamentoID AND d.Activo = 1
WHERE u.PersonaID IS NOT NULL
  AND (UPPER(d.NombreDepartamento) LIKE '%RECURSOS HUMANOS%' OR UPPER(d.NombreDepartamento) LIKE 'RH%');";

                    using (var cmd = new SqlCommand(sqlRH, conn))
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            if (rd[0] != DBNull.Value)
                                rhPersonaIds.Add(Convert.ToInt32(rd[0]));
                        }
                    }

                    if (rhPersonaIds.Count == 0)
                        return;

                    // Datos básicos de la solicitud (para que RH identifique rápido)
                    var sqlSolicitud = @"
SELECT
    s.SolicitudVacacionesID,
    s.FechaInicio,
    s.FechaFin,
    s.DiasSolicitados,
    p.NumeroEmpleado,
    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador
FROM VacacionesSolicitud s
INNER JOIN Persona p ON s.PersonaID = p.PersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;";

                    string nombreColab = "Colaborador";
                    string numEmp = "";
                    DateTime fechaIni = DateTime.Today;
                    DateTime fechaFin = DateTime.Today;
                    decimal dias = 0;

                    using (var cmd2 = new SqlCommand(sqlSolicitud, conn))
                    {
                        cmd2.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd2 = cmd2.ExecuteReader())
                        {
                            if (rd2.Read())
                            {
                                nombreColab = rd2["NombreColaborador"]?.ToString() ?? nombreColab;
                                numEmp = rd2["NumeroEmpleado"]?.ToString() ?? numEmp;
                                fechaIni = (DateTime)rd2["FechaInicio"];
                                fechaFin = (DateTime)rd2["FechaFin"];
                                dias = Convert.ToDecimal(rd2["DiasSolicitados"]);
                            }
                        }
                    }

                    // 3) Correo a RH
                    var asunto = $"Vacaciones aprobadas - Folio {solicitudId} ({numEmp})";

                    var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud de vacaciones aprobada</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Se informa que una solicitud de vacaciones ha sido <strong>aprobada</strong> por el jefe inmediato.</p>

      <div style='background:#f8f9fa; border-left:4px solid #0d6efd; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {solicitudId}</p>
        <p style='margin:0 0 6px;'><strong>Empleado:</strong> {System.Net.WebUtility.HtmlEncode(numEmp)} - {System.Net.WebUtility.HtmlEncode(nombreColab)}</p>
        <p style='margin:0 0 6px;'><strong>Periodo:</strong> {fechaIni:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
        <p style='margin:0;'><strong>Días solicitados:</strong> {dias}</p>
      </div>

      <p><strong>Revisa el módulo de Vacaciones</strong> en Intranet para más información y para realizar el registro correspondiente.</p>
<p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                    await _notif.EnviarABccPersonasAsync(rhPersonaIds, asunto, html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a RH (SolicitudID={Id})", solicitudId);
            }
        }



        [HttpGet]
        public IActionResult SolicitudesPendientesRH()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var listaSolicitudesNormales = new List<VacacionesSolicitudRHVm>();
            var habilitacionesEspeciales = new List<HabilitacionPendienteRHVm>();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. Cargar Solicitudes de Vacaciones Normales (La tabla de abajo)
                    var sqlSolicitudes = @"
                SELECT s.SolicitudVacacionesID, p.NumeroEmpleado, 
                       (p.ApellidoPaterno + ' ' + p.Nombre) AS NombreColaborador,
                       s.FechaSolicitud, s.FechaInicio, s.FechaFin, s.DiasSolicitados,
                       s.EsAnticipada, s.EstadoAutorizacion, s.EstadoRecursosHumanos
                FROM VacacionesSolicitud s
                INNER JOIN Persona p ON s.PersonaID = p.PersonaID
                WHERE s.EstadoAutorizacion = 'Autorizada'
                  AND s.EstadoRecursosHumanos = 'SinRegistrar'
                ORDER BY s.FechaSolicitud DESC;";

                    using (var cmdS = new SqlCommand(sqlSolicitudes, conn))
                    using (var rS = cmdS.ExecuteReader())
                    {
                        while (rS.Read())
                        {
                            listaSolicitudesNormales.Add(new VacacionesSolicitudRHVm
                            {
                                SolicitudVacacionesID = (int)rS["SolicitudVacacionesID"],
                                NumeroEmpleado = rS["NumeroEmpleado"]?.ToString(),
                                NombreColaborador = rS["NombreColaborador"]?.ToString(),
                                FechaSolicitud = (DateTime)rS["FechaSolicitud"],
                                FechaInicio = (DateTime)rS["FechaInicio"],
                                FechaFin = (DateTime)rS["FechaFin"],
                                DiasSolicitados = Convert.ToDecimal(rS["DiasSolicitados"]),
                                EsAnticipada = (bool)rS["EsAnticipada"],
                                EstadoAutorizacion = rS["EstadoAutorizacion"]?.ToString(),
                                EstadoRecursosHumanos = rS["EstadoRecursosHumanos"]?.ToString()
                            });
                        }
                    }

                    // 2. Cargar Habilitaciones Especiales (La tabla de arriba)
                    var sqlH = @"
                SELECT h.HabilitacionID, (p.ApellidoPaterno + ' ' + p.Nombre) AS NombreColaborador,
                       h.MotivoSolicitud, (pj.ApellidoPaterno + ' ' + pj.Nombre) AS NombreJefe,
                       h.FechaAutorizacionJefe
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                LEFT JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                WHERE h.EstatusJefe = 'Autorizado' AND h.EstatusRH = 'Pendiente' AND h.Completada = 0";

                    using (var cmdH = new SqlCommand(sqlH, conn))
                    using (var rH = cmdH.ExecuteReader())
                    {
                        while (rH.Read())
                        {
                            habilitacionesEspeciales.Add(new HabilitacionPendienteRHVm
                            {
                                HabilitacionID = (int)rH["HabilitacionID"],
                                NombreColaborador = rH["NombreColaborador"].ToString(),
                                Motivo = rH["MotivoSolicitud"].ToString(),
                                NombreJefe = rH["NombreJefe"]?.ToString() ?? "N/A",
                                FechaAutorizacionJefe = Convert.ToDateTime(rH["FechaAutorizacionJefe"])
                            });
                        }
                    }
                }

                // PASO CRÍTICO: Asignar al ViewBag para que la vista lo vea
                ViewBag.HabilitacionesEspeciales = habilitacionesEspeciales;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SolicitudesPendientesRH");
                ViewBag.Error = ex.Message;
            }

            return View(listaSolicitudesNormales);
        }



        //REVISAR EL ERROR DE SALDO INSUFICIENTE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RegistrarComoRH(int solicitudId)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    if (!EsUsuarioRecursosHumanos(conn, usuarioId))
                    {
                        return Forbid();
                    }

                    using (var cmd = new SqlCommand("sp_Vacaciones_RegistrarEnRH", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@SolicitudVacacionesID", solicitudId);
                        cmd.Parameters.AddWithValue("@UsuarioRHID", usuarioId);

                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["MensajeVacacionesRH"] = "La solicitud fue registrada en RH y se actualizaron los saldos.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar vacaciones en RH");
                TempData["ErrorVacacionesRH"] = $"Error al registrar en RH: {ex.Message}";
            }

            return RedirectToAction("SolicitudesPendientesRH");
        }


        //Metodo para ocultar solicitudes enRRHHH

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OmitirRegistroRH(int solicitudId)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    if (!EsUsuarioRecursosHumanos(conn, usuarioId))
                        return Forbid();

                    var sql = @"UPDATE VacacionesSolicitud 
                SET EstadoRecursosHumanos = 'Cancelada', 
                    EstadoAutorizacion = 'Rechazada' 
                WHERE SolicitudVacacionesID = @id";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        cmd.ExecuteNonQuery();
                    }
                }

                await NotificarSaldoInsuficienteRHAsync(solicitudId);

                TempData["MensajeVacacionesRH"] =
                    "La solicitud fue descartada por saldo insuficiente. Se notificó al colaborador y a su jefe.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al omitir solicitud");
                TempData["ErrorVacacionesRH"] = "No se pudo ocultar la solicitud.";
            }

            return RedirectToAction("SolicitudesPendientesRH");
        }


        //HELPER PARA NOTIFCAR

        private async Task NotificarSaldoInsuficienteRHAsync(int solicitudId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    var sql = @"
SELECT
    s.SolicitudVacacionesID,
    s.FechaInicio,
    s.FechaFin,
    s.DiasSolicitados,
    p.PersonaID AS PersonaColaboradorID,
    p.NumeroEmpleado,
    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador,
    pj.PersonaID AS PersonaJefeID,
    (pj.ApellidoPaterno + ' ' + pj.ApellidoMaterno + ' ' + pj.Nombre) AS NombreJefe
FROM VacacionesSolicitud s
INNER JOIN Persona p ON s.PersonaID = p.PersonaID
LEFT JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
WHERE s.SolicitudVacacionesID = @SolicitudID;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read()) return;

                            int personaColaboradorId = Convert.ToInt32(rd["PersonaColaboradorID"]);
                            int? personaJefeId = rd["PersonaJefeID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["PersonaJefeID"]);

                            var nombreColaborador = rd["NombreColaborador"]?.ToString() ?? "Colaborador";
                            var nombreJefe = rd["NombreJefe"]?.ToString() ?? "Jefe inmediato";
                            var numEmp = rd["NumeroEmpleado"]?.ToString() ?? "";
                            var fechaIni = Convert.ToDateTime(rd["FechaInicio"]);
                            var fechaFin = Convert.ToDateTime(rd["FechaFin"]);
                            var dias = Convert.ToDecimal(rd["DiasSolicitados"]);

                            var periodoHtml = $@"
<div style='background:#f8f9fa; border-left:4px solid #dc3545; padding:12px 14px; border-radius:6px; margin:14px 0;'>
  <p style='margin:0 0 6px;'><strong>Folio:</strong> {solicitudId}</p>
  <p style='margin:0 0 6px;'><strong>Colaborador:</strong> {System.Net.WebUtility.HtmlEncode(numEmp)} - {System.Net.WebUtility.HtmlEncode(nombreColaborador)}</p>
  <p style='margin:0 0 6px;'><strong>Periodo solicitado:</strong> {fechaIni:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
  <p style='margin:0;'><strong>Días solicitados:</strong> {dias}</p>
</div>";

                            var asuntoColaborador =
                                $"Vacaciones no registradas por RH - saldo insuficiente (folio {solicitudId})";

                            var htmlColaborador = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#dc3545; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Vacaciones no registradas</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>,</p>
      <p>Te informamos que Recursos Humanos <strong>no registró</strong> tus vacaciones debido a <strong>saldo insuficiente</strong>.</p>
      {periodoHtml}
      <p>En caso de que consideres que esto es incorrecto, por favor comunícate con <strong>Recursos Humanos</strong> para revisar el asunto.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                            await _notif.EnviarABccPersonasAsync(
                                new List<int> { personaColaboradorId },
                                asuntoColaborador,
                                htmlColaborador
                            );

                            if (personaJefeId.HasValue)
                            {
                                var asuntoJefe =
                                    $"Vacaciones no registradas por RH - {nombreColaborador} (folio {solicitudId})";

                                var htmlJefe = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#dc3545; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Vacaciones no registradas por RH</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreJefe)}</strong>,</p>
      <p>
        Te informamos que la solicitud de vacaciones que aprobaste para
        <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>
        no fue registrada por Recursos Humanos debido a <strong>saldo insuficiente del colaborador</strong>.
      </p>
      {periodoHtml}
      <p>En caso de que esta información sea incorrecta, el colaborador deberá comunicarse con <strong>Recursos Humanos</strong> para revisar el asunto.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                                await _notif.EnviarABccPersonasAsync(
                                    new List<int> { personaJefeId.Value },
                                    asuntoJefe,
                                    htmlJefe
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correos por saldo insuficiente RH (SolicitudID={Id})", solicitudId);
            }
        }


        //Helper para obtener datos de un jefe y mandar correo

        private async Task NotificarJefeNuevaSolicitudAsync(int solicitudId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    var sql = @"
                SELECT 
                    s.SolicitudVacacionesID,
                    s.FechaInicio,
                    s.FechaFin,
                    s.DiasSolicitados,
                    p.NumeroEmpleado,
                    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS NombreColaborador,
                    pj.PersonaID AS PersonaJefeID,
                    (pj.ApellidoPaterno + ' ' + pj.ApellidoMaterno + ' ' + pj.Nombre) AS NombreJefe
                FROM VacacionesSolicitud s
                INNER JOIN Persona p  ON s.PersonaID = p.PersonaID
                INNER JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                WHERE s.SolicitudVacacionesID = @SolicitudID;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read())
                                return; // nada que enviar

                            var personaJefeIdObj = rd["PersonaJefeID"];
                            if (personaJefeIdObj == DBNull.Value)
                                return;

                            int personaJefeId = (int)personaJefeIdObj;

                            var nombreJefe = rd["NombreJefe"]?.ToString() ?? "Jefe inmediato";
                            var nombreColab = rd["NombreColaborador"]?.ToString() ?? "Colaborador";
                            var numEmp = rd["NumeroEmpleado"]?.ToString() ?? "";

                            var fechaIni = (DateTime)rd["FechaInicio"];
                            var fechaFin = (DateTime)rd["FechaFin"];
                            var dias = Convert.ToDecimal(rd["DiasSolicitados"]);

                            var asunto = $"Solicitud de vacaciones de {nombreColab} (folio {solicitudId})";

                            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #f4f4f9;
            margin: 0;
            padding: 20px;
        }}
        .container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #2ecc71 0%, #27ae60 100%);
            color: white;
            padding: 24px 20px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 22px;
            font-weight: 600;
        }}
        .content {{ padding: 24px 22px; font-size: 15px; color: #333; }}
        .resumen {{
            background-color: #f8f9fa;
            border-left: 4px solid #2ecc71;
            padding: 12px 16px;
            margin: 16px 0;
            border-radius: 4px;
        }}
        .footer {{
            background-color: #f8f9fa;
            padding: 16px;
            text-align: center;
            font-size: 12px;
            color: #888;
        }}
    </style>
</head>
<body>
  <div class='container'>
    <div class='header'>
        <h1>Solicitud de vacaciones</h1>
    </div>
    <div class='content'>
        <p>Hola {System.Net.WebUtility.HtmlEncode(nombreJefe)},</p>
        <p>
            El colaborador <strong>{System.Net.WebUtility.HtmlEncode(nombreColab)}</strong>
            (número de empleado <strong>{System.Net.WebUtility.HtmlEncode(numEmp)}</strong>)
            ha registrado una nueva solicitud de vacaciones.
        </p>
        <div class='resumen'>
            <p><strong>Periodo:</strong> {fechaIni:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
            <p><strong>Días hábiles solicitados:</strong> {dias}</p>
            <p><strong>Folio:</strong> {solicitudId}</p>
        </div>
        <p>
            Por favor ingresa a la Intranet (módulo <strong>Vacaciones</strong>) para
            <strong>autorizar</strong> o <strong>rechazar</strong> esta solicitud.
        </p>
 <p>
           https://intranet.nsgroup.com.mx/
        </p>
    </div>
    <div class='footer'>
        <p>Este mensaje se generó automáticamente desde la Intranet de NS Group.</p>
        <p>No respondas directamente a este correo.</p>
    </div>
  </div>
</body>
</html>";


                            // pero sólo con el PersonaID del jefe
                            var personaIds = new List<int> { personaJefeId };
                            var resultado = await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);

                            _logger.LogInformation(
                                "📧 Correo vacaciones jefe: Enc={Enc}, Env={Env}, Filt={Filt}, Err={Err}",
                                resultado.Encontrados, resultado.Enviados,
                                resultado.FiltradosPorCandados, resultado.Errores);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Importantísimo: si el correo falla, NO rompemos la creación de la solicitud
                _logger.LogError(ex, "Error enviando notificación de vacaciones al jefe (SolicitudID={Id})", solicitudId);
            }
        }





        //Métpodo para el formato de solicitud

        [HttpGet]
        public IActionResult FormatoSolicitud(int id)   // id = SolicitudVacacionesID
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return Unauthorized();

            var vm = new VacacionesSolicitudFormatoVm();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    int personaActualId = ObtenerPersonaIdPorUsuario(conn, usuarioId);

                    // 1) Traer datos de la solicitud + colaborador + jefe
                    int personaSolicitudId = 0;
                    using (var cmd = new SqlCommand(@"
    SELECT 
        s.SolicitudVacacionesID,
        s.FechaSolicitud,
        s.FechaInicio,
        s.FechaFin,
        s.DiasSolicitados,
        s.EsAnticipada,
        s.EstadoAutorizacion,
        s.EstadoRecursosHumanos,
        s.FechaAutorizacionJefe,
        s.FechaAutorizacionRH,

        p.PersonaID,
        p.NumeroEmpleado,
        p.Nombre,
        p.ApellidoPaterno,
        p.ApellidoMaterno,
        p.Puesto,
        p.FechaIngreso,

        pj.Nombre          AS NombreJefeNombre,
        pj.ApellidoPaterno AS NombreJefeApellidoP,
        pj.ApellidoMaterno AS NombreJefeApellidoM,

        e.Nombre           AS NombreEmpresa,
        d.NombreDepartamento AS NombreDepartamento
    FROM VacacionesSolicitud s
    INNER JOIN Persona p  ON s.PersonaID = p.PersonaID
    LEFT JOIN Persona pj  ON p.JefeInmediatoPersonaID = pj.PersonaID
    LEFT JOIN Usuarios u  ON s.UsuarioID = u.UsuarioID
    LEFT JOIN UsuariosEmpresas ue ON u.UsuarioID = ue.UsuarioID AND ue.Activo = 1
    LEFT JOIN Empresas e  ON ue.EmpresaID = e.EmpresaID
    LEFT JOIN EmpleadoDepartamentos ed ON u.UsuarioID = ed.UsuarioID AND ed.Activo = 1
    LEFT JOIN Departamentos d         ON ed.DepartamentoID = d.DepartamentoID
    WHERE s.SolicitudVacacionesID = @SolicitudID;
", conn))

                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", id);

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read())
                                return NotFound();

                            personaSolicitudId = (int)rd["PersonaID"];

                            vm.SolicitudVacacionesID = (int)rd["SolicitudVacacionesID"];
                            vm.FechaSolicitud = (DateTime)rd["FechaSolicitud"];
                            vm.FechaInicio = (DateTime)rd["FechaInicio"];
                            vm.FechaFin = (DateTime)rd["FechaFin"];
                            vm.DiasSolicitados = Convert.ToDecimal(rd["DiasSolicitados"]);
                            vm.EsAnticipada = (bool)rd["EsAnticipada"];
                            vm.EstadoAutorizacion = rd["EstadoAutorizacion"]?.ToString();
                            vm.EstadoRH = rd["EstadoRecursosHumanos"]?.ToString();


                            vm.FechaAutorizacion = rd["FechaAutorizacionJefe"] == DBNull.Value
                                ? (DateTime?)null
                                : (DateTime)rd["FechaAutorizacionJefe"];

                            vm.FechaRegistroRH = rd["FechaAutorizacionRH"] == DBNull.Value
                                ? (DateTime?)null
                                : (DateTime)rd["FechaAutorizacionRH"];

                            vm.NumeroEmpleado = rd["NumeroEmpleado"]?.ToString();

                            var nom = rd["Nombre"]?.ToString() ?? "";
                            var apP = rd["ApellidoPaterno"]?.ToString() ?? "";
                            var apM = rd["ApellidoMaterno"]?.ToString() ?? "";
                            vm.NombreColaborador = $"{apP} {apM} {nom}".Trim();

                            vm.Puesto = rd["Puesto"] == DBNull.Value
                                ? ""
                                : rd["Puesto"].ToString();

                            if (rd["FechaIngreso"] != DBNull.Value)
                            {
                                vm.FechaIngreso = (DateTime)rd["FechaIngreso"];
                            }



                            vm.Sociedad = rd["NombreEmpresa"] == DBNull.Value
     ? ""
     : rd["NombreEmpresa"].ToString();

                            vm.Empresa = vm.Sociedad; // por ahora tratamos sociedad = empresa

                            vm.Departamento = rd["NombreDepartamento"] == DBNull.Value
                                ? ""
                                : rd["NombreDepartamento"].ToString();




                            var njNom = rd["NombreJefeNombre"]?.ToString() ?? "";
                            var njApP = rd["NombreJefeApellidoP"]?.ToString() ?? "";
                            var njApM = rd["NombreJefeApellidoM"]?.ToString() ?? "";

                            vm.NombreJefe = $"{njApP} {njApM} {njNom}".Trim();
                        }
                    }

                    // (Opcional) Seguridad: sólo colaborador, su jefe o RH
                    bool esColaborador = personaActualId == personaSolicitudId;
                    bool esRH = EsUsuarioRecursosHumanos(conn, usuarioId);

                    if (!esColaborador && !esRH)
                    {
                        return Forbid();
                    }

                    // 2) Traer saldos (como en CrearSolicitud: último año disponible)
                    using (var cmdSaldo = new SqlCommand(@"
                SELECT TOP 1 DiasCorrespondientes, DiasTomados, DiasDisponibles
                FROM vw_VacacionesSaldoActual
                WHERE PersonaID = @PersonaID
                ORDER BY AnioSaldo DESC;", conn))
                    {
                        cmdSaldo.Parameters.AddWithValue("@PersonaID", personaSolicitudId);

                        using (var rd2 = cmdSaldo.ExecuteReader())
                        {
                            if (rd2.Read())
                            {
                                vm.DiasCorrespondientes = rd2["DiasCorrespondientes"] != DBNull.Value
                                    ? Convert.ToDecimal(rd2["DiasCorrespondientes"])
                                    : 0;

                                vm.DiasTomados = rd2["DiasTomados"] != DBNull.Value
                                    ? Convert.ToDecimal(rd2["DiasTomados"])
                                    : 0;

                                vm.DiasDisponibles = rd2["DiasDisponibles"] != DBNull.Value
                                    ? Convert.ToDecimal(rd2["DiasDisponibles"])
                                    : 0;
                            }
                        }
                    }

                    // 3) Calcular antigüedad en años (aprox) si hay fecha de ingreso
                    if (vm.FechaIngreso.HasValue)
                    {
                        var hoy = DateTime.Today;
                        var fi = vm.FechaIngreso.Value;
                        int antig = hoy.Year - fi.Year;
                        if (fi.Date > hoy.AddYears(-antig)) antig--;
                        vm.AntiguedadAnios = Math.Max(antig, 0);
                    }
                }

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar formato de vacaciones (SolicitudID={Id})", id);
                return StatusCode(500, "Error al generar el formato de vacaciones.");
            }
        }

        //Métodos para que un jefe pueda crear solicitudes de su equiipo
        [HttpGet]
        public IActionResult CrearSolicitudEquipo()
        {
            int usuarioJefeId = ObtenerUsuarioIdActual();
            if (usuarioJefeId == 0) return Unauthorized();

            var vm = new CrearSolicitudEquipoVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                int personaJefeId = ObtenerPersonaIdPorUsuario(conn, usuarioJefeId);

                // Obtenemos solo las personas que dependen de este jefe
                string sqlEquipo = @"SELECT PersonaID, (ApellidoPaterno + ' ' + Nombre) as Nombre 
                             FROM Persona 
                             WHERE JefeInmediatoPersonaID = @JefeID AND EsColaboradorActivo = 1";

                using (var cmd = new SqlCommand(sqlEquipo, conn))
                {
                    cmd.Parameters.AddWithValue("@JefeID", personaJefeId);
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            vm.MiEquipo.Add(new SelectListItem
                            {
                                Value = rd["PersonaID"].ToString(),
                                Text = rd["Nombre"].ToString()
                            });
                        }
                    }
                }
            }

            vm.FechaInicio = DateTime.Today;
            vm.FechaFin = DateTime.Today;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSolicitudEquipo(CrearSolicitudEquipoVm model)
        {
            int usuarioJefeId = ObtenerUsuarioIdActual();
            if (usuarioJefeId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("sp_Vacaciones_CrearSolicitud", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@PersonaID", model.PersonaID);
                        cmd.Parameters.AddWithValue("@UsuarioSolicitaID", usuarioJefeId);
                        cmd.Parameters.AddWithValue("@FechaInicio", model.FechaInicio.Date);
                        cmd.Parameters.AddWithValue("@FechaFin", model.FechaFin.Date);
                        cmd.Parameters.AddWithValue("@Origen", "JefeDirecto"); // Identificamos que el jefe lo creó
                        cmd.Parameters.AddWithValue("@Observaciones", "[SOLICITUD POR JEFE] " + (model.Observaciones ?? ""));

                        var paramIdOut = new SqlParameter("@SolicitudVacacionesID", SqlDbType.Int) { Direction = ParameterDirection.Output };
                        cmd.Parameters.Add(paramIdOut);

                        cmd.ExecuteNonQuery();
                        int nuevaId = (int)paramIdOut.Value;

                        // lógica de notificar a RH directamente
                        // await NotificarRH_SolicitudAutorizadaAsync(nuevaId);
                    }
                }
                TempData["MensajeVacaciones"] = "Solicitud para el equipo creada correctamente.";
                return RedirectToAction("SolicitudesPendientesJefe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear solicitud para equipo");
                ViewBag.Error = ex.Message;
                return View(model);
            }
        }


        //Contralador para la banndeja de entrada de RRHH

        public IActionResult BandejaRH(string tab = "autorizadas", string? q = null)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            tab = (tab ?? "autorizadas").ToLower().Trim();
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

            var vm = new VacacionesBandejaRHPantallaVm { Tab = tab };

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    if (!EsUsuarioRecursosHumanos(conn, usuarioId))
                        return Forbid();

                    // --- BLOQUE 1: Contadores para tabs ---
                    var sqlConteo = @"
                SELECT
                    SUM(CASE WHEN EstadoAutorizacion = 'Pendiente' THEN 1 ELSE 0 END) AS Pendientes,
                    SUM(CASE WHEN EstadoAutorizacion = 'Autorizada' THEN 1 ELSE 0 END) AS Autorizadas,
                    SUM(CASE WHEN EstadoRecursosHumanos = 'Registrado' THEN 1 ELSE 0 END) AS Registradas,
                    SUM(CASE WHEN EstadoAutorizacion = 'Rechazada' THEN 1 ELSE 0 END) AS Rechazadas
                FROM dbo.vw_Vacaciones_BandejaRH_ConSaldo;";

                    using (var cmdC = new SqlCommand(sqlConteo, conn))
                    using (var rC = cmdC.ExecuteReader())
                    {
                        if (rC.Read())
                        {
                            ViewBag.Pendientes = rC["Pendientes"] != DBNull.Value ? Convert.ToInt32(rC["Pendientes"]) : 0;
                            ViewBag.Autorizadas = rC["Autorizadas"] != DBNull.Value ? Convert.ToInt32(rC["Autorizadas"]) : 0;
                            ViewBag.Registradas = rC["Registradas"] != DBNull.Value ? Convert.ToInt32(rC["Registradas"]) : 0;
                            ViewBag.Rechazadas = rC["Rechazadas"] != DBNull.Value ? Convert.ToInt32(rC["Rechazadas"]) : 0;
                        }
                    }

                    // --- BLOQUE 2: Cargar Habilitaciones Especiales (Paso Final RRHH) ---
                    var habilitacionesParaRRHH = new List<HabilitacionPendienteRHVm>();
                    var sqlHabilitaciones = @"
                SELECT 
                    h.HabilitacionID,
                    (p.ApellidoPaterno + ' ' + p.Nombre) AS NombreColaborador,
                    h.MotivoSolicitud,
                    (pj.ApellidoPaterno + ' ' + pj.Nombre) AS NombreJefe,
                    h.FechaAutorizacionJefe
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                LEFT JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                WHERE h.EstatusJefe = 'Autorizado' 
                  AND h.EstatusRH = 'Pendiente'
                  AND h.Completada = 0;";

                    using (var cmdH = new SqlCommand(sqlHabilitaciones, conn))
                    using (var rH = cmdH.ExecuteReader())
                    {
                        while (rH.Read())
                        {
                            var itemH = new HabilitacionPendienteRHVm
                            {
                                HabilitacionID = Convert.ToInt32(rH["HabilitacionID"]),
                                NombreColaborador = rH["NombreColaborador"]?.ToString() ?? "Desconocido",
                                Motivo = rH["MotivoSolicitud"]?.ToString() ?? "",
                                NombreJefe = rH["NombreJefe"]?.ToString() ?? "S/J",
                                FechaAutorizacionJefe = Convert.ToDateTime(rH["FechaAutorizacionJefe"])
                            };
                            vm.HabilitacionesEspeciales.Add(itemH);
                            habilitacionesParaRRHH.Add(itemH);
                        }
                    }


                    ViewBag.HabilitacionesEspeciales = habilitacionesParaRRHH;
                    ViewBag.Q = q;

                    // --- BLOQUE 3: Lógica de Tabs ---
                    if (vm.Tab == "usuarios")
                    {

                        var sqlUsuarios = @"
        SELECT 
            p.PersonaID, 
            p.NumeroEmpleado, 
            p.ClaveEmpleadoNomina, 
            (ISNULL(p.Nombre,'') + ' ' + ISNULL(p.ApellidoPaterno,'') + ' ' + ISNULL(p.ApellidoMaterno,'')) AS NombreCompleto, 
            p.Puesto,
            v.AnioSaldo AS Anio, 
            v.DiasCorrespondientes, 
            v.DiasExtra, 
            v.DiasTomados, 
            v.DiasCaducados, 
            v.DiasDisponibles,
            0 AS AnticipadasRegistradas, 
            0 AS AnticipadasPorRegistrar
        FROM Persona p
        CROSS APPLY (
            -- Esta es la clave: usamos la vista que SÍ le funciona al usuario en su panel
            SELECT TOP 1 * FROM dbo.vw_VacacionesSaldoActual 
            WHERE PersonaID = p.PersonaID 
            ORDER BY AnioSaldo DESC
        ) v
        WHERE p.EsColaboradorActivo = 1 
        ORDER BY NombreCompleto;";

                        using (var cmdU = new SqlCommand(sqlUsuarios, conn))
                        using (var rU = cmdU.ExecuteReader())
                        {
                            while (rU.Read())
                            {

                                vm.Usuarios.Add(new VacacionesUsuarioSaldoRHVm
                                {
                                    PersonaID = rU["PersonaID"] == DBNull.Value ? 0 : Convert.ToInt32(rU["PersonaID"]),
                                    NumeroEmpleado = rU["NumeroEmpleado"]?.ToString(),
                                    ClaveEmpleadoNomina = rU["ClaveEmpleadoNomina"]?.ToString(),
                                    NombreCompleto = rU["NombreCompleto"]?.ToString(),
                                    Puesto = rU["Puesto"]?.ToString(),
                                    Anio = rU["Anio"] == DBNull.Value ? 0 : Convert.ToInt32(rU["Anio"]),
                                    DiasCorrespondientes = rU["DiasCorrespondientes"] == DBNull.Value ? 0m : Convert.ToDecimal(rU["DiasCorrespondientes"]),
                                    DiasExtra = rU["DiasExtra"] == DBNull.Value ? 0m : Convert.ToDecimal(rU["DiasExtra"]),
                                    DiasTomados = rU["DiasTomados"] == DBNull.Value ? 0m : Convert.ToDecimal(rU["DiasTomados"]),
                                    DiasCaducados = rU["DiasCaducados"] == DBNull.Value ? 0m : Convert.ToDecimal(rU["DiasCaducados"]),
                                    DiasDisponibles = rU["DiasDisponibles"] == DBNull.Value ? 0m : Convert.ToDecimal(rU["DiasDisponibles"]),
                                    AnticipadasRegistradas = rU["AnticipadasRegistradas"] == DBNull.Value ? 0 : Convert.ToInt32(rU["AnticipadasRegistradas"]),
                                    AnticipadasPorRegistrar = rU["AnticipadasPorRegistrar"] == DBNull.Value ? 0 : Convert.ToInt32(rU["AnticipadasPorRegistrar"])
                                });
                            }
                        }
                        if (q != null)
                        {
                            vm.Usuarios = vm.Usuarios.Where(u => (u.NumeroEmpleado ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (u.NombreCompleto ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                        }
                    }
                    else if (vm.Tab == "vistaexcel")
                    {
                        var sqlExcel = @"
        SELECT 
            p.PersonaID, 
            p.NumeroEmpleado, 
            (ISNULL(p.Nombre,'') + ' ' + ISNULL(p.ApellidoPaterno,'') + ' ' + ISNULL(p.ApellidoMaterno,'')) AS NombreCompleto,
            p.Puesto, 
            p.FechaIngreso,
   d.NombreDepartamento AS Departamento, 
            v.AnioSaldo AS Anio,
            v.DiasCorrespondientes, 
            v.DiasDisponibles
      FROM Persona p

-- Relación con usuario
INNER JOIN Usuarios u 
    ON u.PersonaID = p.PersonaID

--  Relación con empleado-departamento
LEFT JOIN EmpleadoDepartamentos ed 
    ON ed.UsuarioID = u.UsuarioID
   AND ed.Activo = 1

--  Catálogo de departamentos
LEFT JOIN Departamentos d 
    ON d.DepartamentoID = ed.DepartamentoID

CROSS APPLY (
    SELECT TOP 1 * 
    FROM dbo.vw_VacacionesSaldoActual 
    WHERE PersonaID = p.PersonaID 
    ORDER BY AnioSaldo DESC
) v
        WHERE p.EsColaboradorActivo = 1
        ORDER BY NombreCompleto";

                        using (var cmd = new SqlCommand(sqlExcel, conn))
                        using (var r = cmd.ExecuteReader())
                        {
                            int cont = 1;
                            while (r.Read())
                            {
                                var fechaIng = r["FechaIngreso"] == DBNull.Value ? DateTime.Now : Convert.ToDateTime(r["FechaIngreso"]);

                                var fechaDeHoy = DateTime.Now;

                                // Cálculo básico de antigüedad para el reporte
                                int antiguedad = DateTime.Now.Year - fechaIng.Year;
                                if (DateTime.Now < fechaIng.AddYears(antiguedad)) antiguedad--;

                                vm.VistaExcel.Add(new VacacionesVistaExcelVm
                                {
                                    N = cont++,
                                    Nombre = r["NombreCompleto"].ToString(),
                                    Puesto = r["Puesto"].ToString(),
                                    FechaIngreso = fechaIng,
                                    Hoy = fechaDeHoy,
                                    Departamento = r["Departamento"]?.ToString(),
                                    AntiguedadAnios = antiguedad,
                                    DiasCorrespondientes = Convert.ToDecimal(r["DiasCorrespondientes"]),
                                    DiasDisponibles = Convert.ToDecimal(r["DiasDisponibles"])
                                });
                            }
                        }

                        if (!string.IsNullOrEmpty(q))
                        {
                            // Filtramos sobre la lista ya cargada para que coincida con lo que el usuario busca
                            vm.VistaExcel = vm.VistaExcel.Where(v =>
                                (v.Nombre ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                v.N2.ToString().Contains(q)
                            ).ToList();
                        }
                    }
                    else
                    {
                        string where;
                        switch (vm.Tab)
                        {
                            case "pendientes": where = "EstadoAutorizacion = 'Pendiente'"; break;
                            case "autorizadas": where = "EstadoAutorizacion = 'Autorizada'"; break;
                            case "registradas": where = "EstadoRecursosHumanos = 'Registrado'"; break;
                            case "rechazadas": where = "EstadoAutorizacion = 'Rechazada'"; break;
                            default: vm.Tab = "autorizadas"; where = "EstadoAutorizacion = 'Autorizada'"; break;
                        }
                        var sqlSolicitudes = $@"
SELECT 
    b.Folio,
    b.PersonaID,
    b.NumeroEmpleado,
    b.NombreCompleto,
    b.ClaveEmpleadoNomina,
    b.Puesto,
    b.FechaSolicitud,
    b.FechaInicio,
    b.FechaFin,
    b.FechaRegresoLabores,
    b.DiasSolicitados,
    b.EsAnticipada,
    b.EstadoAutorizacion,
    b.EstadoRecursosHumanos,

    s.AnioSaldo,
    s.DiasCorrespondientes,
    s.DiasExtra,
    s.DiasTomados,
    s.DiasCaducados,
    s.DiasDisponibles

FROM dbo.vw_Vacaciones_BandejaRH_ConSaldo b
INNER JOIN Persona p ON p.PersonaID = b.PersonaID

CROSS APPLY (
    SELECT TOP 1 *
    FROM dbo.vw_VacacionesSaldoActual v
    WHERE v.PersonaID = b.PersonaID
    ORDER BY v.AnioSaldo DESC
) s

WHERE p.EsColaboradorActivo = 1
  AND {where}

ORDER BY b.FechaSolicitud DESC;";

                        using (var cmdS = new SqlCommand(sqlSolicitudes, conn))
                        using (var rS = cmdS.ExecuteReader())
                        {
                            while (rS.Read())
                            {
                                vm.Solicitudes.Add(new VacacionesBandejaRHVm
                                {
                                    Folio = Convert.ToInt32(rS["Folio"]),
                                    PersonaID = Convert.ToInt32(rS["PersonaID"]),
                                    NumeroEmpleado = rS["NumeroEmpleado"]?.ToString(),
                                    NombreCompleto = rS["NombreCompleto"]?.ToString(),
                                    FechaSolicitud = Convert.ToDateTime(rS["FechaSolicitud"]),
                                    FechaInicio = Convert.ToDateTime(rS["FechaInicio"]),
                                    FechaFin = Convert.ToDateTime(rS["FechaFin"]),
                                    DiasSolicitados = Convert.ToDecimal(rS["DiasSolicitados"]),
                                    EsAnticipada = Convert.ToBoolean(rS["EsAnticipada"]),
                                    EstadoAutorizacion = rS["EstadoAutorizacion"]?.ToString(),
                                    EstadoRecursosHumanos = rS["EstadoRecursosHumanos"]?.ToString(),
                                    DiasDisponibles = Convert.ToDecimal(rS["DiasDisponibles"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando BandejaRH");
                ViewBag.Error = $"Error al cargar bandeja RH: {ex.Message}";
            }

            return View(vm);
        }


        [HttpGet]
        public IActionResult ObtenerSaldoColaborador(int personaId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    // Reutilizando lavista vw_VacacionesSaldoActual
                    string sql = @"SELECT TOP 1 DiasDisponibles 
                           FROM vw_VacacionesSaldoActual 
                           WHERE PersonaID = @PersonaID 
                           ORDER BY AnioSaldo DESC";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        var result = cmd.ExecuteScalar();

                        decimal saldo = (result != null && result != DBNull.Value) ? Convert.ToDecimal(result) : 0;
                        return Json(new { success = true, saldo = saldo });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        //Método post para cargar dias extra a los usuarios

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OtorgarDiasExtra(int PersonaID, decimal DiasRegalo, string Motivo)
        {
            int usuarioRhId = ObtenerUsuarioIdActual();
            if (usuarioRhId == 0) return Unauthorized();

            if (DiasRegalo <= 0)
            {
                return Json(new { success = false, message = "La cantidad de días debe ser mayor a cero." });
            }

            if (string.IsNullOrWhiteSpace(Motivo))
            {
                return Json(new { success = false, message = "El motivo o justificación es obligatorio." });
            }

            try
            {
                string nombreColaborador = "Colaborador";
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Obtener el nombre de la persona para la notificación de éxito
                    using (var cmdNom = new SqlCommand("SELECT (Nombre + ' ' + ApellidoPaterno) FROM Persona WHERE PersonaID = @ID", conn))
                    {
                        cmdNom.Parameters.AddWithValue("@ID", PersonaID);
                        var resNom = await cmdNom.ExecuteScalarAsync();
                        if (resNom != null) nombreColaborador = resNom.ToString();
                    }

                    // 2. Query para sumarle los días extra al registro del año más reciente del empleado
                    string sqlUpdateExtra = @"
                UPDATE VacacionesSaldoAnual 
                SET DiasExtra = DiasExtra + @DiasRegalo,
                    FechaActualizacion = GETDATE(),
                    Observaciones = ISNULL(Observaciones + ' | ', '') + @Observaciones
                WHERE PersonaID = @PersonaID 
                  AND Anio = (SELECT MAX(Anio) FROM VacacionesSaldoAnual WHERE PersonaID = @PersonaID)";

                    using (var cmd = new SqlCommand(sqlUpdateExtra, conn))
                    {
                        cmd.Parameters.AddWithValue("@DiasRegalo", DiasRegalo);
                        cmd.Parameters.AddWithValue("@PersonaID", PersonaID);
                        cmd.Parameters.AddWithValue("@Observaciones", $"[DÍAS EXTRA POR RH] +{DiasRegalo} días. Motivo: {Motivo}");

                        int filasAfectadas = await cmd.ExecuteNonQueryAsync();

                        if (filasAfectadas == 0)
                        {
                            return Json(new { success = false, message = "El empleado no cuenta con un registro de saldo activo para asignarle días." });
                        }
                    }
                }

                try
                {
                    var asunto = $"Te han otorgado días extra de vacaciones";
                    var htmlMail = $@"
            <!DOCTYPE html>
            <html>
            <head><meta charset='UTF-8'></head>
            <body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
              <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
                <div style='padding:20px; background:#198754; color:#fff; text-align:center;'>
                  <h2 style='margin:0;'>¡Días Extra Otorgados!</h2>
                </div>
                <div style='padding:20px; color:#333;'>
                  <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreColaborador)}</strong>,</p>
                  <p>Se te informa que Recursos Humanos te ha asignado días adicionales a tu saldo actual de vacaciones.</p>

                  <div style='background:#f8f9fa; border-left:4px solid #198754; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                    <p style='margin:0 0 6px;'><strong>Días otorgados:</strong> {DiasRegalo}</p>
                    <p style='margin:0;'><strong>Motivo:</strong> {System.Net.WebUtility.HtmlEncode(Motivo)}</p>
                  </div>

                  <p>Puedes revisar el ajuste reflejado entrando a la Intranet, módulo <strong>Vacaciones</strong> (sección <strong>Mis vacaciones</strong>).</p>
                  <p>https://intranet.nsgroup.com.mx/</p>
                  <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
                </div>
              </div>
            </body>
            </html>";

                    var personaIds = new List<int> { PersonaID };
                    await _notif.EnviarABccPersonasAsync(personaIds, asunto, htmlMail);
                }
                catch (Exception exMail)
                {
                    // Si el correo falla logueamos el error pero no rompemos la respuesta JSON de éxito de la BD
                    _logger.LogError(exMail, "Error al enviar correo de notificación por días extra a PersonaID {0}", PersonaID);
                }


                return Json(new { success = true, message = $"Se otorgaron {DiasRegalo} días extra correctamente y se notificó al colaborador." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en OtorgarDiasExtra");
                return Json(new { success = false, message = "Error interno: " + ex.Message });
            }
        }


        private static (string aaMm, int anios) CalcularAntiguedad(DateTime ingreso, DateTime hoy)
        {
            int totalMeses = (hoy.Year - ingreso.Year) * 12 + (hoy.Month - ingreso.Month);
            if (hoy.Day < ingreso.Day) totalMeses--;

            if (totalMeses < 0) totalMeses = 0;

            int anios = totalMeses / 12;
            int meses = totalMeses % 12;
            return ($"{anios:D2}-{meses:D2}", anios);
        }




        //Helper para saber si un usuario pertenece a rhh

        private bool EsUsuarioRecursosHumanos(SqlConnection conn, int usuarioId)
        {
            var sql = @"
        SELECT TOP 1 d.NombreDepartamento
        FROM EmpleadoDepartamentos ed
        INNER JOIN Departamentos d ON ed.DepartamentoID = d.DepartamentoID
        WHERE ed.UsuarioID = @UsuarioID
          AND ed.Activo = 1
          AND d.Activo = 1;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return false;

                var dep = result.ToString()!.ToUpper().Trim();

                return dep.Contains("RECURSOS HUMANOS") || dep.StartsWith("RH");
            }
        }

        [HttpPost]
        public async Task<IActionResult> SolicitarHabilitacion(int usuarioId, string motivo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 15)
                    return Json(new { success = false, message = "El motivo debe tener al menos 15 caracteres." });

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    // Agregamos SELECT SCOPE_IDENTITY() para recuperar el ID autogenerado
                    string query = @"INSERT INTO VacacionesHabilitacionesEspeciales 
                            (UsuarioID, MotivoSolicitud, FechaSolicitud, EstatusJefe, EstatusRH, Completada)
                            VALUES (@UsuarioID, @Motivo, GETDATE(), 'Pendiente', 'Pendiente', 0);
                            SELECT SCOPE_IDENTITY();";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        cmd.Parameters.AddWithValue("@Motivo", motivo);

                        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                        // Ejecutamos con ExecuteScalarAsync para obtener el nuevo ID
                        var resultId = await cmd.ExecuteScalarAsync();
                        int nuevaHabilitacionId = Convert.ToInt32(resultId);

                        // CORRECCIÓN: Ahora sí pasamos el ID de la habilitación correcto
                        await NotificarJefeSolicitudHabilitacionAsync(nuevaHabilitacionId);
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SolicitarHabilitacion");
                return Json(new { success = false, message = "Error de Base de Datos: " + ex.Message });
            }
        }


        [HttpPost]
        public async Task<IActionResult> DecidirHabilitacionJefe(int habilitacionId, bool aprobado)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string estatus = aprobado ? "Autorizado" : "Rechazado";

                    // Actualizamos la decisión del jefe y registramos quién y cuándo
                    string query = @"UPDATE VacacionesHabilitacionesEspeciales 
                             SET EstatusJefe = @Estatus, 
                                 JefeID = @JefeID, 
                                 FechaAutorizacionJefe = GETDATE() 
                             WHERE HabilitacionID = @HabilitacionID";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Estatus", estatus);
                        cmd.Parameters.AddWithValue("@JefeID", usuarioId);
                        cmd.Parameters.AddWithValue("@HabilitacionID", habilitacionId);

                        if (conn.State != ConnectionState.Open) await conn.OpenAsync();
                        await cmd.ExecuteNonQueryAsync();
                        if (aprobado)
                        {
                            await NotificarRH_JefeAutorizoHabilitacionAsync(habilitacionId);
                        }
                    }
                }

                return Json(new { success = true, message = "Decisión registrada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }



        //Metodo para mostrar las solicitudes anticipadas ya aprobadas por el jefe directo



        [HttpGet]
        public IActionResult SolicitudesHabilitacionRH()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var lista = new List<HabilitacionPendienteRHVm>();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // Seguridad: Solo gente de RRHH
                    if (!EsUsuarioRecursosHumanos(conn, usuarioId)) return Forbid();

                    string sql = @"
                SELECT 
                    h.HabilitacionID,
                    (p.Nombre + ' ' + p.ApellidoPaterno) as NombreColaborador,
                    h.MotivoSolicitud,
                    (pj.Nombre + ' ' + pj.ApellidoPaterno) as NombreJefe,
                    h.FechaAutorizacionJefe
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                LEFT JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                WHERE h.EstatusJefe = 'Autorizado' 
                  AND h.EstatusRH = 'Pendiente'
                  AND h.Completada = 0";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lista.Add(new HabilitacionPendienteRHVm
                                {
                                    HabilitacionID = (int)reader["HabilitacionID"],
                                    NombreColaborador = reader["NombreColaborador"].ToString(),
                                    Motivo = reader["MotivoSolicitud"].ToString(),
                                    NombreJefe = reader["NombreJefe"].ToString(),
                                    FechaAutorizacionJefe = (DateTime)reader["FechaAutorizacionJefe"]
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { ViewBag.Error = ex.Message; }

            return View(lista);
        }

        //Metodo de aporbación final, activa el swixh de vacaciones adelantadas 


        [HttpPost]
        public async Task<IActionResult> AprobarHabilitacionFinal(int habilitacionId, bool aprobado)
        {
            int usuarioRhId = ObtenerUsuarioIdActual();
            if (usuarioRhId == 0) return Unauthorized();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Definimos el estatus basado en la decisión de RRHH
                    string estatus = aprobado ? "Autorizado" : "Rechazado";

                    // Solo actualizamos la tabla de trazabilidad que ya existe
                    string sqlHabilitacion = @"UPDATE VacacionesHabilitacionesEspeciales 
                                       SET EstatusRH = @Estatus, 
                                           UsuarioRHID = @RhID, 
                                           FechaAutorizacionRH = GETDATE(), 
                                           Completada = 1
                                       WHERE HabilitacionID = @ID";

                    using (SqlCommand cmd = new SqlCommand(sqlHabilitacion, conn))
                    {
                        cmd.Parameters.AddWithValue("@Estatus", estatus);
                        cmd.Parameters.AddWithValue("@RhID", usuarioRhId);
                        cmd.Parameters.AddWithValue("@ID", habilitacionId);

                        await cmd.ExecuteNonQueryAsync();
                        await NotificarEmpleadoResultadoHabilitacionAsync(habilitacionId, aprobado);
                    }
                }

                // Retornamos éxito para que SweetAlert recargue la página
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al aprobar habilitación final");
                return Json(new { success = false, message = "Error de base de datos: " + ex.Message });
            }
        }
        //Helper para saber si un usuario es un jefe inmediato
        private bool EsJefeInmediato(SqlConnection conn, int personaIdJefe)
        {
            // Revisa si hay al menos UNA persona que tenga configurado a esta persona como jefe
            using (var cmd = new SqlCommand(@"
        SELECT COUNT(*) 
        FROM Persona
        WHERE JefeInmediatoPersonaID = @PersonaJefeID;", conn))
            {
                cmd.Parameters.AddWithValue("@PersonaJefeID", personaIdJefe);
                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                    return false;

                return Convert.ToInt32(result) > 0;
            }
        }






        private int ObtenerUsuarioIdActual()
        {

            var claim = User.FindFirst(ClaimTypes.NameIdentifier);

            if (claim == null || string.IsNullOrEmpty(claim.Value))
                return 0;

            if (int.TryParse(claim.Value, out int usuarioId))
                return usuarioId;

            return 0;
        }

        private int ObtenerPersonaIdPorUsuario(SqlConnection conn, int usuarioId)
        {
            using (var cmd = new SqlCommand(
                "SELECT PersonaID FROM Usuarios WHERE UsuarioID = @UsuarioID;", conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                    return 0;

                return Convert.ToInt32(result);
            }
        }

        //Metodos para notificar sobre vacaciones adelantadas :// 1. Notificar al Jefe que un empleado pidió habilitar adelantadas
        // 1. Notificar al Jefe que un empleado pidió habilitar adelantadas (VISTA MEJORADA)
        private async Task NotificarJefeSolicitudHabilitacionAsync(int habilitacionId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var sql = @"
                SELECT 
                    p.NumeroEmpleado,
                    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) as Empleado, 
                    h.MotivoSolicitud, 
                    pj.PersonaID as JefeID,
                    (pj.Nombre + ' ' + pj.ApellidoPaterno) as NombreJefe
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                INNER JOIN Persona pj ON p.JefeInmediatoPersonaID = pj.PersonaID
                WHERE h.HabilitacionID = @ID";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@ID", habilitacionId);
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                int jefeId = (int)rd["JefeID"];
                                var nombreJefe = rd["NombreJefe"]?.ToString() ?? "Jefe Inmediato";
                                var empleado = rd["Empleado"]?.ToString() ?? "Colaborador";
                                var numEmp = rd["NumeroEmpleado"]?.ToString() ?? "";
                                var motivo = rd["MotivoSolicitud"]?.ToString() ?? "";

                                var asunto = $"Solicitud de habilitación de vacaciones adelantadas - {empleado}";

                                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Nueva Solicitud de Habilitación Especial</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombreJefe)}</strong>,</p>
      <p>El colaborador <strong>{System.Net.WebUtility.HtmlEncode(empleado)}</strong> ({System.Net.WebUtility.HtmlEncode(numEmp)}) ha solicitado tu autorización para poder pedir vacaciones de manera anticipada/adelantada.</p>

      <div style='background:#f8f9fa; border-left:4px solid #0d6efd; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio de Petición:</strong> {habilitacionId}</p>
        <p style='margin:0;'><strong>Justificación del Colaborador:</strong> {System.Net.WebUtility.HtmlEncode(motivo)}</p>
      </div>

      <p>Por favor ingresa a la Intranet, sección <strong>Vacaciones Pendientes de mi Equipo</strong> para evaluar la viabilidad operativa y autorizar o rechazar esta petición.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                                await _notif.EnviarABccPersonasAsync(new List<int> { jefeId }, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al notificar al jefe sobre la habilitación {0}", habilitacionId);
            }
        }

        // 2. Notificar a RRHH que el Jefe ya dio el visto bueno
        // 2. Notificar a RRHH que el Jefe ya dio el visto bueno (CON DATOS COMPLETOS)
        private async Task NotificarRH_JefeAutorizoHabilitacionAsync(int habilitacionId)
        {
            try
            {
                var rhPersonaIds = new List<int>();
                string nombreEmpleado = "Colaborador";
                string numEmp = "";
                string motivo = "";

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Obtener los datos de la habilitación y del empleado involucrado
                    var sqlDatos = @"
                SELECT 
                    p.NumeroEmpleado,
                    (p.ApellidoPaterno + ' ' + p.ApellidoMaterno + ' ' + p.Nombre) AS Empleado,
                    h.MotivoSolicitud
                FROM VacacionesHabilitacionesEspeciales h
                INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                WHERE h.HabilitacionID = @HabilitacionID;";

                    using (var cmdDatos = new SqlCommand(sqlDatos, conn))
                    {
                        cmdDatos.Parameters.AddWithValue("@HabilitacionID", habilitacionId);
                        using (var rdDatos = await cmdDatos.ExecuteReaderAsync())
                        {
                            if (await rdDatos.ReadAsync())
                            {
                                numEmp = rdDatos["NumeroEmpleado"]?.ToString() ?? "";
                                nombreEmpleado = rdDatos["Empleado"]?.ToString() ?? "Colaborador";
                                motivo = rdDatos["MotivoSolicitud"]?.ToString() ?? "";
                            }
                        }
                    }

                    // 2. Obtener todos los PersonaID que pertenecen a RH para enviarles el correo
                    var sqlRH = @"SELECT DISTINCT u.PersonaID 
                          FROM Usuarios u 
                          INNER JOIN EmpleadoDepartamentos ed ON ed.UsuarioID = u.UsuarioID AND ed.Activo = 1
                          INNER JOIN Departamentos d ON d.DepartamentoID = ed.DepartamentoID AND d.Activo = 1
                          WHERE (UPPER(d.NombreDepartamento) LIKE '%RECURSOS HUMANOS%' OR UPPER(d.NombreDepartamento) LIKE 'RH%')
                            AND u.PersonaID IS NOT NULL;";

                    using (var cmdRH = new SqlCommand(sqlRH, conn))
                    using (var rdRH = await cmdRH.ExecuteReaderAsync())
                    {
                        while (await rdRH.ReadAsync())
                        {
                            rhPersonaIds.Add(rdRH.GetInt32(0));
                        }
                    }

                    // 3. Si hay personal de RH, enviar el correo con el formato profesional de la empresa
                    if (rhPersonaIds.Count > 0)
                    {
                        var asunto = $"Habilitación Especial: Turno de RRHH - Folio {habilitacionId} ({numEmp})";

                        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#1a237e; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Validación de Vacaciones Adelantadas</h2>
    </div>
    <div style='padding:20px; color:#333;'>
      <p>Hola equipo de Recursos Humanos,</p>
      <p>Se les informa que un jefe inmediato ha <strong>autorizado </strong> el motivo de un colaborador para solicitar vacaciones de forma adelantada.</p>

      <div style='background:#f8f9fa; border-left:4px solid #1a237e; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio de Habilitación:</strong> {habilitacionId}</p>
        <p style='margin:0 0 6px;'><strong>Colaborador:</strong> {System.Net.WebUtility.HtmlEncode(numEmp)} - {System.Net.WebUtility.HtmlEncode(nombreEmpleado)}</p>
        <p style='margin:0;'><strong>Motivo expuesto:</strong> {System.Net.WebUtility.HtmlEncode(motivo)}</p>
      </div>

      <p>Por favor, ingresen a la <strong>Bandeja de RH</strong> en la Intranet para realizar la validación final y activar de manera oficial el switch del colaborador.</p>
      <p>https://intranet.nsgroup.com.mx/</p>
      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                        await _notif.EnviarABccPersonasAsync(rhPersonaIds, asunto, html);
                        _logger.LogInformation("Notificación enviada a {0} personas de RH para la habilitación {1}", rhPersonaIds.Count, habilitacionId);
                    }
                    else
                    {
                        _logger.LogWarning("No se encontraron personas en el departamento de Recursos Humanos para enviar la habilitación {0}.", habilitacionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al notificar a RRHH sobre la habilitación {0}", habilitacionId);
            }
        }

        // 3. Notificar al Empleado el resultado final (Cuando RRHH termina)
        private async Task NotificarEmpleadoResultadoHabilitacionAsync(int habilitacionId, bool aprobado)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var sql = "SELECT p.PersonaID FROM VacacionesHabilitacionesEspeciales h INNER JOIN Usuarios u ON h.UsuarioID = u.UsuarioID INNER JOIN Persona p ON u.PersonaID = p.PersonaID WHERE h.HabilitacionID = @ID";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@ID", habilitacionId);
                        var personaId = (int)cmd.ExecuteScalar();
                        var res = aprobado ? "APROBADA" : "RECHAZADA";
                        var asunto = $"Resultado de tu solicitud de habilitación: {res}";
                        var html = $@"<h2>Estatus de Solicitud</h2><p>Tu petición para habilitar vacaciones adelantadas ha sido <b>{res}</b>.</p>
                             {(aprobado ? "<p>Ya puedes entrar a 'Mis Vacaciones' y solicitar tus días.</p>" : "")}";
                        await _notif.EnviarABccPersonasAsync(new List<int> { personaId }, asunto, html);
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error notificar empleado habilitacion"); }
        }

        //METODO NUEV PARA DESCONTAR VAVACIONES MASIVAMENTE

        [HttpPost]
        public async Task<IActionResult> AplicarDescuentoMasivo(decimal dias, string motivo)
        {
            int usuarioRhId = ObtenerUsuarioIdActual();
            if (usuarioRhId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("sp_Vacaciones_DescuentoMasivo", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@DiasADescontar", dias);
                        cmd.Parameters.AddWithValue("@UsuarioRHID", usuarioRhId);
                        cmd.Parameters.AddWithValue("@Observaciones", motivo);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                return Json(new { success = true, message = "Descuento aplicado correctamente a todos los colaboradores." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Descuento Masivo");
                return Json(new { success = false, message = ex.Message });
            }
        }



        //Helpers para validar si tiene permiso de vacaciones adelantadas, obtener dias disponibles y contardias habbiles


        private bool TienePermisoVacacionesAdelantadas(
            SqlConnection conn,
            int usuarioId,
            SqlTransaction? trans = null)
        {
            var sql = @"
SELECT COUNT(*)
FROM VacacionesHabilitacionesEspeciales
WHERE UsuarioID = @UsuarioID
  AND EstatusRH = 'Autorizado'
  AND Completada = 1;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                if (trans != null)
                    cmd.Transaction = trans;

                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private decimal ObtenerDiasDisponibles(
            SqlConnection conn,
            int personaId,
            SqlTransaction? trans = null)
        {
            var sql = @"
SELECT TOP 1 DiasDisponibles
FROM vw_VacacionesSaldoActual
WHERE PersonaID = @PersonaID
ORDER BY AnioSaldo DESC;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                if (trans != null)
                    cmd.Transaction = trans;

                cmd.Parameters.AddWithValue("@PersonaID", personaId);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value
                    ? 0m
                    : Convert.ToDecimal(result);
            }
        }

        private decimal ContarDiasHabiles(
            SqlConnection conn,
            DateTime inicio,
            DateTime fin,
            SqlTransaction? trans = null)
        {
            const string sql = "SELECT dbo.fn_ContarDiasHabiles(@Inicio, @Fin);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                if (trans != null)
                    cmd.Transaction = trans;

                cmd.Parameters.AddWithValue("@Inicio", inicio.Date);
                cmd.Parameters.AddWithValue("@Fin", fin.Date);

                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value
                    ? 0m
                    : Convert.ToDecimal(result);
            }
        }

        private DateTime ObtenerSiguienteDiaHabil(
            SqlConnection conn,
            DateTime fechaFin,
            SqlTransaction? trans = null)
        {
            var sql = @"
DECLARE @FechaRegreso DATE = DATEADD(DAY, 1, @FechaFin);

WHILE dbo.fn_ContarDiasHabiles(@FechaRegreso, @FechaRegreso) = 0
BEGIN
    SET @FechaRegreso = DATEADD(DAY, 1, @FechaRegreso);
END;

SELECT @FechaRegreso;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                if (trans != null)
                    cmd.Transaction = trans;

                cmd.Parameters.AddWithValue("@FechaFin", fechaFin.Date);
                return Convert.ToDateTime(cmd.ExecuteScalar());
            }
        }

        private static bool PuedeModificarSolicitud(
            string? estadoAutorizacion,
            string? estadoRecursosHumanos,
            string? origen)
        {
            bool pendienteJefe = string.Equals(
                estadoAutorizacion?.Trim(),
                "Pendiente",
                StringComparison.OrdinalIgnoreCase);

            string estadoRH = estadoRecursosHumanos?.Trim() ?? "";
            bool sinProcesarRH = string.IsNullOrWhiteSpace(estadoRH)
                || string.Equals(estadoRH, "SinRegistrar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(estadoRH, "Pendiente", StringComparison.OrdinalIgnoreCase);

            bool creadaPorColaborador = string.Equals(
                origen?.Trim(),
                "Colaborador",
                StringComparison.OrdinalIgnoreCase);

            return pendienteJefe && sinProcesarRH && creadaPorColaborador;
        }






        //QUITAR HELPERS

        //Helpers para convertir

        private static int GetInt(IDataRecord r, string col) =>
    r[col] == DBNull.Value ? 0 : Convert.ToInt32(r[col]);

        private static decimal GetDecimal(IDataRecord r, string col) =>
            r[col] == DBNull.Value ? 0m : Convert.ToDecimal(r[col]);

        private static DateTime? GetDate(IDataRecord r, string col) =>
            r[col] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r[col]);

        private static bool GetBool(IDataRecord r, string col) =>
            r[col] != DBNull.Value && Convert.ToBoolean(r[col]);

        private static string GetString(IDataRecord r, string col) =>
            r[col] == DBNull.Value ? "" : r[col].ToString();


    }
}
