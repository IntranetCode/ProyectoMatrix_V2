using System;
using System.ComponentModel.DataAnnotations;
namespace ProyectoMatrix.Models
{
    public class ComunicadoListItemVM
    {
        public int ComunicadoID { get; set; }
        public string NombreComunicado { get; set; } = string.Empty;
        public string? Descripcion { get; set; } = string.Empty;
        public DateTime FechaCreacion { get; set; }
        public string DirigidoA { get; set; } = "-"; // Nombres de empresas separadas por comas

        public string? Imagen { get; set; } // Nombre del archivo de imagen si existe

        // Mostrar la categoria del comunicado en la lista

        [Required]
        public string Categoria { get; set; }
        public List<string> CategoriasDisponibles { get; set; } = new() { "Urgente", "General", "Evento" };
    }
}
