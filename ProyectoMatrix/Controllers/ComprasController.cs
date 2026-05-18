using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System.Data;
using System.Security.Claims;
using static System.Net.WebRequestMethods;

namespace ProyectoMatrix.Controllers
{
    public class ComprasController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<ComprasController> _logger;
        private readonly ServicioNotificaciones _notif;
        private readonly RutaNas _rutaNas;          // Usamos solo ObtenerNombreCarpetaProyecto
        private readonly ISftpStorage _sftp;

        public ComprasController(IConfiguration configuration, ILogger<ComprasController> logger, ServicioNotificaciones notif, RutaNas rutaNas, ISftpStorage sftp)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _notif = notif;
            _sftp = sftp;
            _rutaNas = rutaNas;


        }
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new IndexComprasVm();
            var solicitudes = new List<MisComprasVm>();
            var stats = new ComprasDashboardVm();
            int usuarioId = ObtenerUsuarioIdActual();
            string puesto = ViewBag.Puesto; // Asumiendo que ya lo llenas en el Login o un ActionFilter

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // 1. LLENADO DE LA TABLA (Tus compras personales)
                string sqlTabla = @"SELECT S.SolicitudID, S.TipoCompra, S.FechaCreacion, 
                                   S.EstatusID, E.Nombre as EstatusNombre, 
                                   Em.Nombre as NombreEmpresa
                            FROM Compras_Solicitud S
                            INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
                            INNER JOIN Empresas Em ON S.EmpresaID = Em.EmpresaID
                            WHERE S.UsuarioID = @uid
                            ORDER BY S.FechaCreacion DESC";

                using (var cmd = new SqlCommand(sqlTabla, conn))
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
                                Empresa = reader["NombreEmpresa"].ToString(),
                                Fecha = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["EstatusNombre"].ToString(),
                                EstatusID = (int)reader["EstatusID"]
                            });
                        }
                    }
                }

                // 2. LLENADO DE ESTADÍSTICAS (Solo si es Director)
                if (puesto == "DIRECCION COMPRAS")
                {
                    // Query para KPIs y Gráfico de Barras (Cuellos de Botella)
                    string sqlStats = @"
                SELECT 
                    -- KPI: Críticos Vencidos (>24h sin finalizar)
                    (SELECT COUNT(*) FROM Compras_Solicitud WHERE UrgenciaID = 4 AND EstatusID < 4 AND DATEDIFF(HOUR, FechaCreacion, GETDATE()) > 24) as Criticos,
                    
                    -- KPI: Promedio Total de ciclo (Horas)
                    (SELECT ISNULL(AVG(DATEDIFF(HOUR, FechaCreacion, FechaAutorizacion)), 0) FROM Compras_Solicitud WHERE EstatusID = 4) as PromedioGlobal,

                    -- Tiempos por Depto (Promedios para la gráfica de barras)
                    ISNULL(AVG(DATEDIFF(HOUR, FechaCreacion, ISNULL(FechaCotizacion, GETDATE()))), 0) as PromCompras,
                    ISNULL(AVG(DATEDIFF(HOUR, FechaCotizacion, ISNULL(FechaDictamen, GETDATE()))), 0) as PromFinanzas,
                    ISNULL(AVG(DATEDIFF(HOUR, FechaDictamen, ISNULL(FechaAutorizacion, GETDATE()))), 0) as PromDireccion
                FROM Compras_Solicitud 
                WHERE EstatusID != 5"; // No contamos rechazadas para el promedio de éxito

                    using (var cmd = new SqlCommand(sqlStats, conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                stats.CriticosVencidos = (int)reader["Criticos"];
                                stats.PromedioTotal = Convert.ToDouble(reader["PromedioGlobal"]);

                                // Llenamos la lista para ApexCharts (Compras, Finanzas, Dirección)
                                stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromCompras"]));
                                stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromFinanzas"]));
                                stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromDireccion"]));
                            }
                        }
                    }

                    // Query para la Dona (Distribución de Estatus)
                    string sqlDona = @"SELECT E.Nombre, COUNT(S.SolicitudID) as Total 
                               FROM Cat_EstatusCompra E
                               LEFT JOIN Compras_Solicitud S ON E.EstatusID = S.EstatusID
                               GROUP BY E.Nombre";

                    using (var cmd = new SqlCommand(sqlDona, conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                stats.DonaEtiquetas.Add(reader["Nombre"].ToString());
                                stats.DonaValores.Add(Convert.ToDecimal(reader["Total"]));
                            }
                        }
                    }
                }
            }

            // Unimos todo en el modelo híbrido
            model.MisCompras = solicitudes;
            model.Estadisticas = stats;

            return View(model);
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
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            // 1. Validación de Materiales (Punto 6)
            if (model.Materiales == null || !model.Materiales.Any())
            {
                ModelState.AddModelError("", "Debes agregar al menos un material a la lista.");
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(); // Punto 5
                return View(model);
            }

            string puestoAsignado = model.TipoCompra == "Nacional" ? "Comprador Nacional" : "Comprador Internacional";
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
                            // 2. Insertar Solicitud
                            string sqlSolicitud = @"INSERT INTO Compras_Solicitud 
                        (UsuarioID, EmpresaID, TipoCompra, EsProyecto, NombreProyecto, UrgenciaID, 
                         TransporteID, ComentariosExtra, FechaCreacion, EstatusID, PuestoAsignado) 
                        VALUES (@uid, @eid, @tipo, @esp, @nom, @urg, @trans, @com, GETDATE(), 1, @puesto); 
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
                                cmd.Parameters.AddWithValue("@trans", (object)model.TransporteID ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@com", (object)model.Comentarios ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@puesto", puestoAsignado);
                               

                                nuevaSolicitudId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            }


                            // 4. Insertar la lista de materiales
                            if (model.Materiales != null && model.Materiales.Count > 0)
                            {
                                foreach (var mat in model.Materiales)
                                {
                                    string sqlMat = @"INSERT INTO Compras_Detalle_Materiales (SolicitudID, NombreMaterial, Cantidad, UnidadMedida, Descripcion) 
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

                            TempData["Mensaje"] = "Solicitud creada con éxito. Folio: COM-" + nuevaSolicitudId.ToString().PadLeft(5, '0');
                            return RedirectToAction("Index");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError(ex, "Error en la transacción de NuevaSolicitud");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al crear solicitud");
                ModelState.AddModelError("", "Ocurrió un error técnico: " + ex.Message);
                return View(model);
            }
        }


        public async Task<IActionResult> Detalle(int id)
        {
            var vm = new DetalleCompraVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string sqlCabecera = @"
            SELECT S.SolicitudID, S.Folio, 
                   (P.Nombre + ' ' + P.ApellidoPaterno) AS NombreCompleto, 
                   E.Nombre AS EmpresaNombre, 
                   Est.Nombre AS EstatusTexto, 
                   S.EstatusID,
                   S.ComentariosExtra, 
S.TipoGasto,           
                   S.NumeroRequisicion,    
                   S.DentroPresupuesto, 
                   S.ObservacionesPresupuesto
            FROM Compras_Solicitud S
            INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
            INNER JOIN Persona P ON U.PersonaID = P.PersonaID
            INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
            INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
            WHERE S.SolicitudID = @id";

                using (var cmd = new SqlCommand(sqlCabecera, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.SolicitudID = (int)reader["SolicitudID"];
                            vm.Folio = reader["Folio"].ToString();
                            vm.NombreSolicitante = reader["NombreCompleto"].ToString();
                            vm.Empresa = reader["EmpresaNombre"].ToString();
                            vm.EstatusActual = reader["EstatusTexto"].ToString();
                            vm.EstatusID = (int)reader["EstatusID"];
                            vm.TipoGasto = reader["TipoGasto"]?.ToString();
                            vm.DentroPresupuesto = reader["DentroPresupuesto"] as bool?;
                            vm.NumeroRequisicion = reader["NumeroRequisicion"]?.ToString();
                            vm.ObservacionesPresupuesto = reader["ObservacionesPresupuesto"]?.ToString();

                            // vm.Comentarios = reader["ComentariosExtra"].ToString();
                        }
                    }
                }
                string sqlCot = "SELECT MontoTotal, ArchivoPath FROM Compras_Cotizaciones WHERE SolicitudID = @id";
                using (var cmd = new SqlCommand(sqlCot, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // ESTA ES LA LÍNEA CLAVE: 
                            // Asegúrate que el nombre coincida con la vista (MontoFinal)
                            ViewBag.MontoFinal = reader["MontoTotal"];
                            ViewBag.RutaPdf = reader["ArchivoPath"].ToString();
                        }
                        else
                        {
                            // Si no hay cotización, inicializamos en 0 para que string.Format no truene
                            ViewBag.MontoFinal = 0m;
                        }
                    }
                }

                string sqlMateriales = "SELECT NombreMaterial, Cantidad, UnidadMedida FROM Compras_Detalle_Materiales WHERE SolicitudID = @id";

                using (var cmdM = new SqlCommand(sqlMateriales, conn))
                {
                    cmdM.Parameters.AddWithValue("@id", id);
                    using (var reader = await cmdM.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Materiales.Add(new MaterialItem
                            {
                                Nombre = reader["NombreMaterial"].ToString(),
                                Cantidad = Convert.ToDecimal(reader["Cantidad"]),
                                UnidadMedida = reader["UnidadMedida"].ToString()
                            });
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(vm.Folio)) return NotFound();

            return View(vm);
        }




        [HttpGet]
        public IActionResult VerPdfNas(string ruta)
        {
            try
            {
                var bytes = _sftp.DescargarBytes(ruta);
                return File(bytes, "application/pdf");
            }
            catch (Exception)
            {
                return NotFound();
            }
        }



        private async Task CargarCatalogosAsync()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Cargar Empresas
                var empresas = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
                using (var cmd = new SqlCommand("SELECT EmpresaID, Nombre FROM Empresas WHERE Activa = 1", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                        empresas.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = rd["EmpresaID"].ToString(), Text = rd["Nombre"].ToString() });
                }
                ViewBag.Empresas = empresas;

                // Cargar Urgencias
                var urgencias = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
                using (var cmd = new SqlCommand("SELECT UrgenciaID, Descripcion FROM Cat_Urgencia", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                        urgencias.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = rd["UrgenciaID"].ToString(), Text = rd["Descripcion"].ToString() });
                }
                ViewBag.Urgencias = urgencias;
            }
        }

        private int ObtenerUsuarioIdActual()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return (claim != null && int.TryParse(claim.Value, out int id)) ? id : 0;
        }

        [HttpGet("BandejaCompras")]
        public async Task<IActionResult> BandejaCompras()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            var solicitudes = new List<BandejaComprasVm>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // 1. Obtener el puesto de la persona para saber qué mostrar
                string puesto = "";
                using (var cmdP = new SqlCommand("SELECT Puesto FROM Persona p INNER JOIN Usuarios u ON p.PersonaID = u.PersonaID WHERE u.UsuarioID = @Uid", conn))
                {
                    cmdP.Parameters.AddWithValue("@Uid", usuarioId);
                    puesto = (await cmdP.ExecuteScalarAsync())?.ToString() ?? "";
                }

                // 2. Consulta SQL ajustada a tus columnas reales
                // Usamos S.TipoCompra para filtrar en lugar de PuestoAsignado si esa columna no existe
                string sql = @"
            SELECT s.SolicitudID, s.Folio, (p.Nombre + ' ' + p.ApellidoPaterno) as Solicitante, 
                   s.TipoCompra, u.Descripcion as Urgencia, s.FechaCreacion, e.Nombre as Estatus
            FROM Compras_Solicitud s
            INNER JOIN Usuarios us ON s.UsuarioID = us.UsuarioID
            INNER JOIN Persona p ON us.PersonaID = p.PersonaID
            INNER JOIN Cat_Urgencia u ON s.UrgenciaID = u.UrgenciaID
            INNER JOIN Cat_EstatusCompra e ON s.EstatusID = e.EstatusID
            WHERE s.EstatusID = 1"; // Solo lo pendiente (Solicitado)

                // Filtro lógico: Nacionales para Comprador Nacional, etc.
                if (puesto == "COMPRADOR NACIONAL")
                    sql += " AND s.TipoCompra = 'Nacional'";
                else if (puesto == "COMPRADOR INTERNACIONAL")
                    sql += " AND s.TipoCompra = 'Internacional'";
                // Si es DIRECCION COMPRAS, no entra aquí y ve ambos.

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
                                Urgencia = reader["Urgencia"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"]
                                // El campo Estatus se puede asignar si el VM lo requiere
                            });
                        }
                    }
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
                return RedirectToAction("BandejaCompras");
            }

            try
            {
                // 1. Obtener rutas
                string rutaContenedor = _rutaNas.ObtenerRutaUnicaCompras();
                string folioStr = SolicitudID.ToString().PadLeft(5, '0');
                string nombreArchivo = $"COM-{folioStr}_Cotizacion_{DateTime.Now.Ticks}.pdf";
                string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                // 2. Subir al Synology (AsegurarDirectorio crea la carpeta si no existe)
                _sftp.AsegurarDirectorio(rutaContenedor);
                using (var stream = ArchivoCotizacion.OpenReadStream())
                {
                    _sftp.SubirStream(stream, rutaArchivoSftp);
                }

                // 3. Proceso de Base de Datos con Transacción correctamente declarada
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // IMPORTANTE: Aquí se inicia la transacción
                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            // A. Insertar Cotización
                            string sqlCot = @"
                        INSERT INTO Compras_Cotizaciones (SolicitudID, ArchivoPath, MontoTotal, FechaEnvioAlUsuario)
                        VALUES (@Sid, @Path, @Monto, GETDATE())";

                            using (var cmdCot = new SqlCommand(sqlCot, conn, trans))
                            {
                                cmdCot.Parameters.AddWithValue("@Sid", SolicitudID);
                                cmdCot.Parameters.AddWithValue("@Path", rutaArchivoSftp);
                                cmdCot.Parameters.AddWithValue("@Monto", Monto);
                                await cmdCot.ExecuteNonQueryAsync();
                            }

                            // B. Actualizar Estatus de la Solicitud
                            string sqlUpd = "UPDATE Compras_Solicitud SET EstatusID = 2 WHERE SolicitudID = @Sid";
                            using (var cmdUpd = new SqlCommand(sqlUpd, conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@Sid", SolicitudID);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }

                            // C. Registrar en Histórico
                            string sqlH = @"
                        INSERT INTO Compras_Historico_Pasos (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                        VALUES (@Sid, 2, GETDATE(), 
                               (SELECT TOP 1 P.Nombre + ' ' + P.ApellidoPaterno 
                                FROM Persona P 
                                INNER JOIN Usuarios U ON P.PersonaID = U.PersonaID 
                                WHERE U.UsuarioID = @Uid))";

                            using (var cmdH = new SqlCommand(sqlH, conn, trans))
                            {
                                cmdH.Parameters.AddWithValue("@Sid", SolicitudID);
                                cmdH.Parameters.AddWithValue("@Uid", usuarioId);
                                await cmdH.ExecuteNonQueryAsync();
                            }

                            // Si todo salió bien, guardamos cambios
                            trans.Commit();
                            TempData["Mensaje"] = "Cotización procesada correctamente.";

                            string sqlUpdate = @"
    UPDATE Compras_Solicitud 
    SET EstatusID = 2, 
        Progreso = 50 
    WHERE SolicitudID = @Sid";

                            using (var cmdUpd = new SqlCommand(sqlUpdate, conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@Sid", SolicitudID);
                                await cmdUpd.ExecuteNonQueryAsync();
                            }



                        }


                        catch (Exception ex)
                        {
                            // Si algo falla en la BD, deshacemos los inserts/updates
                            trans.Rollback();
                            _logger.LogError(ex, "Error en la transacción de base de datos");
                            throw; // Re-lanzamos para que lo atrape el catch externo
                        }
                    }
                }
                return RedirectToAction("BandejaCompras");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en ProcesarCotizacion");
                TempData["Error"] = "Error técnico: " + ex.Message;
                return RedirectToAction("BandejaCompras");
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


     



        //Metodo para que control presupuestal decida si se aprueba o no el gasto
        [HttpGet("Dictamen/{id}")]
        public async Task<IActionResult> Dictamen(int id)
        {
            var model = new DictamenPresupuestalVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                string sql = @"
            SELECT S.SolicitudID, S.Folio, C.MontoTotal 
            FROM Compras_Solicitud S
            INNER JOIN Compras_Cotizaciones C ON S.SolicitudID = C.SolicitudID
            WHERE S.SolicitudID = @id";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model.SolicitudID = (int)reader["SolicitudID"];
                            model.Folio = reader["Folio"].ToString();
                            model.MontoCotizado = (decimal)reader["MontoTotal"];
                        }
                    }
                }
            }
            return View(model);
        }


        //metodo post para CP

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarDictamen(DictamenPresupuestalVm vm)
        {
            // Si el ID es 0, la vista no está mandando el hidden input correctamente
            if (vm.SolicitudID == 0) return BadRequest("Error: No se recibió el ID de la solicitud.");

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int nuevoEstatus = vm.Pasa ? 3 : 5;

                        string queryUpdate = @"
                    UPDATE Compras_Solicitud 
                    SET EstatusID = @Est, 
FechaDictamen = GETDATE(),
                        TipoGasto = @Tipo,
                        DentroPresupuesto = @Dentro,
                        NumeroRequisicion = @Requi,
                        ObservacionesPresupuesto = @Obs
                    WHERE SolicitudID = @Sid";

                        using (var cmd = new SqlCommand(queryUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmd.Parameters.AddWithValue("@Sid", vm.SolicitudID);

                            // Si es RECHAZO (Pasa = false), mandamos NULL a los campos de dinero
                            cmd.Parameters.AddWithValue("@Tipo", vm.Pasa ? (object)vm.TipoGasto : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Dentro", vm.Pasa ? (object)vm.DentroDePresupuesto : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Requi", (vm.Pasa && vm.TipoGasto == "REQUISICION") ? (object)vm.NumeroRequisicion : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Obs", (object)vm.Observaciones ?? DBNull.Value);

                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();
                        return RedirectToAction("BandejaPresupuestos");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al actualizar Compras_Solicitud");
                        return View("Dictamen", vm);
                    }
                }
            }
        }
        [HttpGet("BandejaPresupuestos")]
        public async Task<IActionResult> BandejaPresupuestos()
        {
            // Usamos el modelo que ya tienes en tu lista
            var solicitudes = new List<BandejaComprasVm>();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Filtramos por EstatusID = 2 (Cotizado) que son las que CP debe revisar
                string sql = @"
            SELECT S.SolicitudID, S.Folio, 
                   (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
                   S.TipoCompra, S.FechaCreacion, Est.Nombre AS Estatus
            FROM Compras_Solicitud S
            INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
            INNER JOIN Persona P ON U.PersonaID = P.PersonaID
            INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
            WHERE S.EstatusID = 2";

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
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["Estatus"].ToString()
                            });
                        }
                    }
                }
            }
            return View(solicitudes);
        }

        // Lógica para detectar si Control Presupuestal se pasó de sus 24h
        public string ObtenerAlertaPresupuesto(DateTime fechaCotizacion)
        {
            var horasTranscurridas = (DateTime.Now - fechaCotizacion).TotalHours;

            if (horasTranscurridas > 48) return "text-danger font-weight-bold"; // Muy retrasado
            if (horasTranscurridas > 24) return "text-warning"; // Al límite
            return "text-success"; // A tiempo
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