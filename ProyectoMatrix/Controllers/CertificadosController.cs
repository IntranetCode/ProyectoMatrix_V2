using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Servicios; // Aquí está tu CertificadoDocument
using System.IO;
using Microsoft.Data.SqlClient; // 👈 Necesario para SQL
using Microsoft.Extensions.Configuration; // 👈 Para leer la cadena de conexión

namespace ProyectoMatrix.Controllers
{
    public class CertificadosController : Controller
    {
        private readonly IConfiguration _configuration;

        public CertificadosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ============================================================
        // 1️⃣ Método para generar un certificado en memoria (test)
        // ============================================================
        [HttpGet]
        public IActionResult Generar(string nombre, string curso)
        {
            // Ruta del logo
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Imagenes/logo.png");
            byte[] logo = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : new byte[0];

            // Crear el certificado temporal
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

        // ============================================================
        // 2️⃣ Método para descargar certificado ya generado (wwwroot)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> DescargarCertificado(int id)
        {
            try
            {
                var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
                if (!usuarioId.HasValue)
                    return RedirectToAction("Index", "Universidad");

                // Buscar certificado en la BD
                string query = @"
                    SELECT CodigoCertificado, ArchivoPDF 
                    FROM CertificadosEmitidos 
                    WHERE CertificadoID = @CertificadoID AND UsuarioID = @UsuarioID";

                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CertificadoID", id);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    await conn.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var archivo = reader["ArchivoPDF"]?.ToString();
                            var codigo = reader["CodigoCertificado"]?.ToString();

                            if (string.IsNullOrEmpty(archivo))
                                return RedirectToAction("MisCertificados", "Universidad");

                            var filePath = Path.Combine("wwwroot/certificados", archivo);
                            if (!System.IO.File.Exists(filePath))
                                return RedirectToAction("MisCertificados", "Universidad");

                            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                            var fileName = $"Certificado_{codigo}.pdf";
                            return File(fileBytes, "application/pdf", fileName);
                        }
                    }
                }

                TempData["Error"] = "Certificado no encontrado.";
                return RedirectToAction("MisCertificados", "Universidad");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al descargar certificado: {ex.Message}");
                TempData["Error"] = "Error al descargar el certificado.";
                return RedirectToAction("MisCertificados", "Universidad");
            }
        }
    }
}
