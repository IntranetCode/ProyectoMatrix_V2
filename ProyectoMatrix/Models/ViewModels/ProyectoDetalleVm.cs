namespace ProyectoMatrix.Models.ViewModels
{
    public class ProyectoDetalleVm
    {
        public Proyecto Proyecto { get; set; }

        public int? CarpetaSeleccionadaId { get; set; } // null = raíz ("Documentos")
        public List<CarpetaNodoVM> Carpetas { get; set; } = new();
        public List<ArchivoVM> Archivos { get; set; } = new();
    }

    public class CarpetaNodoVM
    {
        public int CarpetaId { get; set; }
        public int? CarpetaPadreId { get; set; }
        public string Nombre { get; set; }
        public string RutaRelativa { get; set; } // "Documentos" o "Planos"
        public int Nivel { get; set; }           // para la jerarquia
    }

    public class ArchivoVM
    {
        public string Nombre { get; set; }          // nombre físico
        public string RutaRelativa { get; set; }    // "Documentos/abc.pdf"
        public long Tamano { get; set; }
        public DateTime Fecha { get; set; }
        public string Extension { get; set; }
    }
}
