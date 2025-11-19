namespace ProyectoMatrix.Models.Universidad
{
    public class IntentoEvaluacionViewModel
    {
        public int IntentoId { get; set; }
        public int UsuarioId { get; set; }
        public int SubCursoId { get; set; }

        public int NumeroIntento { get; set; }

        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }

        public decimal PuntajeObtenido { get; set; }
        public decimal PuntajeMaximo { get; set; }
        public bool Aprobado { get; set; }

        public int TiempoEmpleado { get; set; }
    }
}
