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
       // private const int DepartamentoEmpresaComprasIdNSE = 8;

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
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string puesto = "";

                string sqlPuesto = @"
            SELECT P.Puesto
            FROM Usuarios U
            INNER JOIN Persona P ON U.PersonaID = P.PersonaID
            WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                ViewBag.Puesto = puesto;


                string departamentosUsuario = "";

                string sqlDeptos = @"
SELECT STRING_AGG(UPPER(D.NombreDepartamento), ',')
FROM EmpleadoDepartamentos ED
INNER JOIN Departamentos D 
    ON ED.DepartamentoID = D.DepartamentoID
WHERE ED.UsuarioID = @UsuarioID
  AND ED.Activo = 1
  AND D.Activo = 1";

                using (var cmdDeptos = new SqlCommand(sqlDeptos, conn))
                {
                    cmdDeptos.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    departamentosUsuario =
                        (await cmdDeptos.ExecuteScalarAsync())?.ToString() ?? "";
                }

                ViewBag.Puesto = puesto;
                ViewBag.DepartamentosUsuario = departamentosUsuario;


                string sqlTabla = @"
            SELECT S.SolicitudID, S.TipoCompra, S.FechaCreacion, 
                   S.EstatusID, E.Nombre AS EstatusNombre, 
                   Em.Nombre AS NombreEmpresa
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

                if (puesto == "DIRECCION COMPRAS")
                {
                    string sqlStats = @"
                SELECT 
                    (SELECT COUNT(*) 
                     FROM Compras_Solicitud 
                     WHERE UrgenciaID = 4 
                       AND EstatusID NOT IN (4, 6)
                       AND DATEDIFF(HOUR, FechaCreacion, GETDATE()) > 24) AS Criticos,

                    (SELECT ISNULL(AVG(DATEDIFF(HOUR, Inicio.FechaInicio, Ultimo.FechaUltimoMovimiento)), 0)
                     FROM
                     (
                         SELECT SolicitudID, MIN(FechaMovimiento) AS FechaInicio
                         FROM Compras_Historico_Pasos
                         WHERE EstatusID = 1
                         GROUP BY SolicitudID
                     ) Inicio
                     INNER JOIN
                     (
                         SELECT SolicitudID, MAX(FechaMovimiento) AS FechaUltimoMovimiento
                         FROM Compras_Historico_Pasos
                         GROUP BY SolicitudID
                     ) Ultimo
                     ON Inicio.SolicitudID = Ultimo.SolicitudID
                    ) AS PromedioGlobal,

                    ISNULL(AVG(DATEDIFF(HOUR, FechaCreacion, ISNULL(FechaCotizacion, GETDATE()))), 0) AS PromCompras,
                    ISNULL(AVG(DATEDIFF(HOUR, FechaCotizacion, ISNULL(FechaDictamen, GETDATE()))), 0) AS PromFinanzas,
                    ISNULL(AVG(DATEDIFF(HOUR, FechaDictamen, ISNULL(FechaAutorizacion, GETDATE()))), 0) AS PromDireccion
                FROM Compras_Solicitud 
                WHERE EstatusID != 4";

                    using (var cmd = new SqlCommand(sqlStats, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            stats.CriticosVencidos = Convert.ToInt32(reader["Criticos"]);
                            stats.PromedioTotal = Convert.ToDouble(reader["PromedioGlobal"]);
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromCompras"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromFinanzas"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromDireccion"]));
                        }
                    }

                    string sqlDona = @"
                SELECT E.Nombre, COUNT(S.SolicitudID) AS Total 
                FROM Cat_EstatusCompra E
                LEFT JOIN Compras_Solicitud S ON E.EstatusID = S.EstatusID
                GROUP BY E.Nombre";

                    using (var cmd = new SqlCommand(sqlDona, conn))
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

                var empresas = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

                string sqlEmpresas = @"
            SELECT E.EmpresaID, E.Nombre
            FROM UsuariosEmpresas UE
            INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
            WHERE UE.UsuarioID = @UsuarioID
              AND UE.Activo = 1
              AND E.Activa = 1
            ORDER BY E.Nombre";

                using (var cmd = new SqlCommand(sqlEmpresas, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

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

                var urgencias = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

                string sqlUrg = "SELECT UrgenciaID, Descripcion FROM Cat_Urgencia";

                using (var cmdU = new SqlCommand(sqlUrg, conn))
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

                ViewBag.Urgencias = urgencias;
            }

            var model = new CompraViewModel
            {
                Materiales = new List<MaterialItem>()
            };

            return View(model);
        }

        [HttpPost("NuevaSolicitud")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NuevaSolicitud(
    CompraViewModel model,
    IFormFile? ArchivoReferencia)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (model.Materiales == null || !model.Materiales.Any())
            {
                ModelState.AddModelError("", "Debes agregar al menos un material a la lista.");
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(usuarioId);
                return View(model);
            }

            string puestoAsignado = model.TipoCompra == "Nacional"
                ? "Comprador Nacional"
                : "Comprador Internacional";

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
                            bool empresaPermitida = await UsuarioTieneEmpresaAsync(
                                conn,
                                transaction,
                                usuarioId,
                                model.EmpresaID
                            );

                            if (!empresaPermitida)
                            {
                                transaction.Rollback();
                                return Forbid();
                            }

                            string sqlSolicitud = @"
                        INSERT INTO Compras_Solicitud 
                        (UsuarioID, EmpresaID, TipoCompra, EsProyecto, NombreProyecto, UrgenciaID, 
                         TransporteID, ComentariosExtra, FechaCreacion, EstatusID, PuestoAsignado) 
                        VALUES 
                        (@uid, @eid, @tipo, @esp, @nom, @urg, @trans, @com, GETDATE(), 1, @puesto); 

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
                            if (ArchivoReferencia != null && ArchivoReferencia.Length > 0)
                            {
                                var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
                                string extension = Path.GetExtension(ArchivoReferencia.FileName)?.ToLowerInvariant() ?? "";

                                if (!extensionesPermitidas.Contains(extension))
                                {
                                    transaction.Rollback();
                                    ModelState.AddModelError("", "El archivo de referencia solo puede ser PDF o imagen JPG, PNG o WEBP.");
                                    await CargarCatalogosAsync(usuarioId);
                                    return View(model);
                                }

                                string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();

                                _sftp.AsegurarDirectorio(rutaContenedor);

                                string folioStr = nuevaSolicitudId.ToString().PadLeft(5, '0');
                                string nombreArchivo = $"COM-{folioStr}_Referencia_{Guid.NewGuid()}{extension}";
                                string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                                using (var stream = ArchivoReferencia.OpenReadStream())
                                {
                                    _sftp.SubirStream(stream, rutaArchivoSftp);
                                }

                                string sqlArchivoReferencia = @"
UPDATE Compras_Solicitud
SET ArchivoReferenciaPath = @ArchivoReferenciaPath
WHERE SolicitudID = @SolicitudID";

                                using (var cmdArchivo = new SqlCommand(sqlArchivoReferencia, conn, transaction))
                                {
                                    cmdArchivo.Parameters.AddWithValue("@ArchivoReferenciaPath", rutaArchivoSftp);
                                    cmdArchivo.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);

                                    await cmdArchivo.ExecuteNonQueryAsync();
                                }
                            }

                            if (model.Materiales != null && model.Materiales.Count > 0)
                            {
                                foreach (var mat in model.Materiales)
                                {
                                    string sqlMat = @"
                                INSERT INTO Compras_Detalle_Materiales 
                                (SolicitudID, NombreMaterial, Cantidad, UnidadMedida, Descripcion) 
                                VALUES 
                                (@sid, @n, @c, @u, @d)";

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

                            int? compradorAsignadoId = await ObtenerCompradorConMenosCargaAsync(
                                conn,
                                transaction,
                                puestoAsignado,
                                model.EmpresaID
                            );

                            if (compradorAsignadoId.HasValue)
                            {
                                string sqlAsignar = @"
                            UPDATE Compras_Solicitud
                            SET CompradorAsignadoUsuarioID = @CompradorID,
                                FechaAsignacionComprador = GETDATE(),
                                UsuarioAsignoCompradorID = @UsuarioAsignoID
                            WHERE SolicitudID = @SolicitudID";

                                using (var cmdAsignar = new SqlCommand(sqlAsignar, conn, transaction))
                                {
                                    cmdAsignar.Parameters.AddWithValue("@CompradorID", compradorAsignadoId.Value);
                                    cmdAsignar.Parameters.AddWithValue("@UsuarioAsignoID", usuarioId);
                                    cmdAsignar.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);

                                    await cmdAsignar.ExecuteNonQueryAsync();
                                }

                                string sqlHistAsignacion = @"
                            INSERT INTO Compras_Asignaciones_Historico
                            (SolicitudID, UsuarioAsignadoAnteriorID, UsuarioAsignadoNuevoID, UsuarioDireccionID, FechaAsignacion, Comentario)
                            VALUES
                            (@SolicitudID, NULL, @NuevoID, @DireccionID, GETDATE(), @Comentario)";

                                using (var cmdHistAsig = new SqlCommand(sqlHistAsignacion, conn, transaction))
                                {
                                    cmdHistAsig.Parameters.AddWithValue("@SolicitudID", nuevaSolicitudId);
                                    cmdHistAsig.Parameters.AddWithValue("@NuevoID", compradorAsignadoId.Value);
                                    cmdHistAsig.Parameters.AddWithValue("@DireccionID", usuarioId);
                                    cmdHistAsig.Parameters.AddWithValue("@Comentario", "Asignación automática por menor carga");

                                    await cmdHistAsig.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "No se encontró comprador activo para autoasignar la solicitud {SolicitudID}. EmpresaID: {EmpresaID}. Puesto: {Puesto}",
                                    nuevaSolicitudId,
                                    model.EmpresaID,
                                    puestoAsignado
                                );
                            }

                            string sqlHistorico = @"
                        INSERT INTO Compras_Historico_Pasos 
                        (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                        VALUES 
                        (@sid, 1, GETDATE(), @responsable)";

                            using (var cmdHist = new SqlCommand(sqlHistorico, conn, transaction))
                            {
                                cmdHist.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                cmdHist.Parameters.AddWithValue("@responsable", nombreSolicitante);

                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();

                            await NotificarComprador_NuevaSolicitudAsync(nuevaSolicitudId);

                            TempData["Mensaje"] =
                                "Solicitud creada con éxito. Folio: COM-" +
                                nuevaSolicitudId.ToString().PadLeft(5, '0');

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

                await CargarCatalogosAsync(usuarioId);
                return View(model);
            }
        }


        //metodo pricado para obtener a un comprador 
        private async Task<int?> ObtenerCompradorConMenosCargaAsync(
      SqlConnection conn,
      SqlTransaction trans,
      string puestoComprador,
      int empresaId)
        {
            string sql = @"
        SELECT TOP 1 U.UsuarioID
        FROM Usuarios U
        INNER JOIN Persona P ON U.PersonaID = P.PersonaID
        INNER JOIN UsuariosEmpresas UE 
            ON U.UsuarioID = UE.UsuarioID
           AND UE.Activo = 1
           AND UE.EmpresaID = @EmpresaID
        LEFT JOIN Compras_Solicitud S
            ON S.CompradorAsignadoUsuarioID = U.UsuarioID
           AND S.EstatusID = 1
        WHERE P.EsColaboradorActivo = 1
          AND UPPER(P.Puesto) = UPPER(@Puesto)
        GROUP BY U.UsuarioID
        ORDER BY COUNT(S.SolicitudID) ASC, U.UsuarioID ASC";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@Puesto", puestoComprador);
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }

        private async Task<bool> UsuarioTieneEmpresaAsync(
    SqlConnection conn,
    SqlTransaction trans,
    int usuarioId,
    int empresaId)
        {
            string sql = @"
        SELECT COUNT(*)
        FROM UsuariosEmpresas UE
        INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
        WHERE UE.UsuarioID = @UsuarioID
          AND UE.EmpresaID = @EmpresaID
          AND UE.Activo = 1
          AND E.Activa = 1";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);

                int total = (int)await cmd.ExecuteScalarAsync();
                return total > 0;
            }
        }


        public async Task<IActionResult> Detalle(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new DetalleCompraVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string sqlCabecera = @"
SELECT 
    S.SolicitudID,
    S.Folio, 
    (P.Nombre + ' ' + P.ApellidoPaterno) AS NombreCompleto, 
    E.Nombre AS EmpresaNombre, 
    Est.Nombre AS EstatusTexto, 
    S.EstatusID,
    S.TipoGasto,           
    S.NumeroRequisicion,    
    S.DentroPresupuesto, 
    S.ObservacionesPresupuesto,
    S.UsuarioID,
    S.CotizacionSeleccionadaID,
    S.FechaSeleccionCotizacion,
    S.ComentariosSeleccionUsuario,
S.ArchivoReferenciaPath
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
                        if (!await reader.ReadAsync())
                            return NotFound();

                        vm.SolicitudID = (int)reader["SolicitudID"];
                        vm.Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString();
                        vm.NombreSolicitante = reader["NombreCompleto"] == DBNull.Value ? "" : reader["NombreCompleto"].ToString();
                        vm.Empresa = reader["EmpresaNombre"] == DBNull.Value ? "" : reader["EmpresaNombre"].ToString();
                        vm.EstatusActual = reader["EstatusTexto"] == DBNull.Value ? "" : reader["EstatusTexto"].ToString();
                        vm.EstatusID = (int)reader["EstatusID"];

                        vm.TipoGasto = reader["TipoGasto"] == DBNull.Value ? null : reader["TipoGasto"].ToString();
                        vm.DentroPresupuesto = reader["DentroPresupuesto"] == DBNull.Value ? null : (bool?)reader["DentroPresupuesto"];
                        vm.NumeroRequisicion = reader["NumeroRequisicion"] == DBNull.Value ? null : reader["NumeroRequisicion"].ToString();
                        vm.ObservacionesPresupuesto = reader["ObservacionesPresupuesto"] == DBNull.Value ? null : reader["ObservacionesPresupuesto"].ToString();

                        int usuarioSolicitanteId = (int)reader["UsuarioID"];
                        vm.EsSolicitante = usuarioSolicitanteId == usuarioId;

                        vm.CotizacionSeleccionadaID =
                            reader["CotizacionSeleccionadaID"] == DBNull.Value
                                ? null
                                : (int?)reader["CotizacionSeleccionadaID"];

                        vm.FechaSeleccionCotizacion =
                            reader["FechaSeleccionCotizacion"] == DBNull.Value
                                ? null
                                : (DateTime?)reader["FechaSeleccionCotizacion"];

                        vm.ComentariosSeleccionUsuario =
                            reader["ComentariosSeleccionUsuario"] == DBNull.Value
                                ? null
                                : reader["ComentariosSeleccionUsuario"].ToString();

                        vm.ArchivoReferenciaPath =
    reader["ArchivoReferenciaPath"] == DBNull.Value
        ? null
        : reader["ArchivoReferenciaPath"].ToString();
                    }
                }

                string sqlCot = @"
