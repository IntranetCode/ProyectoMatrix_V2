using ProyectoMatrix.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

public class HomeController : Controller
{
    private static readonly List<EmpresaModel> empresas = new()
    {
        new EmpresaModel { Id = "1", Nombre = "Nutriservicios", Logo = "NS.jpg", ColorPrimario = "#004AAD" },
        new EmpresaModel { Id = "2", Nombre = "NS Equipo", Logo = "NE.jpg", ColorPrimario = "#7B3F00" },
        new EmpresaModel { Id = "3", Nombre = "BSP", Logo = "BSP.png", ColorPrimario = "#2E8B57" },
        new EmpresaModel { Id = "4", Nombre = "Hypor", Logo = "Hypor.jpg", ColorPrimario = "#880088" }

    };

    public IActionResult Index()
    {
        return View(empresas);
    }

    [HttpPost]
    public IActionResult Seleccionar(string id)
    {
        var empresa = empresas.FirstOrDefault(e => e.Id == id);
        if (empresa != null)
        {
            HttpContext.Session.SetString("EmpresaId", empresa.Id);
            HttpContext.Session.SetString("EmpresaNombre", empresa.Nombre);
            HttpContext.Session.SetString("EmpresaLogo", empresa.Logo);
            HttpContext.Session.SetString("ColorPrimario", empresa.ColorPrimario);
            return RedirectToAction("Login", "Login");
        }
        return RedirectToAction("Index");
    }
}
