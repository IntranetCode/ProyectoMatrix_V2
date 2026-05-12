
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ProyectoMatrix.Controllers
{
    [Route("[controller]")]
    public class OperacionesController : Controller
    {
        private readonly ILogger<HelpDeskController> _logger;

        public OperacionesController(ILogger<HelpDeskController> logger)
        {
            _logger = logger;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index()
        {

            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            _logger.LogInformation("Renderizando KPI");
            return View();
        }
    }
}
