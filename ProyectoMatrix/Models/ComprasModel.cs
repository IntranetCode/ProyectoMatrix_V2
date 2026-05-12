namespace ProyectoMatrix.Models
{
    public class ComprasModel
    {
    }



    public class CompraViewModel
    {
        // Datos Generales
        public int EmpresaID { get; set; }
        public string TipoCompra { get; set; } // "Nacional" o "Internacional"
        public bool EsProyecto { get; set; }
        public string NombreProyecto { get; set; }
        public int UrgenciaID { get; set; }
        public string Comentarios { get; set; }

        // Solo para Internacional
        public int? TransporteID { get; set; }

        // Listado de materiales (puedes llenarlo con JavaScript en el front)
        public List<MaterialItem> Materiales { get; set; } = new List<MaterialItem>();
    }

    public class MaterialItem
    {
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public string UnidadMedida { get; set; }
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
                    1 => 20,  // Solicitado
                    2 => 40,  // Cotizado
                    3 => 60,  // Presupuesto
                    4 => 100, // Recibido/Finalizado
                    5 => 100, // Rechazado (Barra llena pero color distinto)
                    _ => 0
                };
            }
        }

        // Color de la barra según estatus
        public string ColorProgreso => EstatusID switch
        {
            4 => "bg-success", // Terminado
            5 => "bg-danger",  // Rechazado
            3 => "bg-info",    // En presupuesto
            _ => "bg-primary"  // Por defecto
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
        public bool Pasa { get; set; } // SI / NO 
        public bool DentroDePresupuesto { get; set; } // SI / NO 
        public string NumeroRequisicion { get; set; } // ID de Requi 
        public string Observaciones { get; set; } // Motivo de rechazo o nota de desviación 
    }

}
