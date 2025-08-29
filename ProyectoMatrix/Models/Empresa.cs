namespace ProyectoMatrix.Models

{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class Empresa
    {
        [Key]
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
     

        // Relaciones (opcional)
        public ICollection<ComunicadoEmpresa> ComunicadoEmpresas { get; set; } 
        =new List<ComunicadoEmpresa>();

        public ICollection<WebinarEmpresa> WebinarsEmpresas { get; set; }
        =new List<WebinarEmpresa>();
    }

}

