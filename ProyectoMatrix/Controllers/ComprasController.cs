using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

        // Empresas especiales para el módulo de Compras.
        private const int EmpresaNsEquipoId = 2;
        private const int EmpresaNsFortId = 4;

       // private const int DepartamentoEmpresaComprasIdNSE = 8;

        public ComprasController(IConfiguration configuration, ILogger<ComprasController> logger, ServicioNotificaciones notif, RutaNas rutaNas, ISftpStorage sftp)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _notif = notif;
            _sftp = sftp;
            _rutaNas = rutaNas;


        }


        /// <summary>
        /// Obtiene las empresas que el usuario puede utilizar al crear una solicitud.
        /// NS FORT solo se agrega cuando el usuario pertenece activamente a
        /// NS EQUIPO E IMPLEMENTOS.
        /// </summary>
        private async Task<List<SelectListItem>> ObtenerEmpresasPermitidasParaSolicitudAsync(
            SqlConnection conn,
            int usuarioId)
        {
            var empresas = new List<SelectListItem>();

            const string sql = @"
SELECT
    E.EmpresaID,
    E.Nombre
FROM Empresas AS E
WHERE E.Activa = 1
  AND
  (
      (
          E.EmpresaID <> @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UE
              WHERE UE.UsuarioID = @UsuarioID
                AND UE.EmpresaID = E.EmpresaID
                AND UE.Activo = 1
          )
      )
      OR
      (
          E.EmpresaID = @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UEEquipo
              INNER JOIN Empresas AS EEquipo
                  ON EEquipo.EmpresaID = UEEquipo.EmpresaID
              WHERE UEEquipo.UsuarioID = @UsuarioID
                AND UEEquipo.EmpresaID = @EmpresaNsEquipoId
                AND UEEquipo.Activo = 1
                AND EEquipo.Activa = 1
          )
      )
  )
ORDER BY
    CASE
        WHEN E.EmpresaID = @EmpresaNsEquipoId THEN 0
        WHEN E.EmpresaID = @EmpresaNsFortId THEN 1
        ELSE 2
    END,
    E.Nombre;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empresas.Add(new SelectListItem
                        {
                            Value = reader["EmpresaID"].ToString(),
                            Text = reader["Nombre"]?.ToString() ?? string.Empty
                        });
                    }
                }
            }

            return empresas;
        }

        /// <summary>
        /// NS FORT comparte compradores con NS EQUIPO E IMPLEMENTOS.
        /// </summary>
        private static int ObtenerEmpresaCompradoresId(int empresaSolicitudId)
        {
            return empresaSolicitudId == EmpresaNsFortId
                ? EmpresaNsEquipoId
                : empresaSolicitudId;
        }

        /// <summary>
        /// Determina si una empresa de solicitud pertenece a la bandeja operativa
        /// de la empresa activa del usuario de Compras.
        /// </summary>
        private static bool EmpresaSolicitudPermitidaEnBandeja(
            int empresaActivaId,
            int empresaSolicitudId)
        {
            return empresaSolicitudId == empresaActivaId
                || (empresaActivaId == EmpresaNsEquipoId
                    && empresaSolicitudId == EmpresaNsFortId);
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

                string puestoNormalizado = (puesto ?? "").Trim().ToUpperInvariant();

                bool esDireccionCompras =
                    puestoNormalizado.Contains("DIRECCION COMPRAS") ||
                    puestoNormalizado.Contains("DIRECCIÓN COMPRAS") ||
                    puestoNormalizado.Contains("DIRECTOR DE COMPRAS") ||
                    puestoNormalizado.Contains("DIRECTORA DE COMPRAS");

                if (esDireccionCompras)
                {
                    /*
                     * FLUJO ACTUAL 1 -> 2 -> 10
                     *
                     * La pantalla Index solamente necesita:
                     * - Total de solicitudes críticas activas.
                     * - Datos generales de los estados 1, 2 y 10.
                     *
                     * El procedimiento sp_Compras_DataDashBoard conserva
                     * dos resultsets para mantener compatibilidad:
                     * 1) Heatmap de estados activos 1 y 2.
                     * 2) Dona de estados 1, 2 y 10.
                     */

                    string sqlCriticos = @"
SELECT COUNT(*)
FROM Compras_Solicitud AS S
OUTER APPLY
(
    SELECT MAX(H.FechaMovimiento) AS FechaUltimoMovimiento
    FROM Compras_Historico_Pasos AS H
    WHERE H.SolicitudID = S.SolicitudID
) AS Ultimo
WHERE S.UrgenciaID = 4
  AND S.EstatusID IN (1, 2)
  AND DATEDIFF(
        HOUR,
        ISNULL(Ultimo.FechaUltimoMovimiento, S.FechaCreacion),
        GETDATE()
      ) > 24;";

                    using (var cmdCriticos = new SqlCommand(sqlCriticos, conn))
                    {
                        stats.CriticosVencidos =
                            Convert.ToInt32(await cmdCriticos.ExecuteScalarAsync());
                    }

                    using (var cmdDashboard =
                           new SqlCommand("sp_Compras_DataDashBoard", conn))
                    {
                        cmdDashboard.CommandType = CommandType.StoredProcedure;

                        using (var reader = await cmdDashboard.ExecuteReaderAsync())
                        {
                            var promediosActivos = new List<double>();

                            // Resultset 1: estados activos 1 y 2.
                            while (await reader.ReadAsync())
                            {
                                string departamento =
                                    reader["NombreDepartamento"]?.ToString()
                                    ?? "Compras";

                                string estatus =
                                    reader["Estatus"]?.ToString()
                                    ?? "Sin estatus";

                                int promedioHoras =
                                    reader["PromedioHorasEstancado"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader["PromedioHorasEstancado"]
                                        );

                                var serie = stats.HeatmapData
                                    .FirstOrDefault(x => x.name == departamento);

                                if (serie == null)
                                {
                                    serie = new HeatmapSeries
                                    {
                                        name = departamento
                                    };

                                    stats.HeatmapData.Add(serie);
                                }

                                serie.data.Add(new HeatmapDataPoint
                                {
                                    x = estatus,
                                    y = promedioHoras
                                });

                                promediosActivos.Add(promedioHoras);
                            }

                            stats.PromedioTotal = promediosActivos.Any()
                                ? promediosActivos.Average()
                                : 0;

                            // Resultset 2: estados 1, 2 y 10.
                            if (await reader.NextResultAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    stats.DonaEtiquetas.Add(
                                        reader["Estatus"]?.ToString()
                                        ?? "Sin estatus"
                                    );

                                    stats.DonaValores.Add(
                                        reader["Total"] == DBNull.Value
                                            ? 0
                                            : Convert.ToDecimal(reader["Total"])
                                    );
                                }
                            }
                        }
                    }
                }

                #region CODIGO LEGADO - ESTADISTICAS DEL PROCESO ANTERIOR

                /*
                 * Este bloque calculaba tiempos de:
                 * - Control Presupuestal.
                 * - Orden de compra.
                 * - Proveedor.
                 * - Almacén.
                 *
                 * Se conserva completo como referencia histórica,
                 * pero ya no se compila en el flujo simplificado.
                 */

