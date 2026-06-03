using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProyectoMatrix.Models;

namespace ProyectoMatrix.Models
{
    public class RegistroKpi
    {
        [Key]
        public int RegistroID { get; set; }

        [Required]
        public int MetricaID { get; set; }

        // Mantenemos estos de tu diseño original para poder filtrar por tiempo
        [Required]
        public int Anio { get; set; }

        [Required]
        public int NumeroPeriodo { get; set; }

        [Required]
        public DateTime FechaCaptura { get; set; }

        [Required]
        public int UsuarioID { get; set; }

        [Required]
        public bool Activo { get; set; } = true;

        [ForeignKey("MetricaID")]
        public virtual CatMetricas Metrica { get; set; }

        // Relación Uno a Varios: Un registro tiene muchos valores capturados
        public virtual List<RegistroKpis_Valores> DetallesValores { get; set; } = new();
    }
}