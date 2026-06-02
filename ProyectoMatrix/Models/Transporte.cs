using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("Transporte")]
    public class Transporte
    {
        [Key]
        public int IdTransporte { get; set; }

        // Control del sistema
        public string? Folio { get; set; }
        public int UsuarioID { get; set; }
        public string? EstadoSolicitud { get; set; } // Pendiente, Autorizada, Rechazada, Cancelada, Finalizada
        public bool EstaBorrado { get; set; }
        public string? MotivoBorrado { get; set; }
        public DateTime? FechaBorrado { get; set; }
        public int? BorradoPor { get; set; }
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaActualizacion { get; set; }
        public bool NotificacionLeida { get; set; }
        public string? MensajeEdicion { get; set; }

        // Encabezado oficial del formato
        public string? Area { get; set; }
        public string? ElaboradoPor { get; set; }
        public DateTime? FechaEmision { get; set; }
        public string? CodigoFormato { get; set; }

        // Datos generales para facturación / solicitud
        public DateTime? FechaCarga { get; set; }
        public string? NumeroFactura { get; set; }
        public string? HorarioCarga { get; set; }
        public string? HorarioLlegadaDestino { get; set; }
        public string? DuracionAproxFlete { get; set; }
        public string? Cliente { get; set; }
        public string? Proyecto { get; set; }
        public string? NombreSolicitante { get; set; }
        public string? Departamento { get; set; }
        public string? CompaniaSolicitante { get; set; }
        public string? CentroCosto { get; set; }
        public string? AutorizadoPresupuesto { get; set; }

        // Ruta y unidad
        public string? TipoRuta { get; set; }
        public string? DireccionRecoleccion { get; set; }
        public string? Volumetria { get; set; }
        public string? TipoUnidad { get; set; }
        public string? ComentariosUnidad { get; set; }

        // Fletero
        public string? Fletero { get; set; }
        public decimal? CostoFlete { get; set; }

        public List<TransporteDestino> Destinos { get; set; } = new();
        public List<TransportePlanEmbarque> PlanEmbarque { get; set; } = new();
        public List<TransporteHistorialEstado> HistorialEstados { get; set; } = new();
    }

    [Table("TransporteDestinos")]
    public class TransporteDestino
    {
        [Key]
        public int IdDestino { get; set; }

        public int IdTransporte { get; set; }
        public int NumeroDestino { get; set; }
        public string? NombreRecibe { get; set; }
        public string? ContactoRecibe { get; set; }
        public string? DireccionDestino { get; set; }

        [ForeignKey(nameof(IdTransporte))]
        public Transporte? Transporte { get; set; }
    }

    [Table("TransportePlanEmbarque")]
    public class TransportePlanEmbarque
    {
        [Key]
        public int IdPlanEmbarque { get; set; }

        public int IdTransporte { get; set; }
        public string? ClaveSAT { get; set; }
        public string? Descripcion { get; set; }
        public decimal? Cantidad { get; set; }
        public string? UnidadMedida { get; set; }
        public decimal? Peso { get; set; }
        public decimal? Valor { get; set; }
        public string? ValeSalidaFactura { get; set; }

        [ForeignKey(nameof(IdTransporte))]
        public Transporte? Transporte { get; set; }
    }

    [Table("TransporteHistorialEstados")]
    public class TransporteHistorialEstado
    {
        [Key]
        public int IdHistorial { get; set; }

        public int IdTransporte { get; set; }
        public string? EstadoAnterior { get; set; }
        public string? EstadoNuevo { get; set; }
        public int UsuarioID { get; set; }
        public string? Comentario { get; set; }
        public DateTime FechaMovimiento { get; set; }

        [ForeignKey(nameof(IdTransporte))]
        public Transporte? Transporte { get; set; }
    }
}
