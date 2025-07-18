using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ProyectoMatrix.Models
{
    public enum TipoContenido
    {
        PDF,
        Video,
        Presentacion,
        Manual,
        Documento
    }

    public enum CategoriaContenido
    {
        ManualesYPoliticas,
        CursosCapacitaciones,
        VideosTutoriales,
        Presentaciones,
        Procedimientos
    }
    public class Curso
    {
        public List<Curso> Cursos { get; set; }
        public int CursoID { get; set; }

        [Required]
        [StringLength(200)]
        public string NombreCurso { get; set; }

        [StringLength(100)]
        public string Categoria { get; set; }

        [StringLength(500)]
        public string Descripcion { get; set; }

        public bool EsActivo { get; set; }
        public int? Progreso { get; set; }
    }
    public class ContenidoEducativo
    {
        public string AreaSeleccionada { get; set; }
        public List<ContenidoEducativo> Contenidos { get; set; }
        public int ContenidoID { get; set; }

        [Required]
        [StringLength(200)]
        public string Titulo { get; set; }

        [StringLength(500)]
        public string Descripcion { get; set; }

        public string TipoContenido { get; set; } // Ya no es enum

        public string Categoria { get; set; } // Ya no es enum

        public string ArchivoRuta { get; set; }

        public string UrlVideo { get; set; }

        public string Thumbnail { get; set; }

        public DateTime FechaCreacion { get; set; }

        public string CreadoPor { get; set; }

        public bool EsActivo { get; set; }

        public int OrdenVisualizacion { get; set; }

        public string Tags { get; set; }

        public int Visualizaciones { get; set; }

        public long TamanoArchivo { get; set; }

        public string Extension { get; set; }

        public int EmpresaID { get; set; }

        public int AreaID { get; set; }

        public int? Progreso { get; set; }

    }


    public class Area
    {
        public int AreaID { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; }

        public string Icono { get; set; }

        public string ColorHex { get; set; }

        public bool Activo { get; set; }
    }

    public class UniversidadViewModel
    {
        public List<Area> Areas { get; set; }
        public List<ContenidoEducativo> TodosLosContenidos { get; set; }
        public List<ContenidoEducativo> ContenidosFiltrados { get; set; }
        public int? AreaSeleccionadaId { get; set; }
        public CategoriaContenido? CategoriaSeleccionada { get; set; }
        public string BusquedaTexto { get; set; }
        public Dictionary<string, int> ContadorPorCategoria { get; set; }


        public UniversidadViewModel()
        {
            Areas = new List<Area>();
            TodosLosContenidos = new List<ContenidoEducativo>();
            ContenidosFiltrados = new List<ContenidoEducativo>();
            ContadorPorCategoria = new Dictionary<string, int>();


        }
    }

    public static class UniversidadExtensions
    {
        public static string ObtenerNombreCategoria(this CategoriaContenido categoria)
        {
            return categoria switch
            {
                CategoriaContenido.ManualesYPoliticas => "Manuales y Políticas",
                CategoriaContenido.CursosCapacitaciones => "Cursos y Capacitaciones",
                CategoriaContenido.VideosTutoriales => "Videos Tutoriales",
                CategoriaContenido.Presentaciones => "Presentaciones",
                CategoriaContenido.Procedimientos => "Procedimientos",
                _ => categoria.ToString()
            };
        }

        public static string ObtenerIconoCategoria(this CategoriaContenido categoria)
        {
            return categoria switch
            {
                CategoriaContenido.ManualesYPoliticas => "fas fa-book",
                CategoriaContenido.CursosCapacitaciones => "fas fa-graduation-cap",
                CategoriaContenido.VideosTutoriales => "fas fa-play-circle",
                CategoriaContenido.Presentaciones => "fas fa-chalkboard",
                CategoriaContenido.Procedimientos => "fas fa-list-check",
                _ => "fas fa-file"
            };
        }

        public static string ObtenerIconoTipo(this TipoContenido tipo)
        {
            return tipo switch
            {
                TipoContenido.PDF => "fas fa-file-pdf",
                TipoContenido.Video => "fas fa-play",
                TipoContenido.Presentacion => "fas fa-chalkboard",
                TipoContenido.Manual => "fas fa-book",
                TipoContenido.Documento => "fas fa-file-alt",
                _ => "fas fa-file"
            };
        }

        public static string FormatearTamano(this long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024:F1} KB";
            if (bytes < 1073741824) return $"{bytes / 1048576:F1} MB";
            return $"{bytes / 1073741824:F1} GB";
        }
        public class VisualizacionModel
        {
            public int Id { get; set; }
        }
        

    }
}