#if false
                if (puesto == "DIRECCION COMPRAS")
                {
                    string sqlStats = @"
SELECT 
    (SELECT COUNT(*) 
     FROM Compras_Solicitud 
     WHERE UrgenciaID = 4 
       AND EstatusID NOT IN (10, 11, 12)
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

    ISNULL(AVG(
        CASE 
            WHEN H3.FechaInicio IS NOT NULL 
                THEN DATEDIFF(HOUR, S.FechaCreacion, H3.FechaInicio)
            WHEN S.EstatusID IN (1, 2)
                THEN DATEDIFF(HOUR, S.FechaCreacion, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromCompras,

    ISNULL(AVG(
        CASE 
            WHEN S.FechaDictamen IS NOT NULL AND H4.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H4.FechaInicio, S.FechaDictamen)
            WHEN S.EstatusID = 4 AND H4.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H4.FechaInicio, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromPresupuesto,

    ISNULL(AVG(
        CASE 
            WHEN OC.FechaOC IS NOT NULL AND H5.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H5.FechaInicio, OC.FechaOC)
            WHEN S.EstatusID = 5 AND H5.FechaInicio IS NOT NULL
                THEN DATEDIFF(HOUR, H5.FechaInicio, GETDATE())
            ELSE NULL
        END
    ), 0) AS PromOC,

    ISNULL(AVG(
        CASE 
            WHEN UPPER(ISNULL(S.TipoGasto, '')) NOT IN ('REQUISICION', 'REQUISICIÓN')
                 AND OC.FechaEnvioProveedor IS NOT NULL
                 AND EU.FechaEntrega IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, EU.FechaEntrega)

            WHEN UPPER(ISNULL(S.TipoGasto, '')) NOT IN ('REQUISICION', 'REQUISICIÓN')
                 AND S.EstatusID = 9
                 AND OC.FechaEnvioProveedor IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, GETDATE())

            ELSE NULL
        END
    ), 0) AS PromProveedor,

    ISNULL(AVG(
        CASE 
            WHEN UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
                 AND OC.FechaEnvioProveedor IS NOT NULL
                 AND R.FechaRecepcion IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, R.FechaRecepcion)

            WHEN UPPER(ISNULL(S.TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN')
                 AND S.EstatusID = 8
                 AND OC.FechaEnvioProveedor IS NOT NULL
                THEN DATEDIFF(HOUR, OC.FechaEnvioProveedor, GETDATE())

            ELSE NULL
        END
    ), 0) AS PromAlmacen

FROM Compras_Solicitud S

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 3
) H3

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 4
) H4

OUTER APPLY (
    SELECT MIN(FechaMovimiento) AS FechaInicio
    FROM Compras_Historico_Pasos
    WHERE SolicitudID = S.SolicitudID
      AND EstatusID = 5
) H5

OUTER APPLY (
    SELECT TOP 1 FechaOC, FechaEnvioProveedor
    FROM Compras_OrdenCompra
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY OrdenCompraID DESC
) OC

OUTER APPLY (
    SELECT TOP 1 FechaRecepcion
    FROM Compras_Recepciones
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY RecepcionID DESC
) R

OUTER APPLY (
    SELECT TOP 1 FechaEntrega
    FROM Compras_EntregasUsuario
    WHERE SolicitudID = S.SolicitudID
      AND Activo = 1
    ORDER BY EntregaID DESC
) EU

WHERE S.EstatusID NOT IN (11, 12)";

                    using (var cmd = new SqlCommand(sqlStats, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            stats.CriticosVencidos = Convert.ToInt32(reader["Criticos"]);
                            stats.PromedioTotal = Convert.ToDouble(reader["PromedioGlobal"]);
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromCompras"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromPresupuesto"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromOC"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromProveedor"]));
                            stats.TiemposPromedio.Add(Convert.ToDouble(reader["PromAlmacen"]));
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
#endif

                #endregion
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
            if (usuarioId == 0)
                return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                ViewBag.Empresas =
                    await ObtenerEmpresasPermitidasParaSolicitudAsync(
                        conn,
                        usuarioId
                    );

                var urgencias = new List<SelectListItem>();

                const string sqlUrgencias = @"
SELECT
    UrgenciaID,
    Descripcion
FROM Cat_Urgencia
ORDER BY UrgenciaID;";

                using (var cmdUrgencias = new SqlCommand(sqlUrgencias, conn))
                using (var readerUrgencias = await cmdUrgencias.ExecuteReaderAsync())
                {
                    while (await readerUrgencias.ReadAsync())
                    {
                        urgencias.Add(new SelectListItem
                        {
                            Value = readerUrgencias["UrgenciaID"].ToString(),
                            Text = readerUrgencias["Descripcion"]?.ToString()
                                ?? string.Empty
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
        public async Task<IActionResult> NuevaSolicitud(CompraViewModel model)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            if (model.Materiales == null || !model.Materiales.Any())
            {
                ModelState.AddModelError("", "Debes agregar al menos un material a la lista.");
            }

            if (string.IsNullOrWhiteSpace(model.NombreProyecto))
            {
                ModelState.AddModelError("NombreProyecto", "Debes capturar el nombre del proyecto.");
            }

            // El presupuesto ahora es opcional. Si se captura, debe ser mayor a cero.
            if (model.MontoPresupuestoSolicitado.HasValue
                && model.MontoPresupuestoSolicitado.Value <= 0)
            {
                ModelState.AddModelError(
                    "MontoPresupuestoSolicitado",
                    "El presupuesto debe ser mayor a cero o dejarse como opcional."
                );
            }

            // Sin monto no puede marcarse como compra fuera de presupuesto.
            if (!model.MontoPresupuestoSolicitado.HasValue)
            {
                model.FueraPresupuestoUsuario = false;
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
(
    UsuarioID,    EmpresaID,    TipoCompra,    EsProyecto,    NombreProyecto,   UrgenciaID,    TransporteID,    ComentariosExtra,
    FechaCreacion,   EstatusID,    PuestoAsignado,    MontoPresupuestoSolicitado,    FueraPresupuestoUsuario
) 
VALUES 
(   @uid,    @eid,
    @tipo,    @esp,    @nom,    @urg,    @trans,    @com,    GETDATE(),    1,
    @puesto,   @MontoPresupuestoSolicitado,
    @FueraPresupuestoUsuario
); 

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
                                cmd.Parameters.AddWithValue(
                                    "@MontoPresupuestoSolicitado",
                                    (object?)model.MontoPresupuestoSolicitado ?? DBNull.Value
                                );

                                cmd.Parameters.AddWithValue(
                                    "@FueraPresupuestoUsuario",
                                    model.MontoPresupuestoSolicitado.HasValue
                                        && model.FueraPresupuestoUsuario
                                );

                                nuevaSolicitudId = Convert.ToInt32(await cmd.ExecuteScalarAsync());


                            }



                            if (model.Materiales != null && model.Materiales.Count > 0)
                            {
                                int numeroMaterial = 0;

                                foreach (var mat in model.Materiales)
                                {
                                    numeroMaterial++;

                                    string sqlMat = @"
INSERT INTO Compras_Detalle_Materiales 
(
    SolicitudID,
    NombreMaterial,
    Descripcion,
    Cantidad,
    UnidadMedida
) 
VALUES 
(
    @sid,
    @nombre,
    @descripcion,
    @cantidad,
    @unidad
);

SELECT SCOPE_IDENTITY();";

                                    int detalleId;

                                    using (var cmdMat = new SqlCommand(sqlMat, conn, transaction))
                                    {
                                        cmdMat.Parameters.AddWithValue("@sid", nuevaSolicitudId);
                                        cmdMat.Parameters.AddWithValue("@nombre", (object?)mat.Nombre ?? DBNull.Value);
                                        cmdMat.Parameters.AddWithValue("@descripcion", (object?)mat.Descripcion ?? DBNull.Value);
                                        cmdMat.Parameters.AddWithValue("@cantidad", mat.Cantidad);
                                        cmdMat.Parameters.AddWithValue("@unidad", (object?)mat.UnidadMedida ?? DBNull.Value);

                                        detalleId = Convert.ToInt32(await cmdMat.ExecuteScalarAsync());
                                    }

                                    if (mat.ArchivoReferencia != null && mat.ArchivoReferencia.Length > 0)
                                    {
                                        var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
                                        string extension = Path.GetExtension(mat.ArchivoReferencia.FileName)?.ToLowerInvariant() ?? "";

                                        if (!extensionesPermitidas.Contains(extension))
                                        {
                                            transaction.Rollback();

                                            ModelState.AddModelError(
                                                "",
                                                $"El archivo de referencia del producto {numeroMaterial} solo puede ser PDF o imagen JPG, PNG o WEBP."
                                            );

                                            await CargarCatalogosAsync(usuarioId);
                                            return View(model);
                                        }

                                        string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();

                                        _sftp.AsegurarDirectorio(rutaContenedor);

                                        string folioStr = nuevaSolicitudId.ToString().PadLeft(5, '0');
                                        string nombreOriginal = Path.GetFileName(mat.ArchivoReferencia.FileName);

                                        string nombreArchivo =
                                            $"COM-{folioStr}_Producto_{numeroMaterial}_Referencia_{Guid.NewGuid()}{extension}";

                                        string rutaArchivoSftp = $"{rutaContenedor}/{nombreArchivo}";

                                        using (var stream = mat.ArchivoReferencia.OpenReadStream())
                                        {
                                            _sftp.SubirStream(stream, rutaArchivoSftp);
                                        }

                                        string sqlUpdateArchivo = @"
UPDATE Compras_Detalle_Materiales
SET ArchivoReferenciaPath = @ArchivoReferenciaPath,
    NombreArchivoReferencia = @NombreArchivoReferencia,
    ExtensionArchivoReferencia = @ExtensionArchivoReferencia,
    ContentTypeArchivoReferencia = @ContentTypeArchivoReferencia,
    TamanoArchivoReferencia = @TamanoArchivoReferencia
WHERE DetalleID = @DetalleID";

                                        using (var cmdArchivo = new SqlCommand(sqlUpdateArchivo, conn, transaction))
                                        {
                                            cmdArchivo.Parameters.AddWithValue("@ArchivoReferenciaPath", rutaArchivoSftp);
                                            cmdArchivo.Parameters.AddWithValue("@NombreArchivoReferencia", nombreOriginal);
                                            cmdArchivo.Parameters.AddWithValue("@ExtensionArchivoReferencia", extension);
                                            cmdArchivo.Parameters.AddWithValue("@ContentTypeArchivoReferencia", mat.ArchivoReferencia.ContentType ?? "application/octet-stream");
                                            cmdArchivo.Parameters.AddWithValue("@TamanoArchivoReferencia", mat.ArchivoReferencia.Length);
                                            cmdArchivo.Parameters.AddWithValue("@DetalleID", detalleId);

                                            await cmdArchivo.ExecuteNonQueryAsync();
                                        }
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


        // Método privado para obtener al comprador con menor carga.
        // Las solicitudes de NS FORT se atienden con compradores de NS EQUIPO.
        private async Task<int?> ObtenerCompradorConMenosCargaAsync(
            SqlConnection conn,
            SqlTransaction trans,
            string puestoComprador,
            int empresaId)
        {
            int empresaCompradoresId = ObtenerEmpresaCompradoresId(empresaId);

            const string sql = @"
SELECT TOP 1
    U.UsuarioID
FROM Usuarios AS U
INNER JOIN Persona AS P
    ON U.PersonaID = P.PersonaID
INNER JOIN UsuariosEmpresas AS UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
LEFT JOIN Compras_Solicitud AS S
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
   AND S.EstatusID IN (1, 2)
WHERE P.EsColaboradorActivo = 1
  AND UPPER(LTRIM(RTRIM(P.Puesto))) =
      UPPER(LTRIM(RTRIM(@Puesto)))
GROUP BY U.UsuarioID
ORDER BY
    COUNT(S.SolicitudID) ASC,
    U.UsuarioID ASC;";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@Puesto", puestoComprador);
                cmd.Parameters.AddWithValue(
                    "@EmpresaCompradoresID",
                    empresaCompradoresId
                );

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
            const string sql = @"
SELECT COUNT(*)
FROM Empresas AS EDestino
WHERE EDestino.EmpresaID = @EmpresaID
  AND EDestino.Activa = 1
  AND
  (
      (
          @EmpresaID <> @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UE
              WHERE UE.UsuarioID = @UsuarioID
                AND UE.EmpresaID = @EmpresaID
                AND UE.Activo = 1
          )
      )
      OR
      (
          @EmpresaID = @EmpresaNsFortId
          AND EXISTS
          (
              SELECT 1
              FROM UsuariosEmpresas AS UEEquipo
              INNER JOIN Empresas AS EEquipo
                  ON EEquipo.EmpresaID = UEEquipo.EmpresaID
              WHERE UEEquipo.UsuarioID = @UsuarioID
                AND UEEquipo.EmpresaID = @EmpresaNsEquipoId
                AND UEEquipo.Activo = 1
                AND EEquipo.Activa = 1
          )
      )
  );";

            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                cmd.Parameters.AddWithValue("@EmpresaNsEquipoId", EmpresaNsEquipoId);
                cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);

                int total = Convert.ToInt32(await cmd.ExecuteScalarAsync());
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
    S.MontoPresupuestoSolicitado,
    S.FueraPresupuestoUsuario,

    ADesv.RutaArchivo AS ArchivoDesviacionPath,
    ADesv.NombreOriginal AS NombreArchivoDesviacion,
    ADesv.Extension AS ExtensionArchivoDesviacion,
AReq.RutaArchivo AS ArchivoFormatoRequisicionPath,
AReq.NombreOriginal AS NombreArchivoFormatoRequisicion,
AReq.Extension AS ExtensionArchivoFormatoRequisicion,

    S.ArchivoReferenciaPath
FROM Compras_Solicitud S
INNER JOIN Usuarios U ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P ON U.PersonaID = P.PersonaID
INNER JOIN Empresas E ON S.EmpresaID = E.EmpresaID
INNER JOIN Cat_EstatusCompra Est ON S.EstatusID = Est.EstatusID
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'DESVIACION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesv
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'FORMATO_REQUISICION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) AReq
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

                        vm.CotizacionSeleccionadaID =  reader["CotizacionSeleccionadaID"] == DBNull.Value  ? null  : (int?)reader["CotizacionSeleccionadaID"];

                        vm.FechaSeleccionCotizacion =   reader["FechaSeleccionCotizacion"] == DBNull.Value    ? null      : (DateTime?)reader["FechaSeleccionCotizacion"];

                        vm.ComentariosSeleccionUsuario =    reader["ComentariosSeleccionUsuario"] == DBNull.Value  ? null  : reader["ComentariosSeleccionUsuario"].ToString();

                        vm.ArchivoReferenciaPath =  reader["ArchivoReferenciaPath"] == DBNull.Value  ? null   : reader["ArchivoReferenciaPath"].ToString();

                        vm.MontoPresupuestoSolicitado =  reader["MontoPresupuestoSolicitado"] == DBNull.Value ? null  : (decimal?)Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);

                        vm.FueraPresupuestoUsuario =  reader["FueraPresupuestoUsuario"] != DBNull.Value    && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);
                        vm.ArchivoDesviacionPath =reader["ArchivoDesviacionPath"] == DBNull.Value  ? null  : reader["ArchivoDesviacionPath"].ToString();

                        vm.NombreArchivoDesviacion =  reader["NombreArchivoDesviacion"] == DBNull.Value ? null : reader["NombreArchivoDesviacion"].ToString();

                        vm.ExtensionArchivoDesviacion =  reader["ExtensionArchivoDesviacion"] == DBNull.Value? null : reader["ExtensionArchivoDesviacion"].ToString();

                        vm.ArchivoFormatoRequisicionPath =  reader["ArchivoFormatoRequisicionPath"] == DBNull.Value ? null : reader["ArchivoFormatoRequisicionPath"].ToString();

                        vm.NombreArchivoFormatoRequisicion = reader["NombreArchivoFormatoRequisicion"] == DBNull.Value  ? null  : reader["NombreArchivoFormatoRequisicion"].ToString();

                        vm.ExtensionArchivoFormatoRequisicion =   reader["ExtensionArchivoFormatoRequisicion"] == DBNull.Value  ? null : reader["ExtensionArchivoFormatoRequisicion"].ToString();
                    }

                    bool esControlPresupuestal =
    await UsuarioPerteneceADepartamentoAsync(
        conn,
        usuarioId,
        "CONTROL PRESUPUESTAL",
        "PRESUPUESTOS",
        "CIS"
    );

                    vm.PuedeVerArchivoDesviacion =
                        vm.EsSolicitante || esControlPresupuestal;

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
SELECT 
    NombreMaterial,
    Descripcion,
    Cantidad,
    UnidadMedida,
    ArchivoReferenciaPath,
    NombreArchivoReferencia,
    ExtensionArchivoReferencia
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
                                Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                                Cantidad = reader["Cantidad"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Cantidad"]),
                                UnidadMedida = reader["UnidadMedida"] == DBNull.Value ? "" : reader["UnidadMedida"].ToString(),

                                ArchivoReferenciaPath = reader["ArchivoReferenciaPath"] == DBNull.Value ? null : reader["ArchivoReferenciaPath"].ToString(),
                                NombreArchivoReferencia = reader["NombreArchivoReferencia"] == DBNull.Value ? null : reader["NombreArchivoReferencia"].ToString(),
                                ExtensionArchivoReferencia = reader["ExtensionArchivoReferencia"] == DBNull.Value ? null : reader["ExtensionArchivoReferencia"].ToString()
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
    Comentarios,
    EvidenciaRecepcionPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia
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

                            vm.FechaRecepcionAlmacen = reader["FechaRecepcion"] == DBNull.Value ? null: (DateTime?)reader["FechaRecepcion"];
                            vm.ComentariosRecepcionAlmacen =  reader["Comentarios"] == DBNull.Value? null: reader["Comentarios"].ToString();
                            vm.EvidenciaRecepcionPath =    reader["EvidenciaRecepcionPath"] == DBNull.Value ? null : reader["EvidenciaRecepcionPath"].ToString();
                            vm.NombreArchivoEvidencia =  reader["NombreArchivoEvidencia"] == DBNull.Value   ? null : reader["NombreArchivoEvidencia"].ToString();
                            vm.ExtensionArchivoEvidencia =   reader["ExtensionArchivoEvidencia"] == DBNull.Value   ? null   : reader["ExtensionArchivoEvidencia"].ToString();
                        }
                    }
                }

                string sqlEntrega = @"
SELECT TOP 1
    FechaEntrega,
    NombreRecibe,
    Comentarios,
    EvidenciaEntregaPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia
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
                            vm.EvidenciaEntregaPath =
    reader["EvidenciaEntregaPath"] == DBNull.Value
        ? null
        : reader["EvidenciaEntregaPath"].ToString();

                            vm.NombreArchivoEvidenciaEntrega =
                                reader["NombreArchivoEvidencia"] == DBNull.Value
                                    ? null
                                    : reader["NombreArchivoEvidencia"].ToString();

                            vm.ExtensionArchivoEvidenciaEntrega =
                                reader["ExtensionArchivoEvidencia"] == DBNull.Value
                                    ? null
                                    : reader["ExtensionArchivoEvidencia"].ToString();
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
        public IActionResult VerArchivoNas(string ruta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ruta))
                    return BadRequest("Ruta no válida.");

                var bytes = _sftp.DescargarBytes(ruta);

                string extension = Path.GetExtension(ruta)?.ToLowerInvariant() ?? "";

                string contentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ".xls" => "application/vnd.ms-excel",
                    _ => "application/octet-stream"
                };

                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar archivo desde NAS. Ruta: {Ruta}", ruta);
                return NotFound();
            }
        }



        private async Task CargarCatalogosAsync(int usuarioId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                ViewBag.Empresas =
                    await ObtenerEmpresasPermitidasParaSolicitudAsync(
                        conn,
                        usuarioId
                    );

                var urgencias = new List<SelectListItem>();

                const string sqlUrgencias = @"
SELECT
    UrgenciaID,
    Descripcion
FROM Cat_Urgencia
ORDER BY UrgenciaID;";

                using (var cmd = new SqlCommand(sqlUrgencias, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        urgencias.Add(new SelectListItem
                        {
                            Value = reader["UrgenciaID"].ToString(),
                            Text = reader["Descripcion"]?.ToString()
                                ?? string.Empty
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
            if (usuarioId == 0)
                return Unauthorized();

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

                int? empresaActivaId =
                    await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId);

                if (!empresaActivaId.HasValue)
                    return Forbid();

                bool incluirNsFort =
                    empresaActivaId.Value == EmpresaNsEquipoId;

                int empresaCompradoresId =
                    ObtenerEmpresaCompradoresId(empresaActivaId.Value);

                string puesto = string.Empty;

                using (var cmdP = new SqlCommand(@"
SELECT P.Puesto
FROM Persona AS P
INNER JOIN Usuarios AS U
    ON P.PersonaID = U.PersonaID
WHERE U.UsuarioID = @Uid;", conn))
                {
                    cmdP.Parameters.AddWithValue("@Uid", usuarioId);
                    puesto = (await cmdP.ExecuteScalarAsync())?.ToString()
                        ?? string.Empty;
                }

                string puestoNormalizado = puesto.Trim().ToUpperInvariant();

                vm.EsDireccionCompras =
                    puestoNormalizado == "DIRECCION COMPRAS"
                    || puestoNormalizado == "DIRECCIÓN COMPRAS";

                string filtroTipo = string.Empty;

                if (puestoNormalizado == "COMPRADOR NACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Nacional'";
                else if (puestoNormalizado == "COMPRADOR INTERNACIONAL")
                    filtroTipo = " AND S.TipoCompra = 'Internacional'";

                string filtroEmpresaSolicitud = @"
  AND
  (
      S.EmpresaID = @EmpresaID
      OR
      (
          @IncluirNsFort = 1
          AND S.EmpresaID = @EmpresaNsFortId
      )
  )";

                string sqlPendientes = $@"
SELECT
    S.SolicitudID,
    S.CompradorAsignadoUsuarioID,
    S.Folio,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    S.TipoCompra,
    U.Descripcion AS Urgencia,
    S.FechaCreacion,
    E.Nombre AS Estatus
FROM Compras_Solicitud AS S
INNER JOIN Usuarios AS US
    ON S.UsuarioID = US.UsuarioID
INNER JOIN Persona AS P
    ON US.PersonaID = P.PersonaID
LEFT JOIN EmpleadoDepartamentos AS ED
    ON US.UsuarioID = ED.UsuarioID
   AND ED.Activo = 1
LEFT JOIN Departamentos AS D
    ON ED.DepartamentoID = D.DepartamentoID
INNER JOIN Cat_Urgencia AS U
    ON S.UrgenciaID = U.UrgenciaID
INNER JOIN Cat_EstatusCompra AS E
    ON S.EstatusID = E.EstatusID
WHERE S.EstatusID = 1
{filtroEmpresaSolicitud}
  AND
  (
      @EsDireccion = 1
      OR S.CompradorAsignadoUsuarioID = @UsuarioID
  )
{filtroTipo}
ORDER BY S.FechaCreacion ASC;";

                using (var cmd = new SqlCommand(sqlPendientes, conn))
                {
                    AgregarParametrosBandejaEmpresa(
                        cmd,
                        usuarioId,
                        vm.EsDireccionCompras,
                        empresaActivaId.Value,
                        incluirNsFort
                    );

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Pendientes.Add(new BandejaComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"]?.ToString() ?? string.Empty,
                                CompradorAsignadoUsuarioID =
                                    reader["CompradorAsignadoUsuarioID"] == DBNull.Value
                                        ? null
                                        : (int?)Convert.ToInt32(
                                            reader["CompradorAsignadoUsuarioID"]
                                        ),
                                Solicitante = reader["Solicitante"]?.ToString()
                                    ?? string.Empty,
                                Departamento = reader["Departamento"]?.ToString()
                                    ?? string.Empty,
                                TipoCompra = reader["TipoCompra"]?.ToString()
                                    ?? string.Empty,
                                Urgencia = reader["Urgencia"]?.ToString()
                                    ?? string.Empty,
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                Estatus = reader["Estatus"]?.ToString()
                                    ?? string.Empty
                            });
                        }
                    }
                }

                string sqlHistorico = $@"
SELECT
    S.SolicitudID,
    S.Folio,
    S.EstatusID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    ISNULL(D.NombreDepartamento, 'Sin departamento') AS Departamento,
    S.TipoCompra,
    U.Descripcion AS Urgencia,
    E.Nombre AS Estatus,
    ISNULL(C.MontoTotal, 0) AS MontoTotal,
    S.FechaCreacion,
    C.FechaEnvioAlUsuario AS FechaCotizacion
FROM Compras_Solicitud AS S
INNER JOIN Usuarios AS US
    ON S.UsuarioID = US.UsuarioID
INNER JOIN Persona AS P
    ON US.PersonaID = P.PersonaID
LEFT JOIN EmpleadoDepartamentos AS ED
    ON US.UsuarioID = ED.UsuarioID
   AND ED.Activo = 1
LEFT JOIN Departamentos AS D
    ON ED.DepartamentoID = D.DepartamentoID
INNER JOIN Cat_Urgencia AS U
    ON S.UrgenciaID = U.UrgenciaID
INNER JOIN Cat_EstatusCompra AS E
    ON S.EstatusID = E.EstatusID
LEFT JOIN Compras_Cotizaciones AS C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.EstatusID >= 2
{filtroEmpresaSolicitud}
  AND
  (
      @EsDireccion = 1
      OR S.CompradorAsignadoUsuarioID = @UsuarioID
  )
{filtroTipo}
ORDER BY
    C.FechaEnvioAlUsuario DESC,
    S.FechaCreacion DESC;";

                using (var cmd = new SqlCommand(sqlHistorico, conn))
                {
                    AgregarParametrosBandejaEmpresa(
                        cmd,
                        usuarioId,
                        vm.EsDireccionCompras,
                        empresaActivaId.Value,
                        incluirNsFort
                    );

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            vm.Historico.Add(new HistoricoComprasVm
                            {
                                SolicitudID = (int)reader["SolicitudID"],
                                Folio = reader["Folio"]?.ToString() ?? string.Empty,
                                EstatusID = (int)reader["EstatusID"],
                                Solicitante = reader["Solicitante"]?.ToString()
                                    ?? string.Empty,
                                Departamento = reader["Departamento"]?.ToString()
                                    ?? string.Empty,
                                TipoCompra = reader["TipoCompra"]?.ToString()
                                    ?? string.Empty,
                                Urgencia = reader["Urgencia"]?.ToString()
                                    ?? string.Empty,
                                Estatus = reader["Estatus"]?.ToString()
                                    ?? string.Empty,
                                MontoTotal = Convert.ToDecimal(reader["MontoTotal"]),
                                FechaCreacion = (DateTime)reader["FechaCreacion"],
                                FechaCotizacion =
                                    reader["FechaCotizacion"] == DBNull.Value
                                        ? null
                                        : (DateTime?)reader["FechaCotizacion"]
                            });
                        }
                    }
                }

                if (vm.EsDireccionCompras)
                {
                    const string sqlCargaCompradores = @"
SELECT
    U.UsuarioID,
    P.Nombre + ' ' + P.ApellidoPaterno AS NombreCompleto,
    P.Puesto,
    SUM(CASE WHEN S.EstatusID = 1 THEN 1 ELSE 0 END) AS Pendientes,
    SUM(CASE WHEN S.EstatusID >= 2 THEN 1 ELSE 0 END) AS Cotizadas,
    COUNT(S.SolicitudID) AS TotalAsignadas
FROM Usuarios AS U
INNER JOIN Persona AS P
    ON U.PersonaID = P.PersonaID
INNER JOIN UsuariosEmpresas AS UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
LEFT JOIN Compras_Solicitud AS S
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
   AND
   (
       S.EmpresaID = @EmpresaID
       OR
       (
           @IncluirNsFort = 1
           AND S.EmpresaID = @EmpresaNsFortId
       )
   )
WHERE P.EsColaboradorActivo = 1
  AND UPPER(P.Puesto) IN
      ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')
GROUP BY
    U.UsuarioID,
    P.Nombre,
    P.ApellidoPaterno,
    P.Puesto
ORDER BY
    P.Puesto,
    Pendientes ASC,
    NombreCompleto ASC;";

                    using (var cmdCarga = new SqlCommand(sqlCargaCompradores, conn))
                    {
                        cmdCarga.Parameters.AddWithValue(
                            "@EmpresaCompradoresID",
                            empresaCompradoresId
                        );
                        cmdCarga.Parameters.AddWithValue(
                            "@EmpresaID",
                            empresaActivaId.Value
                        );
                        cmdCarga.Parameters.AddWithValue(
                            "@IncluirNsFort",
                            incluirNsFort ? 1 : 0
                        );
                        cmdCarga.Parameters.AddWithValue(
                            "@EmpresaNsFortId",
                            EmpresaNsFortId
                        );

                        using (var reader = await cmdCarga.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CargaCompradores.Add(new CompradorCargaVm
                                {
                                    UsuarioID = (int)reader["UsuarioID"],
                                    NombreCompleto = reader["NombreCompleto"]?.ToString()
                                        ?? string.Empty,
                                    Puesto = reader["Puesto"]?.ToString()
                                        ?? string.Empty,
                                    Pendientes = Convert.ToInt32(reader["Pendientes"]),
                                    Cotizadas = Convert.ToInt32(reader["Cotizadas"]),
                                    TotalAsignadas = Convert.ToInt32(
                                        reader["TotalAsignadas"]
                                    )
                                });
                            }
                        }
                    }

                    const string sqlCompradores = @"
SELECT
    U.UsuarioID,
    P.Nombre + ' ' + P.ApellidoPaterno AS NombreCompleto,
    P.Puesto
FROM Usuarios AS U
INNER JOIN Persona AS P
    ON U.PersonaID = P.PersonaID
INNER JOIN UsuariosEmpresas AS UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
WHERE P.EsColaboradorActivo = 1
  AND UPPER(P.Puesto) IN
      ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL')
ORDER BY
    P.Puesto,
    P.Nombre,
    P.ApellidoPaterno;";

                    using (var cmdCompradores = new SqlCommand(sqlCompradores, conn))
                    {
                        cmdCompradores.Parameters.AddWithValue(
                            "@EmpresaCompradoresID",
                            empresaCompradoresId
                        );

                        using (var reader = await cmdCompradores.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                vm.CompradoresDisponibles.Add(
                                    new CompradorSelectVm
                                    {
                                        UsuarioID = (int)reader["UsuarioID"],
                                        NombreCompleto =
                                            reader["NombreCompleto"]?.ToString()
                                            ?? string.Empty,
                                        Puesto = reader["Puesto"]?.ToString()
                                            ?? string.Empty
                                    }
                                );
                            }
                        }
                    }
                }
            }

            vm.TotalPendientes = vm.Pendientes.Count;
            vm.TotalCotizadas = vm.Historico.Count(
                x => x.FechaCotizacion != null
            );
            vm.TotalAtendidas = vm.Historico.Count;
            vm.MontoCotizado = vm.Historico.Sum(x => x.MontoTotal);

            return View(vm);
        }

        private static void AgregarParametrosBandejaEmpresa(
            SqlCommand cmd,
            int usuarioId,
            bool esDireccionCompras,
            int empresaActivaId,
            bool incluirNsFort)
        {
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue(
                "@EsDireccion",
                esDireccionCompras ? 1 : 0
            );
            cmd.Parameters.AddWithValue("@EmpresaID", empresaActivaId);
            cmd.Parameters.AddWithValue(
                "@IncluirNsFort",
                incluirNsFort ? 1 : 0
            );
            cmd.Parameters.AddWithValue("@EmpresaNsFortId", EmpresaNsFortId);
        }


        [HttpPost("ProcesarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarCotizacion(
    [FromForm] int SolicitudID,
    [FromForm] List<IFormFile> ArchivosCotizacion,
    [FromForm] List<string> Proveedores,
    [FromForm] List<decimal?> Montos,
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
                                    cmdCot.Parameters.AddWithValue("@EsRecomendada", false);
                                    cmdCot.Parameters.AddWithValue("@NumeroCotizacion", numeroCotizacion);
                                    cmdCot.Parameters.AddWithValue("@NombreArchivoOriginal", nombreOriginal);
                                    cmdCot.Parameters.AddWithValue("@Extension", extension);
                                    cmdCot.Parameters.AddWithValue("@ContentType", archivo.ContentType ?? "application/octet-stream");
                                    cmdCot.Parameters.AddWithValue("@TamanoBytes", archivo.Length);
                                    cmdCot.Parameters.AddWithValue("@UsuarioSubioID", usuarioId);

                                    await cmdCot.ExecuteNonQueryAsync();
                                }
                            }

                            /*
                             * NUEVO FLUJO SOLICITADO:
                             * al terminar de cargar las cotizaciones, la solicitud
                             * se cierra inmediatamente y el progreso pasa al 100 %.
                             *
                             * Estado 1 -> Estado 10
                             */
                            string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 10,
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

                            /*
                             * Se registran los dos hitos del proceso en el historial:
                             * 2  = Cotizaciones cargadas
                             * 10 = Cerrada
                             *
                             * Ambos movimientos se generan en la misma operacion porque
                             * la regla actual indica que la carga de cotizaciones finaliza
                             * automáticamente la solicitud.
                             */
                            string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 2, GETDATE(), @Responsable),
