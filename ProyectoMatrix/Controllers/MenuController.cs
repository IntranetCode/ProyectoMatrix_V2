using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;
using System.Data;
using System.Text.Json;

namespace ProyectoMatrix.Controllers
{
    public class MenuController : Controller
    {
        private readonly IConfiguration _configuration;

        public MenuController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Login");
        }

        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            ViewBag.MostrarBienvenida = (TempData["MostrarBienvenida"] as string) == "true";

            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioID == null)
                return RedirectToAction("Login", "Login");

            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            var menuItems = new List<MenuItemViewModel>();
            string rol = HttpContext.Session.GetString("Rol") ?? "";

            using SqlConnection conn = new(connectionString);
            await conn.OpenAsync();

            // ✅ INICIO: CONSULTA SQL CORREGIDA CON UNION
            string query = @"
                -- Parte 1: Obtiene los menús permitidos por el ROL del usuario
                SELECT m.MenuID, m.Nombre AS NombreMenu, sm.UrlEnlace
                FROM Menus m
                INNER JOIN SubMenus sm ON m.MenuID = sm.MenuID
                INNER JOIN SubMenuAcciones sma ON sm.SubMenuID = sma.SubMenuID
                INNER JOIN PermisosPorRol pr ON sma.SubMenuAccionID = pr.SubMenuAccionID
                INNER JOIN Usuarios u ON pr.RolID = u.RolID
                WHERE u.UsuarioID = @UsuarioID AND sm.Activo = 1 AND sma.Activo = 1

                UNION

                -- Parte 2: Obtiene los menús permitidos por asignación DIRECTA (checkboxes)
                SELECT m.MenuID, m.Nombre AS NombreMenu, sm.UrlEnlace
                FROM Menus m
                INNER JOIN SubMenus sm ON m.MenuID = sm.MenuID
                INNER JOIN Permisos p ON sm.SubMenuID = p.SubMenuID
                WHERE p.UsuarioID = @UsuarioID AND sm.Activo = 1;
            ";
            // ✅ FIN: CONSULTA SQL CORREGIDA

            using SqlCommand cmd = new(query, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            var menusPlanos = new List<MenuModel>();

            while (await reader.ReadAsync())
            {
                var nombreMenu = reader.GetString("NombreMenu");
                var urlEnlace = reader.IsDBNull("UrlEnlace") ? null : reader.GetString("UrlEnlace");

                menusPlanos.Add(new MenuModel
                {
                    MenuID = reader.GetInt32("MenuID"),
                    Nombre = nombreMenu,
                    Url = urlEnlace ?? GetUrlPorDefecto(nombreMenu),
                    Icono = GetIconoParaMenu(nombreMenu),
                    Descripcion = GetDescripcionParaMenu(nombreMenu),
                    Orden = 0,
                    MenuPadreID = null,
                    SubMenus = new List<MenuModel>()
                });
            }

            var menuRaiz = menusPlanos
                .GroupBy(m => m.MenuID)
                .Select(g => g.First())
                .OrderBy(m => m.MenuID)
                .ToList();

            HttpContext.Session.SetString("MenuItems", JsonSerializer.Serialize(menuRaiz));

           
            return View(menuRaiz);
        }

        // --- Tus métodos Helper (GetUrlPorDefecto, GetIconoParaMenu, etc.) van aquí sin cambios ---
        private string GetUrlPorDefecto(string nombreMenu)
        {
            if (string.IsNullOrEmpty(nombreMenu)) return "#";
            var menuLower = nombreMenu?.Trim().ToLowerInvariant() ?? "";
            return menuLower switch
            {
                var x when x.Contains("universidad") => "/Universidad",
                var x when x.Contains("usuario") => "/Usuarios/Index",
                var x when x.Contains("gestión") => "/Usuario",
                var x when x.Contains("líder") || x.Contains("lider") => "/Lider/Entrada",
                var x when x.Contains("proyectos") => "/Proyectos/Index",
                var x when x.Contains("comunicado") => "/Comunicados/Index",
                _ => "#"
            };
        }
        private string GetIconoParaMenu(string nombreMenu)
        {
            if (string.IsNullOrEmpty(nombreMenu)) return "fa-cogs";
            var menuLower = nombreMenu?.Trim().ToLowerInvariant() ?? "";
            return menuLower switch
            {
                var x when x.Contains("universidad") => "fa-graduation-cap",
                var x when x.Contains("usuario") => "fa-users",
                var x when x.Contains("gestión") => "fa-users-cog",
                var x when x.Contains("líder") || x.Contains("lider") => "fa-user-tie",
                var x when x.Contains("comunicado") => "fa-bullhorn",
                var x when x.Contains("mejora") => "fa-chart-line",
                var x when x.Contains("compra") => "fa-shopping-cart",
                var x when x.Contains("logística") => "fa-truck",
                var x when x.Contains("help") => "fa-headset",
                _ => "fa-cogs"
            };
        }
        private string GetDescripcionParaMenu(string nombreMenu)
        {
            if (string.IsNullOrEmpty(nombreMenu)) return "";
            var menuLower = nombreMenu?.Trim().ToLowerInvariant() ?? "";
            return menuLower switch
            {
                var x when x.Contains("universidad") => "Capacitación y Certificación Corporativa",
                var x when x.Contains("usuario") => "Administración de usuarios del sistema",
                var x when x.Contains("gestión") => "Gestión de usuarios y permisos",
                var x when x.Contains("líder") || x.Contains("lider") => "Comunicación entre líderes",
                var x when x.Contains("comunicado") => "Anuncios y comunicaciones internas",
                var x when x.Contains("mejora") => "Procesos de mejora continua",
                var x when x.Contains("compra") => "Solicitudes y gestión de compras",
                var x when x.Contains("logística") => "Gestión logística y entregas",
                var x when x.Contains("help") => "Soporte técnico y asistencia",
                _ => $"Accede a {nombreMenu}"
            };
        }
    }
}