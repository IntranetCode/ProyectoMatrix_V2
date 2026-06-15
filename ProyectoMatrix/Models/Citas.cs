using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    // ==========================================================
    // INDEX PRINCIPAL
    // ==========================================================

    public class CitasIndexVm
    {
        public bool EsEditor { get; set; }

        public bool EsUsuarioFinal { get; set; }

        public int? RolID { get; set; }

        public bool ModuloVisible { get; set; }

        public string? AlertaSistema { get; set; }

        public int? EmpresaID { get; set; }

        public string? NombreEmpresa { get; set; }

        public CitasUsuarioIndexVm Usuario { get; set; } = new();

        public CitasEditorIndexVm Editor { get; set; } = new();
    }

    public class CitasUsuarioIndexVm
    {
        public string NombreUsuario { get; set; } = "Usuario";

        public string NombreEmpresa { get; set; } = string.Empty;

        public bool TieneAsignacionEmpresa { get; set; }

        public bool ModuloVisible { get; set; }

        public string EstadoPantalla { get; set; } = "sin_asignacion";

        public bool TieneEventoDisponible { get; set; }

        public bool PuedeAgendar { get; set; }

        public bool TieneCita { get; set; }

        public bool EntrevistaFinalizada { get; set; }

        public int? AgendaID { get; set; }

        public int? AgendaEmpresaID { get; set; }

        public string? NombreEvento { get; set; }

        public int? CitaID { get; set; }

        public DateTime? FechaInicioEvento { get; set; }

        public DateTime? FechaFinEvento { get; set; }

        public DateTime? FechaInicioSolicitud { get; set; }

        public DateTime? FechaFinSolicitud { get; set; }

        public DateTime? FechaCita { get; set; }

        public TimeSpan? HoraInicio { get; set; }

        public TimeSpan? HoraFin { get; set; }

        public string? EstadoCita { get; set; }

        public string? EstadoFormulario { get; set; }

        public string? MensajeUsuario { get; set; }

        public string? ResultadoResumen { get; set; }

        public List<CitasUsuarioEventoVm> Eventos { get; set; } = new();
    }

    public class CitasUsuarioEventoVm
{
    public int AgendaID { get; set; }

    public int AgendaEmpresaID { get; set; }

    public int EmpresaID { get; set; }

    public string NombreEvento { get; set; } = string.Empty;

    public string NombreEmpresa { get; set; } = string.Empty;

    public DateTime? FechaInicioEvento { get; set; }

    public DateTime? FechaFinEvento { get; set; }

    public DateTime? FechaInicioSolicitud { get; set; }

    public DateTime? FechaFinSolicitud { get; set; }

    public DateTime? FechaCierreAgendamiento { get; set; }

    public string EstadoAsignacion { get; set; } = string.Empty;

    public string EstatusEvento { get; set; } = string.Empty;

    public bool AgendaActiva { get; set; }

    public bool AgendaEmpresaActiva { get; set; }

    public bool CuestionarioActivo { get; set; }

    public string EstatusCuestionario { get; set; } = string.Empty;

    public bool PuedeAgendar { get; set; }

    public bool TieneSlotsDisponibles { get; set; }

    public bool TieneCita { get; set; }

    public int? CitaID { get; set; }

    public DateTime? FechaCita { get; set; }

    public TimeSpan? HoraInicio { get; set; }

    public TimeSpan? HoraFin { get; set; }

    public string? EstadoCita { get; set; }

    public string? EstadoFormulario { get; set; }

    public string EstadoPantalla { get; set; } = "sin_asignacion";

