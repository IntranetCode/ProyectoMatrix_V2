using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class DenunciaAnonimaCreateVm
    {
        [Required]
        public string TipoDenuncia { get; set; }

        [Required]
        public string DepartamentoAfectado { get; set; }

        [Required]
        public string Descripcion { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaHechos { get; set; }

        public string LugarHechos { get; set; }

       
    }

}
