using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    public class CatMetricas
    {
        [Key] // Llave Primaria
        public int MetricaID { get; set; }

        [Required]
        public int DepartamentoID { get; set; }
        
        // Propiedad de navegación con 'virtual' para Lazy Loading
        [ForeignKey("DepartamentoID")]
        public virtual Departamento Departamento { get; set; } 

        [Required]
        public string NombreMetrica { get; set; }
        
        [Required]
        public string Frecuencia { get; set; }
        
        [Required]
        public string TipoValor { get; set; }

        public string? UnidadMedida { get; set; }

        public bool Activo { get; set; } = true;

        public decimal? MetaEsperada { get; set; }
        
        public string? SentidoMeta { get; set; }
        
        [Required]
        // BÓRRAMOS EL DECIMAL AQUÍ
        public string? TipoGraficaDefecto { get; set; } = "ComboChart";

        public int? VariableTarjetaID { get; set; }

        public string? ModoTarjeta { get; set; }

        public virtual List<CatMetricas_Variables> VariablesConfiguradas { get; set; } = new();
    }
}