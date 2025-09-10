namespace ProyectoMatrix.Models
{
    public class FeaturedVideoVm
    {
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string VideoIdYoutube { get; set; } // p.ej. "WRgDEFqrYl0"
        public string Thumbnail { get; set; }      // opcional (si no, mostramos una de respaldo)
        public string Categoria { get; set; }
    }
}
