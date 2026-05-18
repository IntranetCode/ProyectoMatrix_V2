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
                SELECT TOP 1 AnioSaldo, DiasCorrespondientes, DiasExtra, DiasTomados, DiasCaducados, DiasDisponibles
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
                    using (var cmd = new SqlCommand(@"
    SELECT TOP 1 DiasDisponibles
    FROM vw_VacacionesSaldoActual
    WHERE PersonaID = @PersonaID
    ORDER BY AnioSaldo DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);

                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            vm.DiasDisponiblesActuales = Convert.ToDecimal(result);
                        }
                    }


                    // Valores por defecto
                    vm.FechaInicio = DateTime.Today;
                    vm.FechaFin = DateTime.Today;
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

            if (!ModelState.IsValid)
            {
                // Se muestran los errores en la vista
                return View(model);
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

                // Mostrar el detalle mientras depuramos
                ViewBag.Error = $"Error al crear la solicitud: {ex.Message}";
                return View(model);
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
        public IActionResult OmitirRegistroRH(int solicitudId)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    // Actualizamos el estado para que ya no salga en la bandeja de RH
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
                TempData["MensajeVacacionesRH"] = "La solicitud ha sido descartada y no se registrará.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al omitir solicitud");
                TempData["ErrorVacacionesRH"] = "No se pudo ocultar la solicitud.";
            }

            return RedirectToAction("SolicitudesPendientesRH");
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
                        await NotificarRH_SolicitudAutorizadaAsync(nuevaId);
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
                    SELECT Folio, PersonaID, NumeroEmpleado, NombreCompleto, ClaveEmpleadoNomina, Puesto,
                           FechaSolicitud, FechaInicio, FechaFin, FechaRegresoLabores,
                           DiasSolicitados, EsAnticipada, EstadoAutorizacion, EstadoRecursosHumanos,
                           AnioSaldo, DiasCorrespondientes, DiasExtra, DiasTomados, DiasCaducados, DiasDisponibles
                    FROM dbo.vw_Vacaciones_BandejaRH_ConSaldo WHERE {where} ORDER BY FechaSolicitud DESC;";

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
                    string query = @"INSERT INTO VacacionesHabilitacionesEspeciales 
                            (UsuarioID, MotivoSolicitud, FechaSolicitud, EstatusJefe, EstatusRH, Completada)
                            VALUES (@UsuarioID, @Motivo, GETDATE(), 'Pendiente', 'Pendiente', 0)";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        cmd.Parameters.AddWithValue("@Motivo", motivo);

                        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                        await cmd.ExecuteNonQueryAsync();
                        await NotificarJefeSolicitudHabilitacionAsync(usuarioId);
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
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
        private async Task NotificarJefeSolicitudHabilitacionAsync(int habilitacionId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var sql = @"
                SELECT (p.Nombre + ' ' + p.ApellidoPaterno) as Empleado, h.MotivoSolicitud, pj.PersonaID as JefeID
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
                                var asunto = "Nueva solicitud de habilitación de vacaciones adelantadas";
                                var html = $@"<h2>Solicitud de Habilitación</h2>
                                      <p>El colaborador <b>{rd["Empleado"]}</b> solicita habilitar vacaciones adelantadas.</p>
                                      <p><b>Motivo:</b> {rd["MotivoSolicitud"]}</p>
                                      <p>Ingresa a la Intranet para autorizar o rechazar esta petición operativa.</p>";

                                await _notif.EnviarABccPersonasAsync(new List<int> { (int)rd["JefeID"] }, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error notificar jefe habilitacion"); }
        }

        // 2. Notificar a RRHH que el Jefe ya dio el visto bueno
        private async Task NotificarRH_JefeAutorizoHabilitacionAsync(int habilitacionId)
        {
            try
            {
                var rhPersonaIds = new List<int>();
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // SQL Corregido: NombreDepartamento en lugar de Nombre
                    var sqlRH = @"SELECT DISTINCT u.PersonaID 
                          FROM Usuarios u 
                          INNER JOIN EmpleadoDepartamentos ed ON ed.UsuarioID = u.UsuarioID AND ed.Activo = 1
                          INNER JOIN Departamentos d ON d.DepartamentoID = ed.DepartamentoID AND d.Activo = 1
                          WHERE (UPPER(d.NombreDepartamento) LIKE '%RECURSOS HUMANOS%' OR UPPER(d.NombreDepartamento) LIKE 'RH%')
                            AND u.PersonaID IS NOT NULL;";

                    using (var cmd = new SqlCommand(sqlRH, conn))
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            rhPersonaIds.Add(rd.GetInt32(0));
                        }
                    }

                    if (rhPersonaIds.Count > 0)
                    {
                        var asunto = "Habilitación Especial: Turno de RRHH";
                        var html = $@"
                    <div style='font-family: Arial; border: 1px solid #ddd; padding: 20px; border-radius: 10px;'>
                        <h2 style='color: #1a237e;'>Validación de RRHH Pendiente</h2>
                        <p>Hola equipo de Recursos Humanos,</p>
                        <p>Se les informa que un jefe inmediato ha <b>autorizado</b> un motivo para habilitar vacaciones adelantadas.</p>
                        <p>Por favor, ingresen a la Bandeja de RH en la Intranet para realizar la validación final y activar el switch del colaborador.</p>
                        <hr>
                        <p style='font-size: 0.8em; color: #666;'>Este es un mensaje automático de Proyecto Matrix.</p>
                    </div>";

                        await _notif.EnviarABccPersonasAsync(rhPersonaIds, asunto, html);
                        _logger.LogInformation("Notificación enviada a {0} personas de RH para la habilitación {1}", rhPersonaIds.Count, habilitacionId);
                    }
                    else
                    {
                        _logger.LogWarning("No se encontraron personas en el departamento de Recursos Humanos para enviar la notificación.");
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



        //NOTIFICACIOONES POR CORREO 

        // 1. Notificar al Comprador que ya tiene una requisición aprobada para generar la O.C.
        private async Task NotificarCompra_RequisicionListaAsync(int solicitudId, string idRequisicion, bool esDesviacion)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var sql = @"SELECT Folio, PuestoAsignado FROM Compras_Solicitud WHERE SolicitudID = @id";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                var folio = rd["Folio"].ToString();
                                var puesto = rd["PuestoAsignado"].ToString();

                                // Buscamos a las personas con ese puesto para mandarles correo
                                var compradoresIds = new List<int>();
                                var sqlCompradores = "SELECT PersonaID FROM Persona WHERE Puesto = @puesto AND EsColaboradorActivo = 1";
                                // (Lógica para llenar compradoresIds...)

                                var asunto = $"Requisición Lista - Folio {folio}";
                                var html = $@"<h2>Requisición Autorizada</h2>
                                      <p>Control Presupuestal ha liberado la requisición <b>{idRequisicion}</b>.</p>
                                      {(esDesviacion ? "<p style='color:red;'><b>Nota:</b> Esta compra incluye una desviación aprobada.</p>" : "")}
                                      <p>Favor de proceder con la creación de la Orden de Compra.</p>";

                                await _notif.EnviarABccPersonasAsync(compradoresIds, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error notificar compra"); }
        }

        // 2. Notificar al Usuario Solicitante sobre el dictamen
        private async Task NotificarUsuario_DictamenPresupuestalAsync(int solicitudId, bool aprobado, string motivo)
        {
            // Lógica similar enviando correo al UsuarioID de la solicitud
            // Indicando si fue 'Aprobado' o 'Rechazado' por Control Presupuestal.
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
