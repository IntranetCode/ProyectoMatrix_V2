using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("Departamentos")]
    public class Departamento
    {
        [Key]
        public int DepartamentoID { get; set; }
        
        public string NombreDepartamento { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}