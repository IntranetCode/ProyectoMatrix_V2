using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Models;

namespace ProyectoMatrix.Controllers
{
    public class EmpresasControler : Controller
    {
        private readonly ApplicationDbContext _context;
        public EmpresasControler(ApplicationDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            var empresas = _context.Empresas.ToList();
            return View();
        }
    }
}
