using Microsoft.AspNetCore.Mvc;

namespace ProyectoMatrix.Controllers
{
    public class LiderController : Controller
    {
        public IActionResult Index()
        {

            // ===== AGREGAR ESTAS LÍNEAS =====
            ViewBag.TituloNavbar = "Líder a Líder";
            ViewBag.LogoNavbar = ""; // Vacío para usar ícono
                                     // ===== FIN DE LÍNEAS AGREGADAS =====

            return View();
        }
    }
}
