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

        //AGREGANDO LAS NUEVAS COLUMNAS DE BD

        public string UrlRegistro { get; set; }
        public string UrlGrabacion { get; set; }

        public string UrlEnVivoEmbed { get; set; }


        public bool EsAsamblea => !string.IsNullOrWhiteSpace(UrlEnVivoEmbed);

        //Se agregaron helpers de estado 

        public bool YaPaso(DateTime ahora) =>
            ahora > FechaFin;

        public bool EnCurso(DateTime ahora) =>
            ahora >= FechaInicio && ahora <= FechaFin;

        public bool DentroDeVentanaUnirse (DateTime ahora, int minutosAntes = 10 ) =>
            ahora >= FechaInicio.AddMinutes( -minutosAntes );

        public bool TieneGrabacion => !string.IsNullOrWhiteSpace(UrlGrabacion);

        public bool EsConRegistro => !string.IsNullOrWhiteSpace(UrlRegistro);

        public bool Directo => !string.IsNullOrWhiteSpace(UrlTeams);
    }
}
