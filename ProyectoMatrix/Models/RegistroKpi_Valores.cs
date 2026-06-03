using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    public class RegistroKpis_Valores
    {
        [Key]
        public int ValorID { get; set; }

        [Required]
        public int RegistroID { get; set; }

        [Required]
        public int VariableID { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Valor { get; set; }

        // Navegación hacia el registro padre
        [ForeignKey("RegistroID")]
        public virtual RegistroKpi Registro { get; set; }

        // Navegación hacia la configuración de la variable
        [ForeignKey("VariableID")]
        public virtual CatMetricas_Variables Variable { get; set; }
    }
}