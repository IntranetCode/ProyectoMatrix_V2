using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    public class Webinar
    {
        public int WebinarID { get; set; }
        public string Titulo { get; set; } 
        public string Descripcion { get; set; } 
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public bool EsPublico { get; set; } 
        public string? UrlTeams { get; set; }
        public string? Imagen { get; set; }
        public int? UsuarioCreadorID { get; set; }

        public string? UrlGrabacion { get; set; }

        public string? UrlRegistro { get; set; }

        public string? UrlEnVivoEmbed { get; set; }

        public DateTime FechaCreacion { get; set; }

        [NotMapped]
        public bool EsAsamblea { get; set; }  // true = Asamblea , false = Webinar

        public bool Activo { get; set; } = true;
        public ICollection<WebinarEmpresa> WebinarsEmpresas { get; set; } = new List<WebinarEmpresa>();


    }


    public class WebinarEmpresa
    {
        public int WebinarID { get; set; }
        public int EmpresaID { get; set; }
        public Webinar Webinar { get; set; } = null!;
       
        public Empresa Empresa { get; set; } = null!;

       
    }


    public class WebinarGestionVm
    {
        public Webinar Webinar { get; set; } = default!;
        public string DirigidoA { get; set; } = "-";
    }



}
