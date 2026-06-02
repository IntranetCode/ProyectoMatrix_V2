using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("Guias")]
    public class Guia
    {
        [Key]
        public int IdGuia { get; set; }

        public int UsuarioID { get; set; }

        // Flujo del formulario de guías
        public string? Departamento { get; set; }
        public string? ClienteProyecto { get; set; }
        public string? QuienGestiona { get; set; } // Logistica / Usuario
        public string? TipoRequerimiento { get; set; } // Envio de paquete / Recoleccion / Solicitud de guia
        public DateTime? FechaEnvio { get; set; }
        public string? TipoEntrega { get; set; } // Domicilio / Ocurre
        public string? DireccionRemitenteTipo { get; set; } // Antiguo camino / Lateral via / Otro
        public string? DestinatarioCorreo { get; set; }
        public string? InformacionDimensionesPeso { get; set; } // Real / Aproximado

        // Origen / remitente
        public string? Empresa { get; set; }
        public string? RemitenteNombre { get; set; }
        public string? RemitenteTelefono { get; set; }

        [Required]
        public string? Origen { get; set; }

        public string? CodigoPostalOrigen { get; set; }

        // Destinatario / destino
        public string? DestinatarioNombre { get; set; }
        public string? DestinatarioTelefono { get; set; }

        [Required]
        public string? Destino { get; set; }

        public string? CodigoPostalDestino { get; set; }

        // Paquete
        public decimal? PesoKg { get; set; }
        public decimal? LargoCm { get; set; }
        public decimal? AnchoCm { get; set; }
        public decimal? AltoCm { get; set; }
        public string? ContenidoDeclarado { get; set; }

        // Servicio
        public string? TipoEnvio { get; set; }
        public bool? RequiereCadenaFrio { get; set; }
        public decimal Costo { get; set; }
        public string? Observaciones { get; set; }
        public DateTime? FechaSolicitud { get; set; } = DateTime.Now;

        // Flujo de aprobación de cambios
        public string? EstadoEdicion { get; set; }
        public string? MensajeEdicion { get; set; }
        public bool NotificacionLeida { get; set; }
        public string? DatosAntiguos { get; set; }

        // Auditoría de borrado
        public bool EstaBorrado { get; set; }
        public int? BorradoPor { get; set; }
        public string? MotivoBorrado { get; set; }
        public DateTime? FechaBorrado { get; set; }
    }
}
