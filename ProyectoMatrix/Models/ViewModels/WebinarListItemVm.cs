using System;
namespace ProyectoMatrix.Models

{
    public class WebinarListItemVm
    {
        public int WebinarID { get; set; }
        public string Titulo { get; set; } = "";
        public string? Descripcion { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        
        public string? UrlTeams { get; set; }
        public string? Imagen { get; set; }
        public string DirigidoA { get; set; } = "Todos"; // Nombres de empresas separadas por comas
    }
}
