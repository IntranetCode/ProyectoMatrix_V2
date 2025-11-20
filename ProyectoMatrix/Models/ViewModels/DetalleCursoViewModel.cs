namespace ProyectoMatrix.Models.Universidad
{
    public class DetalleCursoViewModel
    {
        public int AsignacionId { get; set; }

        public int CursoId { get; set; }
        public string NombreCurso { get; set; } = string.Empty;

        // Identificación de usuario
        public int UsuarioId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? NombreUsuario { get; set; } 

        // Empresa / área
        public int? EmpresaId { get; set; }
        public string? NombreEmpresa { get; set; }

        public int? DepartamentoId { get; set; }
        public string? NombreDepartamento { get; set; }

        // Estado y métricas
        public string EstadoCurso { get; set; } = string.Empty;
        public decimal? CalificacionFinal { get; set; }

        public int? IntentosTotales { get; set; }
        public int? IntentosAprobados { get; set; }
        public int? IntentosNoAprobados { get; set; }

        // Fechas
        public DateTime FechaAsignacion { get; set; }
        public DateTime? FechaInicioCurso { get; set; }
        public DateTime? FechaTerminoCurso { get; set; }
        public DateTime? FechaUltimaActividad { get; set; }
    }
}
