using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    public class CatMetricas_Variables
    {
        [Key]
        public int VariableID { get; set; }

        [Required]
        public int MetricaID { get; set; }

        [Required]
        public string NombreVariable { get; set; }
        public bool EsLinea { get; set; } = false;

        public string? TipoValor { get; set; }

        public string? UnidadMedida { get; set; }

        public string TipoCaptura { get; set; } = "Manual"; 
        
        public decimal? ValorFijo { get; set; }
        
        public string Formula { get; set; }

        // Navegación hacia la métrica "padre" a la que pertenece esta variable
        [ForeignKey("MetricaID")]
        public virtual CatMetricas Metrica { get; set; }
    }
}