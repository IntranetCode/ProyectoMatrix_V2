using Microsoft.AspNetCore.Mvc;

namespace ProyectoMatrix.Controllers
{
    public class ComunicadosController : Controller
    {
        public IActionResult Index()
        {
            // ===== AGREGAR ESTAS LÍNEAS =====
            ViewBag.TituloNavbar = "Comunicados";
            ViewBag.LogoNavbar = ""; // Vacío para usar ícono
                                     // ===== FIN DE LÍNEAS AGREGADAS =====
            return View();
        }
    }
}
