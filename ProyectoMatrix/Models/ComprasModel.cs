namespace ProyectoMatrix.Models
{
    using System.Linq;
    public class ComprasModel
    {
    }



    public class CompraViewModel
    {
        // Datos Generales
        public int EmpresaID { get; set; }
        public string TipoCompra { get; set; } // "Nacional" o "Internacional"
        public bool EsProyecto { get; set; }
        public string? NombreProyecto { get; set; }
        public int UrgenciaID { get; set; }
        public string? Comentarios { get; set; }

        // Solo para Internacional
        public int? TransporteID { get; set; }

        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public IFormFile? ArchivoDesviacion { get; set; }

        // Listado de materiales (puedes llenarlo con JavaScript en el front)
        public List<MaterialItem> Materiales { get; set; } = new List<MaterialItem>();
    }

    public class MaterialItem
    {
        public string? Nombre { get; set; }
        public string? Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public string? UnidadMedida { get; set; }

        public IFormFile? ArchivoReferencia { get; set; }

        public string? ArchivoReferenciaPath { get; set; }
        public string? NombreArchivoReferencia { get; set; }
        public string? ExtensionArchivoReferencia { get; set; }
    }

    public class MisComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public DateTime Fecha { get; set; }
        public string Estatus { get; set; }
        public int EstatusID { get; set; } // Nuevo: Para lógica de colores

        // Propiedad calculada para la barra de progreso
        public int Porcentaje
        {
            get
            {
                return EstatusID switch
                {
                    1 => 10,   // Solicitado
                    2 => 25,   // Cotizado
                    3 => 40,   // En Presupuesto
                    4 => 55,   // OC Generada
                    5 => 65,   // OC Enviada a Proveedor
                    6 => 75,   // Recibida en Almacén
                    7 => 85,   // Entregada a Usuario
                    8 => 95,   // Pendiente CxP
                    9 => 100,  // Rechazado
                    10 => 100, // Cerrada
                    11 => 100, // Rechazada por CxP
                    _ => 0
                };
            }
        }
        public string ColorProgreso => EstatusID switch
        {
            9 => "bg-danger",
            10 => "bg-success",
            11 => "bg-danger",
            8 => "bg-info",
            6 => "bg-primary",
            7 => "bg-primary",
            4 => "bg-warning",
            5 => "bg-warning",
            3 => "bg-info",
            _ => "bg-primary"
        };
    }

    public class BandejaComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Estatus { get; set; }

        public int? CompradorAsignadoUsuarioID { get; set; }
    }

    public class DataHeatmapVm
    {
        public string Departamento { get; set; }
        public string Estatus { get; set; }
        public int Horas { get; set; }
    }

    public class ComprasDashboardVm
    {
        // Para el Heatmap: Agrupado por Departamento
        public List<HeatmapSeries> HeatmapData { get; set; } = new List<HeatmapSeries>();

        // Para la Dona: Estatus vs Cantidad
        public List<decimal> DonaValores { get; set; } = new List<decimal>();
        public List<string> DonaEtiquetas { get; set; } = new List<string>();

        // Para los Cuellos de Botella
        public List<double> TiemposPromedio { get; set; } = new List<double>();
        public List<string> EtiquetasDepartamentos { get; set; } = new List<string> { "Compras", "Finanzas", "Dirección" };

        // --- NUEVAS PROPIEDADES PARA LOS KPIs 
        public int CriticosVencidos { get; set; }
        public double PromedioTotal { get; set; }
    }

    public class HeatmapSeries
    {
        public string name { get; set; } // Nombre del Departamento
        public List<HeatmapDataPoint> data { get; set; } = new List<HeatmapDataPoint>();
    }

    public class HeatmapDataPoint
    {
        public string x { get; set; } // El Estatus (Solicitado, Cotizado, etc.)
        public int y { get; set; }    // Horas de retraso (El valor que da el color)
    }

    public class DictamenPresupuestalVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public decimal MontoCotizado { get; set; }

        // Decisiones del Dictamen
        public bool Pasa { get; set; } // SI / NO
        public string TipoGasto { get; set; } // GASTO / REQUISICIÓN
        public bool DentroDePresupuesto { get; set; }
        public string NumeroRequisicion { get; set; } // ID de Requi
        public string Observaciones { get; set; } // En caso de rechazo o nota de desviación

        public int CotizacionID { get; set; }
        public string? Proveedor { get; set; }
        public string? ArchivoPath { get; set; }
        public string? NombreArchivoOriginal { get; set; }
        public string? Extension { get; set; }

        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public string? ArchivoDesviacionPath { get; set; }

        public string? NombreArchivoDesviacion { get; set; }

        public string? ExtensionArchivoDesviacion { get; set; }

        public string? ArchivoFormatoRequisicionPath { get; set; }
        public string? NombreArchivoFormatoRequisicion { get; set; }
        public string? ExtensionArchivoFormatoRequisicion { get; set; }
    }

    public class DetalleCompraVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string NombreSolicitante { get; set; }
        public string Empresa { get; set; }
        public string EstatusActual { get; set; }
        public int EstatusID { get; set; }

        // Lista de materiales para la tabla
        public List<MaterialItem> Materiales { get; set; } = new List<MaterialItem>();


        public string TipoGasto { get; set; }
        public bool? DentroPresupuesto { get; set; }
        public string NumeroRequisicion { get; set; }
        public string ObservacionesPresupuesto { get; set; }

        public string? NumeroOC { get; set; }
        public string? ProveedorOC { get; set; }
        public string? ComentariosOC { get; set; }
        public DateTime? FechaOC { get; set; }

        public bool EsCompras { get; set; }

        public bool TieneOC { get; set; }

        public DateTime? FechaEnvioProveedor { get; set; }
        public DateTime? FechaEstimadaEntrega { get; set; }
        public string? ComentariosEnvioProveedor { get; set; }
        public bool OCEnviadaProveedor { get; set; }


        public bool RecibidaEnAlmacen { get; set; }
        public DateTime? FechaRecepcionAlmacen { get; set; }
        public string? ComentariosRecepcionAlmacen { get; set; }


        public bool EntregadaUsuario { get; set; }
        public DateTime? FechaEntregaUsuario { get; set; }
        public string? NombreRecibeUsuario { get; set; }
        public string? ComentariosEntregaUsuario { get; set; }


        public int? CotizacionSeleccionadaID { get; set; }
        public DateTime? FechaSeleccionCotizacion { get; set; }
        public string? ComentariosSeleccionUsuario { get; set; }
        public bool EsSolicitante { get; set; }

        public string? ArchivoReferenciaPath { get; set; }

        public int Porcentaje
        {
            get
            {
                return EstatusID switch
                {
                    1 => 10,   // Solicitado
                    2 => 25,   // Cotizado
                    3 => 40,   // En Presupuesto
                    4 => 55,   // OC Generada
                    5 => 65,   // OC Enviada a Proveedor
                    6 => 75,   // Recibida en Almacén
                    7 => 85,   // Entregada a Usuario
                    8 => 95,   // Pendiente CxP
                    9 => 100,  // Rechazado
                    10 => 100, // Cerrada
                    11 => 100, // Rechazada por CxP
                    _ => 0
                };
            }
        }

        // Color de la barra según estatus
        public string ColorProgreso => EstatusID switch
        {
            9 => "bg-danger",
            10 => "bg-success",
            11 => "bg-danger",
            8 => "bg-info",
            7 => "bg-primary",
            6 => "bg-primary",
            5 => "bg-warning",
            4 => "bg-warning",
            3 => "bg-info",
            _ => "bg-primary"
        };


        public decimal? MontoPresupuestoSolicitado { get; set; }

        public bool FueraPresupuestoUsuario { get; set; }

        public string? ArchivoDesviacionPath { get; set; }

        public string? NombreArchivoDesviacion { get; set; }

        public string? ExtensionArchivoDesviacion { get; set; }

        public bool PuedeVerArchivoDesviacion { get; set; }


        public string? EvidenciaRecepcionPath { get; set; }

        public string? NombreArchivoEvidencia { get; set; }

        public string? ExtensionArchivoEvidencia { get; set; }

        public bool TieneEvidenciaRecepcion =>
    !string.IsNullOrWhiteSpace(EvidenciaRecepcionPath);

        public string? EvidenciaEntregaPath { get; set; }
        public string? NombreArchivoEvidenciaEntrega { get; set; }
        public string? ExtensionArchivoEvidenciaEntrega { get; set; }

        public List<CotizacionDetalleVm> Cotizaciones { get; set; } = new();

        public string? ArchivoFormatoRequisicionPath { get; set; }
        public string? NombreArchivoFormatoRequisicion { get; set; }
        public string? ExtensionArchivoFormatoRequisicion { get; set; }


    }


    //Model para los gráficos de dirección

    public class TiempoProcesoVm
    {
        public string Etapa { get; set; } // "Cotización", "Dictamen", "Autorización"
        public double PromedioHoras { get; set; }
        public string Responsable { get; set; } // Compras, Finanzas, Dirección
    }

    public class CuelloBotellaVm
    {
        public string Departamento { get; set; } // "Compras", "Presupuestos", "Dirección"
        public double HorasPromedio { get; set; }
        public string EstatusSLA => HorasPromedio <= 24 ? "A Tiempo" : "Retrasado";
    }

    public class IndexComprasVm
    {
        public IEnumerable<MisComprasVm> MisCompras { get; set; }
        public ComprasDashboardVm Estadisticas { get; set; } // Tu modelo de gráficos
    }


    //Bandejapresupuestos para el historial

    public class BandejaPresupuestosDashboardVm
    {
        public int Pendientes { get; set; }
        public int Aprobadas { get; set; }
        public int Rechazadas { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal MontoRechazado { get; set; }
        public int Desviaciones { get; set; }

      //  public List<PresupuestoPendienteVm> PendientesLista { get; set; } = new();
      //  public List<PresupuestoHistoricoVm> Historico { get; set; } = new();
    }

    public class ControlPresupuestalVm
    {
        public int TotalPendientes { get; set; }
        public int TotalAprobadas { get; set; }
        public int TotalRechazadas { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal MontoRechazado { get; set; }

        public List<BandejaComprasVm> Pendientes { get; set; } = new();
        public List<HistoricoPresupuestoVm> Historico { get; set; } = new();
    }

    public class HistoricoPresupuestoVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string TipoCompra { get; set; }
        public decimal MontoTotal { get; set; }
        public string Resultado { get; set; }
        public bool? DentroPresupuesto { get; set; }
        public string NumeroRequisicion { get; set; }
        public string Observaciones { get; set; }
        public DateTime FechaDictamen { get; set; }
    }

    //Modelos para el dasboard de compras
    public class BandejaComprasDashboardVm
    {
        public int TotalPendientes { get; set; }
        public int TotalCotizadas { get; set; }
        public int TotalAtendidas { get; set; }

        public decimal MontoCotizado { get; set; }

        public bool EsDireccionCompras { get; set; }
        public List<CompradorCargaVm> CargaCompradores { get; set; } = new();

        public List<CompradorSelectVm> CompradoresDisponibles { get; set; } = new();

        public List<BandejaComprasVm> Pendientes { get; set; } = new();
        public List<HistoricoComprasVm> Historico { get; set; } = new();
    }

    public class CompradorSelectVm
    {
        public int UsuarioID { get; set; }
        public string NombreCompleto { get; set; }
        public string Puesto { get; set; }
    }

    public class HistoricoComprasVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }
        public string Estatus { get; set; }
        public decimal MontoTotal { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaCotizacion { get; set; }
        public int EstatusID { get; set; }
    }

    public class CompradorCargaVm
    {
        public int UsuarioID { get; set; }
        public string NombreCompleto { get; set; }
        public string Puesto { get; set; }
        public int Pendientes { get; set; }
        public int Cotizadas { get; set; }
        public int TotalAsignadas { get; set; }
    }

    //MODELO PARA REGISTRAR ORDEN DE COMPRA

    public class RegistrarOrdenCompraVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Empresa { get; set; }

        public string NumeroOC { get; set; }
        public string? Proveedor { get; set; }
    //    public IFormFile? ArchivoOC { get; set; }
        public string? Comentarios { get; set; }
    }

    public class SeguimientoDireccionVm
    {
        public List<SeguimientoCompraItemVm> Solicitudes { get; set; } = new();

        public int TotalSolicitudes => Solicitudes.Count;
        public int TotalActivas => Solicitudes.Count(x => x.EstatusID != 9 && x.EstatusID != 10 && x.EstatusID != 11);

        public int TotalCerradas => Solicitudes.Count(x => x.EstatusID == 10);

        public int TotalRechazadas => Solicitudes.Count(x => x.EstatusID == 9 || x.EstatusID == 11);

        public int TotalRetrasadas => Solicitudes.Count(x =>
            x.DiasEnEstatus >= 2 &&
            x.EstatusID != 9 &&
            x.EstatusID != 10 &&
            x.EstatusID != 11
        );
    }

        public class SeguimientoCompraItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public DateTime FechaCreacion { get; set; }

        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string Empresa { get; set; }

        public string TipoCompra { get; set; }
        public string Urgencia { get; set; }

        public int EstatusID { get; set; }
        public string Estatus { get; set; }

        public string CompradorAsignado { get; set; }
        public DateTime? FechaAsignacionComprador { get; set; }

        public DateTime? FechaUltimoMovimiento { get; set; }

        public int DiasEnEstatus { get; set; }
        public int DiasDesdeSolicitud { get; set; }
        public int DiasCotizando { get; set; }

        public decimal MontoCotizado { get; set; }


        public int DiasPermitidos { get; set; }
        public int DiasHabilesTranscurridos { get; set; }
        public string SemaforoTexto { get; set; }

            public string SemaforoCss => EstatusID switch
            {
                10 => "badge bg-success",          // Cerrada
                9 => "badge bg-danger",            // Rechazada
                11 => "badge bg-danger",           // Rechazada por CxP
                8 => "badge bg-info text-dark",    // Pendiente CxP
                _ => SemaforoTexto switch
                {
                    "Retrasada" => "badge bg-danger",
                    "Por vencer" => "badge bg-warning text-dark",
                    "A tiempo" => "badge bg-success",
                    _ => "badge bg-secondary"
                }
            };

        public List<TiempoDepartamentoVm> TiemposDepartamento { get; set; } = new();

        public int DiasCompras { get; set; }
        public int DiasPresupuesto { get; set; }
        public int DiasOC { get; set; }
        public int DiasProveedor { get; set; }
        public int DiasAlmacen { get; set; }
        

    }

    public class BandejaAlmacenVm
    {
        public List<AlmacenItemVm> PendientesRecepcion { get; set; } = new();

        public int TotalPendientesRecepcion => PendientesRecepcion.Count;
    }

    public class AlmacenItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }

        public string Solicitante { get; set; }
        public string Departamento { get; set; }
        public string Empresa { get; set; }

        public string NumeroOC { get; set; }
        public string Proveedor { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaEnvioProveedor { get; set; }
        public DateTime? FechaEstimadaEntrega { get; set; }
        public DateTime? FechaRecepcionAlmacen { get; set; }

        public string Estatus { get; set; }
        public int EstatusID { get; set; }

        public int DiasDesdeEnvioProveedor { get; set; }
    }

    public class CotizacionDetalleVm
    {
        public int CotizacionID { get; set; }

        public string Proveedor { get; set; }

        public decimal MontoTotal { get; set; }

        public string ArchivoPath { get; set; }

        public string NombreArchivoOriginal { get; set; }

        public string Extension { get; set; }

        public bool EsRecomendada { get; set; }

        public string ComentariosCompras { get; set; }

        public DateTime FechaEnvioAlUsuario { get; set; }

        public bool FueSeleccionadaPorUsuario { get; set; }

    }

    public class CuentasPorPagarVm
    {
        public List<CuentasPorPagarItemVm> Pendientes { get; set; } = new();
        public List<CuentasPorPagarItemVm> Historico { get; set; } = new();
    }

    public class CuentasPorPagarItemVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; }
        public string Solicitante { get; set; }
        public string Empresa { get; set; }
        public string Departamento { get; set; }
        public string Proveedor { get; set; }
        public decimal MontoTotal { get; set; }
        public string NumeroOC { get; set; }
        public DateTime? FechaEntregaUsuario { get; set; }
        public string NombreRecibeUsuario { get; set; }

        public string TipoGasto { get; set; }
        public string NumeroRequisicion { get; set; }
        public DateTime? FechaDictamen { get; set; }

        public int EstatusID { get; set; }
        public string Estatus { get; set; }
        public string? ComentariosCxP { get; set; }
        public DateTime? FechaCierreCxP { get; set; }
    }

    public class TiempoDepartamentoVm
    {
        public string Departamento { get; set; } = "";
        public string Estatus { get; set; } = "";
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int Dias { get; set; }
        public int Horas { get; set; }
    }

}
