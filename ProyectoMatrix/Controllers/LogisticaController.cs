using Microsoft.AspNetCore.Mvc;

namespace ProyectoMatrix.Controllers
{
    [Route("Logistica")]
    public class LogisticaController : Controller
    {

        private readonly ILogger<ComprasController> _logger;
        public LogisticaController(ILogger<ComprasController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Guias")]
        public IActionResult Guias()
        {
            EstablecerHeadersSinCache();
            _logger.LogInformation("Renderizando logistica.");
            return View();
        }

        [HttpGet("Transporte")]
        public IActionResult Transporte()
        {
            EstablecerHeadersSinCache();
            _logger.LogInformation("Renderizando logistica.");
            return View();
        }

        private void EstablecerHeadersSinCache()
        {
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }
    }
}
