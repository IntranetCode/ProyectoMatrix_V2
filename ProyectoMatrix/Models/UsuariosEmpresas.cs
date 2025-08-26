using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("UsuariosEmpresas")]
    public class UsuariosEmpresas
    {
        [Key]
        public int UsuarioID { get; set; }
        public int EmpresaID { get; set; }
        public bool Activo { get; set; }

        public UsuarioModel Usuario { get; set; } = null!;
        public Empresa Empresa { get; set; } = null!;
    }
}
