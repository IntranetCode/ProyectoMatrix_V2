using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Areas.AdminUsuarios.DTOs;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using System.Threading.Tasks;

namespace ProyectoMatrix.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly ApplicationDbContext _context;

        public UsuariosController(IUsuarioService usuarioService, ApplicationDbContext context)
        {
            _usuarioService = usuarioService;
            _context = context;
        }

        public async Task<IActionResult> Index(bool? activos, string? filtroCampo, string? busqueda)
        {
            bool mostrarActivos = activos ?? true;

            if (mostrarActivos)
            {
                ViewData["Title"] = "Usuarios activos";
            }
            else
            {
                ViewData["Title"] = "Usuarios inactivos";
            }

            ViewData["BusquedaActual"] = busqueda;
            ViewData["FiltroCampoActual"] = filtroCampo ?? "Todos";
            var usuarios = await _usuarioService.ObtenerTodosAsync(mostrarActivos, filtroCampo, busqueda);

            return View(usuarios);
        }

        // GET: /Usuarios/Crear
        public async Task<IActionResult> Crear() // MODIFICADO a async
        {
            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre");
            var viewModel = new UsuarioFormViewModel
            {
                EsModoCrear = true,
                // AÑADIDO: Carga los menús disponibles para el formulario
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync()
            };
            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(UsuarioFormViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var nuevoUsuarioDto = new UsuarioRegistroDTO
                {
                    Nombre = viewModel.Nombre,
                    ApellidoPaterno = viewModel.ApellidoPaterno,
                    ApellidoMaterno = viewModel.ApellidoMaterno,
                    Correo = viewModel.Correo,
                    Telefono = viewModel.Telefono,
                    Username = viewModel.Username,
                    Password = viewModel.Password,
                    RolID = viewModel.RolID,
                    EmpresasIDs = viewModel.EmpresasIDs,
                    SubMenuIDs = viewModel.SubMenuIDs // AÑADIDO
                };
                await _usuarioService.RegistrarAsync(nuevoUsuarioDto);
                TempData["SuccessMessage"] = "Usuario creado exitosamente.";
                return RedirectToAction(nameof(Index), new { activos = true });
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            // AÑADIDO: Vuelve a cargar los menús si la validación falla
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();
            return PartialView("_UsuarioForm", viewModel);
        }

        // GET: /Usuarios/Editar/5
        public async Task<IActionResult> Editar(int id)
        {
            var usuarioDto = await _usuarioService.ObtenerParaEditarAsync(id);
            if (usuarioDto == null)
            {
                return NotFound();
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", usuarioDto.EmpresasIDs);

            var viewModel = new UsuarioFormViewModel
            {
                EsModoCrear = false,
                UsuarioID = usuarioDto.UsuarioID,
                Nombre = usuarioDto.Nombre,
                ApellidoPaterno = usuarioDto.ApellidoPaterno,
                ApellidoMaterno = usuarioDto.ApellidoMaterno,
                Correo = usuarioDto.Correo,
                Telefono = usuarioDto.Telefono,
                RolID = usuarioDto.RolID,
                Activo = usuarioDto.Activo,
                EmpresasIDs = usuarioDto.EmpresasIDs,
                SubMenuIDs = usuarioDto.SubMenuIDs, // AÑADIDO
                HistorialDeCambios = usuarioDto.HistorialDeCambios,
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync() // AÑADIDO
            };
            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, UsuarioFormViewModel viewModel)
        {
            var routeValues = new
            {
                activos = HttpContext.Request.Query["activos"],
                filtroCampo = HttpContext.Request.Query["filtroCampo"],
                busqueda = HttpContext.Request.Query["busqueda"]
            };

            if (id != viewModel.UsuarioID)
            {
                return BadRequest();
            }

            ModelState.Remove(nameof(viewModel.Username));
            ModelState.Remove(nameof(viewModel.Password));

            if (ModelState.IsValid)
            {
                var usuarioEditadoDto = new UsuarioEdicionDTO
                {
                    UsuarioID = viewModel.UsuarioID.Value,
                    Nombre = viewModel.Nombre,
                    ApellidoPaterno = viewModel.ApellidoPaterno,
                    ApellidoMaterno = viewModel.ApellidoMaterno,
                    Correo = viewModel.Correo,
                    Telefono = viewModel.Telefono,
                    RolID = viewModel.RolID,
                    Activo = viewModel.Activo,
                    EmpresasIDs = viewModel.EmpresasIDs,
                    SubMenuIDs = viewModel.SubMenuIDs // AÑADIDO
                };

                await _usuarioService.ActualizarAsync(usuarioEditadoDto);
                TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index), routeValues);
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id);
            // AÑADIDO: Vuelve a cargar los menús si la validación falla
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();
            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Eliminar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            var routeValues = new
            {
                activos = HttpContext.Request.Query["activos"],
                filtroCampo = HttpContext.Request.Query["filtroCampo"],
                busqueda = HttpContext.Request.Query["busqueda"]
            };

            await _usuarioService.DarDeBajaAsync(id);
            TempData["SuccessMessage"] = "Usuario dado de baja correctamente.";
            return RedirectToAction(nameof(Index), routeValues);
        }

        // MÉTODOS PARA VALIDACIÓN REMOTA
        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> VerificarUsername(string username, int? usuarioID)
        {
            var query = _context.Usuarios.AsQueryable();
            if (usuarioID.HasValue)
            {
                query = query.Where(u => u.UsuarioID != usuarioID.Value);
            }
            var existe = await query.AnyAsync(u => u.Username == username);
            return existe ? Json($"El username '{username}' ya está en uso.") : Json(true);
        }

        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> VerificarCorreo(string correo, int? usuarioID)
        {
            var query = _context.Personas.AsQueryable();
            if (usuarioID.HasValue)
            {
                var personaId = await _context.Usuarios
                    .Where(u => u.UsuarioID == usuarioID.Value)
                    .Select(u => u.PersonaID)
                    .FirstOrDefaultAsync();

                if (personaId > 0)
                {
                    query = query.Where(p => p.PersonaID != personaId);
                }
            }
            var existe = await query.AnyAsync(p => p.Correo == correo);
            return existe ? Json($"El correo '{correo}' ya está en uso.") : Json(true);
        }
    }
}