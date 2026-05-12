using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class DenunciasAnonimasController : Controller
    {
        private readonly ServiciosDenuncias _serviciosDenuncias;

        public DenunciasAnonimasController(ServiciosDenuncias serviciosDenuncias)
        {
            _serviciosDenuncias = serviciosDenuncias;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new DenunciaAnonimaCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index (DenunciaAnonimaCreateVm model)
        {
            if(!ModelState.IsValid)
            {
                TempData["ErrorDenunciaAnonima"] = "Revisa la información de la denuncia.";
                return View(model);
            }

            var userId = int.Parse(User.FindFirst("UsuarioID")!.Value);

            await _serviciosDenuncias.CrearDenunciaAsync(model, userId);

            TempData["ExitoDenunciaAnonima"] = "Tu denuncia se envió de forma anónima. Gracias por tu confianza.";

            return RedirectToAction(nameof(Index));
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(DenunciaAnonimaCreateVm model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorDenunciaAnonima"] = "Revisa la información de la denuncia.";
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var userId = int.Parse(User.FindFirst("UsuarioID")!.Value);

            await _serviciosDenuncias.CrearDenunciaAsync(model, userId);

            TempData["ExitoDenunciaAnonima"] = "Tu denuncia se envió de forma anónima. Gracias por tu confianza.";
            return Redirect(Request.Headers["Referer"].ToString());
        }
    }
}
