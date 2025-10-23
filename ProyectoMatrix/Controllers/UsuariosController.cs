using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Areas.AdminUsuarios.DTOs;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using ProyectoMatrix.Seguridad;

namespace ProyectoMatrix.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly ApplicationDbContext _context;

        public UsuariosController(
            IUsuarioService usuarioService,
            ApplicationDbContext context)
        {
            _usuarioService = usuarioService;
            _context = context;
        }


        [AutorizarAccion("Ver Usuarios", "Ver")]
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
        [AutorizarAccion("Crear Usuario", "Crear")]
        public async Task<IActionResult> Crear()
        {
            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre");
            var viewModel = new UsuarioFormViewModel
            {
                EsModoCrear = true,
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync()
            };

            // Evita NullReference en la vista parcial
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();

            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Crear Usuario", "Crear")]
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
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>()
                };

                await _usuarioService.RegistrarAsync(nuevoUsuarioDto);
                TempData["SuccessMessage"] = "Usuario creado exitosamente.";
                return RedirectToAction(nameof(Index), new { activos = true });
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();
            return PartialView("_UsuarioForm", viewModel);
        }

        // GET: /Usuarios/Editar/5
        [AutorizarAccion("Editar Usuario", "Editar")]
        public async Task<IActionResult> Editar(int id)
        {
            var usuarioDto = await _usuarioService.ObtenerParaEditarAsync(id);
            if (usuarioDto == null) return NotFound();

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", usuarioDto.EmpresasIDs);

            var viewModel = new UsuarioFormViewModel
            {
                UsuarioID = usuarioDto.UsuarioID,
                Nombre = usuarioDto.Nombre,
                ApellidoPaterno = usuarioDto.ApellidoPaterno,
                ApellidoMaterno = usuarioDto.ApellidoMaterno,
                Correo = usuarioDto.Correo,
                Telefono = usuarioDto.Telefono,
                RolID = usuarioDto.RolID,
                Activo = usuarioDto.Activo,
                EmpresasIDs = usuarioDto.EmpresasIDs ?? new List<int>(),
                SubMenuIDs = usuarioDto.SubMenuIDs ?? new List<int>(),
                HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id),
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync()
            };

            // CARGA DE OVERRIDES usando el servicio (más limpio y consistente)
            List<OverrideItemDto> overridesItems;
            try
            {
                // Usa el método del servicio que ya tienes
                overridesItems = await _usuarioService.ListarOverridesAsync(id, null); // null = global
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "No se pudieron cargar los overrides: " + ex.Message;
                overridesItems = new List<OverrideItemDto>();
            }

            // Si no hay items, construir catálogo base desde menús
            if (overridesItems.Count == 0)
            {
                var menus = await _usuarioService.ObtenerMenusConSubMenusAsync();
                foreach (var menu in menus)
                {
                    foreach (var subMenu in menu.SubMenus)
                    {
                        // Calcular permiso efectivo del rol base
                        bool permisoEfectivo = await _usuarioService.VerificarPermisoAsync(id, subMenu.SubMenuID);

                        overridesItems.Add(new OverrideItemDto
                        {
                            MenuID = menu.MenuID,
                            MenuNombre = menu.Nombre,
                            SubMenuID = subMenu.SubMenuID,
                            Nombre = subMenu.Nombre,
                            Estado = -1, // heredar
                            PermisoEfectivo = permisoEfectivo
                        });
                    }
                }
            }

            // Agrupar por menú
            var grupos = overridesItems
                .GroupBy(x => new { x.MenuID, x.MenuNombre })
                .Select(g => new OverridesVm
                {
                    UsuarioID = id,
                    EmpresaID = null, // global
                    MenuID = g.Key.MenuID,
                    MenuNombre = g.Key.MenuNombre,
                    Items = g.OrderBy(it => it.Nombre).ToList()
                })
                .OrderBy(g => g.MenuNombre)
                .ToList();

            ViewBag.OverrideGrupos = grupos;
            ViewBag.Overrides = overridesItems;

            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Editar Usuario", "Editar")]
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
                    EmpresasIDs = viewModel.EmpresasIDs ?? new List<int>(),
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>()
                };

                await _usuarioService.ActualizarAsync(usuarioEditadoDto);
                TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
                return RedirectToAction(nameof(Index), routeValues);
            }

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();

            // Recargar overrides
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();

            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Eliminar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Eliminar Usuario", "Eliminar")]
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
        [AutorizarAccion("Editar Usuario", "Editar")]
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarOverrides(int UsuarioID, List<OverrideItemDto> Items)
        {

            Console.WriteLine($"🔵 UsuarioID recibido: {UsuarioID}");
            Console.WriteLine($"🔵 Items count: {Items?.Count ?? 0}");

            if (Items != null)
            {
                foreach (var item in Items)
                {
                    Console.WriteLine($"🔵 Item - SubMenuID: {item.SubMenuID}, Estado: {item.Estado}");
                }
            }

            // Log inicial
            System.Diagnostics.Debug.WriteLine($"🔵 GuardarOverrides llamado: UsuarioID={UsuarioID}, Items={Items?.Count ?? 0}");

            // Detectar AJAX de forma robusta
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                          || Request.Headers.Accept.ToString().Contains("application/json")
                          || Request.ContentType?.Contains("application/x-www-form-urlencoded") == true;

            System.Diagnostics.Debug.WriteLine($"🔵 Es AJAX: {isAjax}");
            System.Diagnostics.Debug.WriteLine($"🔵 Headers: X-Requested-With={Request.Headers["X-Requested-With"]}, Accept={Request.Headers.Accept}");

            try
            {
                Items ??= new List<OverrideItemDto>();

                System.Diagnostics.Debug.WriteLine($"🔵 Items recibidos: {Items.Count}");
                foreach (var item in Items.Take(3)) // Log solo los primeros 3
                {
                    System.Diagnostics.Debug.WriteLine($"  - SubMenuID: {item.SubMenuID}, Estado: {item.Estado}");
                }

                // Guardar en BD
                await _usuarioService.GuardarOverridesAsync(UsuarioID, null, Items);
                System.Diagnostics.Debug.WriteLine("✅ Guardado en BD exitoso");

                // CRÍTICO: Invalidar caché del menú
                HttpContext.Session.Remove("MenuItems");
                HttpContext.Session.Remove("MenuUsuario");
                System.Diagnostics.Debug.WriteLine("🗑️ Caché de menú limpiado");

                // Si el usuario editado es el actual, reconstruir menú AHORA
                var usuarioActualId = HttpContext.Session.GetInt32("UsuarioID");
                string mensajeExtra = "";

                if (usuarioActualId.HasValue && usuarioActualId.Value == UsuarioID)
                {
                    System.Diagnostics.Debug.WriteLine("🔄 Usuario editado es el actual, reconstruyendo menú...");

                    var empresaId = HttpContext.Session.GetInt32("EmpresaID");
                    var menuActualizado = await ObtenerMenuActualizadoAsync(UsuarioID, empresaId);
                    HttpContext.Session.SetString("MenuUsuario", System.Text.Json.JsonSerializer.Serialize(menuActualizado));

                    TempData["RefreshMenu"] = "true";
                    mensajeExtra = " ⚠️ Recarga la página para ver los cambios en el menú.";

                    System.Diagnostics.Debug.WriteLine($"✅ Menú reconstruido: {menuActualizado.Count} items");
                }

                // Respuesta AJAX vs redirect
                if (isAjax)
                {
                    var response = new
                    {
                        ok = true,
                        message = "✓ Permisos actualizados correctamente." + mensajeExtra
                    };

                    System.Diagnostics.Debug.WriteLine($"📤 Devolviendo JSON: {System.Text.Json.JsonSerializer.Serialize(response)}");

                    return Json(response);
                }

                // Fallback para POST tradicional
                TempData["SuccessMessage"] = "Permisos actualizados correctamente." + mensajeExtra;
                return RedirectToAction("Editar", new { id = UsuarioID, tab = "permisos" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack: {ex.StackTrace}");

                if (isAjax)
                {
                    var errorResponse = new { ok = false, message = $"Error: {ex.Message}" };
                    System.Diagnostics.Debug.WriteLine($"📤 Devolviendo error JSON: {System.Text.Json.JsonSerializer.Serialize(errorResponse)}");

                    Response.StatusCode = 500;
                    return Json(errorResponse);
                }

                TempData["ErrorMessage"] = $"Error al guardar permisos: {ex.Message}";
                return RedirectToAction("Editar", new { id = UsuarioID, tab = "permisos" });
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetOverride(ProyectoMatrix.Areas.AdminUsuarios.DTOs.SetOverrideRequest req)
        {
            try
            {
                // Normaliza estado nulo a heredar
                var estado = req.Estado;

                var item = new ProyectoMatrix.Areas.AdminUsuarios.DTOs.OverrideItemDto
                {
                    SubMenuID = req.SubMenuID,
                    Estado = estado
                };

                // Guarda 1 solo override
                await _usuarioService.GuardarOverridesAsync(req.UsuarioID, req.EmpresaID, new[] { item });

                // 🔥 Invalidar caché de menú en sesión
                HttpContext.Session.Remove("MenuItems");
                HttpContext.Session.Remove("MenuUsuario");

                // Recalcular permiso efectivo de esa fila (usa tu servicio)
                var efectivo = await _usuarioService.VerificarPermisoAsync(req.UsuarioID, req.SubMenuID);

                // Responder JSON para que la vista marque selección y ✔️/✖️
                return Json(new
                {
                    ok = true,
                    estado = estado,
                    efectivo = efectivo,
                    refreshMenu = true,
                    message = "Override guardado correctamente."
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }


        // ✅ AGREGAR ESTE MÉTODO AUXILIAR
        private async Task<List<MenuModel>> ObtenerMenuActualizadoAsync(int usuarioId, int? empresaId)
        {
            var lista = new List<MenuModel>();
            string cnn = _context.Database.GetConnectionString();

            using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
WITH Perms AS (
  SELECT SubMenuID
  FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID)
  WHERE TienePermiso = 1
)
SELECT DISTINCT m.MenuID, m.Nombre AS NombreMenu, sm.UrlEnlace
FROM Menus m
JOIN SubMenus sm ON sm.MenuID = m.MenuID
JOIN Perms p ON p.SubMenuID = sm.SubMenuID
WHERE sm.Activo = 1
ORDER BY m.MenuID;";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            var pEmp = cmd.Parameters.Add("@EmpresaID", System.Data.SqlDbType.Int);
            pEmp.Value = (object?)empresaId ?? DBNull.Value;

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new MenuModel
                {
                    MenuID = rd.GetInt32(0),
                    Nombre = rd.GetString(1),
                    Url = rd.IsDBNull(2) ? "" : rd.GetString(2)
                });
            }

            return lista;
        }
    }



    }