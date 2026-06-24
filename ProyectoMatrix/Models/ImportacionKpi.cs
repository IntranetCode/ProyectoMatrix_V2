using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("ImportacionesKpi")]
    public class ImportacionKpi
    {
        [Key]
        public int ImportacionID { get; set; }

        public string NombreArchivo { get; set; } = string.Empty;

        public int UsuarioID { get; set; }

        public DateTime FechaImportacion { get; set; } = DateTime.Now;

        public string Estado { get; set; } = "Pendiente";

        public int TotalFilas { get; set; }

        public int FilasValidas { get; set; }

        public int FilasConError { get; set; }

        public string? Observaciones { get; set; }

        public ICollection<ImportacionKpiDetalle> Detalles { get; set; } = new List<ImportacionKpiDetalle>();
    }
}