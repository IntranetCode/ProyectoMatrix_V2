using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("ImportacionesKpi_Detalle")]
    public class ImportacionKpiDetalle
    {
        [Key]
        public int DetalleImportacionID { get; set; }

        public int ImportacionID { get; set; }

        public int NumeroFilaExcel { get; set; }

        public int? DepartamentoID { get; set; }

        public string? Departamento { get; set; }

        public int? MetricaID { get; set; }

        public string? NombreMetrica { get; set; }

        public string? Frecuencia { get; set; }

        public int? Anio { get; set; }

        public int? NumeroPeriodo { get; set; }

        public DateTime? FechaMedicion { get; set; }

        public int? VariableID { get; set; }

        public string? NombreVariable { get; set; }

        public string? TipoCaptura { get; set; }

        public string? TipoValor { get; set; }
        public string? UnidadMedida { get; set; }

        public decimal? Valor { get; set; }

        public decimal? ValorCalculado { get; set; }

        public string Estado { get; set; } = "Pendiente";

        public string? Observacion { get; set; }

        [ForeignKey("ImportacionID")]
        public ImportacionKpi? Importacion { get; set; }
    }
}