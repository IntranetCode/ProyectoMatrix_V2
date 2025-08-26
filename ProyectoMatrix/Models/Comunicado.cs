using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProyectoMatrix.Models
{
    public class Comunicado
    {
        public int ComunicadoID { get; set; }

        [Column("Nombre")]
        public string NombreComunicado { get; set; } =string.Empty;
        public string? Descripcion { get; set; } = string.Empty;
        public string? Imagen { get; set; }
        public DateTime FechaCreacion { get; set; }= DateTime.Now;
       
        public int? UsuarioCreadorID { get; set; }

        public bool EsPublico { get; set; } = true;

        public ICollection<ComunicadoEmpresa> ComunicadosEmpresas { get; set; } = new List<ComunicadoEmpresa>();
    }

  

}
