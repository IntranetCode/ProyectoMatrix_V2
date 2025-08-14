// =====================================================
// ARCHIVO: Models/UniversidadModels.cs
// PROPÓSITO: Modelos y ViewModels para Universidad NS
// =====================================================

namespace ProyectoMatrix.Models
{
    // =====================================================
    // ENTIDADES BASE
    // =====================================================

    public class NivelEducativo
    {
        public int NivelID { get; set; }
        public string NombreNivel { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int Orden { get; set; }
        public string? ColorHex { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    public class CursoCompleto
    {
        public int CursoID { get; set; }
        public int NivelID { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int? Duracion { get; set; }
        public string? ImagenCurso { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }

        // Información del nivel
        public string NombreNivel { get; set; } = string.Empty;
        public string ColorNivel { get; set; } = "#3b82f6";

        // Estadísticas
        public int TotalSubCursos { get; set; }
    }

    public class SubCursoDetalle
    {
        public int SubCursoID { get; set; }
        public int CursoID { get; set; }
        public string NombreSubCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int Orden { get; set; }
        public string? ArchivoVideo { get; set; }
        public string? ArchivoPDF { get; set; }
        public int? DuracionVideo { get; set; }
        public bool EsObligatorio { get; set; }
        public bool RequiereEvaluacion { get; set; }
        public decimal PuntajeMinimo { get; set; }

        // Estado del progreso del usuario
        public int TiempoTotalVisto { get; set; }
        public decimal PorcentajeVisto { get; set; }
        public bool Completado { get; set; }
        public DateTime? FechaCompletado { get; set; }

        // Control de acceso
        public bool PuedeAcceder { get; set; }

        // Última evaluación
        public IntentoEvaluacion? UltimoIntento { get; set; }

        // Helpers para UI
        public string EstadoTexto => Completado ? "Completado" :
                                   PorcentajeVisto > 0 ? "En Progreso" :
                                   PuedeAcceder ? "Disponible" : "Bloqueado";

        public string IconoEstado => Completado ? "fas fa-check-circle text-success" :
                                   PorcentajeVisto > 0 ? "fas fa-play-circle text-primary" :
                                   PuedeAcceder ? "fas fa-unlock text-info" : "fas fa-lock text-muted";

        public string DuracionFormateada => DuracionVideo.HasValue ?
            TimeSpan.FromSeconds(DuracionVideo.Value).ToString(@"mm\:ss") : "N/A";
    }

    public class IntentoEvaluacion
    {
        public int IntentoID { get; set; }
        public int UsuarioID { get; set; }
        public int SubCursoID { get; set; }
        public int EmpresaID { get; set; }
        public int NumeroIntento { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public decimal? PuntajeObtenido { get; set; }
        public decimal? PuntajeMaximo { get; set; }
        public bool Aprobado { get; set; }
        public int? TiempoEmpleado { get; set; }

        // Helpers
        public decimal PorcentajeCalificacion =>
            PuntajeObtenido.HasValue && PuntajeMaximo.HasValue && PuntajeMaximo > 0 ?
            (PuntajeObtenido.Value / PuntajeMaximo.Value) * 100 : 0;

        public string CalificacionTexto => Aprobado ? "Aprobado" : "Reprobado";

        public string TiempoEmpleadoTexto => TiempoEmpleado.HasValue ?
            $"{TiempoEmpleado} minutos" : "N/A";
    }

    public class CursoAsignado
    {
        public int CursoID { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? ImagenCurso { get; set; }
        public string NombreNivel { get; set; } = string.Empty;
        public string ColorNivel { get; set; } = "#3b82f6";
        public DateTime FechaAsignacion { get; set; }
        public DateTime? FechaLimite { get; set; }
        public bool EsObligatorio { get; set; }
        public int TotalSubCursos { get; set; }
        public int SubCursosCompletados { get; set; }
        public decimal PorcentajeProgreso { get; set; }
        public string Estado { get; set; } = string.Empty;

        // Helpers para UI
        public string EstadoClass => Estado switch
        {
            "Completado" => "badge bg-success",
            "En Progreso" => "badge bg-primary",
            "Asignado" => "badge bg-secondary",
            _ => "badge bg-light"
        };

        public bool TieneFechaLimite => FechaLimite.HasValue;

        public string FechaLimiteTexto => FechaLimite?.ToString("dd/MM/yyyy") ?? "Sin límite";

        public bool ProximoVencimiento => FechaLimite.HasValue &&
            FechaLimite.Value.Subtract(DateTime.Now).TotalDays <= 7 &&
            Estado != "Completado";
    }

    public class CertificadoEmitido
    {
        public int CertificadoID { get; set; }
        public string CodigoCertificado { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaExpiracion { get; set; }
        public string? ArchivoPDF { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string NombreNivel { get; set; } = string.Empty;
        public string NombreEmpresa { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public bool Activo { get; set; }

        // Helpers
        public string EstadoClass => Estado == "Vigente" ? "badge bg-success" : "badge bg-danger";

        public string FechaEmisionTexto => FechaEmision.ToString("dd/MM/yyyy");

        public string FechaExpiracionTexto => FechaExpiracion?.ToString("dd/MM/yyyy") ?? "No expira";

        public bool TieneArchivo => !string.IsNullOrEmpty(ArchivoPDF);
    }

    // =====================================================
    // REQUEST MODELS PARA SERVICIOS
    // =====================================================

    public class CrearCursoRequest
    {
        public int NivelID { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int? Duracion { get; set; }
        public string? ImagenCurso { get; set; }
        public int CreadoPorUsuarioID { get; set; }
    }

    public class CrearSubCursoRequest
    {
        public int CursoID { get; set; }
        public string NombreSubCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int Orden { get; set; }
        public string? ArchivoVideo { get; set; }
        public string? ArchivoPDF { get; set; }
        public int? DuracionVideo { get; set; }
        public bool EsObligatorio { get; set; } = true;
        public bool RequiereEvaluacion { get; set; } = true;
        public decimal PuntajeMinimo { get; set; } = 70.00m;
    }

    public class ActualizarProgresoRequest
    {
        public int UsuarioID { get; set; }
        public int SubCursoID { get; set; }
        public int EmpresaID { get; set; }
        public int TiempoTotalVisto { get; set; }
        public decimal PorcentajeVisto { get; set; }
    }

    // =====================================================
    // VIEWMODELS PARA CONTROLADORES
    // =====================================================

    public class UniversidadDashboardViewModel
    {
        public int UsuarioId { get; set; }
        public int RolId { get; set; }
        public int EmpresaId { get; set; }
        public string NombreUsuario { get; set; } = string.Empty;
        public string NombreEmpresa { get; set; } = string.Empty;

        // Permisos calculados
        public bool PuedeCrearCursos { get; set; }
        public bool PuedeAsignarCursos { get; set; }
        public bool PuedeVerReportes { get; set; }
        public bool PuedeConfiguracion { get; set; }

        // Datos del dashboard
        public List<CursoAsignadoViewModel> MisCursos { get; set; } = new();
        public List<CertificadoUsuarioViewModel> MisCertificados { get; set; } = new();

        public EstadisticasUniversidadViewModel Estadisticas { get; set; } = new();

        // Menú dinámico
        public List<MenuItemUniversidad> MenuItems { get; set; } = new();

        public string GetTituloSeccion()
        {
            return RolId switch
            {
                1 => "Administración Universidad NS",
                2 => "Configuración del Sistema",
                3 => "Gestión de Contenidos",
                4 => "Creación de Cursos",
                5 => "Mis Capacitaciones",
                6 => "Reportes y Auditoría",
                _ => "Universidad NS"
            };
        }
    }

    public class EstadisticasUniversidadViewModel
    {
        public int TotalCursosAsignados { get; set; }
        public int CursosCompletados { get; set; }
        public int CursosEnProgreso { get; set; }
        public int CertificadosObtenidos { get; set; }
        public decimal PromedioProgreso { get; set; }
        public int TiempoTotalEstudio { get; set; } // En minutos

        // Para roles administrativos
        public int TotalUsuariosActivos { get; set; }
        public int TotalCursosCreados { get; set; }
        public int CertificadosEmitidosMes { get; set; }

        // Helpers
        public string TiempoTotalEstudioTexto =>
            TimeSpan.FromMinutes(TiempoTotalEstudio).ToString(@"hh\:mm");

        public decimal PorcentajeCompletados =>
            TotalCursosAsignados > 0 ?
            (decimal)CursosCompletados / TotalCursosAsignados * 100 : 0;
    }

    public class MenuItemUniversidad
    {
        public string Titulo { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Icono { get; set; } = string.Empty;
        public bool EsActivo { get; set; }
        public int? Badge { get; set; }
        public List<MenuItemUniversidad> SubItems { get; set; } = new();
    }

    public class CursoDetalleViewModel
    {
        public CursoCompleto Curso { get; set; } = new();
        public List<SubCursoDetalle> SubCursos { get; set; } = new();
        public EstadisticasUniversidadViewModel ProgresoCurso { get; set; } = new();

        // Permisos del usuario actual
        public bool PuedeEditarCurso { get; set; }
        public bool PuedeEliminarCurso { get; set; }
        public bool PuedeAgregarSubCursos { get; set; }

        // Estado general del curso para el usuario
        public decimal ProgresoGeneral =>
            SubCursos.Any() ? SubCursos.Average(sc => sc.PorcentajeVisto) : 0;

        public bool CursoCompletado =>
            SubCursos.Any() && SubCursos.All(sc => sc.Completado);

        public int SubCursosBloqueados =>
            SubCursos.Count(sc => !sc.PuedeAcceder);
    }

    public class GestionCursosViewModel
    {
        public int RolId { get; set; }
        public bool PuedeAprobar { get; set; }
        public bool PuedeCrear { get; set; }
        public bool PuedeEditar { get; set; }

        public List<NivelEducativoViewModel> Niveles { get; set; } = new();
        public List<CursoCompleto> Cursos { get; set; } = new();
        

        // Filtros
        public int? NivelFiltro { get; set; }
        public string? NombreFiltro { get; set; }
        public string? EstadoFiltro { get; set; }

        public Dictionary<int, int> CursosPorNivel { get; set; } = new();
    }

    public class AsignacionCursosViewModel
    {
        public List<CursoCompleto> CursosDisponibles { get; set; } = new();
        public List<UsuarioResumen> Usuarios { get; set; } = new();
        public List<EmpresaResumen> Empresas { get; set; } = new();
        public List<DepartamentoResumen> Departamentos { get; set; } = new();

        // Formulario de asignación
        public AsignacionMasivaRequest FormularioAsignacion { get; set; } = new();
    }

    public class AsignacionMasivaRequest
    {
        public int CursoID { get; set; }
        public int TipoAsignacionID { get; set; } // 1=Individual, 2=Departamento, 3=Empresa, 4=Sociedad
        public int AsignadoPorUsuarioID { get; set; }

        // Targets específicos
        public int? EmpresaID { get; set; }
        public int? DepartamentoID { get; set; }
        public List<int> UsuariosSeleccionados { get; set; } = new();

        // Configuración
        public DateTime? FechaLimite { get; set; }
        public bool EsObligatorio { get; set; } = true;
        public string? Comentarios { get; set; }
    }

    public class UsuarioResumen
    {
        public int UsuarioID { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? NombreCompleto { get; set; }
        public string NombreEmpresa { get; set; } = string.Empty;
        public int CursosAsignados { get; set; }
        public int CursosCompletados { get; set; }
        public bool Activo { get; set; }
    }

    public class EmpresaResumen
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int TotalUsuarios { get; set; }
        public int UsuariosActivos { get; set; }
        public bool Activa { get; set; }
    }

    public class DepartamentoResumen
    {
        public int DepartamentoID { get; set; }
        public string NombreDepartamento { get; set; } = string.Empty;
        public int EmpresaID { get; set; }
        public string NombreEmpresa { get; set; } = string.Empty;
        public int TotalEmpleados { get; set; }
        public bool Activo { get; set; }
    }

    public class ReportesUniversidadViewModel
    {
        public int RolId { get; set; }
        public bool PuedeVerTodos { get; set; }

        // Datos de reportes
        public List<ReporteProgresoCurso> ProgresosCursos { get; set; } = new();
        public List<ReporteCertificado> Certificados { get; set; } = new();
        public List<ReporteUsuarioActividad> ActividadUsuarios { get; set; } = new();

        // Estadísticas generales
        public EstadisticasGeneralesReporte EstadisticasGenerales { get; set; } = new();

        // Filtros
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int? EmpresaFiltro { get; set; }
        public int? NivelFiltro { get; set; }
    }

    public class ReporteProgresoCurso
    {
        public string NombreCurso { get; set; } = string.Empty;
        public string NombreNivel { get; set; } = string.Empty;
        public int UsuariosAsignados { get; set; }
        public int UsuariosCompletados { get; set; }
        public int UsuariosEnProgreso { get; set; }
        public decimal PromedioProgreso { get; set; }
        public int TiempoPromedioComplecion { get; set; } // En días

        public decimal PorcentajeComplecion =>
            UsuariosAsignados > 0 ? (decimal)UsuariosCompletados / UsuariosAsignados * 100 : 0;
    }

    public class ReporteCertificado
    {
        public string CodigoCertificado { get; set; } = string.Empty;
        public string NombreUsuario { get; set; } = string.Empty;
        public string NombreCurso { get; set; } = string.Empty;
        public string NombreEmpresa { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaExpiracion { get; set; }
        public string Estado { get; set; } = string.Empty;
    }

    public class ReporteUsuarioActividad
    {
        public string NombreUsuario { get; set; } = string.Empty;
        public string NombreEmpresa { get; set; } = string.Empty;
        public int CursosAsignados { get; set; }
        public int CursosCompletados { get; set; }
        public int CursosEnProgreso { get; set; }
        public DateTime? UltimaActividad { get; set; }
        public int TiempoTotalEstudio { get; set; } // En minutos
        public int CertificadosObtenidos { get; set; }
    }

    public class EstadisticasGeneralesReporte
    {
        public int TotalUsuarios { get; set; }
        public int TotalCursos { get; set; }
        public int TotalCertificados { get; set; }
        public decimal ProgresoPromedio { get; set; }
        public int UsuariosActivosUltimos30Dias { get; set; }
        public int CursosCompletadosUltimos30Dias { get; set; }
        public int CertificadosEmitidosUltimos30Dias { get; set; }

        // Comparativa con período anterior
        public decimal CambioUsuariosActivos { get; set; }
        public decimal CambioCursosCompletados { get; set; }
        public decimal CambioCertificadosEmitidos { get; set; }
    }

    public class NivelEducativoViewModel
    {
        public int NivelID { get; set; }
        public string NombreNivel { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? ColorHex { get; set; }
        public int Orden { get; set; }
    }

    public class CursoAsignadoViewModel
    {
        public int CursoID { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int PorcentajeProgreso { get; set; }
        public DateTime FechaAsignacion { get; set; }
        public DateTime? FechaLimite { get; set; }
        public string? ImagenCurso { get; set; }
        public string? NombreNivel { get; set; }
        public bool EsObligatorio { get; set; }
        public string? ColorNivel { get; set; } = "#3b82f6";

        // ✅ AGREGAR ESTAS PROPIEDADES QUE FALTAN:
        public int TotalSubCursos { get; set; }
        public int SubCursosCompletados { get; set; }

        // Helpers para UI (propiedades calculadas)
        public string EstadoClass => Estado switch
        {
            "Completado" => "badge bg-success",
            "En Progreso" => "badge bg-primary",
            "Asignado" => "badge bg-secondary",
            _ => "badge bg-light"
        };

        public bool TieneFechaLimite => FechaLimite.HasValue;

        public string FechaLimiteTexto => FechaLimite?.ToString("dd/MM/yyyy") ?? "Sin límite";

        public bool ProximoVencimiento => FechaLimite.HasValue &&
            FechaLimite.Value.Subtract(DateTime.Now).TotalDays <= 7 &&
            Estado != "Completado";
    }
    public class CertificadoUsuarioViewModel
    {
        public int CertificadoID { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string CodigoCertificado { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaExpiracion { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool TieneArchivo { get; set; }
        public string? ArchivoPDF { get; set; }
        public string? NombreEmpresa { get; set; }
        public string? NombreNivel { get; set; }

        public string EstadoClass => Estado == "Vigente" ? "badge bg-success" : "badge bg-danger";

        public string FechaEmisionTexto => FechaEmision.ToString("dd/MM/yyyy");

        public string FechaExpiracionTexto => FechaExpiracion?.ToString("dd/MM/yyyy") ?? "No expira";



    }

    public class PreguntaEvaluacion
    {
        public int PreguntaID { get; set; }
        public int SubCursoID { get; set; }
        public string TextoPregunta { get; set; } = string.Empty;
        public string TipoPregunta { get; set; } = "Multiple"; // Multiple, Verdadero/Falso, Abierta
        public int Orden { get; set; }
        public decimal PuntajeMaximo { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }

        // Navegación
        public List<OpcionRespuesta> Opciones { get; set; } = new();
    }

    public class OpcionRespuesta
    {
        public int OpcionID { get; set; }
        public int PreguntaID { get; set; }
        public string TextoOpcion { get; set; } = string.Empty;
        public bool EsCorrecta { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; } = true;
    }

    public class CrearEvaluacionRequest
    {
        public int SubCursoID { get; set; }
        public List<CrearPreguntaRequest> Preguntas { get; set; } = new();
    }

    public class CrearPreguntaRequest
    {
        public string TextoPregunta { get; set; } = string.Empty;
        public string TipoPregunta { get; set; } = "Multiple";
        public decimal PuntajeMaximo { get; set; } = 1;
        public List<CrearOpcionRequest> Opciones { get; set; } = new();
    }

    public class CrearOpcionRequest
    {
        public string TextoOpcion { get; set; } = string.Empty;
        public bool EsCorrecta { get; set; }
    }


    public class EvaluacionViewModel
    {
        public int SubCursoID { get; set; }
        public string NombreSubCurso { get; set; } = string.Empty;
        public string NombreCurso { get; set; } = string.Empty;
        public List<PreguntaEvaluacion> Preguntas { get; set; } = new();
        public bool TieneEvaluacion => Preguntas.Any();
        public bool PuedeEditarEvaluacion { get; set; }
    }

    public class TomarEvaluacionViewModel
    {
        public int SubCursoID { get; set; }
        public string NombreSubCurso { get; set; } = string.Empty;
        public string NombreCurso { get; set; } = string.Empty;
        public List<PreguntaEvaluacion> Preguntas { get; set; } = new();
        public int TiempoLimiteMinutos { get; set; } = 30;
        public decimal PuntajeMinimoAprobacion { get; set; } = 70;
        public int NumeroIntento { get; set; } = 1;
        public IntentoEvaluacion? UltimoIntento { get; set; }
    }

    // =====================================================
    // MODELOS PARA ENTREGAR EVALUACIÓN
    // =====================================================

    public class EntregarEvaluacionRequest
    {
        public int SubCursoId { get; set; }
        public Dictionary<string, RespuestaUsuario> Respuestas { get; set; } = new();
        public int TiempoEmpleado { get; set; }
    }

    public class RespuestaUsuario
    {
        public string Tipo { get; set; } = string.Empty; // "opcion" o "abierta"
        public int? OpcionId { get; set; }
        public string? Texto { get; set; }
    }

    public class ResultadoEvaluacion
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal Calificacion { get; set; }
        public bool Aprobado { get; set; }
    }

    /*
    public class SubCursoDetalleViewModel
    {
        public int SubCursoID { get; set; }
        public int CursoID { get; set; }
        public string NombreSubCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int Orden { get; set; }
        public string? ArchivoVideo { get; set; }
        public string? ArchivoPDF { get; set; }
        public int? DuracionVideo { get; set; }
        public bool EsObligatorio { get; set; }
        public bool RequiereEvaluacion { get; set; }
        public bool PuedeAcceder { get; set; }
        public bool Completado { get; set; }
        public decimal PorcentajeVisto { get; set; }
        public DateTime? FechaCompletado { get; set; }
    }*/







}