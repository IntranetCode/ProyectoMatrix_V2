namespace ProyectoMatrix.Models.Universidad
{
    public class CursoResumenViewModel
    {
        public int CursoId { get; set; }
        public string NombreCurso { get; set; } = string.Empty;

        public int UsuariosAsignados { get; set; }
        public int NoIniciados { get; set; }
        public int EnProgreso { get; set; }
        public int Aprobados { get; set; }
        public int Reprobados { get; set; }

        // <summary>
        // Porcentaje de aprobación (0–100).
        // </summary>
        public decimal PorcentajeAprobacion { get; set; }
    }
}
