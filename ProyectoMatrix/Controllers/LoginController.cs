using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;

public class LoginController : Controller
{
    private readonly string _connectionString;

    public LoginController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(UsuarioModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var usuario = await ObtenerUsuarioActivoAsync(model.Username, _connectionString);

        if (usuario == null || usuario.Password != model.Password)
        {
            ModelState.AddModelError("", "Usuario o contraseña incorrectos");
            return View(model);
        }

        var empresas = await ObtenerEmpresasPorUsuarioAsync(usuario.UsuarioID);

        if (empresas.Count > 1)
        {
            // ✅ CAMBIO PRINCIPAL: Retornamos la vista Login con las empresas cargadas
            model.Empresas = empresas;
            model.UsuarioID = usuario.UsuarioID;
            // Limpiar datos sensibles antes de retornar a la vista
            model.Username = "";
            model.Password = "";

            // Retornar la misma vista Login (no SeleccionEmpresa)
            return View(model);
        }
        else if (empresas.Count == 1)
        {
            // Continuar login normal con esa empresa
            return await CompletarLogin(usuario, empresas[0]);
        }
        else
        {
            ModelState.AddModelError("", "El usuario no tiene empresas asignadas");
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> SeleccionarEmpresa(int usuarioId, int empresaId)
    {
        var usuario = await ObtenerUsuarioActivoPorIdAsync(usuarioId);
        if (usuario == null)
            return RedirectToAction("Login");

        var empresa = (await ObtenerEmpresasPorUsuarioAsync(usuarioId))
                        .Find(e => e.EmpresaID == empresaId);

        if (empresa == null)
        {
            ModelState.AddModelError("", "Empresa no válida");

            // ✅ CAMBIO: Si hay error, retornar a Login con las empresas cargadas
            var empresas = await ObtenerEmpresasPorUsuarioAsync(usuarioId);
            var model = new UsuarioModel
            {
                UsuarioID = usuarioId,
                Empresas = empresas,
                Username = "",
                Password = ""
            };

            return View("Login", model);
        }

        return await CompletarLogin(usuario, empresa);
    }

    private async Task<IActionResult> CompletarLogin(UsuarioModel usuario, EmpresaModel empresa)
    {
        HttpContext.Session.SetInt32("UsuarioID", usuario.UsuarioID);
        HttpContext.Session.SetString("Username", usuario.Username);
        HttpContext.Session.SetInt32("EmpresaID", empresa.EmpresaID);
        HttpContext.Session.SetString("EmpresaNombre", empresa.Nombre);
        HttpContext.Session.SetString("EmpresaLogo", string.IsNullOrEmpty(empresa.Logo) ? "default.jpg" : empresa.Logo);
        HttpContext.Session.SetString("ColorPrimario", string.IsNullOrEmpty(empresa.ColorPrimario) ? "#007bff" : empresa.ColorPrimario);
        HttpContext.Session.SetString("Rol", usuario.Rol);

        var menuUsuario = await ObtenerMenuPorUsuarioAsync(usuario.UsuarioID);
        HttpContext.Session.SetString("MenuUsuario", JsonConvert.SerializeObject(menuUsuario));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioID.ToString()),
            new Claim(ClaimTypes.Name, usuario.Username),
            new Claim("EmpresaID", empresa.EmpresaID.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        TempData["Bienvenida"] = $"Bienvenido, {usuario.Username}";
        return RedirectToAction("Index", "Menu");
    }

    // Métodos para obtener datos (usuario, empresas, menú) desde la base de datos
    private async Task<UsuarioModel?> ObtenerUsuarioActivoAsync(string username, string connectionString)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                SELECT TOP 1 u.UsuarioID, 
                               u.Username, 
                               u.PasswordHash, 
                               r.Nombre AS Rol
                FROM Usuarios u
                INNER JOIN UsuarioEmpresaRol uer ON u.UsuarioID = uer.UsuarioID
                INNER JOIN Roles r ON uer.RolID = r.RolID AND uer.EmpresaID = r.EmpresaID
                WHERE u.Username = @Username AND u.Activo = 1;";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new UsuarioModel
                        {
                            UsuarioID = reader.GetInt32(reader.GetOrdinal("UsuarioID")),
                            Username = reader.GetString(reader.GetOrdinal("Username")),
                            Password = reader.GetString(reader.GetOrdinal("PasswordHash")),
                            Rol = reader.GetString(reader.GetOrdinal("Rol"))
                        };
                    }
                }
            }
        }

        return null;
    }

    private async Task<UsuarioModel?> ObtenerUsuarioActivoPorIdAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 u.UsuarioID, u.Username, u.PasswordHash, r.Nombre AS Rol
            FROM Usuarios u
            INNER JOIN UsuarioEmpresaRol uer ON u.UsuarioID = uer.UsuarioID
            INNER JOIN Roles r ON uer.RolID = r.RolID AND uer.EmpresaID = r.EmpresaID
            WHERE u.UsuarioID = @UsuarioID AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new UsuarioModel
            {
                UsuarioID = reader.GetInt32(reader.GetOrdinal("UsuarioID")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Password = reader.GetString(reader.GetOrdinal("PasswordHash")),
                Rol = reader.GetString(reader.GetOrdinal("Rol"))
            };
        }

        return null;
    }

    private async Task<List<EmpresaModel>> ObtenerEmpresasPorUsuarioAsync(int usuarioId)
    {
        var empresas = new List<EmpresaModel>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT e.EmpresaID, e.Nombre, e.Logo, e.ColorPrimario
            FROM Empresas e
            JOIN UsuarioEmpresaRol uer ON e.EmpresaID = uer.EmpresaID
            WHERE uer.UsuarioID = @UsuarioID";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            empresas.Add(new EmpresaModel
            {
                EmpresaID = reader.GetInt32(0),
                Nombre = reader.GetString(1),
                Logo = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ColorPrimario = reader.IsDBNull(3) ? "" : reader.GetString(3)
            });
        }

        return empresas;
    }

    private async Task<List<MenuModel>> ObtenerMenuPorUsuarioAsync(int usuarioId)
    {
        var listaMenu = new List<MenuModel>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT DISTINCT m.MenuID, m.Nombre, m.Icono, m.Url, m.Orden, m.MenuPadreID
            FROM Menu m
            JOIN Permisos p ON m.PermisoID = p.PermisoID
            JOIN RolPermisoAccion rpa ON p.PermisoID = rpa.PermisoID
            JOIN UsuarioRoles ur ON ur.RolID = rpa.RolID
            WHERE ur.UsuarioID = @UsuarioID AND m.Activo = 1
            ORDER BY m.Orden";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            listaMenu.Add(new MenuModel
            {
                MenuID = reader.GetInt32(0),
                Nombre = reader.GetString(1),
                Icono = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Url = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Orden = reader.GetInt32(4),
                MenuPadreID = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
            });
        }

        return listaMenu;
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}