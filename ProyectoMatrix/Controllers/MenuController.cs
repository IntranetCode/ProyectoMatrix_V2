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

            HttpContext.Session.SetInt32("UsuarioID", 1);
            HttpContext.Session.SetInt32("EmpresaID", 2);

            //CACHE DE NAVEGADOR
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

            // ✅ CONSULTA MEJORADA - Elimina duplicados y maneja URLs NULL
            string query = @"SELECT DISTINCT 
                m.MenuID, 
                m.Nombre AS NombreMenu,
                sm.UrlEnlace
            FROM Menus m
            INNER JOIN SubMenus sm 
                ON m.MenuID = sm.MenuID
            INNER JOIN SubMenuAcciones sma 
                ON sm.SubMenuID = sma.SubMenuID
            INNER JOIN PermisosPorRol pr 
                ON sma.SubMenuAccionID = pr.SubMenuAccionID
            INNER JOIN Roles r 
                ON pr.RolID = r.RolID
            INNER JOIN Usuarios u 
                ON r.RolID = u.RolID
            INNER JOIN UsuariosEmpresas ue 
                ON u.UsuarioID = ue.UsuarioID
            INNER JOIN Empresas e 
                ON ue.EmpresaID = e.EmpresaID
            WHERE u.UsuarioID = @UsuarioID
                AND sm.Activo = 1
                AND sma.Activo = 1
            ORDER BY m.MenuID;"; // Cambié a ORDER BY MenuID para orden consistente

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
                    Url = urlEnlace ?? GetUrlPorDefecto(nombreMenu), // ✅ URL por defecto si es NULL
                    Icono = GetIconoParaMenu(nombreMenu), // ✅ Icono dinámico
                    Descripcion = GetDescripcionParaMenu(nombreMenu), // ✅ Descripción para la vista
                    Orden = 0,
                    MenuPadreID = null,
                    SubMenus = new List<MenuModel>()
                });
            }

            // ✅ ELIMINAR DUPLICADOS - Importante porque un menú puede tener múltiples acciones
            var menuRaiz = menusPlanos
                .GroupBy(m => m.MenuID)
                .Select(g => g.First()) // Toma el primer elemento de cada grupo
                .OrderBy(m => m.MenuID)
                .ToList();

            // Ya no necesitas el foreach porque no tienes submenús jerárquicos en tu estructura

            HttpContext.Session.SetString("MenuItems", JsonSerializer.Serialize(menuRaiz));

            if (rol != "Administrador")
            {
                bool esGestor = User.IsInRole("Autor/Editor de Contenido")
             || User.IsInRole("Administrador de Intranet")
             || User.IsInRole("Propietario de Contenido");

                foreach (var m in menuRaiz)
                {
                    var nombre = (m.Nombre ?? "").ToLowerInvariant();
                    if (nombre.Contains("líder") || nombre.Contains("lider"))
                    {
                        m.Url = esGestor ? Url.Action("Index", "Lider")
                                         : Url.Action("Lista", "Webinars");
                    }
                }
                return View("Index", menuRaiz);
            }


            return View(menuRaiz);
        }

        // ✅ MÉTODO HELPER - URLs por defecto para menús sin URL
        private string GetUrlPorDefecto(string nombreMenu)
        {
            if (string.IsNullOrEmpty(nombreMenu)) return "#";

            var menuLower = nombreMenu?.Trim().ToLowerInvariant() ?? "";




            return menuLower switch
            {
                var x when x.Contains("universidad") => "/Universidad",
                var x when x.Contains("usuario") => "/Usuario",
                var x when x.Contains("gestión") => "/Usuario",
                var x when x.Contains("líder") || x.Contains("lider") => "/Lider",

                var x when x.Contains("comunicado") => "/Comunicados/Index",
                _ => "#"
            };
        }

        // ✅ MÉTODO HELPER - Iconos dinámicos
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

        // ✅ MÉTODO HELPER - Descripciones para las tarjetas
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