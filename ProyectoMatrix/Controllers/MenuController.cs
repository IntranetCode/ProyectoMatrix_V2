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



            int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioID == null)
                return RedirectToAction("Login", "Login");

            var empresaIdStr = HttpContext.Session.GetString("EmpresaID");
            int? empresaId = int.TryParse(empresaIdStr, out var tmp) ? tmp : (int?)null;

            var grupos = new List<MenuGrupoModel>();

            string cnn = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
WITH Perms AS (
  SELECT SubMenuID
  FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID)
  WHERE TienePermiso = 1
)


SELECT DISTINCT
    g.MenuGrupoID,
    g.Nombre,
    g.Descripcion,
    g.IconoCss,
    g.Orden
FROM SubMenus sm
JOIN Perms p ON p.SubMenuID = sm.SubMenuID
JOIN Menus m ON m.MenuID = sm.MenuID
JOIN MenuGrupo g ON g.MenuGrupoID = m.MenuGrupoID
WHERE sm.Activo = 1
  AND g.Activo = 1
ORDER BY g.Orden, g.Nombre;";

            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
                var pEmp = cmd.Parameters.Add("@EmpresaID", SqlDbType.Int);
                pEmp.Value = (object?)empresaId ?? DBNull.Value;

                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    grupos.Add(new MenuGrupoModel
                    {
                        MenuGrupoID = rd.GetInt32(rd.GetOrdinal("MenuGrupoID")),
                        Nombre = rd.GetString(rd.GetOrdinal("Nombre")),
                        Descripcion = rd.IsDBNull(rd.GetOrdinal("Descripcion")) ? null : rd.GetString(rd.GetOrdinal("Descripcion")),
                        IconoCss = rd.IsDBNull(rd.GetOrdinal("IconoCss")) ? null : rd.GetString(rd.GetOrdinal("IconoCss")),
                        Orden = rd.GetInt32(rd.GetOrdinal("Orden"))
                    });
                }
            }

            return View("Grupos", grupos); // <- nueva vista
        }



        [HttpGet]
        public async Task<IActionResult> Grupo(int id) // id = MenuGrupoID
        {
            int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioID == null)
                return RedirectToAction("Login", "Login");

            var empresaIdStr = HttpContext.Session.GetString("EmpresaID");
            int? empresaId = int.TryParse(empresaIdStr, out var tmp) ? tmp : (int?)null;

            var menus = new List<MenuModel>();

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
JOIN MenuGrupo g ON g.MenuGrupoID = m.MenuGrupoID
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
WHERE m.MenuGrupoID = @MenuGrupoID
ORDER BY m.MenuID;";

            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
                var pEmp = cmd.Parameters.Add("@EmpresaID", SqlDbType.Int);
                pEmp.Value = (object?)empresaId ?? DBNull.Value;

                cmd.Parameters.AddWithValue("@MenuGrupoID", id);

                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var nombreMenu = rd.GetString(rd.GetOrdinal("NombreMenu"));
                    var homeUrl = rd.IsDBNull(rd.GetOrdinal("HomeUrl")) ? null : rd.GetString(rd.GetOrdinal("HomeUrl"));

                    menus.Add(new MenuModel
                    {
                        MenuID = rd.GetInt32(rd.GetOrdinal("MenuID")),
                        Nombre = nombreMenu,
                        Url = string.IsNullOrWhiteSpace(homeUrl) ? GetUrlPorDefecto(nombreMenu) : homeUrl,
                        Icono = GetIconoParaMenu(nombreMenu),
                        Descripcion = GetDescripcionParaMenu(nombreMenu),
                    });
                }
            }

            // Para breadcrumb
            ViewBag.MenuGrupoID = id;
            ViewBag.NombreGrupo = await ObtenerNombreGrupo(conn, id);

            return View("MenusPorGrupo", menus);
        }

        private static async Task<string> ObtenerNombreGrupo(SqlConnection conn, int menuGrupoId)
        {
            const string sql = "SELECT Nombre FROM MenuGrupo WHERE MenuGrupoID = @Id;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", menuGrupoId);
            var r = await cmd.ExecuteScalarAsync();
            return r?.ToString() ?? "Grupo";
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
                var x when x.Contains("help") => "/HelpDesk/Index",
                var x when x.Contains("compras") => "/Compras/Index",
                var x when x.Contains("nacional") => "/Compras/Nacional",
                var x when x.Contains("guias") => "/Logistica/Guias",
                var x when x.Contains("transporte") => "/Logistica/Transporte",
                var x when x.Contains("directorio") => "/Directorio/Index",
                var x when x.Contains("KPIS") => "/Operaciones/Index",
                var x when x.Contains("Citas") => "/Citas/Index",
                _ => "/"
            };
        }

        
        private string GetIconoParaMenu(string nombreMenu)
{
    if (string.IsNullOrWhiteSpace(nombreMenu))
        return "fa-layer-group";

    var s = RemoveAccents(nombreMenu.Trim().ToLowerInvariant());

    return s switch
    {
        var x when x.Contains("universidad") || x.Contains("capacitacion") => "fa-graduation-cap",
        var x when x.Contains("gestion") || x.Contains("usuario") => "fa-users",
        var x when x.Contains("lider") => "fa-user-shield",
        var x when x.Contains("denuncia") => "fa-user-secret",
        var x when x.Contains("vacacion") => "fa-umbrella-beach",
        var x when x.Contains("cita") => "fa-calendar-check",
        var x when x.Contains("directorio") => "fa-address-book",
        var x when x.Contains("kpi") || x.Contains("indicador") => "fa-chart-pie",
        var x when x.Contains("comunicado") || x.Contains("anuncio") => "fa-bullhorn",
        var x when x.Contains("mejora") || x.Contains("continua") => "fa-chart-line",
        var x when x.Contains("internacional") => "fa-plane-departure",
        var x when x.Contains("nacional") || x.Contains("compra") => "fa-cart-shopping",
        var x when x.Contains("transporte") => "fa-truck",
        var x when x.Contains("embarque") => "fa-ship",
        var x when x.Contains("guia") => "fa-map-location-dot",
        var x when x.Contains("help") || x.Contains("soporte") || x.Contains("asistencia") => "fa-circle-question",
        var x when x.Contains("ticket") => "fa-ticket",
        var x when x.Contains("proyecto") => "fa-folder-open",

        _ => "fa-layer-group"
    };
}
        

        private string RemoveAccents(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
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
                var x when x.Contains("nacional") => "Solicitudes y gestión de compras nacionales",
                var x when x.Contains("guias") => "Gestión logística y entregas",
                var x when x.Contains("help") => "Soporte técnico y asistencia",
                var x when x.Contains("internacional") => "Solicitudes y gestión de compras internacionales",
                var x when x.Contains("transporte") => "Gestión logística y entregas",
                var x when x.Contains("directorio") => "Directorio de usuarios",

                _ => $"Accede a {nombreMenu}"
            };
        }
    }
}