    public string MensajeUsuario { get; set; } = string.Empty;
}

    public class CitasEditorIndexVm
    {
        public int TotalEventosActivos { get; set; }

        public int TotalEmpresasHabilitadas { get; set; }

        public int TotalCitasAgendadas { get; set; }

        public int TotalEntrevistasFinalizadas { get; set; }

        public int TotalNoAsistieron { get; set; }

        public int TotalPendientes { get; set; }

        public List<CitasEventoResumenVm> Eventos { get; set; } = new();

        public List<CitasEmpresaAvanceVm> Empresas { get; set; } = new();
    }

    public class CitasEventoResumenVm
    {
        public int AgendaID { get; set; }

        public string NombreEvento { get; set; } = string.Empty;

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        public int EmpresasHabilitadas { get; set; }

        public int CitasAgendadas { get; set; }
    }

    public class CitasEmpresaAvanceVm
    {
        public int AgendaID { get; set; }

        public string NombreEvento { get; set; } = string.Empty;

        public int EmpresaID { get; set; }

        public string NombreEmpresa { get; set; } = string.Empty;

        public int TotalUsuarios { get; set; }

        public int Agendaron { get; set; }

        public int TomaronEntrevista { get; set; }

        public int NoAsistieron { get; set; }

        public int FaltanPorAgendar { get; set; }

        public int FaltanPorEntrevistar { get; set; }

        public decimal PorcentajeAvance { get; set; }
    }

    // ==========================================================
    // AGENDAR CITA USUARIO
    // ==========================================================

    public class AgendarCitaVm
    {
        public int AgendaID { get; set; }

        public int AgendaEmpresaID { get; set; }

        public string NombreEvento { get; set; } = string.Empty;

        public DateTime FechaInicioSolicitud { get; set; }

        public DateTime FechaFinSolicitud { get; set; }

        public List<SlotDisponibleVm> Slots { get; set; } = new();
    }

    public class SlotDisponibleVm
    {
        public DateTime Fecha { get; set; }

        public TimeSpan HoraInicio { get; set; }

        public TimeSpan HoraFin { get; set; }

        public bool Disponible { get; set; } = true;

        public string TextoFecha => Fecha.ToString("dd/MM/yyyy");

        public string TextoHorario => $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";
    }

    public class AgendarCitaPostVm
    {
        public int AgendaID { get; set; }

        public int AgendaEmpresaID { get; set; }

        [Required]
        public DateTime FechaCita { get; set; }

        [Required]
        public TimeSpan HoraInicio { get; set; }

        [Required]
        public TimeSpan HoraFin { get; set; }
    }

    // ==========================================================
    // SOLO PARA GENERAR SLOTS DESDE AgendaDias
    // ==========================================================
    // Este ViewModel lo usa el helper GenerarSlotsDisponiblesAsync.
    // No es entidad y no lleva [Table].

    public class AgendaDiaEditorVm
    {
        public int AgendaDiaID { get; set; }

        public int AgendaID { get; set; }

        public int DiaSemana { get; set; }

        public string NombreDia { get; set; } = string.Empty;

        public bool Activo { get; set; }

        public TimeSpan HoraInicio { get; set; } = new TimeSpan(9, 0, 0);

        public TimeSpan HoraFin { get; set; } = new TimeSpan(17, 0, 0);

        public int DuracionCitaMinutos { get; set; } = 40;

        public int DescansoMinutos { get; set; } = 20;

        public int CapacidadCitas { get; set; } = 1;
    }

    // ==========================================================
    // MODELOS PARA SIGUIENTES ETAPAS
    // ==========================================================
    // Los dejamos porque tu Index ya tiene ligas a Bandeja, Reportes,
    // CrearAgenda y CrearCuestionario. Todavía no tienen que estar completos.

    public class CuestionarioListaVm
    {
        public int CuestionarioID { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public int Version { get; set; }

        public string Estatus { get; set; } = string.Empty;

        public bool Activo { get; set; }

        public DateTime FechaCreacion { get; set; }

        public string CreadoPor { get; set; } = string.Empty;

        public int TotalPreguntas { get; set; }
    }

    public class CuestionariosIndexVm
    {
        public string FiltroEstatus { get; set; } = "todos";

        public string Busqueda { get; set; } = string.Empty;

        public int TotalCuestionarios { get; set; }

        public int TotalBorradores { get; set; }

        public int TotalActivos { get; set; }

        public int TotalCerrados { get; set; }

        public List<CuestionarioListaVm> Cuestionarios { get; set; } = new();
    }

    public class CrearCuestionarioVm
    {
        public int? CuestionarioID { get; set; }

        [Required(ErrorMessage = "El nombre del cuestionario es obligatorio.")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public int Version { get; set; } = 1;

        public string Estatus { get; set; } = "borrador";

        public List<PreguntaEditorVm> Preguntas { get; set; } = new();
    }

    public class PreguntaEditorVm
    {
        public int? PreguntaID { get; set; }

        public int? CuestionarioID { get; set; }

        [Required(ErrorMessage = "El texto de la pregunta es obligatorio.")]
        [StringLength(500)]
        public string TextoPregunta { get; set; } = string.Empty;

        [Required]
        public string TipoPregunta { get; set; } = "texto";

        [StringLength(100)]
        public string? Dimension { get; set; }

        public int Orden { get; set; }

        public bool Obligatoria { get; set; } = true;

        public string? ConfiguracionJson { get; set; }

        public bool Activa { get; set; } = true;

        public List<OpcionPreguntaEditorVm> Opciones { get; set; } = new();
    }

    public class OpcionPreguntaEditorVm
    {
        public int? OpcionID { get; set; }

        public int PreguntaID { get; set; }

        [Required(ErrorMessage = "El texto de la opción es obligatorio.")]
        [StringLength(300)]
        public string TextoOpcion { get; set; } = string.Empty;

        public decimal ValorPuntaje { get; set; }

        public int Orden { get; set; }

        public bool Activa { get; set; } = true;
    }

    public class CrearAgendaVm
    {
        public int AgendaID { get; set; }

        public int? CuestionarioID { get; set; }

        [Required(ErrorMessage = "El nombre del evento es obligatorio.")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "La fecha de inicio es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime FechaInicio { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "La fecha de fin es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime FechaFin { get; set; } = DateTime.Today;

        public string Estatus { get; set; } = "borrador";

        public List<SelectListItem> Cuestionarios { get; set; } = new();

        public List<AgendaEmpresaEditorVm> Empresas { get; set; } = new();

        public List<AgendaDiaEditorVm> Dias { get; set; } = new();
    }

    public class AgendaEmpresaEditorVm
    {
        public int AgendaEmpresaID { get; set; }

        public int EmpresaID { get; set; }

        public string NombreEmpresa { get; set; } = string.Empty;

        public bool Seleccionada { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? FechaInicioSolicitud { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? FechaFinSolicitud { get; set; }

        public string EstadoAsignacion { get; set; } = "programada";

        public bool MostrarAntesDeInicio { get; set; } = true;

        public bool MostrarDespuesDeFin { get; set; } = true;

        [StringLength(300)]
        public string? MensajeAntesDeInicio { get; set; }

        [StringLength(300)]
        public string? MensajeDespuesDeFin { get; set; }
    }

    public class CitasBandejaVm
    {
        public int? AgendaID { get; set; }

        public int? EmpresaID { get; set; }

        public DateTime? Fecha { get; set; }

        public string? AlertaSistema { get; set; }

        public List<CitasBandejaEventoVm> Eventos { get; set; } = new();

        public List<CitasBandejaEmpresaVm> Empresas { get; set; } = new();

        public List<CitaBandejaVm> Citas { get; set; } = new();

        public int Total => Citas.Count;

        public int Pendientes => Citas.Count(x =>
            string.Equals(x.Estado, "pendiente", StringComparison.OrdinalIgnoreCase));

        public int Asistieron => Citas.Count(x =>
            string.Equals(x.Estado, "asistio", StringComparison.OrdinalIgnoreCase));

        public int Finalizadas => Citas.Count(x =>
            string.Equals(x.Estado, "finalizada", StringComparison.OrdinalIgnoreCase));

        public int NoAsistieron => Citas.Count(x =>
            string.Equals(x.Estado, "no_asistio", StringComparison.OrdinalIgnoreCase));

        public int EnCaptura => Citas.Count(x =>
            string.Equals(x.EstadoFormulario, "en_captura", StringComparison.OrdinalIgnoreCase));
    }

    public class CitasBandejaEventoVm
    {
        public int AgendaID { get; set; }

        public string NombreEvento { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        public string Estatus { get; set; } = string.Empty;

        public int EmpresasParticipantes { get; set; }

        public int TotalCitas { get; set; }

        public int Pendientes { get; set; }

        public int Finalizadas { get; set; }

        public int NoAsistieron { get; set; }

        public decimal PorcentajeAvance
        {
            get
            {
                if (TotalCitas <= 0)
                    return 0;

                return Math.Round((Finalizadas * 100m) / TotalCitas, 1);
            }
        }
    }

    public class CitasBandejaEmpresaVm
    {
        public int AgendaEmpresaID { get; set; }

        public int AgendaID { get; set; }

        public int EmpresaID { get; set; }

        public string NombreEmpresa { get; set; } = string.Empty;

        public string EstadoAsignacion { get; set; } = string.Empty;

        public DateTime FechaInicioSolicitud { get; set; }

        public DateTime FechaFinSolicitud { get; set; }

        public int TotalUsuarios { get; set; }

        public int TotalCitas { get; set; }

        public int Pendientes { get; set; }

        public int Asistieron { get; set; }

        public int Finalizadas { get; set; }

        public int NoAsistieron { get; set; }

        public decimal PorcentajeAvance
        {
            get
            {
                if (TotalCitas <= 0)
                    return 0;

                return Math.Round((Finalizadas * 100m) / TotalCitas, 1);
            }
        }
    }

    public class CitaBandejaVm
    {
        public int CitaID { get; set; }

        public int AgendaID { get; set; }

        public int AgendaEmpresaID { get; set; }

        public int EmpresaID { get; set; }

        public int PersonaID { get; set; }

        public string NombrePersona { get; set; } = string.Empty;

        public string NombreEmpresa { get; set; } = string.Empty;

        public string NombreEvento { get; set; } = string.Empty;

        public DateTime FechaCita { get; set; }

        public TimeSpan HoraInicio { get; set; }

        public TimeSpan HoraFin { get; set; }

        public string Estado { get; set; } = string.Empty;

        public string EstadoFormulario { get; set; } = string.Empty;

        public string Horario => $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";

        public string Folio => $"CIT-{CitaID:D5}";
    }

    public class CapturarEntrevistaVm
    {
        public int CitaID { get; set; }

        public int AgendaID { get; set; }

        public int? CuestionarioID { get; set; }

        public int PersonaID { get; set; }

        public int EmpresaID { get; set; }

        public string NombreEvento { get; set; } = string.Empty;

        public string NombreCuestionario { get; set; } = string.Empty;

        public string NombrePersona { get; set; } = string.Empty;

        public string NombreEmpresa { get; set; } = string.Empty;

        public DateTime FechaCita { get; set; }

        public TimeSpan HoraInicio { get; set; }

        public TimeSpan HoraFin { get; set; }

        public string Estado { get; set; } = string.Empty;

        public string EstadoFormulario { get; set; } = string.Empty;

        public List<PreguntaEntrevistaVm> Preguntas { get; set; } = new();

        public List<RespuestaEntrevistaVm> Respuestas { get; set; } = new();

        public string ModoEvaluacion { get; set; } = "cuestionario";

        public List<DimensionEvaluadaVm> DimensionesEvaluadas { get; set; } = new();
    }

    public class PreguntaEntrevistaVm
    {
        public int? PreguntaID { get; set; }

        public string TextoPregunta { get; set; } = string.Empty;

        public string TipoPregunta { get; set; } = string.Empty;

        public string? Dimension { get; set; }

        public bool Obligatoria { get; set; }

        public List<OpcionEntrevistaVm> Opciones { get; set; } = new();
    }

    public class OpcionEntrevistaVm
    {
        public int? OpcionID { get; set; }

        public string TextoOpcion { get; set; } = string.Empty;

        public decimal ValorPuntaje { get; set; }
    }

    public class RespuestaEntrevistaVm
    {
        public int PreguntaID { get; set; }

        public int? OpcionID { get; set; }

        public List<int> OpcionesSeleccionadasID { get; set; } = new();

        public string? ValorTexto { get; set; }

        public decimal? ValorNumerico { get; set; }
    }


public class ResultadoDimensionVm
{
    public string Dimension { get; set; } = string.Empty;
    public decimal Promedio { get; set; }
    public decimal Porcentaje { get; set; }
    public int TotalPreguntas { get; set; }

    public string Nivel { get; set; } = string.Empty;
    public string CssNivel { get; set; } = string.Empty;
}




// ==========================================================
// RESULTADOS / MAPEO DE TALENTO
// ==========================================================

public class ResultadosAdminVm
{
    public int? AgendaID { get; set; }

    public int? EmpresaID { get; set; }

    public string? Busqueda { get; set; }

    public int TotalPersonas { get; set; }

    public int TotalFinalizadas { get; set; }

    public int TotalAsistieron { get; set; }

    public int TotalConResultado { get; set; }

    public decimal PorcentajeGlobal { get; set; }

    public string NivelGeneral { get; set; } = "Sin calificación";

    public string CssNivelGeneral { get; set; } = "score-muted";

    public List<ResultadoPersonaResumenVm> Personas { get; set; } = new();

    public List<ResultadoDimensionGlobalVm> DimensionesGlobales { get; set; } = new();
}

public class ResultadoPersonaResumenVm
{
    public int CitaID { get; set; }

    public int PersonaID { get; set; }

    public int AgendaID { get; set; }

    public int EmpresaID { get; set; }

    public string Folio => $"CIT-{CitaID:D5}";

    public string NombrePersona { get; set; } = string.Empty;

    public string NombreEmpresa { get; set; } = string.Empty;

    public string NombreEvento { get; set; } = string.Empty;

    public DateTime FechaCita { get; set; }

    public TimeSpan HoraInicio { get; set; }

    public TimeSpan HoraFin { get; set; }

    public string Estado { get; set; } = string.Empty;

    public string EstadoFormulario { get; set; } = string.Empty;

    public decimal PuntajeGlobal { get; set; }

    public decimal PorcentajeGlobal { get; set; }

    public int TotalDimensiones { get; set; }

    public int TotalPreguntasEvaluables { get; set; }

    public string NivelGeneral { get; set; } = "Sin calificación";

    public string CssNivelGeneral { get; set; } = "score-muted";

        public bool TieneResultado =>
         TotalPreguntasEvaluables > 0 || TotalDimensiones > 0;
    }

public class ResultadoDimensionGlobalVm
{
    public string Dimension { get; set; } = string.Empty;

    public decimal Promedio { get; set; }

    public decimal Porcentaje { get; set; }

    public int TotalPersonas { get; set; }

    public string Nivel { get; set; } = string.Empty;

    public string CssNivel { get; set; } = "score-muted";
}

public class ResultadoCitaVm
{
    public int CitaID { get; set; }

    public int AgendaID { get; set; }

    public int? CuestionarioID { get; set; }

    public int EmpresaID { get; set; }

    public int PersonaID { get; set; }

    public string NombrePersona { get; set; } = string.Empty;

    public string NombreEmpresa { get; set; } = string.Empty;

    public string NombreEvento { get; set; } = string.Empty;

    public string NombreCuestionario { get; set; } = string.Empty;

    public DateTime? FechaCita { get; set; }

    public TimeSpan? HoraInicio { get; set; }

    public TimeSpan? HoraFin { get; set; }

    public string Estado { get; set; } = string.Empty;

    public string EstadoFormulario { get; set; } = string.Empty;

    public string ResultadoJson { get; set; } = string.Empty;

    public DateTime? FechaResultado { get; set; }

    public decimal PuntajeGlobal { get; set; }

    public decimal PorcentajeGlobal { get; set; }

    public int TotalDimensiones { get; set; }

    public int TotalPreguntas { get; set; }

    public int TotalRespondidas { get; set; }

    public int TotalPreguntasEvaluables { get; set; }

    public string NivelGeneral { get; set; } = "Sin calificación";

    public string CssNivelGeneral { get; set; } = "score-muted";

    public string ResumenResultado { get; set; } = string.Empty;

        public bool TieneGrafica => TotalPreguntasEvaluables > 0 || TotalDimensiones > 0;
        public List<ResultadoDimensionVm> Dimensiones { get; set; } = new();

    public List<ResultadoPreguntaVm> Preguntas { get; set; } = new();
      
    }



public class ResultadoPreguntaVm
{
    public int PreguntaID { get; set; }

    public string TextoPregunta { get; set; } = string.Empty;

    public string TipoPregunta { get; set; } = string.Empty;

    public string? Dimension { get; set; }

    public string RespuestaTexto { get; set; } = "Sin respuesta";

    public decimal? Puntaje { get; set; }

    public decimal Maximo { get; set; } = 5m;

    public decimal Porcentaje { get; set; }

    public string Nivel { get; set; } = "No evaluable";

    public string CssNivel { get; set; } = "score-muted";

    public bool EsEvaluable => Puntaje.HasValue && Maximo > 0;
}

public class ResultadosIndexVm
{
    public int RolID { get; set; }

    public bool EsAdmin => RolID == 4;

    public bool EsUsuarioFinal => RolID == 5;

    public ResultadosAdminVm Admin { get; set; } = new();

    public ResultadoCitaVm? Individual { get; set; }

    public string? Mensaje { get; set; }
}

    public class DimensionEvaluadaVm
    {
        public int? CitaDimensionID { get; set; }

        public string DimensionNombre { get; set; } = string.Empty;

        public decimal? Calificacion { get; set; }

        public decimal CalificacionMaxima { get; set; } = 10m;

        public string? Comentario { get; set; }

        public int Orden { get; set; }
    }



}