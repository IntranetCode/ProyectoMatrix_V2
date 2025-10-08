using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class Empresa
    {
        [Key]
        public int EmpresaID { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public bool Activa { get; set; }

        // Propiedades de navegación
        public virtual ICollection<ComunicadoEmpresa> ComunicadoEmpresas { get; set; } = new List<ComunicadoEmpresa>();

        public virtual ICollection<WebinarEmpresa> WebinarsEmpresas { get; set; } = new List<WebinarEmpresa>();
    }
}