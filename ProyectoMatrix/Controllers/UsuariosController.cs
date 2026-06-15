using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Areas.AdminUsuarios.DTOs;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using ProyectoMatrix.Seguridad;
using ProyectoMatrix.Servicios;
using System.Net;

namespace ProyectoMatrix.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly ApplicationDbContext _context;
        private readonly ServicioNotificaciones _servicioNotificaciones;

        public UsuariosController(
            IUsuarioService usuarioService,
            ApplicationDbContext context,
            ServicioNotificaciones servicioNotificaciones)
        {
            _usuarioService = usuarioService;
            _context = context;
            _servicioNotificaciones = servicioNotificaciones;
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
            // 1. Cargamos las empresas (manteniendo tu lógica actual)
            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre");

            // 2. Cargamos los Departamentos usando SQL Puro
            var listaDeptos = new List<SelectListItem>();
            string cnn = _context.Database.GetConnectionString();

            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();
                // Consulta SQL para traer solo departamentos activos
                const string sql = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";

                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            listaDeptos.Add(new SelectListItem
                            {
                                Value = rd["DepartamentoID"].ToString(),
                                Text = rd["NombreDepartamento"].ToString()
                            });
                        }
                    }
                }
            }
            // Pasamos la lista a la vista mediante ViewBag
            ViewBag.Departamentos = listaDeptos;

            // 3. Inicializamos el ViewModel
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
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>(),

                    NumeroEmpleado = viewModel.NumeroEmpleado,
                    ClaveEmpleadoNomina = viewModel.ClaveEmpleadoNomina,
                    FechaIngreso = viewModel.FechaIngreso,
                    Puesto = viewModel.Puesto,
                    FechaNacimiento = viewModel.FechaNacimiento,
                    JefeInmediatoPersonaID = viewModel.JefeInmediatoPersonaID,
                    // AGREGAR ESTA LÍNEA:
                    DepartamentoID = viewModel.DepartamentoID
                };
                ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

                await _usuarioService.RegistrarAsync(nuevoUsuarioDto);

                var usuarioCreado = await _context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.Username == viewModel.Username)
                    .OrderByDescending(u => u.UsuarioID)
                    .Select(u => new { u.UsuarioID })
                    .FirstOrDefaultAsync();

                var credencialesEnviadas = await EnviarCredenciales(
                    usuarioCreado?.UsuarioID,
                    viewModel.Nombre,
                    viewModel.Username,
                    viewModel.Password);

                TempData["SuccessMessage"] = credencialesEnviadas
                    ? "Usuario creado exitosamente. Las credenciales fueron enviadas al correo registrado."
                    : "Usuario creado exitosamente. No se enviaron credenciales porque el usuario no tiene correo válido o el envío fue bloqueado por la configuración de notificaciones.";

                if (EsPeticionAjax())
                {
                    return Json(new
                    {
                        ok = true,
                        message = TempData["SuccessMessage"]?.ToString(),
                        redirectUrl = Url.Action(nameof(Index), new { activos = true })
                    });
                }

                return RedirectToAction(nameof(Index), new { activos = true });
            }

            // RECARGAR LISTA DE DEPARTAMENTOS SI HAY ERRORES (SQL Puro)
            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

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

            var departamentoIdActual = usuarioDto.DepartamentoID 
                           ?? await ObtenerDepartamentoUsuarioAsync(id);

            string nombreJefe = "";
            string nombreDepto = "";
            var listaDeptos = new List<SelectListItem>(); // Nueva lista para el select
            string cnn = _context.Database.GetConnectionString();

            // --- BLOQUE SQL PURO PARA CARGAR DATOS Y LISTA ---
            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();

                // 1. Obtener nombre del Jefe
                if (usuarioDto.JefeInmediatoPersonaID.HasValue)
                {
                    var sqlJefe = @"SELECT (Nombre + ' ' + ApellidoPaterno + ' ' + ISNULL(ApellidoMaterno, '')) 
                            FROM Persona WHERE PersonaID = @id";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sqlJefe, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", usuarioDto.JefeInmediatoPersonaID);
                        nombreJefe = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
                    }
                }

                // 2. CARGAR LISTA COMPLETA DE DEPARTAMENTOS
                const string sqlLista = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";
                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sqlLista, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            listaDeptos.Add(new SelectListItem
                            {
                                Value = rd["DepartamentoID"].ToString(),
                                Text = rd["NombreDepartamento"].ToString(),
                                // Pre-selecciona el depto actual del usuario
                                Selected = departamentoIdActual.HasValue && 
                                           (int)rd["DepartamentoID"] == departamentoIdActual.Value
                            });
                        }
                    }
                }

                // 3. Obtener nombre del Departamento actual (para el ViewModel)
                if (departamentoIdActual.HasValue)
                {
                    var sqlDepto = "SELECT NombreDepartamento FROM Departamentos WHERE DepartamentoID = @id";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sqlDepto, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", departamentoIdActual.Value);
                        nombreDepto = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
                    }
                }
            }

            ViewBag.Departamentos = listaDeptos;
            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", usuarioDto.EmpresasIDs);

            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(departamentoIdActual);

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
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync(),
                NumeroEmpleado = usuarioDto.NumeroEmpleado,
                ClaveEmpleadoNomina = usuarioDto.ClaveEmpleadoNomina,
                FechaIngreso = usuarioDto.FechaIngreso,
                Puesto = usuarioDto.Puesto,
                FechaNacimiento = usuarioDto.FechaNacimiento,
                JefeInmediatoPersonaID = usuarioDto.JefeInmediatoPersonaID,
                JefeInmediatoNombreCompleto = nombreJefe,
                DepartamentoID = departamentoIdActual,
                NombreDepartamento = nombreDepto
            };
            // --- LÓGICA DE OVERRIDES ---
            List<OverrideItemDto> overridesItems;
            try
            {
                overridesItems = await _usuarioService.ListarOverridesAsync(id, null);
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "No se pudieron cargar los overrides: " + ex.Message;
                overridesItems = new List<OverrideItemDto>();
            }

            if (overridesItems.Count == 0)
            {
                var menus = await _usuarioService.ObtenerMenusConSubMenusAsync();
                foreach (var menu in menus)
                {
                    foreach (var subMenu in menu.SubMenus)
                    {
                        bool permisoEfectivo = await _usuarioService.VerificarPermisoAsync(id, subMenu.SubMenuID);
                        overridesItems.Add(new OverrideItemDto
                        {
                            MenuID = menu.MenuID,
                            MenuNombre = menu.Nombre,
                            SubMenuID = subMenu.SubMenuID,
                            Nombre = subMenu.Nombre,
                            Estado = -1,
                            PermisoEfectivo = permisoEfectivo
                        });
                    }
                }
            }

            var grupos = overridesItems
                .GroupBy(x => new { x.MenuID, x.MenuNombre })
                .Select(g => new OverridesVm
                {
                    UsuarioID = id,
                    EmpresaID = null,
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
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>(),

                    NumeroEmpleado = viewModel.NumeroEmpleado,
                    ClaveEmpleadoNomina = viewModel.ClaveEmpleadoNomina,
                    FechaIngreso = viewModel.FechaIngreso,
                    Puesto = viewModel.Puesto,
                    FechaNacimiento = viewModel.FechaNacimiento,
                    JefeInmediatoPersonaID = viewModel.JefeInmediatoPersonaID,
                    // AGREGADO PARA GUARDAR:
                    DepartamentoID = viewModel.DepartamentoID
                };

                await _usuarioService.ActualizarAsync(usuarioEditadoDto);
                
                var empresaIdParaDepartamento = viewModel.EmpresasIDs?.FirstOrDefault();

                await GuardarDepartamentoUsuarioAsync(
                    viewModel.UsuarioID!.Value,
                    viewModel.DepartamentoID,
                    empresaIdParaDepartamento.HasValue && empresaIdParaDepartamento.Value > 0
                        ? empresaIdParaDepartamento.Value
                        : null
                );

                if (!viewModel.Activo)
                {
                    await OcultarContenidoAsignadoAsync(viewModel.UsuarioID!.Value);
                }

                TempData["SuccessMessage"] = viewModel.Activo
                    ? "Usuario actualizado correctamente."
                    : "Usuario actualizado correctamente. Al quedar desactivado, se ocultó su contenido asignado.";

                if (EsPeticionAjax())
                {
                    return Json(new
                    {
                        ok = true,
                        message = TempData["SuccessMessage"]?.ToString(),
                        redirectUrl = Url.Action(nameof(Index), routeValues)
                    });
                }

                return RedirectToAction(nameof(Index), routeValues);
            }

            // AGREGADO PARA RECARGAR LA LISTA SI HAY ERROR DE VALIDACIÓN:
            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

            ViewBag.Empresas = new SelectList(_context.Empresas, "EmpresaID", "Nombre", viewModel.EmpresasIDs);
            viewModel.HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();

            // Recargar overrides
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();

            if (EsPeticionAjax())
            {
                Response.StatusCode = 400;
            }

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
            await OcultarContenidoAsignadoAsync(id);
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
        public async Task<IActionResult> VerificarCorreo(string? correo, int? usuarioID)
        {
            if (string.IsNullOrWhiteSpace(correo)) return Json(true);

            correo = correo.Trim();

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
                          || Request.Headers.Accept.ToString().Contains("application/json");

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

        //Metodoo que devuelve un jason para el buscador de jefes

        [HttpGet]
        public async Task<IActionResult> BuscarPersonasJefes(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1)
                return Json(new { results = new List<object>() });

            term = term.Trim().ToLower();

            var personas = await _context.Personas
                .Where(p =>
                    p.Nombre.ToLower().Contains(term) ||
                    p.ApellidoPaterno.ToLower().Contains(term) ||
                    (p.ApellidoMaterno != null && p.ApellidoMaterno.ToLower().Contains(term))
                )
                .Select(p => new
                {
                    id = p.PersonaID,
                    text = (p.Nombre + " " + p.ApellidoPaterno +
                           (p.ApellidoMaterno != null ? " " + p.ApellidoMaterno : "")).Trim()
                })
                .Take(15)
                .ToListAsync();

            return Json(new { results = personas });
        }

        [HttpGet]
        public async Task<IActionResult> BuscarDepartamentos(string term, int? empresaId)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(new { results = new List<object>() });

            var resultados = new List<object>();
            string cnn = _context.Database.GetConnectionString();

            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();
                // Ajustado a 'EmpleadoDepartamentos' o la tabla pivote correcta según tu diagrama
                var sql = @"
            SELECT TOP 15 d.DepartamentoID, d.NombreDepartamento
            FROM Departamentos d
            INNER JOIN EmpleadoDepartamentos ed ON ed.DepartamentoID = d.DepartamentoID
            WHERE d.NombreDepartamento LIKE @t
              AND d.Activo = 1
              AND (@empresaId IS NULL OR ed.EmpresaID = @empresaId)";

                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@t", $"%{term}%");
                    cmd.Parameters.AddWithValue("@empresaId", (object?)empresaId ?? DBNull.Value);

                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            resultados.Add(new { id = rd["DepartamentoID"], text = rd["NombreDepartamento"].ToString() });
                        }
                    }
                }
            }
            return Json(new { results = resultados });
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


        private async Task<List<SelectListItem>> ObtenerListaDepartamentosSQL(int? seleccionadoId = null)
        {
            var lista = new List<SelectListItem>();
            string cnn = _context.Database.GetConnectionString();

            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();
                const string sql = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";
                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            int idDepto = (int)rd["DepartamentoID"];
                            lista.Add(new SelectListItem
                            {
                                Value = idDepto.ToString(),
                                Text = rd["NombreDepartamento"].ToString(),
                                Selected = seleccionadoId.HasValue && idDepto == seleccionadoId.Value
                            });
                        }
                    }
                }
            }
            return lista;
        }

        private bool EsPeticionAjax()
        {
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                   || Request.Headers.Accept.ToString().Contains("application/json");
        }

        private async Task<bool> EnviarCredenciales(int? usuarioId, string? nombre, string? username, string? password)
        {
            if (!usuarioId.HasValue
                || string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            var nombreSeguro = WebUtility.HtmlEncode(nombre ?? "");
            var usernameSeguro = WebUtility.HtmlEncode(username);
            var passwordSeguro = WebUtility.HtmlEncode(password);

            const string asunto = "Credenciales de acceso a la intranet";
            var html = $@"
<h2>Bienvenido(a) a la intranet</h2>
<p>Hola {nombreSeguro},</p>
<p>Tu usuario fue dado de alta en la intranet.</p>
<table style=""border-collapse:collapse; margin:12px 0;"">
    <tr>
        <td style=""padding:6px 10px; font-weight:bold;"">Usuario:</td>
        <td style=""padding:6px 10px;"">{usernameSeguro}</td>
    </tr>
    <tr>
        <td style=""padding:6px 10px; font-weight:bold;"">Contraseña:</td>
        <td style=""padding:6px 10px;"">{passwordSeguro}</td>
    </tr>
</table>
<p>Por seguridad, cambia tu contraseña después del primer inicio de sesión.</p>
<p>Saludos.</p>";

            try
            {
                var resultado = await _servicioNotificaciones.EnviarCursosAUsuariosAsync(
                    new[] { usuarioId.Value },
                    asunto,
                    html,
                    batchSize: 1);

                return resultado.Enviados > 0 && resultado.Errores == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"No se pudieron enviar credenciales al usuario: {ex.Message}");
                return false;
            }
        }

        private async Task<int?> ObtenerDepartamentoUsuarioAsync(int usuarioId)
        {
            string cnn = _context.Database.GetConnectionString();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                SELECT TOP 1 ed.DepartamentoID
                FROM EmpleadoDepartamentos ed
                WHERE ed.UsuarioID = @UsuarioID
                AND ed.Activo = 1
                ORDER BY ed.FechaAsignacion DESC, ed.EmpleadoDepartamentoID DESC";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return null;

            return Convert.ToInt32(result);
        }

        private async Task OcultarContenidoAsignadoAsync(int usuarioId)
        {
            var menus = await _usuarioService.ObtenerMenusConSubMenusAsync();
            var denegaciones = menus
                .SelectMany(menu => menu.SubMenus)
                .Select(subMenu => new OverrideItemDto
                {
                    SubMenuID = subMenu.SubMenuID,
                    Estado = 0
                })
                .ToList();

            if (denegaciones.Any())
            {
                await _usuarioService.GuardarOverridesAsync(usuarioId, null, denegaciones);
            }

            InvalidarCacheMenuSiAplica(usuarioId);
        }

        private void InvalidarCacheMenuSiAplica(int usuarioId)
        {
            HttpContext.Session.Remove("MenuItems");
            HttpContext.Session.Remove("MenuUsuario");

            var usuarioActualId = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioActualId.HasValue && usuarioActualId.Value == usuarioId)
            {
                TempData["RefreshMenu"] = "true";
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

        private async Task GuardarDepartamentoUsuarioAsync(int usuarioId, int? departamentoId, int? empresaId)
        {
            if (!departamentoId.HasValue)
                return;

            string cnn = _context.Database.GetConnectionString();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            await using var tran = await conn.BeginTransactionAsync();

            try
            {
                int? empleadoDepartamentoId = null;

                const string sqlRegistroActivo = @"
                    SELECT TOP 1 EmpleadoDepartamentoID
                    FROM EmpleadoDepartamentos
                    WHERE UsuarioID = @UsuarioID
                    AND Activo = 1
                    ORDER BY FechaAsignacion DESC, EmpleadoDepartamentoID DESC";

                await using (var cmdActivo = new Microsoft.Data.SqlClient.SqlCommand(
                    sqlRegistroActivo,
                    conn,
                    (Microsoft.Data.SqlClient.SqlTransaction)tran))
                {
                    cmdActivo.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    var result = await cmdActivo.ExecuteScalarAsync();

                    if (result != null && result != DBNull.Value)
                        empleadoDepartamentoId = Convert.ToInt32(result);
                }

                if (empleadoDepartamentoId.HasValue)
                {
                    const string sqlUpdate = @"
                        UPDATE EmpleadoDepartamentos
                        SET DepartamentoID = @DepartamentoID,
                            EmpresaID = COALESCE(@EmpresaID, EmpresaID),
                            FechaAsignacion = GETDATE()
                        WHERE EmpleadoDepartamentoID = @EmpleadoDepartamentoID";

                    await using var cmdUpdate = new Microsoft.Data.SqlClient.SqlCommand(
                        sqlUpdate,
                        conn,
                        (Microsoft.Data.SqlClient.SqlTransaction)tran);

                    cmdUpdate.Parameters.AddWithValue("@EmpleadoDepartamentoID", empleadoDepartamentoId.Value);
                    cmdUpdate.Parameters.AddWithValue("@DepartamentoID", departamentoId.Value);
                    cmdUpdate.Parameters.AddWithValue("@EmpresaID", empresaId.HasValue && empresaId.Value > 0
                        ? empresaId.Value
                        : DBNull.Value);

                    await cmdUpdate.ExecuteNonQueryAsync();
                }
                else
                {
                    if (!empresaId.HasValue || empresaId.Value <= 0)
                        throw new InvalidOperationException("No se pudo determinar la empresa para guardar el departamento.");

                    const string sqlInsert = @"
                        INSERT INTO EmpleadoDepartamentos
                            (UsuarioID, EmpresaID, DepartamentoID, Activo, FechaAsignacion)
                        VALUES
                            (@UsuarioID, @EmpresaID, @DepartamentoID, 1, GETDATE())";

                    await using var cmdInsert = new Microsoft.Data.SqlClient.SqlCommand(
                        sqlInsert,
                        conn,
                        (Microsoft.Data.SqlClient.SqlTransaction)tran);

                    cmdInsert.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmdInsert.Parameters.AddWithValue("@EmpresaID", empresaId.Value);
                    cmdInsert.Parameters.AddWithValue("@DepartamentoID", departamentoId.Value);

                    await cmdInsert.ExecuteNonQueryAsync();
                }

                await tran.CommitAsync();
            }
            catch
            {
                await tran.RollbackAsync();
                throw;
            }
        } 

        private string ResolverTipoAgregacionDefault(string? tipoCaptura, bool esLinea)
        {
            if (esLinea)
                return "ValorFijo";

            if (!string.IsNullOrWhiteSpace(tipoCaptura) &&
                tipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                return "ValorFijo";

            return "Promedio";
        }

        private bool VariablePerteneceAMetrica(CatMetricas metrica, int? variableId)
        {
            if (!variableId.HasValue || variableId.Value <= 0)
                return false;

            return metrica.VariablesConfiguradas.Any(v => v.VariableID == variableId.Value);
        }

    }
}