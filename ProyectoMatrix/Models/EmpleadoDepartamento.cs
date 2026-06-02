using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("EmpleadoDepartamentos")]
    public class EmpleadoDepartamento
    {
        [Key]
        public int EmpleadoDepartamentoID { get; set; }
        
        // Esta tabla pivote se enlaza con Usuarios.UsuarioID
        [Column("UsuarioID")] 
        public int UsuarioID { get; set; }
        
        [Column("DepartamentoID")]
        public int DepartamentoID { get; set; }
    }
}