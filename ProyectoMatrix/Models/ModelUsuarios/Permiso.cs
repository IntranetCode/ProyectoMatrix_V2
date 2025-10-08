using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models.ModelUsuarios // <-- Namespace corregido
{
    [Table("Permisos")]
    public class Permiso
    {
        [Key]
        public int PermisoID { get; set; }

        public int UsuarioID { get; set; }

        public int EmpresaID { get; set; }

        public int SubMenuID { get; set; }

        public int AccionID { get; set; }

        [ForeignKey("UsuarioID")]
        public virtual Usuario Usuario { get; set; }

        [ForeignKey("SubMenuID")]
        public virtual SubMenu SubMenu { get; set; }

        [ForeignKey("EmpresaID")]
        public virtual Empresa Empresa { get; set; }
    }
}