(@SolicitudID, 10, DATEADD(MILLISECOND, 1, GETDATE()), @Responsable)";

                            using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                            {
                                cmdHist.Parameters.AddWithValue("@SolicitudID", SolicitudID);
                                cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                                await cmdHist.ExecuteNonQueryAsync();
                            }

                            trans.Commit();

                            await NotificarSolicitante_CotizacionesListasAsync(SolicitudID);

                            TempData["Mensaje"] =
                                "Cotizaciones guardadas correctamente. " +
                                "La solicitud fue cerrada y el progreso se actualizo al 100 %.";
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
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            var model = new DictamenPresupuestalVm();

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

                string sql = @"
SELECT 
    S.SolicitudID,
    S.Folio,
    S.MontoPresupuestoSolicitado,
    S.FueraPresupuestoUsuario,

    ADesv.RutaArchivo AS ArchivoDesviacionPath,
    ADesv.NombreOriginal AS NombreArchivoDesviacion,
    ADesv.Extension AS ExtensionArchivoDesviacion,

AReq.RutaArchivo AS ArchivoFormatoRequisicionPath,
AReq.NombreOriginal AS NombreArchivoFormatoRequisicion,
AReq.Extension AS ExtensionArchivoFormatoRequisicion,

    C.CotizacionID,
    C.Proveedor,
    C.MontoTotal,
    C.ArchivoPath,
    C.NombreArchivoOriginal,
    C.Extension
FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'DESVIACION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) ADesv
OUTER APPLY
(
    SELECT TOP 1
        CA.RutaArchivo,
        CA.NombreOriginal,
        CA.Extension
    FROM Compras_Archivos CA
    WHERE CA.SolicitudID = S.SolicitudID
      AND CA.CotizacionID = S.CotizacionSeleccionadaID
      AND CA.TipoArchivo = 'FORMATO_REQUISICION'
      AND CA.Vigente = 1
      AND CA.Activo = 1
    ORDER BY CA.FechaCarga DESC
) AReq
WHERE S.SolicitudID = @id
  AND S.EstatusID = 4";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            TempData["Error"] = "No se encontró una cotización seleccionada para dictaminar.";
                            return RedirectToAction("BandejaPresupuestos");
                        }

                        model.SolicitudID = (int)reader["SolicitudID"];
                        model.Folio = reader["Folio"] == DBNull.Value ? "" : reader["Folio"].ToString();
                        model.CotizacionID = (int)reader["CotizacionID"];
                        model.Proveedor = reader["Proveedor"] == DBNull.Value ? "" : reader["Proveedor"].ToString();
                        model.MontoCotizado = reader["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["MontoTotal"]);
                        model.ArchivoPath = reader["ArchivoPath"] == DBNull.Value ? "" : reader["ArchivoPath"].ToString();
                        model.NombreArchivoOriginal = reader["NombreArchivoOriginal"] == DBNull.Value ? "" : reader["NombreArchivoOriginal"].ToString();
                        model.Extension = reader["Extension"] == DBNull.Value ? "" : reader["Extension"].ToString();
                        model.MontoPresupuestoSolicitado = reader["MontoPresupuestoSolicitado"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);
                        model.FueraPresupuestoUsuario =reader["FueraPresupuestoUsuario"] != DBNull.Value  && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);
                        model.ArchivoDesviacionPath =   reader["ArchivoDesviacionPath"] == DBNull.Value   ? null : reader["ArchivoDesviacionPath"].ToString();
                        model.NombreArchivoDesviacion =  reader["NombreArchivoDesviacion"] == DBNull.Value? null: reader["NombreArchivoDesviacion"].ToString();
                        model.ExtensionArchivoDesviacion = reader["ExtensionArchivoDesviacion"] == DBNull.Value  ? null: reader["ExtensionArchivoDesviacion"].ToString();
                        model.ArchivoFormatoRequisicionPath =    reader["ArchivoFormatoRequisicionPath"] == DBNull.Value  ? null: reader["ArchivoFormatoRequisicionPath"].ToString();
                        model.NombreArchivoFormatoRequisicion = reader["NombreArchivoFormatoRequisicion"] == DBNull.Value ? null: reader["NombreArchivoFormatoRequisicion"].ToString();
                        model.ExtensionArchivoFormatoRequisicion =  reader["ExtensionArchivoFormatoRequisicion"] == DBNull.Value   ? null  : reader["ExtensionArchivoFormatoRequisicion"].ToString();
                    }
                }
            }

            return View(model);
        }

        //METODO PARA ASIGNAR A UN COMPRADOR

        [HttpPost("AsignarComprador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarComprador(
            int solicitudId,
            int compradorUsuarioId)
        {
            int usuarioDireccionId = ObtenerUsuarioIdActual();
            if (usuarioDireccionId == 0)
                return Unauthorized();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                int? empresaActivaId =
                    await ObtenerEmpresaActivaUsuarioAsync(
                        conn,
                        usuarioDireccionId
                    );

                if (!empresaActivaId.HasValue)
                    return Forbid();

                string puesto = string.Empty;

                using (var cmdPuesto = new SqlCommand(@"
SELECT P.Puesto
FROM Usuarios AS U
INNER JOIN Persona AS P
    ON U.PersonaID = P.PersonaID
WHERE U.UsuarioID = @UsuarioID;", conn))
                {
                    cmdPuesto.Parameters.AddWithValue(
                        "@UsuarioID",
                        usuarioDireccionId
                    );

                    puesto = (await cmdPuesto.ExecuteScalarAsync())?.ToString()
                        ?? string.Empty;
                }

                string puestoNormalizado = puesto.Trim().ToUpperInvariant();

                bool esDireccionCompras =
                    puestoNormalizado == "DIRECCION COMPRAS"
                    || puestoNormalizado == "DIRECCIÓN COMPRAS";

                if (!esDireccionCompras)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int empresaSolicitudId;
                        int? anteriorId;

                        const string sqlSolicitud = @"
SELECT
    EmpresaID,
    CompradorAsignadoUsuarioID
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID;";

                        using (var cmdSolicitud =
                               new SqlCommand(sqlSolicitud, conn, trans))
                        {
                            cmdSolicitud.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );

                            using (var reader =
                                   await cmdSolicitud.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] =
                                        "No se encontró la solicitud.";
                                    return RedirectToAction("BandejaCompras");
                                }

                                empresaSolicitudId =
                                    Convert.ToInt32(reader["EmpresaID"]);

                                anteriorId =
                                    reader["CompradorAsignadoUsuarioID"]
                                        == DBNull.Value
                                            ? null
                                            : Convert.ToInt32(
                                                reader[
                                                    "CompradorAsignadoUsuarioID"
                                                ]
                                            );
                            }
                        }

                        if (!EmpresaSolicitudPermitidaEnBandeja(
                                empresaActivaId.Value,
                                empresaSolicitudId
                            ))
                        {
                            trans.Rollback();
                            return Forbid();
                        }

                        int empresaCompradoresId =
                            ObtenerEmpresaCompradoresId(empresaSolicitudId);

                        const string sqlValidarComprador = @"
SELECT COUNT(*)
FROM Usuarios AS U
INNER JOIN Persona AS P
    ON U.PersonaID = P.PersonaID
INNER JOIN UsuariosEmpresas AS UE
    ON U.UsuarioID = UE.UsuarioID
   AND UE.Activo = 1
   AND UE.EmpresaID = @EmpresaCompradoresID
WHERE U.UsuarioID = @CompradorID
  AND P.EsColaboradorActivo = 1
  AND UPPER(P.Puesto) IN
      ('COMPRADOR NACIONAL', 'COMPRADOR INTERNACIONAL');";

                        using (var cmdValidar =
                               new SqlCommand(
                                   sqlValidarComprador,
                                   conn,
                                   trans
                               ))
                        {
                            cmdValidar.Parameters.AddWithValue(
                                "@CompradorID",
                                compradorUsuarioId
                            );
                            cmdValidar.Parameters.AddWithValue(
                                "@EmpresaCompradoresID",
                                empresaCompradoresId
                            );

                            int existe = Convert.ToInt32(
                                await cmdValidar.ExecuteScalarAsync()
                            );

                            if (existe == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] =
                                    "El comprador seleccionado no pertenece a la empresa operativa de Compras.";
                                return RedirectToAction("BandejaCompras");
                            }
                        }

                        const string sqlUpdate = @"
UPDATE Compras_Solicitud
SET CompradorAsignadoUsuarioID = @NuevoCompradorID,
    FechaAsignacionComprador = GETDATE(),
    UsuarioAsignoCompradorID = @DireccionID
WHERE SolicitudID = @SolicitudID
  AND EmpresaID = @EmpresaSolicitudID;";

                        using (var cmdUpdate =
                               new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue(
                                "@NuevoCompradorID",
                                compradorUsuarioId
                            );
                            cmdUpdate.Parameters.AddWithValue(
                                "@DireccionID",
                                usuarioDireccionId
                            );
                            cmdUpdate.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );
                            cmdUpdate.Parameters.AddWithValue(
                                "@EmpresaSolicitudID",
                                empresaSolicitudId
                            );

                            int filas = await cmdUpdate.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] =
                                    "No se pudo actualizar la asignación.";
                                return RedirectToAction("BandejaCompras");
                            }
                        }

                        const string sqlHist = @"
INSERT INTO Compras_Asignaciones_Historico
(
    SolicitudID,
    UsuarioAsignadoAnteriorID,
    UsuarioAsignadoNuevoID,
    UsuarioDireccionID,
    FechaAsignacion,
    Comentario
)
VALUES
(
    @SolicitudID,
    @AnteriorID,
    @NuevoID,
    @DireccionID,
    GETDATE(),
    @Comentario
);";

                        using (var cmdHist =
                               new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );
                            cmdHist.Parameters.AddWithValue(
                                "@AnteriorID",
                                (object?)anteriorId ?? DBNull.Value
                            );
                            cmdHist.Parameters.AddWithValue(
                                "@NuevoID",
                                compradorUsuarioId
                            );
                            cmdHist.Parameters.AddWithValue(
                                "@DireccionID",
                                usuarioDireccionId
                            );
                            cmdHist.Parameters.AddWithValue(
                                "@Comentario",
                                "Asignación manual desde Dirección Compras"
                            );

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        TempData["Mensaje"] =
                            "Comprador asignado correctamente.";
                        return RedirectToAction("BandejaCompras");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(
                            ex,
                            "Error al reasignar comprador"
                        );

                        TempData["Error"] =
                            "No se pudo reasignar comprador.";

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

            string tipoGastoNormalizado = (vm.TipoGasto ?? "")
                .Trim()
                .ToUpper()
                .Replace("Ó", "O");

            if (vm.Pasa &&
                tipoGastoNormalizado == "REQUISICION" &&
                string.IsNullOrWhiteSpace(vm.NumeroRequisicion))
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
                        int nuevoEstatus = vm.Pasa ? 5 : 11;

                        bool fueraPresupuestoUsuario = false;
                        bool tieneArchivoDesviacion = false;
                        bool tieneFormatoRequisicion = false;

                        decimal? montoPresupuestoSolicitado = null;
                        decimal montoCotizado = 0;
                        bool requiereDesviacionPorMonto = false;

                        string sqlValidarPresupuestoInicial = @"
SELECT 
    S.FueraPresupuestoUsuario,
    S.MontoPresupuestoSolicitado,
    C.MontoTotal AS MontoCotizado,

    CASE 
        WHEN EXISTS (
            SELECT 1
            FROM Compras_Archivos A
            WHERE A.SolicitudID = S.SolicitudID
              AND A.TipoArchivo = 'DESVIACION'
              AND A.Vigente = 1
              AND A.Activo = 1
              AND (
                    A.CotizacionID = S.CotizacionSeleccionadaID
                    OR A.CotizacionID IS NULL
                  )
        )
        THEN 1 ELSE 0
    END AS TieneArchivoDesviacion,

    CASE 
        WHEN EXISTS (
            SELECT 1
            FROM Compras_Archivos A
            WHERE A.SolicitudID = S.SolicitudID
              AND A.TipoArchivo = 'FORMATO_REQUISICION'
              AND A.Vigente = 1
              AND A.Activo = 1
              AND (
                    A.CotizacionID = S.CotizacionSeleccionadaID
                    OR A.CotizacionID IS NULL
                  )
        )
        THEN 1 ELSE 0
    END AS TieneFormatoRequisicion

FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 4";

                        using (var cmdValPres = new SqlCommand(sqlValidarPresupuestoInicial, conn, trans))
                        {
                            cmdValPres.Parameters.AddWithValue("@SolicitudID", vm.SolicitudID);

                            using (var reader = await cmdValPres.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud ya no está pendiente de dictamen.";
                                    return RedirectToAction("BandejaPresupuestos");
                                }

                                fueraPresupuestoUsuario =
                                    reader["FueraPresupuestoUsuario"] != DBNull.Value
                                    && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);

                                montoPresupuestoSolicitado =
                                    reader["MontoPresupuestoSolicitado"] == DBNull.Value
                                        ? null
                                        : Convert.ToDecimal(reader["MontoPresupuestoSolicitado"]);

                                montoCotizado =
                                    reader["MontoCotizado"] == DBNull.Value
                                        ? 0
                                        : Convert.ToDecimal(reader["MontoCotizado"]);

                                requiereDesviacionPorMonto =
                                    montoPresupuestoSolicitado.HasValue
                                    && montoCotizado > montoPresupuestoSolicitado.Value;

                                tieneArchivoDesviacion =
                                    reader["TieneArchivoDesviacion"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneArchivoDesviacion"]) == 1;

                                tieneFormatoRequisicion =
                                    reader["TieneFormatoRequisicion"] != DBNull.Value
                                    && Convert.ToInt32(reader["TieneFormatoRequisicion"]) == 1;
                            }
                        }

                        bool requiereDesviacion =
                            fueraPresupuestoUsuario ||
                            requiereDesviacionPorMonto ||
                            vm.DentroDePresupuesto == false;

                        if (vm.Pasa && requiereDesviacion && !tieneArchivoDesviacion)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "No puedes aprobar esta solicitud porque requiere desviación y no existe archivo de desviación vigente.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        if (vm.Pasa && requiereDesviacion && vm.DentroDePresupuesto == true)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "La solicitud requiere desviación. No puede dictaminarse como dentro de presupuesto.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        if (vm.Pasa &&
                            tipoGastoNormalizado == "REQUISICION" &&
                            !tieneFormatoRequisicion)
                        {
                            trans.Rollback();

                            TempData["Error"] =
                                "No puedes aprobar como requisición porque no existe formato de requisición vigente cargado.";

                            return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
                        }

                        string queryUpdate = @"
