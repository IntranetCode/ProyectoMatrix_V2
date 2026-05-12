using Microsoft.AspNetCore.Mvc;

namespace ProyectoMatrix.Controllers


{

    [Route("[controller]")]

    public class DirectorioController : Controller
    {
        private readonly ILogger<DirectorioController> _logger;

        public DirectorioController(ILogger<DirectorioController> logger)
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

            _logger.LogInformation("Renderizando directorio.");
            return View();
        }
    }
}
