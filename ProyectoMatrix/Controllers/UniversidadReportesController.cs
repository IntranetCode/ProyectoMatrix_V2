using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.Universidad;
using System.Text;
using System.Globalization;

namespace ProyectoMatrix.Controllers
{
    public class UniversidadReportesController : Controller
    {
        private readonly UniversidadReportesRepository _repositorio;

        public UniversidadReportesController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            _repositorio = new UniversidadReportesRepository(connectionString);
        }

        //Vista de resumen de cursos

        public IActionResult ResumenCursos()
        {
            var modelo = _repositorio.ObtenerResumenCursos();
            return View("~/Views/Universidad/ResumenCursos.cshtml", modelo);
        }


        //Vista para detalles de un curso
        public IActionResult DetalleCurso(int cursoId)
        {
            var modelo = _repositorio.ObtenerDetalleCurso(cursoId);

            var curso = modelo.FirstOrDefault();
            ViewBag.NombreCurso = curso?.NombreCurso ?? "Curso";

            return View("~/Views/Universidad/DetalleCurso.cshtml", modelo);
        }

        //Vista para intentos de evaluación en un usuario

        public IActionResult IntentosUsuarioCurso(int cursoId, int usuarioId, string? nombreUsuario, string? nombreCurso)
        {
            var modelo = _repositorio.ObtenerIntentosPorUsuarioCurso(cursoId, usuarioId);

            // Para el título de la vista:
            ViewBag.NombreUsuario = nombreUsuario ?? $"Usuario {usuarioId}";
            ViewBag.NombreCurso = nombreCurso ?? $"Curso {cursoId}";

            return View("~/Views/Universidad/IntentosUsuarioCurso.cshtml", modelo);
        }

        public IActionResult ExportarResumenCursos()
        {
            var datos = _repositorio.ObtenerResumenCursos();

            var sb = new StringBuilder();

            // Encabezados (usa ; porque Excel en español lo entiende bien)
            sb.AppendLine("Curso;UsuariosAsignados;NoIniciados;EnProgreso;Aprobados;Reprobados;PorcentajeAprobacion");

            foreach (var item in datos)
            {
                // Por si el nombre del curso trae ; o saltos de línea
                var nombreCurso = (item.NombreCurso ?? string.Empty)
                    .Replace(";", ",")
                    .Replace("\r", " ")
                    .Replace("\n", " ");

                var linea = string.Format(CultureInfo.InvariantCulture,
                    "{0};{1};{2};{3};{4};{5};{6}",
                    nombreCurso,
                    item.UsuariosAsignados,
                    item.NoIniciados,
                    item.EnProgreso,
                    item.Aprobados,
                    item.Reprobados,
                    item.PorcentajeAprobacion.ToString("0.##", CultureInfo.InvariantCulture)
                );

                sb.AppendLine(linea);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Reporte_Cursos_Resumen_{DateTime.Now:yyyyMMdd_HHmm}.csv";

            return File(bytes, "text/csv", fileName);
        }


    }
}
