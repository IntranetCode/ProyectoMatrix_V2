using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;
using System.Data;
using System.Text.Json;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class MenuController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IServicioAcceso _acceso;

        public MenuController(IConfiguration configuration, IServicioAcceso acceso)
        {
            _configuration = configuration;
            _acceso = acceso;
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

            // ✅ FORZAR RECARGA SI HUBO CAMBIOS DE PERMISOS
            bool forzarRecarga = (TempData["RefreshMenu"] as string) == "true";

            if (forzarRecarga)
            {
                HttpContext.Session.Remove("MenuItems");
                HttpContext.Session.Remove("MenuUsuario");
            }

            // Empresa actual (puede ser null => global)
            var empresaIdStr = HttpContext.Session.GetString("EmpresaID");
            int? empresaId = int.TryParse(empresaIdStr, out var tmp) ? tmp : (int?)null;



            var menuRaiz = new List<MenuModel>();
            string cnn = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
WITH Perms AS (
  SELECT SubMenuID
  FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID)
  WHERE TienePermiso = 1
)
SELECT  m.MenuID,
        m.Nombre AS NombreMenu,
        ca.HomeUrl
FROM Menus m
CROSS APPLY (
    SELECT TOP 1 sm.UrlEnlace AS HomeUrl
    FROM SubMenus sm
    JOIN Perms p ON p.SubMenuID = sm.SubMenuID
    WHERE sm.MenuID = m.MenuID
      AND sm.Activo = 1
      AND sm.UrlEnlace IS NOT NULL
    ORDER BY
      CASE WHEN sm.Nombre LIKE N'Ver %' THEN 1 ELSE 2 END,
      CASE WHEN sm.UrlEnlace LIKE '%/Index' OR sm.UrlEnlace LIKE '%/Entrada' THEN 1 ELSE 2 END,
      sm.SubMenuID
) ca
ORDER BY m.MenuID;";


            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
                var pEmp = cmd.Parameters.Add("@EmpresaID", SqlDbType.Int);
                pEmp.Value = (object?)empresaId ?? DBNull.Value;

                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var nombreMenu = rd.GetString(rd.GetOrdinal("NombreMenu"));
                    var homeUrl = rd.IsDBNull(rd.GetOrdinal("HomeUrl"))
                                    ? null
                                    : rd.GetString(rd.GetOrdinal("HomeUrl"));

                    menuRaiz.Add(new MenuModel
                    {
                        MenuID = rd.GetInt32(rd.GetOrdinal("MenuID")),
                        Nombre = nombreMenu,
                        Url = string.IsNullOrWhiteSpace(homeUrl) ? GetUrlPorDefecto(nombreMenu) : homeUrl,
                        Icono = GetIconoParaMenu(nombreMenu),
                        Descripcion = GetDescripcionParaMenu(nombreMenu),
                        Orden = 0,
                        MenuPadreID = null,
                        SubMenus = new List<MenuModel>()
                    });
                }

            }

            menuRaiz = menuRaiz.GroupBy(m => m.MenuID).Select(g => g.First()).OrderBy(m => m.MenuID).ToList();
            HttpContext.Session.SetString("MenuItems", System.Text.Json.JsonSerializer.Serialize(menuRaiz));

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
                _ => "/"
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