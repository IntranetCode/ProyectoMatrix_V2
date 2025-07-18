using System.Collections.Generic;

namespace ProyectoMatrix.Models
{
    public class UniversidadNivelViewModel
    {
        public string NivelSeleccionado { get; set; }
        /*public List<ContenidoEducativo> Cursos { get; set; }

        public UniversidadNivelViewModel()
        {
            Cursos = new List<ContenidoEducativo>();
        }
        */
        public List<Curso> Cursos { get; set; }  // ← nombre correcto en plural

        public UniversidadNivelViewModel()
        {
            Cursos = new List<Curso>();
        }
    }
}
