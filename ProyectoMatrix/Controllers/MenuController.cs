using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;

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

        public async Task<IActionResult> Index()
        {
            int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioID == null)
                return RedirectToAction("Login", "Login");

            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            var menuItems = new List<MenuItemViewModel>();
            string rol = HttpContext.Session.GetString("Rol") ?? "";
            
            using SqlConnection conn = new(connectionString);
            await conn.OpenAsync();

            // Consulta para traer solo menús que el usuario tiene permiso según su rol
            /*            string query = @"
            SELECT DISTINCT m.MenuID, m.Nombre, m.Icono, m.Url, m.Orden, m.MenuPadreID
            FROM Menu m
            INNER JOIN Permisos p ON m.MenuID = p.MenuID
            INNER JOIN RolPermisoAccion rpa ON rpa.PermisoID = p.PermisoID
            INNER JOIN UsuarioEmpresaRol uer ON uer.RolID = rpa.RolID
            WHERE uer.UsuarioID = @UsuarioID AND m.Activo = 1
            ORDER BY m.Orden;

                        ";*/
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
ORDER BY m.Nombre;";
            using SqlCommand cmd = new(query, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            var menusPlanos = new List<MenuModel>();

            while (await reader.ReadAsync())
            {
                menusPlanos.Add(new MenuModel
                {
                    /*MenuID = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    Icono = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Url = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Orden = reader.GetInt32(4),
                    MenuPadreID = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    SubMenus = new List<MenuModel>()*/
                    MenuID = reader.GetInt32(0),
                    Nombre = reader.GetString(1),
                    Url = reader.IsDBNull(2) ? null : reader.GetString(2),

                    // Como no tienes Icono, Orden ni MenuPadreID en la consulta, pon valores por defecto
                    Icono = null,
                    Orden = 0,
                    MenuPadreID = null,
                    SubMenus = new List<MenuModel>()
                });
            }

            // Construir jerarquía de menús padre-submenús
            var menuRaiz = menusPlanos.Where(m => m.MenuPadreID == null).OrderBy(m => m.Orden).ToList();

            foreach (var menu in menuRaiz)
            {
                menu.SubMenus = menusPlanos.Where(sm => sm.MenuPadreID == menu.MenuID).OrderBy(sm => sm.Orden).ToList();
            }

            HttpContext.Session.SetString("MenuItems", JsonSerializer.Serialize(menuRaiz));
            if (rol != "Administrador")
            {
                return View("Index", menuRaiz); // ✅ PASAS EL MODELO
            }

            return View(menuRaiz);
        }
    }
}