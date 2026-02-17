using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using ProyectoMatrix.Servicios;

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
                    // Saber si esta persona es jefe de alguien
                    bool esJefe = EsJefeInmediato(conn, personaId);
                    ViewBag.EsJefeInmediato = esJefe;

                    //Detertar si el usuario es de rrhh ocn ayuda del helper
                    bool esRH = EsUsuarioRecursosHumanos(conn, usuarioId);
                    ViewBag.EsUsuarioRH = esRH;



                    int anio = DateTime.Now.Year;

                    // datos de la persona (nombre y número)
                    using (var cmd = new SqlCommand(@"
                        SELECT 
                            NumeroEmpleado,
                            (ApellidoPaterno + ' ' + ApellidoMaterno + ' ' + Nombre) AS NombreCompleto
                        FROM Persona
                        WHERE PersonaID = @PersonaID;", conn))
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
                        SELECT 
                            Anio,
                            DiasCorrespondientes,
                            DiasExtra,
                            DiasTomados,
                            DiasCaducados,
                            DiasDisponibles
                        FROM vw_VacacionesResumenAnual
                        WHERE PersonaID = @PersonaID
                          AND Anio = @Anio;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);
                        cmd.Parameters.AddWithValue("@Anio", anio);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                vm.ResumenActual = new VacacionesResumenAnualVm
                                {
                                    Anio = (int)reader["Anio"],
                                    DiasCorrespondientes = Convert.ToInt32(reader["DiasCorrespondientes"]),
                                    DiasExtra = Convert.ToDecimal(reader["DiasExtra"]),
                                    DiasTomados = Convert.ToDecimal(reader["DiasTomados"]),
                                    DiasCaducados = Convert.ToDecimal(reader["DiasCaducados"]),
                                    DiasDisponibles = Convert.ToDecimal(reader["DiasDisponibles"])
                                };
                            }
                        }
                    }

                    // historial de solicitudes
                    vm.Solicitudes = new List<VacacionesSolicitudItemVm>();

                    using (var cmd = new SqlCommand(@"
                        SELECT 
                            SolicitudVacacionesID,
                            FechaSolicitud,
                            FechaInicio,
                            FechaFin,
                            FechaRegresoLabores,
                            DiasSolicitados,
                            EsAnticipada,
                            EstadoAutorizacion,
                            EstadoRecursosHumanos,
                            Origen
                        FROM vw_VacacionesSolicitudesDetalle
                        WHERE PersonaID = @PersonaID
                        ORDER BY FechaSolicitud DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@PersonaID", personaId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = new VacacionesSolicitudItemVm
                                {
                                    SolicitudVacacionesID = (int)reader["SolicitudVacacionesID"],
                                    FechaSolicitud = (DateTime)reader["FechaSolicitud"],
                                    FechaInicio = (DateTime)reader["FechaInicio"],
                                    FechaFin = (DateTime)reader["FechaFin"],
                                    FechaRegresoLabores = (DateTime)reader["FechaRegresoLabores"],
                                    DiasSolicitados = Convert.ToDecimal(reader["DiasSolicitados"]),
                                    EsAnticipada = (bool)reader["EsAnticipada"],
                                    EstadoAutorizacion = reader["EstadoAutorizacion"]?.ToString(),
                                    EstadoRecursosHumanos = reader["EstadoRecursosHumanos"]?.ToString(),
                                    Origen = reader["Origen"]?.ToString()
                                };

                                vm.Solicitudes.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando MisVacaciones");

                // Solo para depurar: muestra el detalle en pantalla
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
    FROM vw_VacacionesResumenAnual
    WHERE PersonaID = @PersonaID
    ORDER BY Anio DESC;", conn))
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

            var lista = new List<VacacionesSolicitudJefeVm>();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // Ojo: ajusta los nombres de columnas/tablas si alguno es distinto
                    var sql = @"
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
                  AND s.EstadoAutorizacion = 'Pendiente'
                ORDER BY s.FechaSolicitud DESC;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UsuarioJefeID", usuarioId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = new VacacionesSolicitudJefeVm
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
                                };

                                lista.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando SolicitudesPendientesJefe");
                ViewBag.Error = $"Error al cargar solicitudes: {ex.Message}";
            }

            return View(lista);
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
  AND (UPPER(d.Nombre) LIKE '%RECURSOS HUMANOS%' OR UPPER(d.Nombre) LIKE 'RH%');";

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
            if (usuarioId == 0)
                return Unauthorized();

            var lista = new List<VacacionesSolicitudRHVm>();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // Seguridad extra: verificar que realmente es usuario de RH
                    if (!EsUsuarioRecursosHumanos(conn, usuarioId))
                    {
                        return Forbid(); // 403
                    }

                    var sql = @"
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
                WHERE s.EstadoAutorizacion = 'Autorizada'
                  AND s.EstadoRecursosHumanos = 'SinRegistrar'
                ORDER BY s.FechaSolicitud DESC;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = new VacacionesSolicitudRHVm
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
                                };

                                lista.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando SolicitudesPendientesRH");
                ViewBag.Error = $"Error al cargar solicitudes para RH: {ex.Message}";
            }

            return View(lista);
        }


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
                FROM vw_VacacionesResumenAnual
                WHERE PersonaID = @PersonaID
                ORDER BY Anio DESC;", conn))
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




        //Contralador para la banndeja de entrada de RRHH

        public IActionResult BandejaRH(string tab = "autorizadas")
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new VacacionesBandejaRHPantallaVm
            {
                Tab = (tab ?? "autorizadas").ToLower()
            };

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    if (!EsUsuarioRecursosHumanos(conn, usuarioId))
                        return Forbid();

                    // Contadores para tabs
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
                            ViewBag.Pendientes = Convert.ToInt32(rC["Pendientes"]);
                            ViewBag.Autorizadas = Convert.ToInt32(rC["Autorizadas"]);
                            ViewBag.Registradas = Convert.ToInt32(rC["Registradas"]);
                            ViewBag.Rechazadas = Convert.ToInt32(rC["Rechazadas"]);
                        }
                    }

                    if (vm.Tab == "usuarios")
                    {
                        var sqlUsuarios = @"
                    SELECT
                        PersonaID, NumeroEmpleado, ClaveEmpleadoNomina, NombreCompleto, Puesto,
                        Anio, DiasCorrespondientes, DiasExtra, DiasTomados, DiasCaducados, DiasDisponibles,
                        AnticipadasRegistradas, AnticipadasPorRegistrar
                    FROM dbo.vw_Vacaciones_UsuariosSaldoRH
                    ORDER BY NombreCompleto;";

                        using (var cmdU = new SqlCommand(sqlUsuarios, conn))
                        using (var rU = cmdU.ExecuteReader())
                        {
                            while (rU.Read())
                            {
                                vm.Usuarios.Add(new VacacionesUsuarioSaldoRHVm
                                {
                                    PersonaID = Convert.ToInt32(rU["PersonaID"]),
                                    NumeroEmpleado = rU["NumeroEmpleado"]?.ToString(),
                                    ClaveEmpleadoNomina = rU["ClaveEmpleadoNomina"]?.ToString(),
                                    NombreCompleto = rU["NombreCompleto"]?.ToString(),
                                    Puesto = rU["Puesto"]?.ToString(),

                                    Anio = Convert.ToInt32(rU["Anio"]),
                                    DiasCorrespondientes = Convert.ToDecimal(rU["DiasCorrespondientes"]),
                                    DiasExtra = Convert.ToDecimal(rU["DiasExtra"]),
                                    DiasTomados = Convert.ToDecimal(rU["DiasTomados"]),
                                    DiasCaducados = Convert.ToDecimal(rU["DiasCaducados"]),
                                    DiasDisponibles = Convert.ToDecimal(rU["DiasDisponibles"]),

                                    AnticipadasRegistradas = Convert.ToInt32(rU["AnticipadasRegistradas"]),
                                    AnticipadasPorRegistrar = Convert.ToInt32(rU["AnticipadasPorRegistrar"])
                                });
                            }
                        }
                    }
                    else
                    {
                        string where;
                        switch (vm.Tab)
                        {
                            case "pendientes":
                                where = "EstadoAutorizacion = 'Pendiente'";
                                break;
                            case "autorizadas":
                                where = "EstadoAutorizacion = 'Autorizada'";
                                break;
                            case "registradas":
                                where = "EstadoRecursosHumanos = 'Registrado'";
                                break;
                            case "rechazadas":
                                where = "EstadoAutorizacion = 'Rechazada'";
                                break;
                            default:
                                vm.Tab = "autorizadas";
                                where = "EstadoAutorizacion = 'Autorizada'";
                                break;
                        }

                        var sqlSolicitudes = $@"
                    SELECT
                        Folio, PersonaID, NumeroEmpleado, NombreCompleto, ClaveEmpleadoNomina, Puesto,
                        FechaSolicitud, FechaInicio, FechaFin, FechaRegresoLabores,
                        DiasSolicitados, EsAnticipada,
                        EstadoAutorizacion, EstadoRecursosHumanos,
                        AnioSaldo, DiasCorrespondientes, DiasExtra, DiasTomados, DiasCaducados, DiasDisponibles
                    FROM dbo.vw_Vacaciones_BandejaRH_ConSaldo
                    WHERE {where}
                    ORDER BY FechaSolicitud DESC;";

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
                                    ClaveEmpleadoNomina = rS["ClaveEmpleadoNomina"]?.ToString(),
                                    Puesto = rS["Puesto"]?.ToString(),

                                    FechaSolicitud = Convert.ToDateTime(rS["FechaSolicitud"]),
                                    FechaInicio = Convert.ToDateTime(rS["FechaInicio"]),
                                    FechaFin = Convert.ToDateTime(rS["FechaFin"]),
                                    FechaRegresoLabores = Convert.ToDateTime(rS["FechaRegresoLabores"]),

                                    DiasSolicitados = Convert.ToDecimal(rS["DiasSolicitados"]),
                                    EsAnticipada = Convert.ToBoolean(rS["EsAnticipada"]),

                                    EstadoAutorizacion = rS["EstadoAutorizacion"]?.ToString(),
                                    EstadoRecursosHumanos = rS["EstadoRecursosHumanos"]?.ToString(),

                                    AnioSaldo = Convert.ToInt32(rS["AnioSaldo"]),
                                    DiasCorrespondientes = Convert.ToDecimal(rS["DiasCorrespondientes"]),
                                    DiasExtra = Convert.ToDecimal(rS["DiasExtra"]),
                                    DiasTomados = Convert.ToDecimal(rS["DiasTomados"]),
                                    DiasCaducados = Convert.ToDecimal(rS["DiasCaducados"]),
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
