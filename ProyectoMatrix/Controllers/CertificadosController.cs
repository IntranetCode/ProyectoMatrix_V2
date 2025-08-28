using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Servicios; // Aquí está tu CertificadoDocument
using System.IO;

namespace ProyectoMatrix.Controllers
{
    public class CertificadosController : Controller
    {
        [HttpGet]
        public IActionResult Generar(string nombre, string curso)
        {
            // Ruta del logo
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Imagenes/logo.png");
            byte[] logo = System.IO.File.ReadAllBytes(logoPath);

            // Crear el certificado
            var certificado = new CertificadoDocument(
                nombre,
                curso,
                DateTime.Now,
                logo
            );

            // Guardar en memoria
            using var stream = new MemoryStream();
            certificado.GeneratePdf(stream);

            // Devolver al navegador como descarga
            return File(stream.ToArray(), "application/pdf", $"Certificado_{nombre}.pdf");
        }
    }
}
