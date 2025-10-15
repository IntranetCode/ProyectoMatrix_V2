using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Areas.AdminUsuarios.DTOs;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace ProyectoMatrix.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly ApplicationDbContext _context;
        private readonly string _connectionString;

        public UsuariosController(
            IUsuarioService usuarioService,
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _usuarioService = usuarioService;
            _context = context;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> Index(bool? activos, string? filtroCampo, string? busqueda)
        {
            bool mostrarActivos = activos ?? true;

            ViewData["Title"] = mostrarActivos ? "Usuarios activos" : "Usuarios inactivos";
            ViewData["BusquedaActual"] = busqueda;
            ViewData["FiltroCampoActual"] = filtroCampo ?? "Todos";

            var usuarios = await _usuarioService.ObtenerTodosAsync(mostrarActivos, filtroCampo, busqueda);
            return View(usuarios);
        }

        // GET: /Usuarios/Crear
        public async Task<IActionResult> Crear()
        {
            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre");
            var viewModel = new UsuarioFormViewModel
            {
                EsModoCrear = true,
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
                    SubMenuIDs = viewModel.SubMenuIDs
                };

                await _usuarioService.RegistrarAsync(nuevoUsuarioDto);
                TempData["SuccessMessage"] = "Usuario creado exitosamente.";
                return RedirectToAction(nameof(Index), new { activos = true });
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();
            return PartialView("_UsuarioForm", viewModel);
        }

        // GET: /Usuarios/Editar/5
        public async Task<IActionResult> Editar(int id)
        {
            var usuarioDto = await _usuarioService.ObtenerParaEditarAsync(id);
            if (usuarioDto == null) return NotFound();

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
                SubMenuIDs = usuarioDto.SubMenuIDs,
                HistorialDeCambios = usuarioDto.HistorialDeCambios,
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync()
            };

            // ⚠️ Cargar overrides desde SP (GLOBAL), para que la vista siempre tenga el mismo formato
            var overridesItems = new List<OverrideItemDto>();
            using (var cn = new SqlConnection(_connectionString))
            {
                await cn.OpenAsync();
                using var cmd = new SqlCommand("dbo.sp_Overrides_ListarUsuario", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@UsuarioID", id);
                cmd.Parameters.AddWithValue("@EmpresaID", DBNull.Value); // GLOBAL
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    overridesItems.Add(new OverrideItemDto
                    {
                        SubMenuID = rd.GetInt32(0),
                        Nombre = rd.GetString(1),
                        Estado = rd.GetInt32(2),
                        PermisoEfectivo = rd.GetBoolean(3)
                    });
                }
            }
            ViewBag.Overrides = overridesItems;

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

            if (id != viewModel.UsuarioID) return BadRequest();

            // No se editan en esta pantalla
            ModelState.Remove(nameof(viewModel.Username));
            ModelState.Remove(nameof(viewModel.Password));

            if (ModelState.IsValid)
            {
                var usuarioEditadoDto = new UsuarioEdicionDTO
                {
                    UsuarioID = viewModel.UsuarioID!.Value,
                    Nombre = viewModel.Nombre,
                    ApellidoPaterno = viewModel.ApellidoPaterno,
                    ApellidoMaterno = viewModel.ApellidoMaterno,
                    Correo = viewModel.Correo,
                    Telefono = viewModel.Telefono,
                    RolID = viewModel.RolID,
                    Activo = viewModel.Activo,
                    EmpresasIDs = viewModel.EmpresasIDs,
                    SubMenuIDs = viewModel.SubMenuIDs
                };

                await _usuarioService.ActualizarAsync(usuarioEditadoDto);
                TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index), routeValues);
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id);
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

        // VALIDACIÓN REMOTA
        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> VerificarUsername(string username, int? usuarioID)
        {
            var query = _context.Usuarios.AsQueryable();
            if (usuarioID.HasValue) query = query.Where(u => u.UsuarioID != usuarioID.Value);
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

                if (personaId > 0) query = query.Where(p => p.PersonaID != personaId);
            }

            var existe = await query.AnyAsync(p => p.Correo == correo);
            return existe ? Json($"El correo '{correo}' ya está en uso.") : Json(true);
        }

        // GET: /Usuarios/Overrides (pantalla aparte si la usas)
        [HttpGet]
        public async Task<IActionResult> Overrides(int usuarioId, int rolId, bool? mostrarTodo = null)
        {
            ViewBag.RolId = rolId;

            var vm = new OverridesVm
            {
                UsuarioID = usuarioId,
                EmpresaID = null, // GLOBAL
                Items = new List<OverrideItemDto>()
            };

            using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();

            using var cmd = new SqlCommand("dbo.sp_Overrides_ListarUsuario", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@EmpresaID", DBNull.Value); // GLOBAL

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vm.Items.Add(new OverrideItemDto
                {
                    SubMenuID = rd.GetInt32(0),
                    Nombre = rd.GetString(1),
                    Estado = rd.GetInt32(2),
                    PermisoEfectivo = rd.GetBoolean(3)
                });
            }

            return View(vm);
        }

        // POST: /Usuarios/GuardarOverrides  (único POST de overrides)

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarOverrides(int UsuarioID, List<OverrideItemDto> Items)
        {
            Items ??= new();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var it in Items)
            {
                // Estado:  1=Permitir, 0=Denegar, -1=Heredar(Limpiar)
                switch (it.Estado)
                {
                    case 1:
                    case 0:
                        await using (var cmd = new SqlCommand("sp_Overrides_Upsert", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@UsuarioID", UsuarioID);
                            cmd.Parameters.AddWithValue("@SubMenuID", it.SubMenuID);
                            cmd.Parameters.Add("@Estado", SqlDbType.Bit).Value = it.Estado == 1 ? 1 : 0;
                            await cmd.ExecuteNonQueryAsync();
                        }
                        break;

                    default: // -1 o null => limpiar
                        await using (var cmd = new SqlCommand("sp_Overrides_Limpiar", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@UsuarioID", UsuarioID);
                            cmd.Parameters.AddWithValue("@SubMenuID", it.SubMenuID);
                            await cmd.ExecuteNonQueryAsync();
                        }
                        break;
                }
            }

            TempData["Ok"] = "Overrides guardados.";
            return RedirectToAction("Editar", new { id = UsuarioID, tab = "permisos" });
        }


        public class OverrideItemDto
        {
            public int SubMenuID { get; set; }
            public string? Nombre { get; set; }
            public int? Estado { get; set; }          // 1, 0, -1
            public bool PermisoEfectivo { get; set; } // solo display
        }

    }
}