UPDATE Compras_Solicitud 
SET EstatusID = @Est,
    FechaDictamen = GETDATE(),
    TipoGasto = @Tipo,
    DentroPresupuesto = @Dentro,
    NumeroRequisicion = @Requi,
    ObservacionesPresupuesto = @Obs
WHERE SolicitudID = @Sid
  AND EstatusID = 4";

                        using (var cmd = new SqlCommand(queryUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Est", nuevoEstatus);
                            cmd.Parameters.AddWithValue("@Sid", vm.SolicitudID);

                            cmd.Parameters.AddWithValue(
                                "@Tipo",
                                vm.Pasa ? (object)tipoGastoNormalizado : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Dentro",
                                vm.Pasa ? (object)vm.DentroDePresupuesto : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Requi",
                                (vm.Pasa && tipoGastoNormalizado == "REQUISICION")
                                    ? (object)(vm.NumeroRequisicion ?? "")
                                    : DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Obs",
                                (object?)vm.Observaciones ?? DBNull.Value
                            );

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

                        TempData["Error"] = "Ocurrió un error al procesar el dictamen.";
                        return RedirectToAction("Dictamen", new { id = vm.SolicitudID });
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
WHERE S.EstatusID = 4
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
    WHEN S.EstatusID >= 5 AND S.EstatusID NOT IN (11, 12) THEN 'Aprobado'
    WHEN S.EstatusID = 11 THEN 'Rechazado'
    WHEN S.EstatusID = 12 THEN 'Cancelado'
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
  AND S.EstatusID IN (5, 6, 7, 8, 9, 10, 11, 12)
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

        #region CODIGO LEGADO - DASHBOARD DIRECCION

        /*
         * Accion deshabilitada por simplificacion del modulo Compras.
         * No cuenta con una vista asociada y no tiene referencias activas.
         * Se conserva completa como codigo historico.
         */

#if false
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

#endif

        #endregion

        //HELPERS PARA OBTENER EMPRESAS DEL USUARIO
        private async Task<int?> ObtenerEmpresaActivaUsuarioAsync(
            SqlConnection conn,
            int usuarioId)
        {
            const string sql = @"
SELECT TOP 1
    UE.EmpresaID
FROM UsuariosEmpresas AS UE
INNER JOIN Empresas AS E
    ON UE.EmpresaID = E.EmpresaID
WHERE UE.UsuarioID = @UsuarioID
  AND UE.Activo = 1
  AND E.Activa = 1
ORDER BY
    CASE
        WHEN UE.EmpresaID = @EmpresaNsEquipoId THEN 0
        ELSE 1
    END,
    UE.EmpresaID;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd.Parameters.AddWithValue(
                    "@EmpresaNsEquipoId",
                    EmpresaNsEquipoId
                );

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
          AND S.EstatusID = 5";

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
        public async Task<IActionResult> RegistrarOC(int solicitudId, string numeroOC, string? comentarios)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            numeroOC = numeroOC?.Trim();
           
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
  AND EstatusID = 5
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

                        string sqlCotizacionSeleccionada = @"
SELECT C.Proveedor, C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 5";

                        string proveedorCotizacion = "";
                        decimal montoCotizacion = 0;

                        using (var cmdCot = new SqlCommand(sqlCotizacionSeleccionada, conn, trans))
                        {
                            cmdCot.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            using var rd = await cmdCot.ExecuteReaderAsync();
                            if (!await rd.ReadAsync())
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se encontró la cotización seleccionada.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            proveedorCotizacion = rd["Proveedor"]?.ToString() ?? "";
                            montoCotizacion = rd["MontoTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MontoTotal"]);
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
                            cmd.Parameters.AddWithValue("@Proveedor", proveedorCotizacion);
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

                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID = 6
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 5";

                        using (var cmdUpdateSol = new SqlCommand(sqlUpdateSolicitud, conn, trans))
                        {
                            cmdUpdateSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasUpdateSol = await cmdUpdateSol.ExecuteNonQueryAsync();

                            if (filasUpdateSol == 0)
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


        //METODO PARA SUBIR UNA DESVIACIÓN 



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
  AND S.EstatusID = 6
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

                        string sqlHistOCEnviada = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 7, GETDATE(), @Responsable)";

                        using (var cmdHist7 = new SqlCommand(sqlHistOCEnviada, conn, trans))
                        {
                            cmdHist7.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist7.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");
                            await cmdHist7.ExecuteNonQueryAsync();
                        }



                        string sqlUpdateSolicitud = @"
UPDATE Compras_Solicitud
SET EstatusID =
    CASE
        WHEN UPPER(ISNULL(TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN') THEN 8
        ELSE 9
    END
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 6";

                        int nuevoEstatusHistorico;

                        using (var cmdTipo = new SqlCommand(@"
SELECT 
    CASE
        WHEN UPPER(ISNULL(TipoGasto, '')) IN ('REQUISICION', 'REQUISICIÓN') THEN 8
        ELSE 9
    END
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID", conn, trans))
                        {
                            cmdTipo.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            nuevoEstatusHistorico = Convert.ToInt32(await cmdTipo.ExecuteScalarAsync());
                        }

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
(@SolicitudID, @EstatusID, GETDATE(), @Responsable)";

                        using (var cmd = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@EstatusID", nuevoEstatusHistorico);
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
        public async Task<IActionResult> RegistrarRecepcionAlmacen(    int solicitudId,    string? comentarios,    IFormFile? evidenciaRecepcion)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();

            if (evidenciaRecepcion == null || evidenciaRecepcion.Length == 0)
            {
                TempData["Error"] = "Debes adjuntar evidencia de recepción del material.";
                return RedirectToAction("BandejaAlmacen");
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

            string extensionEvidencia =
                Path.GetExtension(evidenciaRecepcion.FileName)?.ToLowerInvariant() ?? "";

            if (!extensionesPermitidas.Contains(extensionEvidencia))
            {
                TempData["Error"] = "La evidencia solo puede ser PDF o imagen JPG, PNG o WEBP.";
                return RedirectToAction("BandejaAlmacen");
            }

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
  AND S.EstatusID = 8
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
    SolicitudID,   OrdenCompraID,    FechaRecepcion,    UsuarioRecibioID,    Comentarios,    EvidenciaRecepcionPath,    NombreArchivoEvidencia,    ExtensionArchivoEvidencia,    ContentTypeEvidencia,
    TamanoBytesEvidencia,    FechaCargaEvidencia)
VALUES
(
    @SolicitudID,    @OrdenCompraID,    GETDATE(),    @UsuarioID,    @Comentarios,    @EvidenciaRecepcionPath,    @NombreArchivoEvidencia,    @ExtensionArchivoEvidencia,    @ContentTypeEvidencia,
   @TamanoBytesEvidencia,   GETDATE()
)";

                        string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();

                        _sftp.AsegurarDirectorio(rutaContenedor);

                        string folioStr = solicitudId.ToString().PadLeft(5, '0');
                        string nombreOriginalEvidencia = Path.GetFileName(evidenciaRecepcion.FileName);

                        string nombreArchivoEvidencia =
                            $"COM-{folioStr}_RecepcionAlmacen_{Guid.NewGuid()}{extensionEvidencia}";

                        string rutaArchivoEvidenciaSftp =
                            $"{rutaContenedor}/{nombreArchivoEvidencia}";

                        using (var stream = evidenciaRecepcion.OpenReadStream())
                        {
                            _sftp.SubirStream(stream, rutaArchivoEvidenciaSftp);
                        }

                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@OrdenCompraID", ordenCompraId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@EvidenciaRecepcionPath", rutaArchivoEvidenciaSftp);
                            cmd.Parameters.AddWithValue("@NombreArchivoEvidencia", nombreOriginalEvidencia);
                            cmd.Parameters.AddWithValue("@ExtensionArchivoEvidencia", extensionEvidencia);
                            cmd.Parameters.AddWithValue("@ContentTypeEvidencia", evidenciaRecepcion.ContentType ?? "application/octet-stream");
                            cmd.Parameters.AddWithValue("@TamanoBytesEvidencia", evidenciaRecepcion.Length);

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
SET EstatusID = 10
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 8";

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
(@SolicitudID, 10, GETDATE(), @Responsable)";

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
        public async Task<IActionResult> RegistrarEntregaUsuario(    int solicitudId, string nombreRecibe,  string? comentarios,  IFormFile? evidenciaEntrega)
        {
            int usuarioEntregaId = ObtenerUsuarioIdActual();
            if (usuarioEntregaId == 0) return Unauthorized();

            if (string.IsNullOrWhiteSpace(nombreRecibe))
            {
                TempData["Error"] = "Debes capturar quién recibe la mercancía.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (evidenciaEntrega == null || evidenciaEntrega.Length == 0)
            {
                TempData["Error"] = "Debes adjuntar evidencia de entrega al usuario.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

            string extensionEvidencia =
                Path.GetExtension(evidenciaEntrega.FileName)?.ToLowerInvariant() ?? "";

            if (!extensionesPermitidas.Contains(extensionEvidencia))
            {
                TempData["Error"] = "La evidencia de entrega solo puede ser PDF o imagen JPG, PNG o WEBP.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceCompras =
    await UsuarioPerteneceADepartamentoAsync(
        conn,
        usuarioEntregaId,
        "COMPRAS"
    );

                if (!perteneceCompras)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int usuarioSolicitanteId;

                        string sqlSolicitud = @"
                    SELECT UsuarioID
                    FROM Compras_Solicitud
                    WHERE SolicitudID = @SolicitudID
                      AND EstatusID = 9";

                        using (var cmdSol = new SqlCommand(sqlSolicitud, conn, trans))
                        {
                            cmdSol.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var result = await cmdSol.ExecuteScalarAsync();

                            if (result == null || result == DBNull.Value)
                            {
                                trans.Rollback();
                                TempData["Error"] = "Solo puedes registrar entrega al usuario cuando la solicitud está pendiente de entrega directa.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            usuarioSolicitanteId = Convert.ToInt32(result);
                        }

                        string rutaContenedor = _rutaNas.ObtenerRutaSolicitudesCompras();

                        _sftp.AsegurarDirectorio(rutaContenedor);

                        string folioStr = solicitudId.ToString().PadLeft(5, '0');
                        string nombreOriginalEvidencia = Path.GetFileName(evidenciaEntrega.FileName);

                        string nombreArchivoEvidencia =
                            $"COM-{folioStr}_EntregaUsuario_{Guid.NewGuid()}{extensionEvidencia}";

                        string rutaArchivoEvidenciaSftp =
                            $"{rutaContenedor}/{nombreArchivoEvidencia}";

                        using (var stream = evidenciaEntrega.OpenReadStream())
                        {
                            _sftp.SubirStream(stream, rutaArchivoEvidenciaSftp);
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
    EvidenciaEntregaPath,
    NombreArchivoEvidencia,
    ExtensionArchivoEvidencia,
    ContentTypeEvidencia,
    TamanoBytesEvidencia,
    FechaCargaEvidencia,
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
    @EvidenciaEntregaPath,
    @NombreArchivoEvidencia,
    @ExtensionArchivoEvidencia,
    @ContentTypeEvidencia,
    @TamanoBytesEvidencia,
    GETDATE(),
    1
)";
                        using (var cmd = new SqlCommand(sqlInsert, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@UsuarioEntregaID", usuarioEntregaId);
                            cmd.Parameters.AddWithValue("@UsuarioRecibeID", usuarioSolicitanteId);
                            cmd.Parameters.AddWithValue("@NombreRecibe", nombreRecibe);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);

                            cmd.Parameters.AddWithValue("@EvidenciaEntregaPath", rutaArchivoEvidenciaSftp);
                            cmd.Parameters.AddWithValue("@NombreArchivoEvidencia", nombreOriginalEvidencia);
                            cmd.Parameters.AddWithValue("@ExtensionArchivoEvidencia", extensionEvidencia);
                            cmd.Parameters.AddWithValue("@ContentTypeEvidencia", evidenciaEntrega.ContentType ?? "application/octet-stream");
                            cmd.Parameters.AddWithValue("@TamanoBytesEvidencia", evidenciaEntrega.Length);

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
                    SET EstatusID = 10
                    WHERE SolicitudID = @SolicitudID
                      AND EstatusID = 9";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            int filasUpdate = await cmd.ExecuteNonQueryAsync();

                            if (filasUpdate == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está disponible para entrega al usuario.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHistoricoCierre = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 10, GETDATE(), @Responsable)";

                        using (var cmdHistCierre = new SqlCommand(sqlHistoricoCierre, conn, trans))
                        {
                            cmdHistCierre.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHistCierre.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");

                            await cmdHistCierre.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                     //   await NotificarCuentasPorPagar_MaterialEntregadoAsync(solicitudId);


                        TempData["Mensaje"] = "Mercancía entregada al usuario.";
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
       WHERE S.EstatusID = 8
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


        #region CODIGO LEGADO - SELECCION DE COTIZACION CON PROCESO PRESUPUESTAL

        /*
         * Metodo anterior del flujo completo.
         * Permitía seleccionar o cambiar una cotizacion y enviaba la solicitud
         * del estado 2 al estado 3. Se conserva completo como referencia.
         */

#if false
        [HttpPost("SeleccionarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarCotizacion(
     int solicitudId,
     int cotizacionId,
     string? comentarios,
     string? motivoCambio)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0) return Unauthorized();

            comentarios = comentarios?.Trim();
            motivoCambio = motivoCambio?.Trim();

            if (!string.IsNullOrWhiteSpace(comentarios) && comentarios.Length > 500)
            {
                TempData["Error"] = "El comentario no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            if (!string.IsNullOrWhiteSpace(motivoCambio) && motivoCambio.Length > 500)
            {
                TempData["Error"] = "El motivo del cambio no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                bool perteneceCompras =
                    await UsuarioPerteneceADepartamentoAsync(conn, usuarioId, "COMPRAS");

                if (!perteneceCompras)
                    return Forbid();

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int estatusActual;
                        int? cotizacionAnteriorId = null;

                        string sqlDatosSolicitud = @"
SELECT 
    S.EstatusID,
    S.CotizacionSeleccionadaID
FROM Compras_Solicitud S
WHERE S.SolicitudID = @SolicitudID";

                        using (var cmdDatos = new SqlCommand(sqlDatosSolicitud, conn, trans))
                        {
                            cmdDatos.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            using (var reader = await cmdDatos.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "No se encontró la solicitud.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                estatusActual = Convert.ToInt32(reader["EstatusID"]);

                                cotizacionAnteriorId =
                                    reader["CotizacionSeleccionadaID"] == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(reader["CotizacionSeleccionadaID"]);
                            }
                        }

                        if (estatusActual != 2 && estatusActual != 3)
                        {
                            trans.Rollback();
                            TempData["Error"] = "Solo se puede seleccionar o cambiar la cotización cuando la solicitud está cotizada o pendiente de documentación del usuario.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }

                        bool esPrimeraDictaminacion = !cotizacionAnteriorId.HasValue;

                        bool esCambio =
                            cotizacionAnteriorId.HasValue &&
                            cotizacionAnteriorId.Value != cotizacionId;

                        if (cotizacionAnteriorId.HasValue &&
                            cotizacionAnteriorId.Value == cotizacionId)
                        {
                            trans.Rollback();
                            TempData["Error"] = "La cotización seleccionada ya es la cotización dictaminada.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }

                        if (esCambio && string.IsNullOrWhiteSpace(motivoCambio))
                        {
                            trans.Rollback();
                            TempData["Error"] = "Debes capturar el motivo del cambio de cotización.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }

                        string sqlValidarCotizacion = @"
SELECT COUNT(*)
FROM Compras_Cotizaciones
WHERE SolicitudID = @SolicitudID
  AND CotizacionID = @CotizacionID";

                        using (var cmdVal = new SqlCommand(sqlValidarCotizacion, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdVal.Parameters.AddWithValue("@CotizacionID", cotizacionId);

                            int valido = Convert.ToInt32(await cmdVal.ExecuteScalarAsync());

                            if (valido == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La cotización seleccionada no pertenece a esta solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET CotizacionSeleccionadaID = @CotizacionID,
    FechaSeleccionCotizacion = GETDATE(),
    UsuarioSeleccionCotizacionID = @UsuarioID,
    ComentariosSeleccionUsuario = @Comentarios,
    EstatusID = 3,
    FechaDictamen = NULL,
    TipoGasto = NULL,
    DentroPresupuesto = NULL,
    NumeroRequisicion = NULL,
    ObservacionesPresupuesto = NULL
WHERE SolicitudID = @SolicitudID
  AND EstatusID IN (2, 3)
  AND (
        (@CotizacionAnteriorID IS NULL AND CotizacionSeleccionadaID IS NULL)
        OR CotizacionSeleccionadaID = @CotizacionAnteriorID
      )";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@CotizacionID", cotizacionId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                            cmd.Parameters.AddWithValue("@Comentarios", (object?)comentarios ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                            var paramCotAnterior = new SqlParameter("@CotizacionAnteriorID", SqlDbType.Int);
                            paramCotAnterior.Value = cotizacionAnteriorId.HasValue
                                ? cotizacionAnteriorId.Value
                                : DBNull.Value;

                            cmd.Parameters.Add(paramCotAnterior);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está disponible para dictaminar cotización o la cotización fue modificada por otro usuario.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        if (esCambio)
                        {
                            string sqlHistCambio = @"
INSERT INTO Compras_CotizacionCambios_Historico
(
    SolicitudID,
    CotizacionAnteriorID,
    CotizacionNuevaID,
    UsuarioCambioID,
    FechaCambio,
    MotivoCambio,
    EstatusAnteriorID,
    EstatusNuevoID
)
VALUES
(
    @SolicitudID,
    @CotizacionAnteriorID,
    @CotizacionNuevaID,
    @UsuarioCambioID,
    GETDATE(),
    @MotivoCambio,
    @EstatusAnteriorID,
    3
)";

                            using (var cmdHistCambio = new SqlCommand(sqlHistCambio, conn, trans))
                            {
                                cmdHistCambio.Parameters.AddWithValue("@SolicitudID", solicitudId);
                                cmdHistCambio.Parameters.AddWithValue("@CotizacionAnteriorID", cotizacionAnteriorId.Value);
                                cmdHistCambio.Parameters.AddWithValue("@CotizacionNuevaID", cotizacionId);
                                cmdHistCambio.Parameters.AddWithValue("@UsuarioCambioID", usuarioId);
                                cmdHistCambio.Parameters.AddWithValue("@MotivoCambio", motivoCambio);
                                cmdHistCambio.Parameters.AddWithValue("@EstatusAnteriorID", estatusActual);

                                await cmdHistCambio.ExecuteNonQueryAsync();
                            }

                            string sqlInvalidarDocs = @"
UPDATE Compras_Archivos
SET Vigente = 0
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo IN ('DESVIACION', 'FORMATO_REQUISICION')
  AND Vigente = 1
  AND Activo = 1";

                            using (var cmdInvalidarDocs = new SqlCommand(sqlInvalidarDocs, conn, trans))
                            {
                                cmdInvalidarDocs.Parameters.AddWithValue("@SolicitudID", solicitudId);
                                await cmdInvalidarDocs.ExecuteNonQueryAsync();
                            }



                        }

                        string comentarioHistorico = esCambio
                            ? "Cambio de cotización dictaminada por Compras"
                            : "Cotización dictaminada por Compras";

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

                       
                        await NotificarSolicitante_CotizacionDictaminadaAsync(solicitudId, esCambio);

                        TempData["Mensaje"] = esCambio
    ? "Cotización cambiada correctamente. El usuario deberá revisar nuevamente la documentación presupuestal."
    : "Cotización seleccionada correctamente. La solicitud quedó pendiente de documentación del usuario.";

                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        _logger.LogError(ex, "Error al dictaminar o cambiar cotización");

                        TempData["Error"] = "Solo se puede seleccionar o cambiar la cotización cuando la solicitud está cotizada o pendiente de documentación del usuario.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
            }     
        }

#endif

        #endregion

        #region FLUJO ACTUAL - SELECCIONAR COTIZACION Y CERRAR COMPRA

        [HttpPost("SeleccionarCotizacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarCotizacion(
            int solicitudId,
            int cotizacionId,
            string? comentarios,
            string? motivoCambio)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            if (solicitudId <= 0 || cotizacionId <= 0)
            {
                TempData["Error"] = "No se recibio correctamente la solicitud o la cotizacion.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

            comentarios = comentarios?.Trim();

            // El parametro se conserva para mantener compatibilidad con el formulario anterior.
            // En el flujo actual no se permite cambiar una cotizacion despues del cierre.
            motivoCambio = motivoCambio?.Trim();

            if (!string.IsNullOrWhiteSpace(comentarios) && comentarios.Length > 500)
            {
                TempData["Error"] = "El comentario no puede superar los 500 caracteres.";
                return RedirectToAction("Detalle", new { id = solicitudId });
            }

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

                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // La solicitud debe estar en Cotizaciones cargadas y no tener
                        // una cotizacion seleccionada previamente.
                        string sqlValidarSolicitud = @"
SELECT COUNT(*)
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 2
  AND CotizacionSeleccionadaID IS NULL;";

                        using (var cmdValidarSolicitud =
                               new SqlCommand(sqlValidarSolicitud, conn, trans))
                        {
                            cmdValidarSolicitud.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );

                            int solicitudValida = Convert.ToInt32(
                                await cmdValidarSolicitud.ExecuteScalarAsync()
                            );

                            if (solicitudValida == 0)
                            {
                                trans.Rollback();

                                TempData["Error"] =
                                    "La solicitud ya no esta disponible para seleccionar una cotizacion. " +
                                    "Debe encontrarse en el estado Cotizaciones cargadas.";

                                return RedirectToAction(
                                    "Detalle",
                                    new { id = solicitudId }
                                );
                            }
                        }

                        // La cotizacion debe pertenecer a la misma solicitud.
                        string sqlValidarCotizacion = @"
SELECT COUNT(*)
FROM Compras_Cotizaciones
WHERE SolicitudID = @SolicitudID
  AND CotizacionID = @CotizacionID;";

                        using (var cmdValidarCotizacion =
                               new SqlCommand(sqlValidarCotizacion, conn, trans))
                        {
                            cmdValidarCotizacion.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );

                            cmdValidarCotizacion.Parameters.AddWithValue(
                                "@CotizacionID",
                                cotizacionId
                            );

                            int cotizacionValida = Convert.ToInt32(
                                await cmdValidarCotizacion.ExecuteScalarAsync()
                            );

                            if (cotizacionValida == 0)
                            {
                                trans.Rollback();

                                TempData["Error"] =
                                    "La cotizacion seleccionada no pertenece a esta solicitud.";

                                return RedirectToAction(
                                    "Detalle",
                                    new { id = solicitudId }
                                );
                            }
                        }

                        // Nuevo flujo: estado 2 -> estado 10.
                        string sqlActualizarSolicitud = @"
UPDATE Compras_Solicitud
SET CotizacionSeleccionadaID = @CotizacionID,
    FechaSeleccionCotizacion = GETDATE(),
    UsuarioSeleccionCotizacionID = @UsuarioID,
    ComentariosSeleccionUsuario = @Comentarios,
    EstatusID = 10
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 2
  AND CotizacionSeleccionadaID IS NULL;";

                        using (var cmdActualizar =
                               new SqlCommand(sqlActualizarSolicitud, conn, trans))
                        {
                            cmdActualizar.Parameters.AddWithValue(
                                "@CotizacionID",
                                cotizacionId
                            );

                            cmdActualizar.Parameters.AddWithValue(
                                "@UsuarioID",
                                usuarioId
                            );

                            cmdActualizar.Parameters.AddWithValue(
                                "@Comentarios",
                                (object?)comentarios ?? DBNull.Value
                            );

                            cmdActualizar.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );

                            int filasActualizadas =
                                await cmdActualizar.ExecuteNonQueryAsync();

                            if (filasActualizadas == 0)
                            {
                                trans.Rollback();

                                TempData["Error"] =
                                    "No se pudo cerrar la solicitud. " +
                                    "Es posible que haya sido modificada por otro usuario.";

                                return RedirectToAction(
                                    "Detalle",
                                    new { id = solicitudId }
                                );
                            }
                        }

                        // Registrar el cierre en el historial dentro de la misma transaccion.
                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    10,
    GETDATE(),
    @Responsable
);";

                        using (var cmdHistorico =
                               new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmdHistorico.Parameters.AddWithValue(
                                "@SolicitudID",
                                solicitudId
                            );

                            cmdHistorico.Parameters.AddWithValue(
                                "@Responsable",
                                User.Identity?.Name ?? "Sistema"
                            );

                            await cmdHistorico.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        // El correo se envia despues de confirmar la transaccion.
                        await NotificarSolicitante_CompraCerradaAsync(solicitudId);

                        TempData["Mensaje"] =
                            "Cotizacion seleccionada correctamente. " +
                            "La solicitud de compra ha sido cerrada.";

                        return RedirectToAction(
                            "Detalle",
                            new { id = solicitudId }
                        );
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(
                            ex,
                            "Error al seleccionar cotizacion y cerrar la solicitud {SolicitudID}",
                            solicitudId
                        );

                        TempData["Error"] =
                            "No se pudo seleccionar la cotizacion ni cerrar la solicitud.";

                        return RedirectToAction(
                            "Detalle",
                            new { id = solicitudId }
                        );
                    }
                }
            }
        }

        #endregion

        //METODOS PARA LA CONFIRMACION  DE DOCUMENTOS

        [HttpPost("ConfirmarDocumentacionPresupuestal")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarDocumentacionPresupuestal(
    int solicitudId,
    IFormFile? archivoDesviacion,
    IFormFile? archivoFormatoRequisicion)
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
                        int cotizacionSeleccionadaId;
                        bool fueraPresupuestoUsuario;

                        string sqlValidar = @"
SELECT 
    CotizacionSeleccionadaID,
    FueraPresupuestoUsuario
FROM Compras_Solicitud
WHERE SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND EstatusID = 3
  AND CotizacionSeleccionadaID IS NOT NULL";

                        using (var cmdVal = new SqlCommand(sqlValidar, conn, trans))
                        {
                            cmdVal.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdVal.Parameters.AddWithValue("@UsuarioID", usuarioId);

                            using (var reader = await cmdVal.ExecuteReaderAsync())
                            {
                                if (!await reader.ReadAsync())
                                {
                                    trans.Rollback();
                                    TempData["Error"] = "La solicitud no está disponible para enviar a Control Presupuestal.";
                                    return RedirectToAction("Detalle", new { id = solicitudId });
                                }

                                cotizacionSeleccionadaId = Convert.ToInt32(reader["CotizacionSeleccionadaID"]);

                                fueraPresupuestoUsuario =
                                    reader["FueraPresupuestoUsuario"] != DBNull.Value
                                    && Convert.ToBoolean(reader["FueraPresupuestoUsuario"]);

                               
                            }
                        }

                        if (fueraPresupuestoUsuario && (archivoDesviacion == null || archivoDesviacion.Length == 0))
                        {
                            trans.Rollback();
                            TempData["Error"] = "Debes adjuntar la desviación presupuestal antes de enviar a Control Presupuestal.";
                            return RedirectToAction("Detalle", new { id = solicitudId });
                        }


                        if (archivoDesviacion != null && archivoDesviacion.Length > 0)
                        {
                            string extensionDesviacion =
                                Path.GetExtension(archivoDesviacion.FileName)?.ToLowerInvariant() ?? "";

                            if (!ExtensionPermitida(extensionDesviacion, ".pdf", ".jpg", ".jpeg", ".png", ".webp"))
                            {
                                trans.Rollback();
                                TempData["Error"] = "La desviación solo puede ser PDF o imagen JPG, PNG o WEBP.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            await GuardarArchivoCompraAsync(
                                conn,
                                trans,
                                solicitudId,
                                cotizacionSeleccionadaId,
                                "DESVIACION",
                                archivoDesviacion,
                                usuarioId,
                                _rutaNas.ObtenerRutaComprasDesviaciones(),
                                "Desviacion"
                            );
                        }

                        if (archivoFormatoRequisicion != null && archivoFormatoRequisicion.Length > 0)
                        {
                            string extensionRequisicion =
                                Path.GetExtension(archivoFormatoRequisicion.FileName)?.ToLowerInvariant() ?? "";

                            if (!ExtensionPermitida(extensionRequisicion, ".xlsx", ".xls", ".pdf"))
                            {
                                trans.Rollback();
                                TempData["Error"] = "El formato de requisición solo puede ser Excel o PDF.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }

                            await GuardarArchivoCompraAsync(
                                conn,
                                trans,
                                solicitudId,
                                cotizacionSeleccionadaId,
                                "FORMATO_REQUISICION",
                                archivoFormatoRequisicion,
                                usuarioId,
                                _rutaNas.ObtenerRutaComprasRequisiciones(),
                                "FormatoRequisicion"
                            );
                        }

                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 4
WHERE SolicitudID = @SolicitudID
  AND UsuarioID = @UsuarioID
  AND EstatusID = 3";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "No se pudo actualizar la solicitud.";
                                return RedirectToAction("Detalle", new { id = solicitudId });
                            }
                        }

                        string sqlHist = @"
INSERT INTO Compras_Historico_Pasos
(SolicitudID, EstatusID, FechaMovimiento, UsuarioResponsable)
VALUES
(@SolicitudID, 4, GETDATE(), @Responsable)";

                        using (var cmdHist = new SqlCommand(sqlHist, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@Responsable", User.Identity?.Name ?? "Sistema");

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarControlPresupuestal_CotizacionSeleccionadaAsync(solicitudId);

                        TempData["Mensaje"] = "Documentación confirmada. La solicitud pasó a Control Presupuestal.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al confirmar documentación presupuestal");

                        TempData["Error"] = "No se pudo enviar la solicitud a Control Presupuestal.";
                        return RedirectToAction("Detalle", new { id = solicitudId });
                    }
                }
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

                string asunto = $"Cotizaciones cargadas y solicitud cerrada - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Cotizaciones cargadas y proceso finalizado</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Compras ha cargado las cotizaciones correspondientes a tu solicitud.</p>
      <p>De acuerdo con el flujo actual, la solicitud quedó cerrada automáticamente y su progreso llegó al 100 %.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, si deseas consultar las cotizaciones y el detalle de la solicitud cerrada.</p>
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


        private async Task NotificarSolicitante_CotizacionDictaminadaAsync(
    int solicitudId,
    bool esCambio)
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
    C.Proveedor,
    C.MontoTotal
FROM Compras_Solicitud S
INNER JOIN Usuarios U 
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P 
    ON U.PersonaID = P.PersonaID
LEFT JOIN Compras_Cotizaciones C 
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);

                string folio = rd["Folio"]?.ToString()
                    ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";

                string solicitante = rd["Solicitante"]?.ToString()
                    ?? "Solicitante";

                string proveedor = rd["Proveedor"] == DBNull.Value
                    ? "N/A"
                    : rd["Proveedor"].ToString();

                decimal monto = rd["MontoTotal"] == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(rd["MontoTotal"]);

                string asunto = esCambio
                    ? $"Cambio de cotización dictaminada por Compras - {folio}"
                    : $"Cotización dictaminada por Compras - {folio}";

                string titulo = esCambio
                    ? "Cotización actualizada por Compras"
                    : "Cotización dictaminada por Compras";

                string mensaje = esCambio
                    ? "Compras ha cambiado la cotización dictaminada para tu solicitud."
                    : "Compras ha dictaminado la cotización que procede para tu solicitud.";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#2563eb; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>{titulo}</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>{System.Net.WebUtility.HtmlEncode(mensaje)}</p>

      <div style='background:#eff6ff; border-left:4px solid #2563eb; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0 0 6px;'><strong>Proveedor:</strong> {System.Net.WebUtility.HtmlEncode(proveedor)}</p>
        <p style='margin:0;'><strong>Monto:</strong> {monto:C}</p>
      </div>

      <p>Ahora debes revisar la cotización seleccionada y confirmar la documentación presupuestal.</p>
<p>Si aplica, carga la desviación o el formato de requisición antes de enviarla a Control Presupuestal.</p>

      <p>Puedes consultar el detalle en la Intranet, módulo <strong>Compras</strong>.</p>
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
                _logger.LogError(
                    ex,
                    "Error enviando correo de cotización dictaminada al solicitante. SolicitudID={SolicitudID}",
                    solicitudId
                );
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
      <p>Compras ha dictaminado la cotización que procede. La solicitud está lista para revisión de Control Presupuestal.</p>

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



        //METODO PARA EXPORTAR EL SEGUIMIENTO A EXCEL
        [HttpGet("ExportarSeguimientoExcel")]
        public async Task<IActionResult> ExportarSeguimientoExcel(
            int? estatus,
            string? departamento,
            string? comprador,
            bool? soloRetrasadas)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            var solicitudes = new List<SeguimientoCompraItemVm>();

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

                puesto = puesto.ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                    return Forbid();

                using (var cmd = new SqlCommand("sp_Compras_SeguimientoDireccion", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            solicitudes.Add(new SeguimientoCompraItemVm
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

                                DiasEnEstatus =
                                    reader["DiasEnEstatus"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasEnEstatus"]),

                                DiasCotizando =
                                    reader["DiasCotizando"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasCotizando"]),

                                MontoCotizado =
                                    reader["MontoCotizado"] == DBNull.Value
                                        ? 0
                                        : Convert.ToDecimal(reader["MontoCotizado"]),

                                DiasPermitidos =
                                    reader["DiasPermitidos"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasPermitidos"]),

                                DiasHabilesTranscurridos =
                                    reader["DiasHabilesTranscurridos"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasHabilesTranscurridos"]),

                                SemaforoTexto = reader["SemaforoTexto"].ToString(),

                                // En el flujo actual representa el tiempo total
                                // desde la creacion hasta el cierre o hasta hoy.
                                DiasCompras =
                                    reader["DiasCompras"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasCompras"]),

                                // Se conservan para solicitudes historicas y para
                                // mantener compatibilidad con el procedimiento.
                                DiasPresupuesto =
                                    reader["DiasPresupuesto"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasPresupuesto"]),

                                DiasOC =
                                    reader["DiasOC"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasOC"]),

                                DiasProveedor =
                                    reader["DiasProveedor"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasProveedor"]),

                                DiasAlmacen =
                                    reader["DiasAlmacen"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["DiasAlmacen"])
                            });
                        }
                    }
                }
            }

            if (estatus.HasValue)
            {
                solicitudes = solicitudes
                    .Where(x => x.EstatusID == estatus.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(departamento))
            {
                solicitudes = solicitudes
                    .Where(x =>
                        x.Departamento != null &&
                        x.Departamento.Contains(
                            departamento,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(comprador))
            {
                solicitudes = solicitudes
                    .Where(x =>
                        x.CompradorAsignado != null &&
                        x.CompradorAsignado.Contains(
                            comprador,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
            }

            if (soloRetrasadas == true)
            {
                solicitudes = solicitudes
                    .Where(x => x.SemaforoTexto == "Retrasada")
                    .ToList();
            }

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Seguimiento Compras");

                /*
                 * COLUMNAS DEL FLUJO ACTUAL
                 *
                 * 1 -> Solicitada / Pendiente de cotizacion
                 * 2 -> Cotizaciones cargadas
                 * 10 -> Cerrada
                 */
                string[] headers =
                {
                    "Folio",
                    "Fecha creación",
                    "Solicitante",
                    "Departamento",
                    "Empresa",
                    "Tipo compra",
                    "Comprador asignado",
                    "Estatus",
                    "Urgencia",
                    "Último movimiento",
                    "Días en estatus",
                    "Días para cotizar",
                    "Días totales transcurridos",
                    "Días permitidos",
                    "Semáforo",
                    "Monto cotizado",
                    "Días totales del proceso"
                };

#if false
                /*
                 * CODIGO LEGADO - COLUMNAS DEL PROCESO ANTERIOR
                 *
                 * Se conserva como referencia. Ya no se muestran en el Excel
                 * porque las nuevas solicitudes no pasan por Presupuestos,
                 * O.C., Proveedor ni Almacen.
                 */
                string[] headersProcesoAnterior =
                {
                    "Folio",
                    "Fecha creación",
                    "Solicitante",
                    "Departamento",
                    "Empresa",
                    "Tipo compra",
                    "Comprador asignado",
                    "Estatus",
                    "Urgencia",
                    "Días transcurridos",
                    "Días permitidos",
                    "Semáforo",
                    "Monto cotizado",
                    "Días Compras",
                    "Días Presupuesto",
                    "Días O.C.",
                    "Días Proveedor",
                    "Días Almacén"
                };
#endif

                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                }

                int row = 2;

                foreach (var item in solicitudes)
                {
                    ws.Cell(row, 1).Value = item.Folio;
                    ws.Cell(row, 2).Value = item.FechaCreacion;
                    ws.Cell(row, 3).Value = item.Solicitante;
                    ws.Cell(row, 4).Value = item.Departamento;
                    ws.Cell(row, 5).Value = item.Empresa;
                    ws.Cell(row, 6).Value = item.TipoCompra;
                    ws.Cell(row, 7).Value = item.CompradorAsignado;
                    ws.Cell(row, 8).Value = item.Estatus;
                    ws.Cell(row, 9).Value = item.Urgencia;

                    if (item.FechaUltimoMovimiento.HasValue)
                    {
                        ws.Cell(row, 10).Value = item.FechaUltimoMovimiento.Value;
                    }
                    else
                    {
                        ws.Cell(row, 10).Value = "Sin movimientos";
                    }

                    ws.Cell(row, 11).Value = item.DiasEnEstatus;
                    ws.Cell(row, 12).Value = item.DiasCotizando;
                    ws.Cell(row, 13).Value = item.DiasHabilesTranscurridos;
                    ws.Cell(row, 14).Value = item.DiasPermitidos;
                    ws.Cell(row, 15).Value = item.SemaforoTexto;
                    ws.Cell(row, 16).Value = item.MontoCotizado;
                    ws.Cell(row, 17).Value = item.DiasCompras;

#if false
                    /*
                     * CODIGO LEGADO - ESCRITURA DE METRICAS ANTERIORES
                     *
                     * ws.Cell(row, 14).Value = item.DiasCompras;
                     * ws.Cell(row, 15).Value = item.DiasPresupuesto;
                     * ws.Cell(row, 16).Value = item.DiasOC;
                     * ws.Cell(row, 17).Value = item.DiasProveedor;
                     * ws.Cell(row, 18).Value = item.DiasAlmacen;
                     */
#endif

                    row++;
                }

                int ultimaFila = Math.Max(row - 1, 1);
                int ultimaColumna = headers.Length;

                var rango = ws.Range(1, 1, ultimaFila, ultimaColumna);
                rango.CreateTable("TablaSeguimientoCompras");

                ws.Row(1).Style.Font.Bold = true;
                ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                ws.Row(1).Style.Font.FontColor = XLColor.White;
                ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Row(1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Style.Alignment.WrapText = true;

                ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                ws.Column(10).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                ws.Column(16).Style.NumberFormat.Format = "$#,##0.00";

                ws.Columns(11, 14).Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;

                ws.Column(17).Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;

                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents();

                // Evita columnas excesivamente anchas por textos largos.
                foreach (var column in ws.ColumnsUsed())
                {
                    if (column.Width > 45)
                        column.Width = 45;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var contenido = stream.ToArray();

                    string nombreArchivo =
                        $"Seguimiento_Compras_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                    return File(
                        contenido,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        nombreArchivo
                    );
                }
            }
        }


        //ENDPOINT JASON PARA OBTENER LA CARGA POR COMPRADOR
        [HttpGet("CargaCompradoresSeguimiento")]
        public async Task<IActionResult> CargaCompradoresSeguimiento(int anio, int mes)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            if (anio < 2020 || mes < 1 || mes > 12)
                return BadRequest("Mes o año inválido.");

            var inicioMes = new DateTime(anio, mes, 1);
            var inicioMesSiguiente = inicioMes.AddMonths(1);

            var data = new List<object>();

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

                puesto = puesto.ToUpper().Trim();

                bool esDireccion =
                    puesto.Contains("DIRECCION") ||
                    puesto.Contains("DIRECCIÓN") ||
                    puesto.Contains("DIRECTOR") ||
                    puesto.Contains("DIRECTORA") ||
                    puesto.StartsWith("DIR.");

                if (!esDireccion)
                    return Forbid();

                string sql = @"
SELECT
    ISNULL(P.Nombre + ' ' + P.ApellidoPaterno, 'Sin asignar') AS Comprador,
    SUM(CASE WHEN S.FechaCotizacion IS NOT NULL THEN 1 ELSE 0 END) AS Atendidas,
    SUM(CASE WHEN S.EstatusID = 1 THEN 1 ELSE 0 END) AS Pendientes,
    COUNT(S.SolicitudID) AS Total
FROM Compras_Solicitud S
LEFT JOIN Usuarios U
    ON S.CompradorAsignadoUsuarioID = U.UsuarioID
LEFT JOIN Persona P
    ON U.PersonaID = P.PersonaID
WHERE S.FechaCotizacion >= @InicioMes
  AND S.FechaCotizacion < @InicioMesSiguiente
GROUP BY
    ISNULL(P.Nombre + ' ' + P.ApellidoPaterno, 'Sin asignar')
ORDER BY Atendidas DESC";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@InicioMes", inicioMes);
                    cmd.Parameters.AddWithValue("@InicioMesSiguiente", inicioMesSiguiente);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new
                            {
                                comprador = reader["Comprador"].ToString(),
                                atendidas = Convert.ToInt32(reader["Atendidas"]),
                                pendientes = Convert.ToInt32(reader["Pendientes"]),
                                total = Convert.ToInt32(reader["Total"])
                            });
                        }
                    }
                }
            }

            return Json(data);
        }

        private async Task<int> GuardarArchivoCompraAsync(
            SqlConnection conn,
            SqlTransaction trans,
            int solicitudId,
            int? cotizacionId,
            string tipoArchivo,
            IFormFile archivo,
            int usuarioSubioId,
            string rutaContenedor,
            string prefijoNombre)
        {
            if (archivo == null || archivo.Length == 0)
                throw new Exception("No se recibió archivo para guardar.");

            string extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";

            string nombreOriginal = Path.GetFileName(archivo.FileName);

            string folioStr = solicitudId.ToString().PadLeft(5, '0');

            string nombreSistema =
                $"COM-{folioStr}_{prefijoNombre}_{Guid.NewGuid()}{extension}";

            _sftp.AsegurarDirectorio(rutaContenedor);

            string rutaArchivoSftp = $"{rutaContenedor}/{nombreSistema}";

            using (var stream = archivo.OpenReadStream())
            {
                _sftp.SubirStream(stream, rutaArchivoSftp);
            }

            string sqlVersion = @"
SELECT ISNULL(MAX(VersionArchivo), 0) + 1
FROM Compras_Archivos
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo = @TipoArchivo";

            int versionArchivo;

            using (var cmdVersion = new SqlCommand(sqlVersion, conn, trans))
            {
                cmdVersion.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdVersion.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);

                versionArchivo = Convert.ToInt32(await cmdVersion.ExecuteScalarAsync());
            }

            string sqlDesactivarAnteriores = @"
UPDATE Compras_Archivos
SET Vigente = 0
WHERE SolicitudID = @SolicitudID
  AND TipoArchivo = @TipoArchivo
  AND Vigente = 1
  AND Activo = 1";

            using (var cmdDesactivar = new SqlCommand(sqlDesactivarAnteriores, conn, trans))
            {
                cmdDesactivar.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdDesactivar.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);

                await cmdDesactivar.ExecuteNonQueryAsync();
            }

            string sqlInsert = @"
INSERT INTO Compras_Archivos
(
    SolicitudID,
    CotizacionID,
    TipoArchivo,
    RutaArchivo,
    NombreOriginal,
    NombreSistema,
    Extension,
    ContentType,
    TamanoBytes,
    VersionArchivo,
    Vigente,
    Activo,
    UsuarioSubioID,
    FechaCarga
)
VALUES
(
    @SolicitudID,
    @CotizacionID,
    @TipoArchivo,
    @RutaArchivo,
    @NombreOriginal,
    @NombreSistema,
    @Extension,
    @ContentType,
    @TamanoBytes,
    @VersionArchivo,
    1,
    1,
    @UsuarioSubioID,
    GETDATE()
);

SELECT SCOPE_IDENTITY();";

            using (var cmdInsert = new SqlCommand(sqlInsert, conn, trans))
            {
                cmdInsert.Parameters.AddWithValue("@SolicitudID", solicitudId);
                cmdInsert.Parameters.AddWithValue("@CotizacionID", (object?)cotizacionId ?? DBNull.Value);
                cmdInsert.Parameters.AddWithValue("@TipoArchivo", tipoArchivo);
                cmdInsert.Parameters.AddWithValue("@RutaArchivo", rutaArchivoSftp);
                cmdInsert.Parameters.AddWithValue("@NombreOriginal", nombreOriginal);
                cmdInsert.Parameters.AddWithValue("@NombreSistema", nombreSistema);
                cmdInsert.Parameters.AddWithValue("@Extension", extension);
                cmdInsert.Parameters.AddWithValue("@ContentType", archivo.ContentType ?? "application/octet-stream");
                cmdInsert.Parameters.AddWithValue("@TamanoBytes", archivo.Length);
                cmdInsert.Parameters.AddWithValue("@VersionArchivo", versionArchivo);
                cmdInsert.Parameters.AddWithValue("@UsuarioSubioID", usuarioSubioId);

                return Convert.ToInt32(await cmdInsert.ExecuteScalarAsync());
            }
        }

        private bool ExtensionPermitida(string extension, params string[] permitidas)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            return permitidas.Contains(extension.ToLowerInvariant());
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarDocumentosPresupuesto(int solicitudId, string observaciones)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return Unauthorized();

            if (solicitudId <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud.";
                return RedirectToAction("BandejaPresupuestos");
            }

            if (string.IsNullOrWhiteSpace(observaciones))
            {
                TempData["Error"] = "Debes indicar qué documentación falta o debe corregirse.";
                return RedirectToAction("Dictamen", new { id = solicitudId });
            }

            string responsable = User.Identity?.Name ?? "Control Presupuestal";

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
                        string sqlUpdate = @"
UPDATE Compras_Solicitud
SET EstatusID = 3,
    ObservacionesPresupuesto = @Observaciones
WHERE SolicitudID = @SolicitudID
  AND EstatusID = 4";

                        using (var cmd = new SqlCommand(sqlUpdate, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmd.Parameters.AddWithValue("@Observaciones", observaciones.Trim());

                            int filas = await cmd.ExecuteNonQueryAsync();

                            if (filas == 0)
                            {
                                trans.Rollback();
                                TempData["Error"] = "La solicitud ya no está en Control Presupuestal.";
                                return RedirectToAction("BandejaPresupuestos");
                            }
                        }

                        string sqlHistorico = @"
INSERT INTO Compras_Historico_Pasos
(
    SolicitudID,
    EstatusID,
    FechaMovimiento,
    UsuarioResponsable
)
VALUES
(
    @SolicitudID,
    3,
    GETDATE(),
    @Responsable
)";

                        using (var cmdHist = new SqlCommand(sqlHistorico, conn, trans))
                        {
                            cmdHist.Parameters.AddWithValue("@SolicitudID", solicitudId);
                            cmdHist.Parameters.AddWithValue("@Responsable", responsable);

                            await cmdHist.ExecuteNonQueryAsync();
                        }

                        trans.Commit();

                        await NotificarSolicitante_DocumentosSolicitadosAsync(
                            solicitudId,
                            observaciones.Trim()
                        );

                        TempData["Mensaje"] = "Se solicitó documentación adicional al usuario.";

                        return RedirectToAction("BandejaPresupuestos");
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();

                        _logger.LogError(ex, "Error al solicitar documentos para la solicitud {SolicitudID}", solicitudId);

                        TempData["Error"] = "Ocurrió un error al solicitar documentos.";
                        return RedirectToAction("Dictamen", new { id = solicitudId });
                    }
                }
            }
        }


        #region NOTIFICACION - COMPRA CERRADA

        private async Task NotificarSolicitante_CompraCerradaAsync(int solicitudId)
        {
            try
            {
                int personaId;
                string folio;
                string solicitante;
                string proveedor;
                decimal monto;
                string comentarios;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
SELECT
    ISNULL(
        S.Folio,
        'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)
    ) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante,
    C.Proveedor,
    C.MontoTotal,
    S.ComentariosSeleccionUsuario
FROM Compras_Solicitud S
INNER JOIN Usuarios U
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P
    ON U.PersonaID = P.PersonaID
INNER JOIN Compras_Cotizaciones C
    ON S.CotizacionSeleccionadaID = C.CotizacionID
WHERE S.SolicitudID = @SolicitudID
  AND S.EstatusID = 10;";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@SolicitudID",
                            solicitudId
                        );

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                                return;

                            personaId = Convert.ToInt32(reader["PersonaID"]);

                            folio = reader["Folio"]?.ToString()
                                ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";

                            solicitante = reader["Solicitante"]?.ToString()
                                ?? "Solicitante";

                            proveedor = reader["Proveedor"] == DBNull.Value
                                ? "No especificado"
                                : reader["Proveedor"]?.ToString() ?? "No especificado";

                            monto = reader["MontoTotal"] == DBNull.Value
                                ? 0
                                : Convert.ToDecimal(reader["MontoTotal"]);

                            comentarios = reader["ComentariosSeleccionUsuario"] == DBNull.Value
                                ? ""
                                : reader["ComentariosSeleccionUsuario"]?.ToString() ?? "";
                        }
                    }
                }

                string comentariosHtml = string.IsNullOrWhiteSpace(comentarios)
                    ? ""
                    : $@"
<p>
    <strong>Comentarios de Compras:</strong>
    {System.Net.WebUtility.HtmlEncode(comentarios)}
</p>";

                string asunto = $"Solicitud de compra cerrada - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
    <div style='max-width:650px; margin:0 auto; background:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
        <div style='padding:20px; background:#16a34a; color:#ffffff; text-align:center;'>
            <h2 style='margin:0;'>Solicitud de compra cerrada</h2>
        </div>

        <div style='padding:20px; color:#333333;'>
            <p>
                Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,
            </p>

            <p>
                Compras selecciono la cotizacion procedente y finalizo el proceso de tu solicitud.
            </p>

            <div style='background:#f0fdf4; border-left:4px solid #16a34a; padding:12px 14px; border-radius:6px; margin:14px 0;'>
                <p style='margin:0 0 6px;'>
                    <strong>Folio:</strong>
                    {System.Net.WebUtility.HtmlEncode(folio)}
                </p>

                <p style='margin:0 0 6px;'>
                    <strong>Proveedor seleccionado:</strong>
                    {System.Net.WebUtility.HtmlEncode(proveedor)}
                </p>

                <p style='margin:0;'>
                    <strong>Monto:</strong>
                    {monto:C}
                </p>
            </div>

            {comentariosHtml}

            <p>
                Puedes consultar la cotizacion seleccionada desde el modulo de
                <strong>Compras</strong> en la Intranet.
            </p>

            <p>https://intranet.nsgroup.com.mx/</p>

            <p style='color:#666666; font-size:12px; margin-top:18px;'>
                Mensaje generado automaticamente por la Intranet NS Group.
                No respondas a este correo.
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
                _logger.LogError(
                    ex,
                    "Error enviando correo de cierre de compra. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }

        #endregion

        //METODO PARA SOLICITAR DOCUMENTOS

        private async Task NotificarSolicitante_DocumentosSolicitadosAsync(
    int solicitudId,
    string observaciones)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
SELECT 
    ISNULL(S.Folio, 'COM-' + RIGHT('00000' + CAST(S.SolicitudID AS VARCHAR(10)), 5)) AS Folio,
    P.PersonaID,
    P.Nombre + ' ' + P.ApellidoPaterno AS Solicitante
FROM Compras_Solicitud S
INNER JOIN Usuarios U 
    ON S.UsuarioID = U.UsuarioID
INNER JOIN Persona P 
    ON U.PersonaID = P.PersonaID
WHERE S.SolicitudID = @SolicitudID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SolicitudID", solicitudId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                int personaId = Convert.ToInt32(rd["PersonaID"]);

                string folio = rd["Folio"]?.ToString()
                    ?? $"COM-{solicitudId.ToString().PadLeft(5, '0')}";

                string solicitante = rd["Solicitante"]?.ToString()
                    ?? "Solicitante";

                string asunto = $"Documentación requerida para tu solicitud - {folio}";

                string html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#f97316; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Documentación requerida</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(solicitante)}</strong>,</p>

      <p>Control Presupuestal revisó tu solicitud y requiere documentación adicional o corrección de documentos antes de emitir dictamen.</p>

      <div style='background:#fff7ed; border-left:4px solid #f97316; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio:</strong> {System.Net.WebUtility.HtmlEncode(folio)}</p>
        <p style='margin:0;'><strong>Observaciones:</strong> {System.Net.WebUtility.HtmlEncode(observaciones)}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Compras</strong>, revisa el detalle de la solicitud y vuelve a confirmar la documentación presupuestal.</p>
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
                _logger.LogError(
                    ex,
                    "Error enviando correo de solicitud de documentos. SolicitudID={SolicitudID}",
                    solicitudId
                );
            }
        }

    }
}