// =====================================================
// ARCHIVO: Models/AsignacionViewModels.cs
// PROPÓSITO: ViewModels para asignación masiva de cursos
// =====================================================

namespace ProyectoMatrix.Models
{
    // =====================================================
    // VIEWMODEL PRINCIPAL PARA ASIGNACIÓN MASIVA
    // =====================================================
    public class AsignacionMasivaViewModel
    {
        public List<CursoSimpleViewModel> Cursos { get; set; } = new List<CursoSimpleViewModel>();
        public List<EmpresaViewModel> Empresas { get; set; } = new List<EmpresaViewModel>();
    }

    // =====================================================
    // VIEWMODELS DE APOYO
    // =====================================================
    public class CursoSimpleViewModel
    {
        public int Id { get; set; }
        public string NombreCurso { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? NombreNivel { get; set; }
    }

    public class EmpresaViewModel
    {
        public int Id { get; set; }
        public string NombreEmpresa { get; set; } = string.Empty;
    }

    public class DepartamentoViewModel
    {
        public int Id { get; set; }
        public string NombreDepartamento { get; set; } = string.Empty;
        public int IdEmpresa { get; set; }
    }

    public class UsuarioAsignacionViewModel
    {
        public int Id { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? NombreEmpresa { get; set; }
        public string? NombreDepartamento { get; set; }
        public bool YaTieneCurso { get; set; }
        public bool Seleccionado { get; set; } = false;
    }

    public class ResultadoAsignacionMasiva
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public int UsuariosAsignados { get; set; }
        public int UsuariosOmitidos { get; set; }
    }

    public class AsignacionRecienteViewModel
    {
        public string NombreCurso { get; set; } = string.Empty;
        public int CantidadUsuarios { get; set; }
        public DateTime FechaAsignacion { get; set; }
        public DateTime? FechaLimite { get; set; }
        public string AsignadoPor { get; set; } = string.Empty;
        public string? NombreEmpresa { get; set; }
    }
}