SELECT
    CotizacionID,
    Proveedor,
    MontoTotal,
    ArchivoPath,
    NombreArchivoOriginal,
    Extension,
    EsRecomendada,
    ComentariosCompras,
    FechaEnvioAlUsuario
FROM Compras_Cotizaciones
WHERE SolicitudID = @id
ORDER BY NumeroCotizacion";

                using (var cmd = new SqlCommand(sqlCot, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int cotizacionId = (int)reader["CotizacionID"];

                            vm.Cotizaciones.Add(new CotizacionDetalleVm
                            {
                                CotizacionID = cotizacionId,
                                Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString(),
                                MontoTotal = reader["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["MontoTotal"]),
                                ArchivoPath = reader["ArchivoPath"] == DBNull.Value ? "" : reader["ArchivoPath"].ToString(),
                                NombreArchivoOriginal = reader["NombreArchivoOriginal"] == DBNull.Value ? "" : reader["NombreArchivoOriginal"].ToString(),
                                Extension = reader["Extension"] == DBNull.Value ? "" : reader["Extension"].ToString(),
                                EsRecomendada = reader["EsRecomendada"] != DBNull.Value && Convert.ToBoolean(reader["EsRecomendada"]),
                                ComentariosCompras = reader["ComentariosCompras"] == DBNull.Value ? null : reader["ComentariosCompras"].ToString(),
                                FechaEnvioAlUsuario = reader["FechaEnvioAlUsuario"] == DBNull.Value ? DateTime.MinValue : (DateTime)reader["FechaEnvioAlUsuario"],
                                FueSeleccionadaPorUsuario = vm.CotizacionSeleccionadaID.HasValue &&
                                                            vm.CotizacionSeleccionadaID.Value == cotizacionId
                            });
                        }
                    }
                }

                string sqlMateriales = @"
SELECT NombreMaterial, Cantidad, UnidadMedida
FROM Compras_Detalle_Materiales
WHERE SolicitudID = @id";

                using (var cmdM = new SqlCommand(sqlMateriales, conn))
                {
                    cmdM.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdM.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Materiales.Add(new MaterialItem
                            {
                                Nombre = reader["NombreMaterial"] == DBNull.Value ? "" : reader["NombreMaterial"].ToString(),
                                Cantidad = reader["Cantidad"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Cantidad"]),
                                UnidadMedida = reader["UnidadMedida"] == DBNull.Value ? "" : reader["UnidadMedida"].ToString()
                            });
                        }
                    }
                }

                string sqlOC = @"
SELECT TOP 1 
    NumeroOC,
    Proveedor,
    Comentarios,
    FechaOC,
    FechaEnvioProveedor,
    FechaEstimadaEntrega
FROM Compras_OrdenCompra
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY OrdenCompraID DESC";

                using (var cmdOC = new SqlCommand(sqlOC, conn))
                {
                    cmdOC.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdOC.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.TieneOC = true;
                            vm.NumeroOC = reader["NumeroOC"] == DBNull.Value ? "" : reader["NumeroOC"].ToString();
                            vm.ProveedorOC = reader["Proveedor"] == DBNull.Value ? null : reader["Proveedor"].ToString();
                            vm.ComentariosOC = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                            vm.FechaOC = reader["FechaOC"] == DBNull.Value ? null : (DateTime?)reader["FechaOC"];
                            vm.FechaEnvioProveedor = reader["FechaEnvioProveedor"] == DBNull.Value ? null : (DateTime?)reader["FechaEnvioProveedor"];
                            vm.FechaEstimadaEntrega = reader["FechaEstimadaEntrega"] == DBNull.Value ? null : (DateTime?)reader["FechaEstimadaEntrega"];
                            vm.OCEnviadaProveedor = vm.FechaEnvioProveedor.HasValue;
                        }
                    }
                }

                string sqlRecepcion = @"
SELECT TOP 1
    FechaRecepcion,
    Comentarios
FROM Compras_Recepciones
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY RecepcionID DESC";

                using (var cmdRecep = new SqlCommand(sqlRecepcion, conn))
                {
                    cmdRecep.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdRecep.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.RecibidaEnAlmacen = true;
                            vm.FechaRecepcionAlmacen = reader["FechaRecepcion"] == DBNull.Value ? null : (DateTime?)reader["FechaRecepcion"];
                            vm.ComentariosRecepcionAlmacen = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                        }
                    }
                }

                string sqlEntrega = @"
SELECT TOP 1
    FechaEntrega,
    NombreRecibe,
    Comentarios
FROM Compras_EntregasUsuario
WHERE SolicitudID = @id
  AND Activo = 1
ORDER BY EntregaID DESC";

                using (var cmdEntrega = new SqlCommand(sqlEntrega, conn))
                {
                    cmdEntrega.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmdEntrega.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            vm.EntregadaUsuario = true;
                            vm.FechaEntregaUsuario = reader["FechaEntrega"] == DBNull.Value ? null : (DateTime?)reader["FechaEntrega"];
                            vm.NombreRecibeUsuario = reader["NombreRecibe"] == DBNull.Value ? null : reader["NombreRecibe"].ToString();
                            vm.ComentariosEntregaUsuario = reader["Comentarios"] == DBNull.Value ? null : reader["Comentarios"].ToString();
                        }
                    }
                }

                string sqlPuesto = @"
