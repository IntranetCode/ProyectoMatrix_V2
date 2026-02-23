using System;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class Vacaciones
    {
    }

    public class VacacionesResumenAnualVm
    {
        public int Anio { get; set; }
        public int DiasCorrespondientes { get; set; }
        public decimal DiasExtra { get; set; }
        public decimal DiasTomados { get; set; }
        public decimal DiasCaducados { get; set; }
        public decimal DiasDisponibles { get; set; }
    }

    public class VacacionesSolicitudItemVm
    {
        public int SolicitudVacacionesID { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public DateTime FechaRegresoLabores { get; set; }
        public decimal DiasSolicitados { get; set; }
        public bool EsAnticipada { get; set; }
        public string EstadoAutorizacion { get; set; }
        public string EstadoRecursosHumanos { get; set; }
        public string Origen { get; set; }


        //Nuevos campos en la tabla de solicitudes 

        public DateTime FechaIngreso { get; set; }

        public decimal DiasTomados { get; set; }

        public decimal DiasDisponibles { get; set; }
    }

    public class MisVacacionesVm
    {
        public string NombreCompleto { get; set; }
        public string NumeroEmpleado { get; set; }
        public VacacionesResumenAnualVm ResumenActual { get; set; }
        public List<VacacionesSolicitudItemVm> Solicitudes { get; set; } = new();
    }


    public class CrearSolicitudVacacionesVm
    {
        [Required(ErrorMessage = "La fecha de inicio es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime FechaInicio { get; set; }

        [Required(ErrorMessage = "La fecha de fin es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime FechaFin { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Solo para mostrar info al usuario
        public decimal DiasDisponiblesActuales { get; set; }
    }


    public class VacacionesSolicitudJefeVm
    {
        public int SolicitudVacacionesID { get; set; }
        public string NumeroEmpleado { get; set; }
        public string NombreColaborador { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public decimal DiasSolicitados { get; set; }
        public bool EsAnticipada { get; set; }
        public string EstadoAutorizacion { get; set; }
        public string EstadoRecursosHumanos { get; set; }
    }


    public class VacacionesSolicitudRHVm
    {
        public int SolicitudVacacionesID { get; set; }
        public string NumeroEmpleado { get; set; }
        public string NombreColaborador { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public decimal DiasSolicitados { get; set; }
        public bool EsAnticipada { get; set; }
        public string EstadoAutorizacion { get; set; }
        public string EstadoRecursosHumanos { get; set; }
    }

    //Modelo para el formato imprimible de solicitud de vacaiones

    public class VacacionesSolicitudFormatoVm
    {
        public int SolicitudVacacionesID { get; set; }

        // Colaborador
        public string NumeroEmpleado { get; set; }
        public string NombreColaborador { get; set; }
        public string Puesto { get; set; }
        public DateTime? FechaIngreso { get; set; }
        public int AntiguedadAnios { get; set; }

        public string Sociedad { get; set; }
        public string Empresa { get; set; }   // si quieres, luego diferencias ambos

        public string Departamento { get; set; }

        // Jefe
        public string NombreJefe { get; set; }

        // Solicitud
        public DateTime FechaSolicitud { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public decimal DiasSolicitados { get; set; }
        public bool EsAnticipada { get; set; }
        public string EstadoAutorizacion { get; set; }
        public string EstadoRH { get; set; }
        public DateTime? FechaAutorizacion { get; set; }
        public DateTime? FechaRegistroRH { get; set; }

        // Saldos (del año relevante / último)
        public decimal DiasCorrespondientes { get; set; }
        public decimal DiasTomados { get; set; }
        public decimal DiasDisponibles { get; set; }
    }

    public class VacacionesBandejaRHVm
    {
        public int Folio { get; set; }
        public int PersonaID { get; set; }

        public string NumeroEmpleado { get; set; }
        public string NombreCompleto { get; set; }
        public string ClaveEmpleadoNomina { get; set; }
        public string Puesto { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public DateTime FechaRegresoLabores { get; set; }

        public decimal DiasSolicitados { get; set; }
        public bool EsAnticipada { get; set; }

        public string EstadoAutorizacion { get; set; }
        public string EstadoRecursosHumanos { get; set; }

        public int AnioSaldo { get; set; }
        public decimal DiasCorrespondientes { get; set; }
        public decimal DiasExtra { get; set; }
        public decimal DiasTomados { get; set; }
        public decimal DiasCaducados { get; set; }
        public decimal DiasDisponibles { get; set; }
    }

    public class VacacionesUsuarioSaldoRHVm
    {
        public int PersonaID { get; set; }
        public string NumeroEmpleado { get; set; }
        public string ClaveEmpleadoNomina { get; set; }
        public string NombreCompleto { get; set; }
        public string Puesto { get; set; }

        public int Anio { get; set; }
        public decimal DiasCorrespondientes { get; set; }
        public decimal DiasExtra { get; set; }
        public decimal DiasTomados { get; set; }
        public decimal DiasCaducados { get; set; }
        public decimal DiasDisponibles { get; set; }

        public int AnticipadasRegistradas { get; set; }
        public int AnticipadasPorRegistrar { get; set; }
    }

    public class VacacionesBandejaRHPantallaVm
    {
        public string Tab { get; set; } = "autorizadas";
        public List<VacacionesBandejaRHVm> Solicitudes { get; set; } = new();
        public List<VacacionesUsuarioSaldoRHVm> Usuarios { get; set; } = new();

        public List<VacacionesVistaExcelVm> VistaExcel { get; set; } = new();


    }

    public class SolicitudesPendientesJefePantallaVm
    {
        public List<VacacionesSolicitudJefeVm> Pendientes { get; set; } = new();
        public List<VacacionesSolicitudJefeVm> Proximas { get; set; } = new();
        public List<VacacionesSolicitudJefeVm> Historial { get; set; } = new();
    }

    public class VacacionesVistaExcelVm
    {
        public int N { get; set; }
        public int N2 { get; set; }
        public string Sociedad { get; set; }
        public string Nombre { get; set; }
        public string Puesto { get; set; }
        public string Departamento { get; set; }

        public DateTime FechaIngreso { get; set; }
        public DateTime Hoy { get; set; }

        public string AntiguedadAniosMeses { get; set; }   // ej: "17-01"
        public int AntiguedadAnios { get; set; }           // ej: 17

        public decimal DiasCorrespondientes { get; set; }
        public decimal DiasPendientes { get; set; }        // si RH lo quiere
        public decimal DiasDisponibles { get; set; }
    }







}
