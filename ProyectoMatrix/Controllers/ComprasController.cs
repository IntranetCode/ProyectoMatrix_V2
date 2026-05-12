using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System.Data;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    public class ComprasController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<ComprasController> _logger;
        private readonly ServicioNotificaciones _notif;

        public ComprasController(IConfiguration configuration, ILogger<ComprasController> logger, ServicioNotificaciones notif)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _notif = notif;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var solicitudes = new List<MisComprasVm>();
            int usuarioId = ObtenerUsuarioIdActual();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                // Consulta que trae el nombre de la empresa y el estatus actual
                string sql = @"SELECT S.SolicitudID, S.EmpresaID, S.TipoCompra, S.FechaCreacion, 
                              S.EstatusID, E.Nombre as EstatusNombre, Em.EmpresaID
                       FROM Compras_Solicitud S
                       INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
                       INNER JOIN Empresas Em ON S.EmpresaID = Em.EmpresaID
                       WHERE S.UsuarioID = @uid
                       ORDER BY S.FechaCreacion DESC";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", usuarioId);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            solicitudes.Add(new MisComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = "COM-" + reader["SolicitudID"].ToString().PadLeft(5, '0'),
                                Tipo = reader["TipoCompra"].ToString(),
                                Empresa = reader["Empresa"].ToString(),
                                Fecha = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["EstatusNombre"].ToString(),
                                EstatusID = (int)reader["EstatusID"]
                            });
                        }
                    }
                }
            }
            return View(solicitudes);
        }


        //METODO GET PARA CREAR NUEVAS SOLICITUDES

        [HttpGet("NuevaSolicitud")]
        public async Task<IActionResult> NuevaSolicitud()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // 1. Cargar Catálogo de Empresas
                var empresas = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
                string sqlEmpresas = "SELECT EmpresaID, Nombre FROM Empresas WHERE Activa = 1";
                using (var cmd = new SqlCommand(sqlEmpresas, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            empresas.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["EmpresaID"].ToString(),
                                Text = reader["Nombre"].ToString()
                            });
                        }
                    }
                }
                ViewBag.Empresas = empresas;

                // 2. Cargar Catálogo de Urgencia (ajusta los nombres de tu tabla Cat_Urgencia)
                var urgencias = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
                string sqlUrg = "SELECT UrgenciaID, Descripcion FROM Cat_Urgencia";
                using (var cmdU = new SqlCommand(sqlUrg, conn))
                {
                    using (var readerU = await cmdU.ExecuteReaderAsync())
                    {
                        while (await readerU.ReadAsync())
                        {
                            urgencias.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = readerU["UrgenciaID"].ToString(),
                                Text = readerU["Descripcion"].ToString()
                            });
                        }
                    }
                }
                ViewBag.Urgencias = urgencias;
            }

            // Inicializamos el modelo con una lista vacía de materiales
            var model = new CompraViewModel { Materiales = new List<MaterialItem>() };
            return View(model);
        }


        [HttpPost("NuevaSolicitud")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NuevaSolicitud(CompraViewModel model)
        {
            // 1. Obtener datos del solicitante (Basado en tu lógica de Proyecto Matrix)
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            // Nombre para el campo 'UsuarioResponsable' del historial
            string nombreSolicitante = User.Identity.Name ?? "Sistema";

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // 2. Insertar la Solicitud Principal
                            // Nota: EstatusID 1 = 'Solicitado'
                            string sqlSolicitud = @"INSERT INTO Compras_Solicitud 
                        (UsuarioID, EmpresaID, TipoCompra, EsProyecto, NombreProyecto, UrgenciaID, Comentarios, FechaCreacion, EstatusID) 
                        VALUES (@uid, @eid, @tipo, @esp, @nom, @urg, @com, GETDATE(), 1); 
                        SELECT SCOPE_IDENTITY();";

                            int nuevaSolicitudId;
                            using (var cmd = new SqlCommand(sqlSolicitud, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@uid", usuarioId);
                                cmd.Parameters.AddWithValue("@eid", model.EmpresaID);
                                cmd.Parameters.AddWithValue("@tipo", model.TipoCompra);
                                cmd.Parameters.AddWithValue("@esp", model.EsProyecto);
                                cmd.Parameters.AddWithValue("@nom", (object)model.NombreProyecto ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@urg", model.UrgenciaID);
                                cmd.Parameters.AddWithValue("@com", (object)model.Comentarios ?? DBNull.Value);

                                nuevaSolicitudId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            }

                            // 3. INSERTAR EN EL HISTORIAL (Esto activa el Dataline Time)
                            string sqlHistorial = @"INSERT INTO Compras_Historico_Pasos 
                        (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable) 
                        VALUES (@sid, 1, GETDATE(), @resp)";

                            using (var cmdHist = new SqlCommand(sqlHistorial, conn, transaction))
                            {
                                cmdHist.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                cmdHist.Parameters.AddWithValue("@resp", nombreSolicitante);
                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            // 4. Insertar la lista de materiales
                            if (model.Materiales != null && model.Materiales.Count > 0)
                            {
                                foreach (var mat in model.Materiales)
                                {
                                    string sqlMat = @"INSERT INTO Compras_Materiales (SolicitudID, Nombre, Cantidad, UnidadMedida, Descripcion) 
                                              VALUES (@sid, @n, @c, @u, @d)";
                                    using (var cmdMat = new SqlCommand(sqlMat, conn, transaction))
                                    {
                                        cmdMat.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                        cmdMat.Parameters.AddWithValue("@n", mat.Nombre);
                                        cmdMat.Parameters.AddWithValue("@c", mat.Cantidad);
                                        cmdMat.Parameters.AddWithValue("@u", mat.UnidadMedida);
                                        cmdMat.Parameters.AddWithValue("@d", (object)mat.Descripcion ?? DBNull.Value);
                                        await cmdMat.ExecuteNonQueryAsync();
                                    }
                                }
                            }

                            transaction.Commit();
                            return RedirectToAction("Index");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw ex;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al crear solicitud y paso de historial");
                ModelState.AddModelError("", "No se pudo guardar la solicitud. Intente de nuevo.");
                return View(model);
            }
        }
        private int ObtenerUsuarioIdActual()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return (claim != null && int.TryParse(claim.Value, out int id)) ? id : 0;
        }

        [HttpGet("BandejaAdmin")]
        public async Task<IActionResult> BandejaAdmin()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            var solicitudes = new List<BandejaComprasVm>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Obtener el puesto de la persona
                string puesto = "";
                using (var cmd = new SqlCommand("SELECT Puesto FROM Persona p INNER JOIN Usuarios u ON p.PersonaID = u.PersonaID WHERE u.UsuarioID = @Uid", conn))
                {
                    cmd.Parameters.AddWithValue("@Uid", usuarioId);
                    puesto = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
                }

                // Consulta dinámica: Si es DIRECCION COMPRAS ve TODO, si no, filtra por su puesto asignado
                string sql = @"
            SELECT s.SolicitudID, s.Folio, (p.Nombre + ' ' + p.ApellidoPaterno) as Solicitante, 
                   s.TipoCompra, u.Descripcion as Urgencia, s.FechaCreacion, e.Nombre as Estatus
            FROM Compras_Solicitud s
            INNER JOIN Usuarios us ON s.UsuarioID = us.UsuarioID
            INNER JOIN Persona p ON us.PersonaID = p.PersonaID
            INNER JOIN Cat_Urgencia u ON s.UrgenciaID = u.UrgenciaID
            INNER JOIN Cat_EstatusCompra e ON s.EstatusID = e.EstatusID
            WHERE s.EstatusID = 1"; // Paso inicial: Solicitado

                if (puesto != "DIRECCION COMPRAS")
                {
                    sql += " AND s.PuestoAsignado = @Puesto";
                }

                using (var cmd = new SqlCommand(sql, conn))
                {
                    if (puesto != "DIRECCION COMPRAS") cmd.Parameters.AddWithValue("@Puesto", puesto);
                    // ... resto del mapeo ...
                }
            }
            return View(solicitudes);
        }


        [HttpPost("ProcesarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarCotizacion(int SolicitudID, decimal Monto, IFormFile ArchivoCotizacion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (ArchivoCotizacion == null || ArchivoCotizacion.Length == 0)
            {
                TempData["Error"] = "Debes subir un archivo de cotización.";
                return RedirectToAction("BandejaAdmin");
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Guardar el archivo en el servidor (Carpeta de Intranet)
                    string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/compras/cotizaciones");
                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    string fileName = $"Cot_Folio_{SolicitudID}_{DateTime.Now.Ticks}{Path.GetExtension(ArchivoCotizacion.FileName)}";
                    string fullPath = Path.Combine(folderPath, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await ArchivoCotizacion.CopyToAsync(stream);
                    }

                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            // 2. Registrar la Cotización
                            string sqlCot = @"
                        INSERT INTO Compras_Cotizaciones (SolicitudID, ArchivoPath, MontoTotal, FechaEnvioAlUsuario)
                        VALUES (@Sid, @Path, @Monto, GETDATE())";

                            using (var cmdCot = new SqlCommand(sqlCot, conn, trans))
                            {
                                cmdCot.Parameters.AddWithValue("@Sid", SolicitudID);
                                cmdCot.Parameters.AddWithValue("@Path", fileName);
                                cmdCot.Parameters.AddWithValue("@Monto", Monto);
                                await cmdCot.ExecuteNonQueryAsync();
                            }

                            // 3. Actualizar Estatus de la Solicitud (2 = Cotizado)
                            string sqlUpdate = "UPDATE Compras_Solicitud SET EstatusID = 2 WHERE SolicitudID = @Sid";
                            using (var cmdUpd = new SqlCommand(sqlUpdate, conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@Sid", SolicitudID);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }

                            // 4. Registrar en Histórico (Timeline)
                            string sqlH = @"
                        INSERT INTO Compras_Historico_Pasos (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                        VALUES (@Sid, 2, GETDATE(), (SELECT Nombre + ' ' + ApellidoPaterno FROM Persona p INNER JOIN Usuarios u ON p.PersonaID = u.PersonaID WHERE u.UsuarioID = @Uid))";

                            using (var cmdH = new SqlCommand(sqlH, conn, trans))
                            {
                                cmdH.Parameters.AddWithValue("@Sid", SolicitudID);
                                cmdH.Parameters.AddWithValue("@Uid", usuarioId);
                                await cmdH.ExecuteNonQueryAsync();
                            }

                            trans.Commit();
                            TempData["Mensaje"] = "Cotización procesada y enviada a validación.";
                        }
                        catch (Exception) { trans.Rollback(); throw; }
                    }
                }
                return RedirectToAction("BandejaAdmin");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar cotización");
                TempData["Error"] = "Ocurrió un error: " + ex.Message;
                return RedirectToAction("BandejaAdmin");
            }
        }


        [HttpGet("BandejaPresupuestal")]
        public async Task<IActionResult> BandejaPresupuestal()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            var solicitudes = new List<BandejaComprasVm>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // --- NUEVA VALIDACIÓN POR DEPARTAMENTO ---
                // Verificamos si el usuario pertenece al departamento de Control Presupuestal
                string sqlDepto = @"
            SELECT COUNT(*) 
            FROM EmpleadoDepartamentos ed
            INNER JOIN Departamentos d ON ed.DepartamentoID = d.DepartamentoID
            WHERE ed.UsuarioID = @Uid 
              AND ed.Activo = 1 
              AND (UPPER(d.NombreDepartamento) = 'CONTROL PRESUPUESTAL' OR d.NombreDepartamento LIKE 'CONTROL%')";

                using (var cmdD = new SqlCommand(sqlDepto, conn))
                {
                    cmdD.Parameters.AddWithValue("@Uid", usuarioId);
                    int pertenece = (int)await cmdD.ExecuteScalarAsync();

                    if (pertenece == 0 && !User.IsInRole("Admin")) // Si no es del depto ni admin general
                    {
                        return Forbid();
                    }
                }

                // Consulta de solicitudes en estatus 2 (Cotizado) esperando dictamen
                string sql = @"
            SELECT s.SolicitudID, s.Folio, (p.Nombre + ' ' + p.ApellidoPaterno) as Solicitante, 
                   s.TipoCompra, c.MontoTotal, s.FechaCreacion
            FROM Compras_Solicitud s
            INNER JOIN Usuarios us ON s.UsuarioID = us.UsuarioID
            INNER JOIN Persona p ON us.PersonaID = p.PersonaID
            INNER JOIN Compras_Cotizaciones c ON s.SolicitudID = c.SolicitudID
            WHERE s.EstatusID = 2"; // 2 = Cotizado

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            solicitudes.Add(new BandejaComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                Solicitante = reader["Solicitante"].ToString(),
                                TipoCompra = reader["TipoCompra"].ToString(),
                              
                                FechaCreacion = (DateTime)reader["FechaCreacion"]
                            });
                        }
                    }
                }
            }
            return View(solicitudes);
        }
        //METODOS PARA NOTIFICAR POR CORREO 
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


        [HttpPost("AplicarDictamen")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AplicarDictamen(int SolicitudID, bool Aprobado, string IdRequisicion, bool EsDesviacion, string MotivoRechazo)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Guardar en Base de Datos
                            // (Lógica de Update mostrada anteriormente...)
                            trans.Commit();
                        }
                        catch (Exception) { trans.Rollback(); throw; }
                    }
                }

                // --- ENVÍO DE CORREOS (Sin romper el flujo) ---
                if (Aprobado)
                {
                    // Avisa a Compras que ya puede comprar
                    await NotificarCompra_RequisicionListaAsync(SolicitudID, IdRequisicion, EsDesviacion);
                }

                // Avisa al Usuario si pasó o por qué se rechazó
                await NotificarUsuario_DictamenPresupuestalAsync(SolicitudID, Aprobado, MotivoRechazo);

                TempData["Mensaje"] = "Dictamen aplicado correctamente.";
                return RedirectToAction("BandejaPresupuestal");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en dictamen");
                return View();
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarDictamen(DictamenPresupuestalVm model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            int nuevoEstatus;
            string notaHistorial;

            // Determinamos el rumbo de la solicitud
            if (!model.Pasa)
            {
                nuevoEstatus = 4; // Rechazado
                notaHistorial = "Rechazado por Control Presupuestal: " + model.Observaciones;
            }
            else if (model.DentroDePresupuesto)
            {
                nuevoEstatus = 3; // Aprobado / Generar OC
                notaHistorial = $"Aprobado (Dentro de Presupuesto). Requisición: {model.NumeroRequisicion}";
            }
            else
            {
                nuevoEstatus = 3; // Aprobado con Desviación 
                notaHistorial = $"Aprobado con DESVIACIÓN. Requisición: {model.NumeroRequisicion}";
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Actualizar la Solicitud Principal
                        string sqlUpdate = @"UPDATE Compras_Solicitud 
                                     SET EstatusID = @Est, IdRequisicion = @Req, EsDesviacion = @Desv 
                                     WHERE SolicitudID = @Sid";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmd.Parameters.AddWithValue("@Req", (object)model.NumeroRequisicion ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Desv", !model.DentroDePresupuesto);
                            cmd.Parameters.AddWithValue("@Sid", model.SolicitudID);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // 2. Insertar en Historial para alimentar el Dataline Time
                        string sqlH = @"INSERT INTO Compras_Historico_Pasos (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                                VALUES (@Sid, @Est, GETDATE(), @User)";

                        using (var cmdH = new SqlCommand(sqlH, conn, trans))
                        {
                            cmdH.Parameters.AddWithValue("@Sid", model.SolicitudID);
                            cmdH.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmdH.Parameters.AddWithValue("@User", "Control Presupuestal");
                            await cmdH.ExecuteNonQueryAsync();
                        }

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }

            // 3. Notificaciones automáticas (Se ejecutan después del Commit)
            if (model.Pasa)
            {
                await NotificarACompras_NuevaRequisicion(model.SolicitudID, model.NumeroRequisicion);
            }
            else
            {
                await NotificarUsuario_Rechazo(model.SolicitudID, model.Observaciones);
            }

            return RedirectToAction("BandejaPresupuestal");
        }

        // --- MÉTODOS DE NOTIFICACIÓN QUE FALTABAN ---

        private async Task NotificarACompras_NuevaRequisicion(int solicitudId, string requisicion)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    // Buscamos quién es el comprador asignado (Nacional o Internacional)
                    var sql = "SELECT Folio, PuestoAsignado FROM Compras_Solicitud WHERE SolicitudID = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                string puesto = rd["PuestoAsignado"].ToString();
                                string folio = rd["Folio"].ToString();

                                // Buscamos los correos de las personas con ese puesto
                                var listaIds = new List<int>();
                                using (var conn2 = new SqlConnection(_connectionString))
                                {
                                    await conn2.OpenAsync();
                                    var sqlP = "SELECT PersonaID FROM Persona WHERE Puesto = @p AND EsColaboradorActivo = 1";
                                    using (var cmdP = new SqlCommand(sqlP, conn2))
                                    {
                                        cmdP.Parameters.AddWithValue("@p", puesto);
                                        using (var rdP = await cmdP.ExecuteReaderAsync())
                                        {
                                            while (await rdP.ReadAsync()) listaIds.Add((int)rdP["PersonaID"]);
                                        }
                                    }
                                }

                                var asunto = $"Nueva Requisición lista para O.C. - {folio}";
                                var html = $"<h2>Requisición Liberada</h2><p>La solicitud {folio} ya tiene número de requisición: <b>{requisicion}</b>. Favor de generar la Orden de Compra.</p>";
                                await _notif.EnviarABccPersonasAsync(listaIds, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error al notificar a compras"); }
        }

        private async Task NotificarUsuario_Rechazo(int solicitudId, string motivo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    // Buscamos al usuario que creó la solicitud
                    var sql = @"SELECT S.UsuarioID, S.Folio, U.PersonaID 
                        FROM Compras_Solicitud S 
                        INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID 
                        WHERE S.SolicitudID = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", solicitudId);
                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (await rd.ReadAsync())
                            {
                                int personaId = (int)rd["PersonaID"];
                                string folio = rd["Folio"].ToString();

                                var asunto = $"Solicitud de Compra Rechazada - {folio}";
                                var html = $"<h2>Solicitud No Procedente</h2><p>Lo sentimos, su solicitud <b>{folio}</b> ha sido rechazada por Control Presupuestal.</p><p><b>Motivo:</b> {motivo}</p>";
                                await _notif.EnviarABccPersonasAsync(new List<int> { personaId }, asunto, html);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error al notificar rechazo"); }
        }

        //METODO PARA LLOS GRAFICOS
        [HttpGet]
        public async Task<IActionResult> DashboardDireccion()
        {
            var vm = new ComprasDashboardVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("sp_Compras_DataDashBoard", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        // Lectura 1: Datos del Heatmap
                        while (await reader.ReadAsync())
                        {
                            var depto = reader["NombreDepartamento"].ToString();
                            var serie = vm.HeatmapData.FirstOrDefault(s => s.name == depto);

                            if (serie == null)
                            {
                                serie = new HeatmapSeries { name = depto };
                                vm.HeatmapData.Add(serie);
                            }

                            serie.data.Add(new HeatmapDataPoint
                            {
                                x = reader["Estatus"].ToString(),
                                y = Convert.ToInt32(reader["PromedioHorasEstancado"])
                            });
                        }

                        // Lectura 2: Datos de la Dona (Cumplimiento)
                        if (await reader.NextResultAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.DonaEtiquetas.Add(reader["Estatus"].ToString());
                                vm.DonaValores.Add(Convert.ToDecimal(reader["Total"]));
                            }
                        }
                    }
                }
            }
            return View(vm);
        }


        

    }
}