SELECT P.Puesto
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID";

                using (var cmd = new SqlCommand(sqlPuesto, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    string puesto = (await cmd.ExecuteScalarAsync())?.ToString()?.Trim().ToUpper() ?? "";

                    vm.EsCompras =
                        puesto == "COMPRADOR NACIONAL" ||
                        puesto == "COMPRADOR INTERNACIONAL" ||
                        puesto == "DIRECCION COMPRAS" ||
                        puesto == "DIRECCIÓN COMPRAS";
                }
            }

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



        private async Task CargarCatalogosAsync(int usuarioId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var empresas = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

                string sqlEmpresas = @"
            SELECT E.EmpresaID, E.Nombre
            FROM UsuariosEmpresas UE
            INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
            WHERE UE.UsuarioID = @UsuarioID
              AND UE.Activo = 1
              AND E.Activa = 1
            ORDER BY E.Nombre";

                using (var cmd = new SqlCommand(sqlEmpresas, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            empresas.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = rd["EmpresaID"].ToString(),
                                Text = rd["Nombre"].ToString()
                            });
                        }
                    }
                }

                ViewBag.Empresas = empresas;

                var urgencias = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

                using (var cmd = new SqlCommand("SELECT UrgenciaID, Descripcion FROM Cat_Urgencia", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        urgencias.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = rd["UrgenciaID"].ToString(),
                            Text = rd["Descripcion"].ToString()
                        });
                    }
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
            if (usuarioId == 0) return Unauthorized();

            var vm = new BandejaComprasDashboardVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                bool perteneceCompras =
    await UsuarioPerteneceADepartamentoAsync(
        conn,
        usuarioId,
        "COMPRAS"
    );

                if (!perteneceCompras)
                    return Forbid();

                int? empresaActivaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId);

                if (!empresaActivaId.HasValue)
                    return Forbid();


                string puesto = "";

                using (var cmdP = new SqlCommand(@"
            SELECT P.Puesto 
            FROM Persona P
            INNER JOIN Usuarios U ON P.PersonaID = U.PersonaID 
            WHERE U.UsuarioID = @Uid", conn))
                {
                    cmdP.Parameters.AddWithValue("@Uid", usuarioId);
                    puesto = (await cmdP.ExecuteScalarAsync())?.ToString() ?? "";
                }

                vm.EsDireccionCompras = puesto == "DIRECCION COMPRAS";

                string filtroTipo = "";

                if (puesto == "COMPRADOR NACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Nacional'";
                else if (puesto == "COMPRADOR INTERNACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Internacional'";

                string sqlPendientes = $@"
            SELECT S.SolicitudID,S.CompradorAsignadoUsuarioID, S.Folio, 
                   (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
                   ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
                   S.TipoCompra,
                   U.Descripcion AS Urgencia,
                   S.FechaCreacion,
                   E.Nombre AS Estatus
            FROM Compras_Solicitud S
            INNER JOIN Usuarios US ON S.UsuarioID = US.UsuarioID
            INNER JOIN Persona P ON US.PersonaID = P.PersonaID
            LEFT JOIN EmpleadoDepartamentos ED ON US.UsuarioID = ED.UsuarioID AND ED.Activo = 1
            LEFT JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID
            INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
            INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
           WHERE S.EstatusID = 1
  AND S.EmpresaID = @EmpresaID
  AND (
                    @EsDireccion = 1
                    OR S.CompradorAsignadoUsuarioID = @UsuarioID
                  )
            {filtroTipo}
            ORDER BY S.FechaCreacion ASC";

                using (var cmd = new SqlCommand(sqlPendientes, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@EsDireccion", vm.EsDireccionCompras ? 1 : 0);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Pendientes.Add(new BandejaComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                CompradorAsignadoUsuarioID =
    reader["CompradorAsignadoUsuarioID"] == DBNull.Value
        ? null
        : (int?)reader["CompradorAsignadoUsuarioID"],
                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["Estatus"].ToString()

                            });
                        }
                    }
                }

                string sqlHistorico = $@"
            SELECT S.SolicitudID, S.Folio,S.EstatusID,
                   (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
                   ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
                   S.TipoCompra,
                   U.Descripcion AS Urgencia,
                   E.Nombre AS Estatus,
                   ISNULL(C.MontoTotal, 0) AS MontoTotal,
                   S.FechaCreacion,
                   C.FechaEnvioAlUsuario AS FechaCotizacion
            FROM Compras_Solicitud S
            INNER JOIN Usuarios US ON S.UsuarioID = US.UsuarioID
            INNER JOIN Persona P ON US.PersonaID = P.PersonaID
            LEFT JOIN EmpleadoDepartamentos ED ON US.UsuarioID = ED.UsuarioID AND ED.Activo = 1
            LEFT JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID
            INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
            INNER JOIN Cat_EstatusCompra E ON S.EstatusID = E.EstatusID
            LEFT JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
          WHERE S.EstatusID >= 2
  AND S.EmpresaID = @EmpresaID
  AND (
                    @EsDireccion = 1
                    OR S.CompradorAsignadoUsuarioID = @UsuarioID
                  )
            {filtroTipo}
            ORDER BY C.FechaEnvioAlUsuario DESC, S.FechaCreacion DESC";

                using (var cmd = new SqlCommand(sqlHistorico, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@EsDireccion", vm.EsDireccionCompras ? 1 : 0);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Historico.Add(new HistoricoComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                EstatusID = (int)reader["EstatusID"],
                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),
                                Estatus = reader["Estatus"].ToString(),
                                MontoTotal = Convert.ToDecimal(reader["MontoTotal"]),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                FechaCotizacion = reader["FechaCotizacion"] == DBNull.Value
                                    ? null
                                    : (DateTime?)reader["FechaCotizacion"]
                            });
                        }
                    }
                }

                if (vm.EsDireccionCompras)
                {
                    string sqlCargaCompradores = @"
    SELECT 
        U.UsuarioID,
        (P.Nombre + ' ' + P.ApellidoPaterno) AS NombreCompleto,
        P.Puesto,

        SUM(CASE WHEN S.EstatusID = 1 THEN 1 ELSE 0 END) AS Pendientes,

        SUM(CASE WHEN S.EstatusID >= 2 THEN 1 ELSE 0 END) AS Cotizadas,

        COUNT(S.SolicitudID) AS TotalAsignadas

    FROM Usuarios U

    INNER JOIN Persona P 
        ON U.PersonaID = P.PersonaID

    INNER JOIN UsuariosEmpresas UE
        ON U.UsuarioID = UE.UsuarioID
       AND UE.Activo = 1
       AND UE.EmpresaID = @EmpresaID

    LEFT JOIN Compras_Solicitud S 
        ON S.CompradorAsignadoUsuarioID = U.UsuarioID
       AND S.EmpresaID = @EmpresaID

    WHERE P.EsColaboradorActivo = 1
      AND UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')

    GROUP BY 
        U.UsuarioID,
        P.Nombre,
        P.ApellidoPaterno,
        P.Puesto

    ORDER BY 
        P.Puesto,
        Pendientes ASC,
        NombreCompleto ASC";

                    using (var cmdCarga = new SqlCommand(sqlCargaCompradores, conn))
                    {
                        cmdCarga.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                        using (var reader = await cmdCarga.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CargaCompradores.Add(new CompradorCargaVm
                                {
                                    UsuarioID = (int)reader["UsuarioID"],
                                    NombreCompleto = reader["NombreCompleto"].ToString(),
                                    Puesto = reader["Puesto"].ToString(),
                                    Pendientes = Convert.ToInt32(reader["Pendientes"]),
                                    Cotizadas = Convert.ToInt32(reader["Cotizadas"]),
                                    TotalAsignadas = Convert.ToInt32(reader["TotalAsignadas"])
                                });
                            }
                        }
                    }

                    string sqlCompradores = @"
    SELECT 
        U.UsuarioID,
        (P.Nombre + ' ' + P.ApellidoPaterno) AS NombreCompleto,
        P.Puesto

    FROM Usuarios U

    INNER JOIN Persona P 
        ON U.PersonaID = P.PersonaID

    INNER JOIN UsuariosEmpresas UE
        ON U.UsuarioID = UE.UsuarioID
       AND UE.Activo = 1
       AND UE.EmpresaID = @EmpresaID

    WHERE P.EsColaboradorActivo = 1
      AND UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')

    ORDER BY 
        P.Puesto,
        P.Nombre,
        P.ApellidoPaterno";

                    using (var cmdCompradores = new SqlCommand(sqlCompradores, conn))
                    {
                        cmdCompradores.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                        using (var reader = await cmdCompradores.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CompradoresDisponibles.Add(new CompradorSelectVm
                                {
                                    UsuarioID = (int)reader["UsuarioID"],
                                    NombreCompleto = reader["NombreCompleto"].ToString(),
                                    Puesto = reader["Puesto"].ToString()
                                });
                            }
                        }
                    }
                }
            }

            vm.TotalPendientes = vm.Pendientes.Count;
            vm.TotalCotizadas = vm.Historico.Count(x => x.FechaCotizacion != null);
            vm.TotalAtendidas = vm.Historico.Count;
            vm.MontoCotizado = vm.Historico.Sum(x => x.MontoTotal);

            return View(vm);
        }




        [HttpPost("ProcesarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarCotizacion(
    [FromForm] int SolicitudID,
    [FromForm] List<IFormFile> ArchivosCotizacion,
    [FromForm] List<string> Proveedores,
    [FromForm] List<decimal?> Montos,
    [FromForm] int CotizacionRecomendada,
    [FromForm] string? ComentariosCompras)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (ArchivosCotizacion == null || !ArchivosCotizacion.Any(x => x != null && x.Length > 0))
            {
                TempData["Error"] = "Debes subir al menos una cotización.";
                return RedirectToAction("Detalle", new { id = SolicitudID });
            }

            if (ArchivosCotizacion.Count(x => x != null && x.Length > 0) > 3)
            {
                TempData["Error"] = "Solo puedes subir máximo 3 cotizaciones.";
                return RedirectToAction("Detalle", new { id = SolicitudID });
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var trans = conn.BeginTransaction())
                    {
                        try
                        {
                            string sqlValidarSolicitud = @"
SELECT COUNT(*)
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 1";

                            using (var cmdVal = new SqlCommand(sqlValidarSolicitud, conn, trans))
                            {
                                cmdVal.Parameters.AddWithValue("@SolicitudID", SolicitudID);

                                int valida = Convert.ToInt32(await cmdVal.ExecuteScalarAsync());

                                if (valida == 0)
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud ya no está pendiente de cotización.";
                                    return RedirectToAction("Detalle", new { id = SolicitudID });
                                }
                            }

                            string rutaContenedor = _rutaNas.ObtenerRutaUnicaCompras();
                            string folioStr = SolicitudID.ToString().PadLeft(5, '0');

                            _sftp.AsegurarDirectorio(rutaContenedor);

                            int numeroCotizacion = 0;

                            for (int i = 0; i < ArchivosCotizacion.Count; i++)
                            {
                                var archivo = ArchivosCotizacion[i];

                                if (archivo == null || archivo.Length == 0)
                                    continue;

                                string proveedor = Proveedores.ElementAtOrDefault(i)?.Trim() ?? "";
                                decimal? monto = Montos.ElementAtOrDefault(i);

                                if (string.IsNullOrWhiteSpace(proveedor) || monto == null)
                                {
                                    trans.Rollback();

                                    TempData["Error"] =
                                        $"La cotización {i + 1} debe tener proveedor, monto y archivo.";

                                    return RedirectToAction("Detalle", new { id = SolicitudID });
                                }

                                numeroCotizacion++;

                                if (string.IsNullOrWhiteSpace(proveedor) || monto == null)
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "Cada cotización debe tener proveedor y monto.";
                                    return RedirectToAction("Detalle", new { id = SolicitudID });
                                }

                                string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";

                                if (!extensionesPermitidas.Contains(extension))
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "Solo se permiten PDF o imágenes: JPG, PNG o WEBP.";
                                    return RedirectToAction("Detalle", new { id = SolicitudID });
                                }

                                string nombreOriginal = Path.GetFileName(archivo.FileName);
                                string nombreArchivo = $"COM-{folioStr}_Cotizacion_{numeroCotizacion}_{Guid.NewGuid()}{extension}";
                                string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                                using (var stream = archivo.OpenReadStream())
                                {
                                    _sftp.SubirStream(stream, rutaArchivoSftp);
                                }

                                bool esRecomendada = CotizacionRecomendada == numeroCotizacion;

                                string sqlCot = @"
INSERT INTO Compras_Cotizaciones
(
    SolicitudID,
    ArchivoPath,
    MontoTotal,
    FechaEnvioAlUsuario,
    Proveedor,
    ComentariosCompras,
    EsRecomendada,
    NumeroCotizacion,
    NombreArchivoOriginal,
    Extension,
    ContentType,
    TamanoBytes,
    UsuarioSubioID
)
VALUES
(
    @SolicitudID,
    @ArchivoPath,
    @MontoTotal,
    GETDATE(),
    @Proveedor,
    @ComentariosCompras,
    @EsRecomendada,
    @NumeroCotizacion,
    @NombreArchivoOriginal,
    @Extension,
    @ContentType,
    @TamanoBytes,
    @UsuarioSubioID
)";

                                using (var cmdCot = new SqlCommand(sqlCot, conn, trans))
                                {
                                    cmdCot.Parameters.AddWithValue("@SolicitudID", SolicitudID);
                                    cmdCot.Parameters.AddWithValue("@ArchivoPath", rutaArchivoSftp);
                                    var paramMonto = new SqlParameter("@MontoTotal", SqlDbType.Decimal);
                                    paramMonto.Precision = 18;
                                    paramMonto.Scale = 2;
                                    paramMonto.Value = monto.Value;

                                    cmdCot.Parameters.Add(paramMonto);
                                    cmdCot.Parameters.AddWithValue("@Proveedor", proveedor);
                                    cmdCot.Parameters.AddWithValue("@ComentariosCompras", (object?)ComentariosCompras ?? DBNull.Value);
                                    cmdCot.Parameters.AddWithValue("@EsRecomendada", esRecomendada);
                                    cmdCot.Parameters.AddWithValue("@NumeroCotizacion", numeroCotizacion);
                                    cmdCot.Parameters.AddWithValue("@NombreArchivoOriginal", nombreOriginal);
                                    cmdCot.Parameters.AddWithValue("@Extension", extension);
                                    cmdCot.Parameters.AddWithValue("@ContentType", archivo.ContentType ?? "application/octet-stream");
                                    cmdCot.Parameters.AddWithValue("@TamanoBytes", archivo.Length);
                                    cmdCot.Parameters.AddWithValue("@UsuarioSubioID", usuarioId);

                                    await cmdCot.ExecuteNonQueryAsync();
                                }
                            }

                            string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 2,
    FechaCotizacion = GETDATE()
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 1";

                            using (var cmdUpd = new SqlCommand(sqlUpdate, conn, trans))
                            {
                                cmdUpd.Parameters.AddWithValue("@SolicitudID", SolicitudID);

                                int filas = await cmdUpd.ExecuteNonQueryAsync();

                                if (filas == 0)
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "No se pudo actualizar la solicitud. Verifica que siga en estatus 1.";
                                    return RedirectToAction("Detalle", new { id = SolicitudID });
                                }
                            }

                            string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 2, GETDATE(), @Responsable)";

                            using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                            {
                                cmdHist.Parameters.AddWithValue("@SolicitudID", SolicitudID);
                                cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            trans.Commit();

                            await NotificarSolicitante_CotizacionesListasAsync(SolicitudID);

                            TempData["Mensaje"] = "Cotizaciones guardadas correctamente.";
                            return RedirectToAction("Detalle", new { id = SolicitudID });
                        }
                        catch (Exception ex)
                        {
                            trans.Rollback();

                            _logger.LogError(ex, "Error al guardar cotizaciones");

                            TempData["Error"] = ex.Message;

                            return RedirectToAction("Detalle", new { id = SolicitudID });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en ProcesarCotizacion");
                TempData["Error"] = "Error técnico: " + ex.Message;
                return RedirectToAction("Detalle", new { id = SolicitudID });
            }
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

        //METODO PARA ASIGNAR A UN COMPRADOR

        [HttpPost("AsignarComprador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarComprador(int solicitudId, int compradorUsuarioId)
        {
            int usuarioDireccionId = ObtenerUsuarioIdActual();
            if (usuarioDireccionId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                int? empresaActivaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioDireccionId);

                if (!empresaActivaId.HasValue)
                    return Forbid();

                string puesto = "";

                using (var cmdPuesto = new SqlCommand(@"
            SELECT P.Puesto
            FROM Usuarios U
            INNER JOIN Persona P ON U.PersonaID = P.PersonaID
            WHERE U.UsuarioID = @UsuarioID", conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioDireccionId);
                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                if (puesto != "DIRECCION COMPRAS")
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidarComprador = @"
    SELECT COUNT(*)
    FROM Usuarios U
    INNER JOIN Persona P ON U.PersonaID = P.PersonaID
    INNER JOIN UsuariosEmpresas UE
        ON U.UsuarioID = UE.UsuarioID
       AND UE.Activo = 1
       AND UE.EmpresaID = @EmpresaID
    WHERE U.UsuarioID = @CompradorID
      AND P.EsColaboradorActivo = 1
      AND UPPER(P.Puesto) IN ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')";


                        using (var cmdValidar = new SqlCommand(sqlValidarComprador, conn, trans))
                        {
                            cmdValidar.Parameters.AddWithValue("@CompradorID", compradorUsuarioId);
                            cmdValidar.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                            int existe = (int)await cmdValidar.ExecuteScalarAsync();

                            if (existe == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "El usuario seleccionado no pertenece al departamento de Compras.";
                                return RedirectToAction("BandejaCompras");
                            }
                        }

                        int? anteriorId = null;

                        using (var cmdAnterior = new SqlCommand(@"
                    SELECT CompradorAsignadoUsuarioID
                    FROM Compras_Solicitud
                    WHERE SolicitudID = @SolicitudID", conn, trans))
                        {
                            cmdAnterior.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var result = await cmdAnterior.ExecuteScalarAsync();

                            if (result != null && result != DBNull.Value)
                                anteriorId = Convert.ToInt32(result);
                        }
                        string sqlUpdate = @"
    UPDATE Compras_Solicitud
    SET CompradorAsignadoUsuarioID = @NuevoCompradorID,
        FechaAsignacionComprador = GETDATE(),
        UsuarioAsignoCompradorID = @DireccionID
    WHERE SolicitudID = @SolicitudID
      AND EmpresaID = @EmpresaID";

                        using (var cmdUpdate = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue("@NuevoCompradorID", compradorUsuarioId);
                            cmdUpdate.Parameters.AddWithValue("@DireccionID", usuarioDireccionId);
                            cmdUpdate.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdUpdate.Parameters.AddWithValue("@EmpresaID", empresaActivaId.Value);

                            await cmdUpdate.ExecuteNonQueryAsync();
                        }

                        string sqlHist = @"
                    INSERT INTO Compras_Asignaciones_Historico
                    (SolicitudID, UsuarioAsignadoAnteriorID, UsuarioAsignadoNuevoID, UsuarioDireccionID, FechaAsignacion, Comentario)
                    VALUES
                    (@SolicitudID, @AnteriorID, @NuevoID, @DireccionID, GETDATE(), @Comentario)";

                        using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@AnteriorID", (object)anteriorId ?? DBNull.Value);
                            cmdHist.Parameters.AddWithValue("@NuevoID", compradorUsuarioId);
                            cmdHist.Parameters.AddWithValue("@DireccionID", usuarioDireccionId);
                            cmdHist.Parameters.AddWithValue("@Comentario", "Asignación manual desde Dirección Compras");

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        TempData["Mensaje"] = "Comprador asignado correctamente.";
                        return RedirectToAction("BandejaCompras");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al reasignar comprador");
                        TempData["Error"] = "No se pudo reasignar comprador.";

                        return RedirectToAction("BandejaCompras");
                    }
                }
            }
        }


        //metodo post para CP

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarDictamen(DictamenPresupuestalVm vm)
        {
            if (vm.SolicitudID == 0)
                return BadRequest("Error: No se recibió el ID de la solicitud.");

            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            string responsable = User.Identity?.Name ?? "Sistema";

            vm.TipoGasto = vm.TipoGasto?.Trim();
            vm.NumeroRequisicion = vm.NumeroRequisicion?.Trim();
            vm.Observaciones = vm.Observaciones?.Trim();

            if (vm.Pasa && string.IsNullOrWhiteSpace(vm.TipoGasto))
            {
                TempData["Error"] = "Debes seleccionar el tipo de gasto.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            if (vm.Pasa && vm.TipoGasto == "REQUISICION" && string.IsNullOrWhiteSpace(vm.NumeroRequisicion))
            {
                TempData["Error"] = "Debes capturar el número de requisición.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            if (!vm.Pasa && string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] = "Debes capturar el motivo del rechazo.";
                return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"
                    );

                if (!esControlPresupuestal)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int nuevoEstatus = vm.Pasa ? 4 : 9;

                        string queryUpdate = @"
UPDATE Compras_Solicitud 
SET EstatusID = @Est,
    FechaDictamen = GETDATE(),
    TipoGasto = @Tipo,
    DentroPresupuesto = @Dentro,
    NumeroRequisicion = @Requi,
    ObservacionesPresupuesto = @Obs
WHERE SolicitudID = @Sid
  AND EstatusID = 3";

                        using (var cmd = new SqlCommand(queryUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmd.Parameters.AddWithValue("@Sid", vm.SolicitudID);
                            cmd.Parameters.AddWithValue("@Tipo", vm.Pasa ? (object)(vm.TipoGasto ?? "") : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Dentro", vm.Pasa ? (object)vm.DentroDePresupuesto : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Requi",
                                (vm.Pasa && vm.TipoGasto == "REQUISICION")
                                    ? (object)(vm.NumeroRequisicion ?? "")
                                    : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Obs", (object?)vm.Observaciones ?? DBNull.Value);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está pendiente de dictamen.";
                                return RedirectToAction("BandejaPresupuestos");
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@Sid, @Est, GETDATE(), @Resp)";

                        using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@Sid", vm.SolicitudID);
                            cmdHist.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmdHist.Parameters.AddWithValue("@Resp", responsable);

                            await cmdHist.ExecuteNonQueryAsync();
                        }
                        trans.Commit();

                        await NotificarSolicitante_DictamenPresupuestalAsync(
                            vm.SolicitudID,
                            vm.Pasa,
                            vm.Observaciones
                        );

                        if (vm.Pasa)
                        {
                            await NotificarCompras_DictamenAprobadoAsync(vm.SolicitudID);
                        }

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
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var vm = new ControlPresupuestalVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool esControlPresupuestal =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "CONTROL PRESUPUESTAL",
                        "PRESUPUESTOS",
                        "CIS"
                       
                    );

                if (!esControlPresupuestal)
                    return Forbid();

                string sqlPendientes = @"
SELECT S.SolicitudID, S.Folio, 
       (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
       S.TipoCompra, S.FechaCreacion, Est.Nombre AS Estatus
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
WHERE S.EstatusID = 3
ORDER BY S.FechaCreacion ASC";

                using (var cmd = new SqlCommand(sqlPendientes, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        vm.Pendientes.Add(new BandejaComprasVm
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

                string sqlHistorico = @"
SELECT S.SolicitudID, S.Folio,
       (P.Nombre + ' ' + P.ApellidoPaterno) AS Solicitante,
       S.TipoCompra,
       ISNULL(C.MontoTotal, 0) AS MontoTotal,
       CASE 
    WHEN S.EstatusID >= 4 AND S.EstatusID <> 9 THEN 'Aprobado'
    WHEN S.EstatusID = 9 THEN 'Rechazado'
    ELSE 'Otro'
END AS Resultado,
       S.DentroPresupuesto,
       S.NumeroRequisicion,
       S.ObservacionesPresupuesto,
       S.FechaDictamen
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.FechaDictamen IS NOT NULL
  AND S.EstatusID IN (4, 5, 6, 7, 8, 9, 10, 11)
ORDER BY S.FechaDictamen DESC";

                using (var cmd = new SqlCommand(sqlHistorico, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        vm.Historico.Add(new HistoricoPresupuestoVm
                        {
                            SolicitudID = (int)reader["SolicitudID"],
                            Folio = reader["Folio"].ToString(),
                            Solicitante = reader["Solicitante"].ToString(),
                            TipoCompra = reader["TipoCompra"].ToString(),
                            MontoTotal = Convert.ToDecimal(reader["MontoTotal"]),
                            Resultado = reader["Resultado"].ToString(),
                            DentroPresupuesto = reader["DentroPresupuesto"] as bool?,
                            NumeroRequisicion = reader["NumeroRequisicion"]?.ToString(),
                            Observaciones = reader["ObservacionesPresupuesto"]?.ToString(),
                            FechaDictamen = reader["FechaDictamen"] == DBNull.Value
                                ? DateTime.MinValue
                                : (DateTime)reader["FechaDictamen"]
                        });
                    }
                }
            }

            vm.TotalPendientes = vm.Pendientes.Count;
            vm.TotalAprobadas = vm.Historico.Count(x => x.Resultado == "Aprobado");
            vm.TotalRechazadas = vm.Historico.Count(x => x.Resultado == "Rechazado");
            vm.MontoAprobado = vm.Historico.Where(x => x.Resultado == "Aprobado").Sum(x => x.MontoTotal);
            vm.MontoRechazado = vm.Historico.Where(x => x.Resultado == "Rechazado").Sum(x => x.MontoTotal);

            return View(vm);
        }

        // Lógica para detectar si Control Presupuestal se pasó de sus 24h
        public string ObtenerAlertaPresupuesto(DateTime fechaCotizacion)
        {
            var horasTranscurridas = (DateTime.Now - fechaCotizacion).TotalHours;

            if (horasTranscurridas > 48) return "text-danger font-weight-bold"; // Muy retrasado
            if (horasTranscurridas > 24) return "text-warning"; // Al límite
            return "text-success"; // A tiempo
        }


      

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

        //HELPERS PARA OBTENER EMPRESAS DEL USUARIO
        private async Task<int?> ObtenerEmpresaActivaUsuarioAsync(
    SqlConnection conn,
    int usuarioId)
        {
            string sql = @"
        SELECT TOP 1 UE.EmpresaID
        FROM UsuariosEmpresas UE
        INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
        WHERE UE.UsuarioID = @UsuarioID
          AND UE.Activo = 1
          AND E.Activa = 1
        ORDER BY UE.EmpresaID";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }

        private async Task<List<int>> ObtenerEmpresasActivasUsuarioAsync(SqlConnection conn, int usuarioId)
        {
            var empresas = new List<int>();

            string sql = @"
        SELECT UE.EmpresaID
        FROM UsuariosEmpresas UE
        INNER JOIN Empresas E ON UE.EmpresaID = E.EmpresaID
        WHERE UE.UsuarioID = @UsuarioID
          AND UE.Activo = 1
          AND E.Activa = 1";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empresas.Add((int)reader["EmpresaID"]);
                    }
                }
            }

            return empresas;
        }

        private async Task<int?> ObtenerDepartamentoEmpresaIdAsync(
    SqlConnection conn,
    int empresaId,
    string nombreDepartamento)
        {
            string sql = @"
        SELECT TOP 1 DE.DepartamentoEmpresaID
        FROM DepartamentoEmpresa DE
        INNER JOIN Departamentos D ON DE.DepartamentoID = D.DepartamentoID
        WHERE DE.EmpresaID = @EmpresaID
          AND D.Activo = 1
          AND UPPER(D.NombreDepartamento) = UPPER(@NombreDepartamento)";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                cmd.Parameters.AddWithValue("@NombreDepartamento", nombreDepartamento);

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToInt32(result);
            }
        }


        //METODO PARA REGISTRAR OC
        [HttpGet("RegistrarOC/{id}")]
        public async Task<IActionResult> RegistrarOC(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new RegistrarOrdenCompraVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                string sql = @"
        SELECT
            S.SolicitudID,
            S.Folio,
            E.Nombre AS Empresa,
            P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
        FROM Compras_Solicitud S
        INNER JOIN Empresas E
            ON S.EmpresaID = E.EmpresaID
        INNER JOIN Usuarios U
            ON S.UsuarioID = U.UsuarioID
        INNER JOIN Persona P
            ON U.PersonaID = P.PersonaID
        WHERE S.SolicitudID = @SolicitudID
          AND S.EstatusID = 4";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                            return NotFound();

                        vm.SolicitudID = (int)reader["SolicitudID"];
                        vm.Folio = reader["Folio"].ToString();
                        vm.Empresa = reader["Empresa"].ToString();
                        vm.Solicitante = reader["Solicitante"].ToString();
                    }
                }
            }

            return View(vm);
        }


        [HttpPost("RegistrarOC")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarOC(int solicitudId, string numeroOC, string? proveedor, string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            numeroOC = numeroOC?.Trim();
            proveedor = proveedor?.Trim();
            comentarios = comentarios?.Trim();

            if (string.IsNullOrWhiteSpace(numeroOC))
            {
                TempData["Error"] = "Debes capturar el número de O.C.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidarSolicitud = @"
SELECT COUNT(*)
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 4
  AND CotizacionSeleccionadaID IS NOT NULL";

                        using (var cmdValSol = new SqlCommand(sqlValidarSolicitud, conn, trans))
                        {
                            cmdValSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int validaSolicitud = Convert.ToInt32(await cmdValSol.ExecuteScalarAsync());

                            if (validaSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud no está autorizada para registrar O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlExiste = @"
SELECT COUNT(*)
FROM Compras_OrdenCompra
WHERE SolicitudID = @SolicitudID
  AND Activo = 1";

                        using (var cmdExiste = new SqlCommand(sqlExiste, conn, trans))
                        {
                            cmdExiste.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int existe = Convert.ToInt32(await cmdExiste.ExecuteScalarAsync());

                            if (existe > 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "Esta solicitud ya tiene una O.C. registrada.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlInsert = @"
INSERT INTO Compras_OrdenCompra
(SolicitudID, NumeroOC, Proveedor, FechaOC, Comentarios, UsuarioOCID)
VALUES
(@SolicitudID, @NumeroOC, @Proveedor, GETDATE(), @Comentarios, @UsuarioOCID)";

                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@NumeroOC", numeroOC);
                            cmd.Parameters.AddWithValue("@Proveedor", (object?)proveedor ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@UsuarioOCID", usuarioId);

                            int filasInsert = await cmd.ExecuteNonQueryAsync();

                            if (filasInsert == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo registrar la O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 4, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_OCRegistradaAsync(solicitudId);

                        TempData["Mensaje"] = "O.C. registrada correctamente. Ahora puedes enviarla al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });

                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al registrar O.C.");

                        TempData["Error"] = "No se pudo registrar la O.C.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }



        [HttpPost("EnviarOCProveedor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarOCProveedor(
     int solicitudId,
     DateTime fechaEstimadaEntrega,
     string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();

            if (fechaEstimadaEntrega.Date < DateTime.Today)
            {
                TempData["Error"] = "La fecha estimada de entrega no puede ser anterior a hoy.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidar = @"
SELECT COUNT(*)
FROM Compras_Solicitud S
INNER JOIN Compras_OrdenCompra OC
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 4
  AND OC.FechaEnvioProveedor IS NULL";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int valido = Convert.ToInt32(await cmdVal.ExecuteScalarAsync());

                            if (valido == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud no está disponible para enviar la O.C. al proveedor.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdateOC = @"
UPDATE Compras_OrdenCompra
SET FechaEnvioProveedor = GETDATE(),
    FechaEstimadaEntrega = @FechaEstimadaEntrega,
    UsuarioEnvioProveedorID = @UsuarioID,
    Comentarios = 
        CASE 
            WHEN @Comentarios IS NULL OR @Comentarios = '' 
            THEN Comentarios
            ELSE @Comentarios
        END
WHERE SolicitudID = @SolicitudID
  AND Activo = 1
  AND FechaEnvioProveedor IS NULL";

                        using (var cmd = new SqlCommand(sqlUpdateOC, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@FechaEstimadaEntrega", fechaEstimadaEntrega);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                            int filasOC = await cmd.ExecuteNonQueryAsync();

                            if (filasOC == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar la O.C.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = 5
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 4";

                        using (var cmd = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasSolicitud = await cmd.ExecuteNonQueryAsync();

                            if (filasSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar el estatus de la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 5, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_OCEnviadaProveedorAsync(solicitudId);

                        TempData["Mensaje"] = "O.C. marcada como enviada al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al enviar O.C. al proveedor");

                        TempData["Error"] = "No se pudo registrar el envío al proveedor.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }

        [HttpPost("RegistrarRecepcionAlmacen")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRecepcionAlmacen(
     int solicitudId,
     string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceAlmacen =
                    await UsuarioPerteneceADepartamentoAsync(
                        conn,
                        usuarioId,
                        "ALMACEN",
                        "ALMACÉN"
                    );

                if (!perteneceAlmacen)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int ordenCompraId;

                        string sqlValidar = @"
SELECT TOP 1 OC.OrdenCompraID
FROM Compras_Solicitud S
INNER JOIN Compras_OrdenCompra OC
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
   AND OC.FechaEnvioProveedor IS NOT NULL
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 5
  AND NOT EXISTS (
      SELECT 1
      FROM Compras_Recepciones R
      WHERE R.SolicitudID = S.SolicitudID
        AND R.Activo = 1
  )
ORDER BY OC.OrdenCompraID DESC";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var result = await cmdVal.ExecuteScalarAsync();

                            if (result == null || result == DBNull.Value)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud no está disponible para recepción en almacén.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            ordenCompraId = Convert.ToInt32(result);
                        }

                        string sqlInsert = @"
INSERT INTO Compras_Recepciones
(
    SolicitudID,
    OrdenCompraID,
    FechaRecepcion,
    UsuarioRecibioID,
    Comentarios
)
VALUES
(
    @SolicitudID,
    @OrdenCompraID,
    GETDATE(),
    @UsuarioID,
    @Comentarios
)";

                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@OrdenCompraID", ordenCompraId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                            int filasInsert = await cmd.ExecuteNonQueryAsync();

                            if (filasInsert == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo registrar la recepción.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = 6
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 5";

                        using (var cmd = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasSolicitud = await cmd.ExecuteNonQueryAsync();

                            if (filasSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar el estatus de la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 6, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_MaterialRecibidoAlmacenAsync(solicitudId);

                        TempData["Mensaje"] = "Recepción en almacén registrada correctamente.";
                        return RedirectToAction("BandejaAlmacen");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al registrar recepción en almacén");

                        TempData["Error"] = "No se pudo registrar la recepción en almacén.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }

        [HttpPost("RegistrarEntregaUsuario")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarEntregaUsuario(
    int solicitudId,
    string nombreRecibe,
    string? comentarios)
        {
            int usuarioEntregaId = ObtenerUsuarioIdActual();
            if (usuarioEntregaId == 0) return Unauthorized();

            if (string.IsNullOrWhiteSpace(nombreRecibe))
            {
                TempData["Error"] = "Debes capturar quién recibe la mercancía.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int usuarioSolicitanteId;

                        string sqlSolicitud = @"
                    SELECT UsuarioID
                    FROM Compras_Solicitud
                    WHERE SolicitudID = @SolicitudID
                      AND EstatusID = 6";

                        using (var cmdSol = new SqlCommand(sqlSolicitud, conn, trans))
                        {
                            cmdSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var result = await cmdSol.ExecuteScalarAsync();

                            if (result == null || result == DBNull.Value)
                            {
                                trans.Rollback();
                                TempData["Error"] = "Solo puedes entregar al usuario cuando la mercancía ya fue recibida en almacén.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            usuarioSolicitanteId = Convert.ToInt32(result);
                        }

                        string sqlInsert = @"
INSERT INTO Compras_EntregasUsuario
(
    SolicitudID,
    FechaEntrega,
    UsuarioEntregaID,
    UsuarioRecibeID,
    NombreRecibe,
    Comentarios,
    Activo
)
VALUES
(
    @SolicitudID,
    GETDATE(),
    @UsuarioEntregaID,
    @UsuarioRecibeID,
    @NombreRecibe,
    @Comentarios,
    1
)";

                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@UsuarioEntregaID", usuarioEntregaId);
                            cmd.Parameters.AddWithValue("@UsuarioRecibeID", usuarioSolicitanteId);
                            cmd.Parameters.AddWithValue("@NombreRecibe", nombreRecibe);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                            int filasSolicitud = await cmd.ExecuteNonQueryAsync();

                            if (filasSolicitud == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar el estatus de la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdate = @"
                    UPDATE Compras_Solicitud
                    SET EstatusID = 8
                    WHERE SolicitudID = @SolicitudID
                      AND EstatusID = 6";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        string sqlHistoricoEntrega = @"
                    INSERT INTO Compras_Historico_Pasos
                    (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                    VALUES
                    (@SolicitudID, 7, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistoricoEntrega, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        string sqlHistoricoCierre = @"
                    INSERT INTO Compras_Historico_Pasos
                    (SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
                    VALUES
                    (@SolicitudID, 8, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistoricoCierre, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarCuentasPorPagar_MaterialEntregadoAsync(solicitudId);

                        TempData["Mensaje"] = "Mercancía entregada al usuario. Solicitud enviada a Cuentas por Pagar.";
                        return RedirectToAction("Detalle", new { id = solicitudId });


                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al registrar entrega al usuario");

                        TempData["Error"] = "No se pudo registrar la entrega al usuario: " + ex.Message;

                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }




        [HttpGet("Seguimiento")]
        public async Task<IActionResult> Seguimiento(
    int? estatus,
    string? departamento,
    string? comprador,
    bool? soloRetrasadas
)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new SeguimientoDireccionVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // VALIDAR PUESTO
                string puesto = "";

                string sqlPuesto = @"
        SELECT P.Puesto
        FROM Usuarios U
        INNER JOIN Persona P ON U.PersonaID = P.PersonaID
        WHERE U.UsuarioID = @UsuarioID";

                using (var cmdPuesto = new SqlCommand(sqlPuesto, conn))
                {
                    cmdPuesto.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString() ?? "";
                }

                // SOLO DIRECCIÓN
                puesto = (puesto ?? "").ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                {
                    return Forbid();
                }

                // EJECUTAR SP
                using (var cmd = new SqlCommand("sp_Compras_SeguimientoDireccion", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Solicitudes.Add(new SeguimientoCompraItemVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"].ToString(),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],

                                Solicitante = reader["Solicitante"].ToString(),
                                Departamento = reader["Departamento"].ToString(),
                                Empresa = reader["Empresa"].ToString(),

                                TipoCompra = reader["TipoCompra"].ToString(),
                                Urgencia = reader["Urgencia"].ToString(),

                                EstatusID = (int)reader["EstatusID"],
                                Estatus = reader["Estatus"].ToString(),

                                CompradorAsignado = reader["CompradorAsignado"].ToString(),

                                FechaAsignacionComprador =
                                    reader["FechaAsignacionComprador"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaAsignacionComprador"],

                                FechaUltimoMovimiento =
                                    reader["FechaUltimoMovimiento"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaUltimoMovimiento"],

                                DiasEnEstatus = Convert.ToInt32(reader["DiasEnEstatus"]),
                           
                                DiasCotizando = Convert.ToInt32(reader["DiasCotizando"]),

                                MontoCotizado = Convert.ToDecimal(reader["MontoCotizado"]),

                                DiasPermitidos = Convert.ToInt32(reader["DiasPermitidos"]),
                                DiasHabilesTranscurridos = Convert.ToInt32(reader["DiasHabilesTranscurridos"]),
                                SemaforoTexto = reader["SemaforoTexto"].ToString(),

                                DiasCompras = reader["DiasCompras"] == DBNull.Value   ? 0   : Convert.ToInt32(reader["DiasCompras"]),

                                DiasPresupuesto = reader["DiasPresupuesto"] == DBNull.Value    ? 0    : Convert.ToInt32(reader["DiasPresupuesto"]),

                                DiasOC = reader["DiasOC"] == DBNull.Value    ? 0    : Convert.ToInt32(reader["DiasOC"]),

                                DiasProveedor = reader["DiasProveedor"] == DBNull.Value    ? 0    : Convert.ToInt32(reader["DiasProveedor"]),

                                DiasAlmacen = reader["DiasAlmacen"] == DBNull.Value   ? 0    : Convert.ToInt32(reader["DiasAlmacen"]),

                                DiasCxP = reader["DiasCxP"] == DBNull.Value    ? 0    : Convert.ToInt32(reader["DiasCxP"]),
                            });
                        }
                    }
                }
            }
            if (estatus.HasValue)
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x => x.EstatusID == estatus.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(departamento))
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x =>
                        x.Departamento.Contains(
                            departamento,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(comprador))
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x =>
                        x.CompradorAsignado.Contains(
                            comprador,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (soloRetrasadas == true)
            {
                vm.Solicitudes = vm.Solicitudes
                    .Where(x => x.SemaforoTexto == "Retrasada")
                    .ToList();
            }


            return View(vm);
        }


        [HttpGet("BandejaAlmacen")]
        public async Task<IActionResult> BandejaAlmacen()
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var vm = new BandejaAlmacenVm();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceAlmacen =
    await UsuarioPerteneceADepartamentoAsync(
        conn,
        usuarioId,
        "ALMACEN",
        "ALMACÉN"
    );

                if (!perteneceAlmacen)
                    return Forbid();


                
                string sql = @"
        SELECT
            S.SolicitudID,
            ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
            P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
            ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
            E.Nombre AS Empresa,
            OC.NumeroOC,
            OC.Proveedor,
            S.FechaCreacion,
            OC.FechaEnvioProveedor,
            OC.FechaEstimadaEntrega,
            CE.Nombre AS Estatus,
            S.EstatusID,
            DATEDIFF(DAY, OC.FechaEnvioProveedor, GETDATE()) AS DiasDesdeEnvioProveedor
        FROM Compras_Solicitud S
        INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
        INNER JOIN Persona P ON U.PersonaID = P.PersonaID
        INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
        INNER JOIN Cat_EstatusCompra CE ON S.EstatusID = CE.EstatusID
        INNER JOIN Compras_OrdenCompra OC 
            ON S.SolicitudID = OC.SolicitudID
           AND OC.Activo = 1
           AND OC.FechaEnvioProveedor IS NOT NULL
        LEFT JOIN EmpleadoDepartamentos ED 
            ON U.UsuarioID = ED.UsuarioID 
           AND ED.Activo = 1
        LEFT JOIN Departamentos D 
            ON ED.DepartamentoID = D.DepartamentoID
       WHERE S.EstatusID = 5
  AND NOT EXISTS (
      SELECT 1
      FROM Compras_Recepciones R
      WHERE R.SolicitudID = S.SolicitudID
        AND R.Activo = 1
  )
        ORDER BY OC.FechaEstimadaEntrega ASC, OC.FechaEnvioProveedor ASC";

                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        vm.PendientesRecepcion.Add(new AlmacenItemVm
                        {
                            SolicitudID = (int)reader["SolicitudID"],
                            Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString(),
                            Solicitante = reader["Solicitante"] == DBNull.Value ? "" : reader["Solicitante"].ToString(),
                            Departamento = reader["Departamento"] == DBNull.Value ? "" : reader["Departamento"].ToString(),
                            Empresa = reader["Empresa"] == DBNull.Value ? "" : reader["Empresa"].ToString(),
                            NumeroOC = reader["NumeroOC"] == DBNull.Value ? "" : reader["NumeroOC"].ToString(),
                            Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString(),
                            FechaCreacion = (DateTime)reader["FechaCreacion"],
                            FechaEnvioProveedor = reader["FechaEnvioProveedor"] == DBNull.Value
                                ? null
                                : (DateTime?)reader["FechaEnvioProveedor"],
                            FechaEstimadaEntrega = reader["FechaEstimadaEntrega"] == DBNull.Value
                                ? null
                                : (DateTime?)reader["FechaEstimadaEntrega"],
                            Estatus = reader["Estatus"] == DBNull.Value ? "" : reader["Estatus"].ToString(),
                            EstatusID = (int)reader["EstatusID"],
                            DiasDesdeEnvioProveedor = reader["DiasDesdeEnvioProveedor"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["DiasDesdeEnvioProveedor"])
                        });
                    }
                }
            }

            return View(vm);
        }


        //HELPER PARA SABER A QUE DEPARTAMENTO PERTENECE UN USUARIO
        private async Task<bool> UsuarioPerteneceADepartamentoAsync(
    SqlConnection conn,
    int usuarioId,
    params string[] palabrasClaveDepartamento)
        {
            string sql = @"
SELECT D.NombreDepartamento
FROM EmpleadoDepartamentos ED
INNER JOIN Departamentos D
    ON ED.DepartamentoID = D.DepartamentoID
WHERE ED.UsuarioID = @UsuarioID
  AND ED.Activo = 1
  AND D.Activo = 1";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string depto = reader["NombreDepartamento"]?.ToString()?.ToUpper().Trim() ?? "";

                        foreach (var palabra in palabrasClaveDepartamento)
                        {
                            if (depto.Contains(palabra.ToUpper()))
                                return true;
                        }
                    }
                }
            }

            return false;
        }


        [HttpPost("SeleccionarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarCotizacion(
    int solicitudId,
    int cotizacionId,
    string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sqlValidar = @"
SELECT COUNT(*)
FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C
    ON S.SolicitudID = C.SolicitudID
WHERE S.SolicitudID = @SolicitudID
  AND C.CotizacionID = @CotizacionID
  AND S.UsuarioID = @UsuarioID
  AND S.EstatusID = 2
  AND S.CotizacionSeleccionadaID IS NULL";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdVal.Parameters.AddWithValue("@CotizacionID", cotizacionId);
                            cmdVal.Parameters.AddWithValue("@UsuarioID", usuarioId);

                            int valido = Convert.ToInt32(await cmdVal.ExecuteScalarAsync());

                            if (valido == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No puedes seleccionar esta cotización.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }


                        comentarios = comentarios?.Trim();
                        if (!string.IsNullOrWhiteSpace(comentarios) && comentarios.Length > 500)
                        {
                            trans.Rollback();
                            TempData["Error"] = "El comentario no puede superar los 500 caracteres.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }

                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET CotizacionSeleccionadaID = @CotizacionID,
    FechaSeleccionCotizacion = GETDATE(),
    UsuarioSeleccionCotizacionID = @UsuarioID,
    ComentariosSeleccionUsuario = @Comentarios,
    EstatusID = 3
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 2
  AND CotizacionSeleccionadaID IS NULL";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@CotizacionID", cotizacionId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La cotización ya fue seleccionada o la solicitud cambió de estatus.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 3, GETDATE(), @Responsable)";

                        using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarControlPresupuestal_CotizacionSeleccionadaAsync(solicitudId);

                        TempData["Mensaje"] = "Cotización seleccionada correctamente. La solicitud pasó a Control Presupuestal.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al seleccionar cotización");

                        TempData["Error"] = "No se pudo seleccionar la cotización.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }
        }

        //METODOS PARA CUANETAS POR PAGAR

        [HttpGet("BandejaCuentasPorPagar")]
        public async Task<IActionResult> BandejaCuentasPorPagar()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceCxP = await UsuarioPerteneceADepartamentoAsync(
                conn,
                usuarioId,
                "CUENTAS POR PAGAR",
                "CXP"
            );

            if (!perteneceCxP)
                return Forbid();

            var vm = new CuentasPorPagarVm();

            string sql = @"
SELECT
    S.SolicitudID,
    S.Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    C.Proveedor,
    C.MontoTotal,
    OC.NumeroOC,
    EU.FechaEntrega,
    EU.NombreRecibe,
    S.TipoGasto,
    S.NumeroRequisicion,
    S.FechaDictamen
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN EmpleadoDepartamentos ED ON U.UsuarioID = ED.UsuarioID AND ED.Activo = 1
LEFT JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
OUTER APPLY (
    SELECT TOP 1
        NumeroOC,
        Proveedor
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC

OUTER APPLY (
    SELECT TOP 1
        FechaEntrega,
        NombreRecibe
    FROM Compras_EntregasUsuario
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY EntregaID DESC
) EU
WHERE S.EstatusID = 8
ORDER BY EU.FechaEntrega ASC";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                vm.Pendientes.Add(new CuentasPorPagarItemVm
                {
                    SolicitudID = (int)reader["SolicitudID"],
                    Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString(),
                    Solicitante = reader["Solicitante"] == DBNull.Value ? "" : reader["Solicitante"].ToString(),
                    Empresa = reader["Empresa"] == DBNull.Value ? "" : reader["Empresa"].ToString(),
                    Departamento = reader["Departamento"] == DBNull.Value ? "" : reader["Departamento"].ToString(),
                    Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString(),
                    MontoTotal = reader["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["MontoTotal"]),
                    NumeroOC = reader["NumeroOC"] == DBNull.Value ? "" : reader["NumeroOC"].ToString(),
                    FechaEntregaUsuario = reader["FechaEntrega"] == DBNull.Value ? null : (DateTime?)reader["FechaEntrega"],
                    NombreRecibeUsuario = reader["NombreRecibe"] == DBNull.Value ? "" : reader["NombreRecibe"].ToString(),
                    TipoGasto = reader["TipoGasto"] == DBNull.Value ? "" : reader["TipoGasto"].ToString(),
                    NumeroRequisicion = reader["NumeroRequisicion"] == DBNull.Value ? "" : reader["NumeroRequisicion"].ToString(),
                    FechaDictamen = reader["FechaDictamen"] == DBNull.Value ? null : (DateTime?)reader["FechaDictamen"]
                });
            }

            return View(vm);
        }

        [HttpGet("DetalleCuentasPorPagar/{id}")]
        public async Task<IActionResult> DetalleCuentasPorPagar(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceCxP = await UsuarioPerteneceADepartamentoAsync(
                    conn,
                    usuarioId,
                    "CUENTAS POR PAGAR",
                    "CXP"
                );

                if (!perteneceCxP)
                    return Forbid();
            }

            var resultado = await Detalle(id);

            if (resultado is ViewResult viewResult && viewResult.Model is DetalleCompraVm vm)
            {
                if (vm.EstatusID != 8)
                {
                    TempData["Error"] = "Esta solicitud no está pendiente de Cuentas por Pagar.";
                    return RedirectToAction("BandejaCuentasPorPagar");
                }

                return View(vm);
            }

            return resultado;
        }

        [HttpPost("ValidarCuentasPorPagar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarCuentasPorPagar(
    int solicitudId,
    bool aceptado,
    string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            bool perteneceCxP = await UsuarioPerteneceADepartamentoAsync(
                conn,
                usuarioId,
                "CUENTAS POR PAGAR",
                "CXP"
            );

            if (!perteneceCxP)
                return Forbid();

            using var trans = conn.BeginTransaction();

            try
            {
                int nuevoEstatus = aceptado ? 10 : 11;

                string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = @EstatusID,
    ObservacionesPresupuesto = 
        CASE 
            WHEN @Comentarios IS NULL OR @Comentarios = '' 
            THEN ObservacionesPresupuesto
            ELSE @Comentarios
        END
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 8";

                using var cmd = new SqlCommand(sqlUpdate, conn, trans);
                cmd.Parameters.AddWithValue("@EstatusID", nuevoEstatus);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                int filas = await cmd.ExecuteNonQueryAsync();

                if (filas == 0)
                {
                    trans.Rollback();
                    TempData["Error"] = "La solicitud ya no está pendiente de Cuentas por Pagar.";
                    return RedirectToAction("BandejaCuentasPorPagar");
                }

                string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, @EstatusID, GETDATE(), @Responsable)";

                using var cmdHist = new SqlCommand(sqlHist, conn, trans);
                cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdHist.Parameters.AddWithValue("@EstatusID", nuevoEstatus);
                cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");

                await cmdHist.ExecuteNonQueryAsync();

                trans.Commit();

                await NotificarSolicitanteYCompras_CxPAsync(
                    solicitudId,
                    aceptado,
                    comentarios
                );

                TempData["Mensaje"] = aceptado
                    ? "Solicitud cerrada correctamente por Cuentas por Pagar."
                    : "Solicitud rechazada por Cuentas por Pagar.";
                return RedirectToAction("BandejaCuentasPorPagar");
            }
            catch
            {
                trans.Rollback();
                TempData["Error"] = "No se pudo validar la solicitud.";
                return RedirectToAction("BandejaCuentasPorPagar");
            }
        }


        //METODOS PARA LOS CORREOS
        private async Task NotificarComprador_NuevaSolicitudAsync(int solicitudId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
SELECT 
    S.SolicitudID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoCompra,
    S.FechaCreacion,
    S.ComentariosExtra,
    S.CompradorAsignadoUsuarioID,
    PComprador.PersonaID AS PersonaCompradorID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    U.Descripcion AS Urgencia
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante ON USolicitante.PersonaID = PSolicitante.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
INNER JOIN Cat_Urgencia U ON S.UrgenciaID = U.UrgenciaID
LEFT JOIN Usuarios UComprador ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador ON UComprador.PersonaID = PComprador.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                        using (var rd = await cmd.ExecuteReaderAsync())
                        {
                            if (!await rd.ReadAsync())
                                return;

                            if (rd["PersonaCompradorID"] == DBNull.Value)
                                return;

                            int personaCompradorId = Convert.ToInt32(rd["PersonaCompradorID"]);

                            string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                            string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                            string empresa = rd["Empresa"]?.ToString() ?? "";
                            string tipoCompra = rd["TipoCompra"]?.ToString() ?? "";
                            string urgencia = rd["Urgencia"]?.ToString() ?? "";
                            DateTime fechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]);
                            string comentarios = rd["ComentariosExtra"] == DBNull.Value ? "" : rd["ComentariosExtra"].ToString();

                            string asunto = $"Nueva solicitud de compra asignada - {folio}";

                            string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Nueva solicitud de compra</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Se te ha asignado una nueva solicitud de compra.</p>

      <div style='background:#f8f9fa; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Tipo:</strong> {System.Net.WebUtility.HtmlEncode(tipoCompra)}</p>
        <p style='margin:0 0 6px;'><strong>Urgencia:</strong> {System.Net.WebUtility.HtmlEncode(urgencia)}</p>
        <p style='margin:0;'><strong>Fecha:</strong> {fechaCreacion:dd/MM/yyyy HH:mm}</p>
      </div>

      {(string.IsNullOrWhiteSpace(comentarios) ? "" : $"<p><strong>Comentarios:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>")}

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, para cotizar la solicitud.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                            await _notif.EnviarABccPersonasAsync(
                                new List<int> { personaCompradorId },
                                asunto,
                                html
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de nueva solicitud de compra. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarSolicitante_CotizacionesListasAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    S.SolicitudID,
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.UsuarioID,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";

                string asunto = $"Cotizaciones listas para revisión - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Cotizaciones listas</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Compras ha cargado cotizaciones para tu solicitud.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, para revisar y seleccionar la cotización correspondiente.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de cotizaciones listas. SolicitudID={SolicitudID}", solicitudId);
            }
        }



        private async Task NotificarControlPresupuestal_CotizacionSeleccionadaAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();

                string sqlPersonas = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED ON U.UsuarioID = ED.UsuarioID AND ED.Activo = 1
INNER JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID AND D.Activo = 1
WHERE P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> ''
  AND (
        UPPER(D.NombreDepartamento) LIKE '%CONTROL PRESUPUESTAL%'
        OR UPPER(D.NombreDepartamento) LIKE '%PRESUPUESTOS%'
        OR UPPER(D.NombreDepartamento) LIKE '%CIS%'
      )";

                using (var cmdPersonas = new SqlCommand(sqlPersonas, conn))
                using (var rdPersonas = await cmdPersonas.ExecuteReaderAsync())
                {
                    while (await rdPersonas.ReadAsync())
                    {
                        personaIds.Add(Convert.ToInt32(rdPersonas["PersonaID"]));
                    }
                }

                if (!personaIds.Any())
                    return;

                string sqlSolicitud = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    S.TipoCompra,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "";
                string empresa = "";
                string tipoCompra = "";
                string proveedor = "";
                decimal monto = 0;

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        folio = rd["Folio"]?.ToString() ?? folio;
                        solicitante = rd["Solicitante"]?.ToString() ?? "";
                        empresa = rd["Empresa"]?.ToString() ?? "";
                        tipoCompra = rd["TipoCompra"]?.ToString() ?? "";
                        proveedor = rd["Proveedor"]?.ToString() ?? "";
                        monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                    }
                }

                string asunto = $"Solicitud pendiente de dictamen presupuestal - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0891b2; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Dictamen presupuestal pendiente</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>El solicitante ha seleccionado una cotización. La solicitud está lista para revisión de Control Presupuestal.</p>

      <div style='background:#f0f9ff; border-left:4px solid #0891b2; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Tipo:</strong> {System.Net.WebUtility.HtmlEncode(tipoCompra)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Monto:</strong> {monto:C}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, bandeja de <strong>Control Presupuestal</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a Control Presupuestal. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarSolicitante_DictamenPresupuestalAsync(
    int solicitudId,
    bool aprobado,
    string? motivo)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoGasto,
    S.DentroPresupuesto,
    S.NumeroRequisicion,
    S.ObservacionesPresupuesto,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";

                string tipoGasto = rd["TipoGasto"] == DBNull.Value ? "" : rd["TipoGasto"].ToString();
                bool? dentroPresupuesto = rd["DentroPresupuesto"] == DBNull.Value ? null : (bool?)rd["DentroPresupuesto"];
                string requisicion = rd["NumeroRequisicion"] == DBNull.Value ? "" : rd["NumeroRequisicion"].ToString();
                string observaciones = rd["ObservacionesPresupuesto"] == DBNull.Value ? motivo ?? "" : rd["ObservacionesPresupuesto"].ToString();

                string estadoTexto = aprobado ? "APROBADA" : "RECHAZADA";
                string color = aprobado ? "#16a34a" : "#dc2626";
                string fondo = aprobado ? "#f0fdf4" : "#fef2f2";

                string detalleAprobacion = "";

                if (aprobado)
                {
                    detalleAprobacion = $@"
              <div style='background:{fondo}; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                <p style='margin:0 0 6px;'><strong>Tipo de gasto:</strong> {System.Net.WebUtility.HtmlEncode(tipoGasto)}</p>
                <p style='margin:0 0 6px;'><strong>Presupuesto:</strong> {(dentroPresupuesto == true ? "Dentro de presupuesto" : "Desviación autorizada")}</p>
                <p style='margin:0;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(requisicion) ? "N/A" : requisicion)}</p>
              </div>";
                }

                string motivoHtml = string.IsNullOrWhiteSpace(observaciones)
                    ? ""
                    : $"<p><strong>Observaciones:</strong> {System.Net.WebUtility.HtmlEncode(observaciones)}</p>";

                string asunto = $"Dictamen presupuestal {estadoTexto.ToLower()} - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:{color}; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud {estadoTexto}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Control Presupuestal ha emitido dictamen para tu solicitud de compra.</p>

      <div style='background:#f8f9fa; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
      </div>

      {detalleAprobacion}
      {motivoHtml}

      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de dictamen al solicitante. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarCompras_DictamenAprobadoAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    S.TipoGasto,
    S.DentroPresupuesto,
    S.NumeroRequisicion,
    S.CompradorAsignadoUsuarioID,
    PComprador.PersonaID AS PersonaCompradorID,
    PSolicitante.Nombre + ' ' + PSolicitante.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios USolicitante ON S.UsuarioID = USolicitante.UsuarioID
INNER JOIN Persona PSolicitante ON USolicitante.PersonaID = PSolicitante.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Usuarios UComprador ON S.CompradorAsignadoUsuarioID = UComprador.UsuarioID
LEFT JOIN Persona PComprador ON UComprador.PersonaID = PComprador.PersonaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                if (rd["PersonaCompradorID"] == DBNull.Value)
                    return;

                int personaCompradorId = Convert.ToInt32(rd["PersonaCompradorID"]);

                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "";
                string empresa = rd["Empresa"]?.ToString() ?? "";
                string tipoGasto = rd["TipoGasto"]?.ToString() ?? "";
                string requisicion = rd["NumeroRequisicion"] == DBNull.Value ? "" : rd["NumeroRequisicion"].ToString();
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                decimal monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                bool? dentroPresupuesto = rd["DentroPresupuesto"] == DBNull.Value ? null : (bool?)rd["DentroPresupuesto"];

                string presupuestoTexto =
                    dentroPresupuesto == true
                        ? "Dentro de presupuesto"
                        : "Desviación autorizada";

                string asunto = $"Solicitud aprobada para generar O.C. - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#16a34a; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud aprobada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Control Presupuestal aprobó la solicitud. Ya puedes continuar con la generación de la O.C.</p>

      <div style='background:#f0fdf4; border-left:4px solid #16a34a; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Monto:</strong> {monto:C}</p>
        <p style='margin:0 0 6px;'><strong>Tipo de gasto:</strong> {System.Net.WebUtility.HtmlEncode(tipoGasto)}</p>
        <p style='margin:0 0 6px;'><strong>Presupuesto:</strong> {System.Net.WebUtility.HtmlEncode(presupuestoTexto)}</p>
        <p style='margin:0;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(requisicion) ? "N/A" : requisicion)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, para registrar la Orden de Compra.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaCompradorId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de dictamen aprobado a compras. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarSolicitante_OCRegistradaAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    OC.NumeroOC,
    OC.Proveedor,
    OC.FechaOC
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                DateTime? fechaOC = rd["FechaOC"] == DBNull.Value ? null : (DateTime?)rd["FechaOC"];

                string asunto = $"Orden de compra registrada - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Orden de compra registrada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Compras ha registrado la Orden de Compra de tu solicitud.</p>

      <div style='background:#eff6ff; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(proveedor) ? "No capturado" : proveedor)}</p>
        <p style='margin:0;'><strong>Fecha:</strong> {(fechaOC.HasValue ? fechaOC.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
      </div>

      <p>El siguiente paso será el envío de la O.C. al proveedor.</p>
      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de O.C. registrada. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarSolicitante_OCEnviadaProveedorAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    OC.NumeroOC,
    OC.Proveedor,
    OC.FechaEnvioProveedor,
    OC.FechaEstimadaEntrega
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                DateTime? fechaEnvio = rd["FechaEnvioProveedor"] == DBNull.Value ? null : (DateTime?)rd["FechaEnvioProveedor"];
                DateTime? fechaEstimada = rd["FechaEstimadaEntrega"] == DBNull.Value ? null : (DateTime?)rd["FechaEstimadaEntrega"];

                string asunto = $"O.C. enviada al proveedor - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>O.C. enviada al proveedor</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>La Orden de Compra de tu solicitud fue enviada al proveedor.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(proveedor) ? "No capturado" : proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Fecha de envío:</strong> {(fechaEnvio.HasValue ? fechaEnvio.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
        <p style='margin:0;'><strong>Entrega estimada:</strong> {(fechaEstimada.HasValue ? fechaEstimada.Value.ToString("dd/MM/yyyy") : "N/A")}</p>
      </div>

      <p>El siguiente paso será la recepción en almacén.</p>
      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de O.C. enviada a proveedor. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarSolicitante_MaterialRecibidoAlmacenAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    OC.NumeroOC,
    OC.Proveedor,
    R.FechaRecepcion,
    R.Comentarios
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
LEFT JOIN Compras_Recepciones R
    ON S.SolicitudID = R.SolicitudID
   AND R.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);
                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "Solicitante";
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                DateTime? fechaRecepcion = rd["FechaRecepcion"] == DBNull.Value ? null : (DateTime?)rd["FechaRecepcion"];
                string comentarios = rd["Comentarios"] == DBNull.Value ? "" : rd["Comentarios"].ToString();

                string comentariosHtml = string.IsNullOrWhiteSpace(comentarios)
                    ? ""
                    : $"<p><strong>Comentarios de almacén:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>";

                string asunto = $"Material recibido en almacén - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0891b2; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Material recibido en almacén</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Almacén registró la recepción del material correspondiente a tu solicitud.</p>

      <div style='background:#f0f9ff; border-left:4px solid #0891b2; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(proveedor) ? "No capturado" : proveedor)}</p>
        <p style='margin:0;'><strong>Fecha recepción:</strong> {(fechaRecepcion.HasValue ? fechaRecepcion.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
      </div>

      {comentariosHtml}

      <p>El siguiente paso será la entrega al usuario solicitante.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de material recibido en almacén. SolicitudID={SolicitudID}", solicitudId);
            }
        }


        private async Task NotificarCuentasPorPagar_MaterialEntregadoAsync(int solicitudId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaIds = new List<int>();

                string sqlPersonas = @"
SELECT DISTINCT P.PersonaID
FROM Usuarios U
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN EmpleadoDepartamentos ED ON U.UsuarioID = ED.UsuarioID AND ED.Activo = 1
INNER JOIN Departamentos D ON ED.DepartamentoID = D.DepartamentoID AND D.Activo = 1
WHERE P.Correo IS NOT NULL
  AND LTRIM(RTRIM(P.Correo)) <> ''
  AND (
        UPPER(D.NombreDepartamento) LIKE '%CUENTAS POR PAGAR%'
        OR UPPER(D.NombreDepartamento) LIKE '%CXP%'
      )";

                using (var cmdPersonas = new SqlCommand(sqlPersonas, conn))
                using (var rdPersonas = await cmdPersonas.ExecuteReaderAsync())
                {
                    while (await rdPersonas.ReadAsync())
                    {
                        personaIds.Add(Convert.ToInt32(rdPersonas["PersonaID"]));
                    }
                }

                if (!personaIds.Any())
                    return;

                string sqlSolicitud = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    PS.Nombre + ' ' + PS.ApellidoPaterno AS Solicitante,
    E.Nombre AS Empresa,
    C.Proveedor,
    C.MontoTotal,
    OC.NumeroOC,
    EU.FechaEntrega,
    EU.NombreRecibe,
    S.TipoGasto,
    S.NumeroRequisicion
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona PS ON U.PersonaID = PS.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
LEFT JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
LEFT JOIN Compras_EntregasUsuario EU 
    ON S.SolicitudID = EU.SolicitudID
   AND EU.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                string folio = $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = "";
                string empresa = "";
                string proveedor = "";
                decimal monto = 0;
                string numeroOC = "";
                DateTime? fechaEntrega = null;
                string nombreRecibe = "";
                string tipoGasto = "";
                string numeroRequisicion = "";

                using (var cmd = new SqlCommand(sqlSolicitud, conn))
                {
                    cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                    using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        folio = rd["Folio"]?.ToString() ?? folio;
                        solicitante = rd["Solicitante"]?.ToString() ?? "";
                        empresa = rd["Empresa"]?.ToString() ?? "";
                        proveedor = rd["Proveedor"]?.ToString() ?? "";
                        monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                        numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                        fechaEntrega = rd["FechaEntrega"] == DBNull.Value ? null : (DateTime?)rd["FechaEntrega"];
                        nombreRecibe = rd["NombreRecibe"]?.ToString() ?? "";
                        tipoGasto = rd["TipoGasto"]?.ToString() ?? "";
                        numeroRequisicion = rd["NumeroRequisicion"]?.ToString() ?? "";
                    }
                }

                string asunto = $"Solicitud pendiente de CxP - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#64748b; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud pendiente de Cuentas por Pagar</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>La mercancía fue entregada al usuario. La solicitud ya está disponible para validación de Cuentas por Pagar.</p>

      <div style='background:#f8fafc; border-left:4px solid #64748b; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Monto:</strong> {monto:C}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0 0 6px;'><strong>Tipo de gasto:</strong> {System.Net.WebUtility.HtmlEncode(tipoGasto)}</p>
        <p style='margin:0 0 6px;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(numeroRequisicion) ? "N/A" : numeroRequisicion)}</p>
        <p style='margin:0 0 6px;'><strong>Fecha entrega:</strong> {(fechaEntrega.HasValue ? fechaEntrega.Value.ToString("dd/MM/yyyy HH:mm") : "N/A")}</p>
        <p style='margin:0;'><strong>Recibió:</strong> {System.Net.WebUtility.HtmlEncode(nombreRecibe)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, bandeja de <strong>Cuentas por Pagar</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(personaIds, asunto, html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a CxP por entrega al usuario. SolicitudID={SolicitudID}", solicitudId);
            }
        }

        private async Task NotificarSolicitanteYCompras_CxPAsync(
    int solicitudId,
    bool aceptado,
    string? comentarios)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    PS.PersonaID AS PersonaSolicitanteID,
    PS.Nombre + ' ' + PS.ApellidoPaterno AS Solicitante,
    PC.PersonaID AS PersonaCompradorID,
    E.Nombre AS Empresa,
    C.Proveedor,
    C.MontoTotal,
    OC.NumeroOC,
    S.NumeroRequisicion
FROM Compras_Solicitud S
INNER JOIN Usuarios US ON S.UsuarioID = US.UsuarioID
INNER JOIN Persona PS ON US.PersonaID = PS.PersonaID
LEFT JOIN Usuarios UC ON S.CompradorAsignadoUsuarioID = UC.UsuarioID
LEFT JOIN Persona PC ON UC.PersonaID = PC.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
LEFT JOIN Compras_Cotizaciones C ON S.CotizacionSeleccionadaID = C.CotizacionID
LEFT JOIN Compras_OrdenCompra OC 
    ON S.SolicitudID = OC.SolicitudID
   AND OC.Activo = 1
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                var personaIds = new List<int>();

                int personaSolicitanteId = Convert.ToInt32(rd["PersonaSolicitanteID"]);
                personaIds.Add(personaSolicitanteId);

                if (rd["PersonaCompradorID"] != DBNull.Value)
                    personaIds.Add(Convert.ToInt32(rd["PersonaCompradorID"]));

                string folio = rd["Folio"]?.ToString() ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";
                string solicitante = rd["Solicitante"]?.ToString() ?? "";
                string empresa = rd["Empresa"]?.ToString() ?? "";
                string proveedor = rd["Proveedor"]?.ToString() ?? "";
                decimal monto = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
                string numeroOC = rd["NumeroOC"]?.ToString() ?? "";
                string requisicion = rd["NumeroRequisicion"]?.ToString() ?? "";

                string estado = aceptado ? "CERRADA" : "RECHAZADA POR CXP";
                string color = aceptado ? "#16a34a" : "#dc2626";
                string fondo = aceptado ? "#f0fdf4" : "#fef2f2";

                string comentariosHtml = string.IsNullOrWhiteSpace(comentarios)
                    ? ""
                    : $"<p><strong>Comentarios de CxP:</strong> {System.Net.WebUtility.HtmlEncode(comentarios)}</p>";

                string asunto = aceptado
                    ? $"Solicitud cerrada por CxP - {folio}"
                    : $"Solicitud rechazada por CxP - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:{color}; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Solicitud {estado}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Cuentas por Pagar ha procesado la solicitud de compra.</p>

      <div style='background:{fondo}; border-left:4px solid {color}; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Solicitante:</strong> {System.Net.WebUtility.HtmlEncode(solicitante)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(empresa)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0 0 6px;'><strong>Monto:</strong> {monto:C}</p>
        <p style='margin:0 0 6px;'><strong>O.C.:</strong> {System.Net.WebUtility.HtmlEncode(numeroOC)}</p>
        <p style='margin:0;'><strong>Requisición:</strong> {System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(requisicion) ? "N/A" : requisicion)}</p>
      </div>

      {comentariosHtml}

      <p>Puedes revisar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    personaIds.Distinct().ToList(),
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de resultado CxP. SolicitudID={SolicitudID}", solicitudId);
            }
        }

    }
}