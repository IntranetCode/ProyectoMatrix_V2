using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public enum EstadoProyecto
    {
        Planificacion,
        EnProgreso,
        EnRevision,
        Completado,
        Cancelado,
        Pausado
    }

    public enum PrioridadProyecto
    {
        Baja,
        Media,
        Alta,
        Critica
    }

    public class Proyecto
    {
        public int ProyectoID { get; set; }

        [Required]
        [StringLength(200)]
        public string NombreProyecto { get; set; }

        [StringLength(500)]
        public string Descripcion { get; set; }

        [StringLength(100)]
        public string CodigoProyecto { get; set; }

        public string ArchivoRuta { get; set; }

        public DateTime FechaCreacion { get; set; }

        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFinPrevista { get; set; }

        public DateTime? FechaFinReal { get; set; }

        public string CreadoPor { get; set; }

        public string ResponsableProyecto { get; set; }

        public bool EsActivo { get; set; }

        public int EmpresaID { get; set; }

        public string Tags { get; set; }

        public long TamanoArchivo { get; set; }

        public string Extension { get; set; }

        public EstadoProyecto Estado { get; set; }

        public PrioridadProyecto Prioridad { get; set; }

        public decimal? Presupuesto { get; set; }

        public int Progreso { get; set; } // Porcentaje de 0 a 100

        public string Observaciones { get; set; }

        public int Visualizaciones { get; set; }

        // Propiedades de navegación
        public List<ProyectoArchivo> Archivos { get; set; } = new List<ProyectoArchivo>();
    }

    public class ProyectoArchivo
    {
        public int ProyectoArchivoID { get; set; }
        public int ProyectoID { get; set; }
        public string NombreArchivo { get; set; }
        public string RutaArchivo { get; set; }
        public string TipoArchivo { get; set; }
        public long TamanoArchivo { get; set; }
        public DateTime FechaSubida { get; set; }
        public string SubidoPor { get; set; }
        public string Descripcion { get; set; }
        public bool EsActivo { get; set; }
    }

    public class ProyectosViewModel
    {
        public List<Proyecto> TodosLosProyectos { get; set; } = new List<Proyecto>();
        public List<Proyecto> ProyectosFiltrados { get; set; } = new List<Proyecto>();
        public EstadoProyecto? EstadoSeleccionado { get; set; }
        public PrioridadProyecto? PrioridadSeleccionada { get; set; }
        public string BusquedaTexto { get; set; }
        public int? EmpresaID { get; set; }
        public Dictionary<EstadoProyecto, int> ContadorPorEstado { get; set; } = new Dictionary<EstadoProyecto, int>();
        public Dictionary<PrioridadProyecto, int> ContadorPorPrioridad { get; set; } = new Dictionary<PrioridadProyecto, int>();
    }

    // Extensions para facilitar el manejo de enums
    public static class ProyectoExtensions
    {
        public static string ObtenerNombreEstado(this EstadoProyecto estado)
        {
            return estado switch
            {
                EstadoProyecto.Planificacion => "Planificación",
                EstadoProyecto.EnProgreso => "En Progreso",
                EstadoProyecto.EnRevision => "En Revisión",
                EstadoProyecto.Completado => "Completado",
                EstadoProyecto.Cancelado => "Cancelado",
                EstadoProyecto.Pausado => "Pausado",
                _ => estado.ToString()
            };
        }

        public static string ObtenerColorEstado(this EstadoProyecto estado)
        {
            return estado switch
            {
                EstadoProyecto.Planificacion => "secondary",
                EstadoProyecto.EnProgreso => "primary",
                EstadoProyecto.EnRevision => "warning",
                EstadoProyecto.Completado => "success",
                EstadoProyecto.Cancelado => "danger",
                EstadoProyecto.Pausado => "info",
                _ => "secondary"
            };
        }

        public static string ObtenerIconoEstado(this EstadoProyecto estado)
        {
            return estado switch
            {
                EstadoProyecto.Planificacion => "fas fa-clipboard-list",
                EstadoProyecto.EnProgreso => "fas fa-cogs",
                EstadoProyecto.EnRevision => "fas fa-search",
                EstadoProyecto.Completado => "fas fa-check-circle",
                EstadoProyecto.Cancelado => "fas fa-times-circle",
                EstadoProyecto.Pausado => "fas fa-pause-circle",
                _ => "fas fa-project-diagram"
            };
        }

        public static string ObtenerNombrePrioridad(this PrioridadProyecto prioridad)
        {
            return prioridad switch
            {
                PrioridadProyecto.Baja => "Baja",
                PrioridadProyecto.Media => "Media",
                PrioridadProyecto.Alta => "Alta",
                PrioridadProyecto.Critica => "Crítica",
                _ => prioridad.ToString()
            };
        }

        public static string ObtenerColorPrioridad(this PrioridadProyecto prioridad)
        {
            return prioridad switch
            {
                PrioridadProyecto.Baja => "success",
                PrioridadProyecto.Media => "warning",
                PrioridadProyecto.Alta => "danger",
                PrioridadProyecto.Critica => "dark",
                _ => "secondary"
            };
        }

        public static string FormatearTamanoArchivo(this long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024:F1} KB";
            if (bytes < 1073741824) return $"{bytes / 1048576:F1} MB";
            return $"{bytes / 1073741824:F1} GB";
        }
    }
}
