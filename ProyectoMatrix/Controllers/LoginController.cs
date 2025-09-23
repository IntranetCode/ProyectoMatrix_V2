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
using ProyectoMatrix.Servicios;

public class LoginController : Controller
{

    private readonly string _connectionString;
    private readonly BitacoraService _bitacoraService;

    public LoginController(IConfiguration configuration, BitacoraService bitacora)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _bitacoraService = bitacora;


    }

    // ---------- LOGIN GET ----------
    [HttpGet]
  
    public IActionResult Login()
    {
        if (HttpContext.Session.GetInt32("UsuarioID") != null)
        {
            return RedirectToAction("Index", "Menu"); // Redirige si el usuario es diferente de null, porque ya tiene la sesio abierta
       
        }

        return View();
    }


    // ---------- LOGIN POST ----------
    [HttpPost]
    [AuditarAccion(Modulo = "SEGURIDAD", Entidad = "Login", Operacion = "LOGIN", OmitirListas = false)]
    public async Task<IActionResult> Login(UsuarioModel model, EmpresaModel empresa)
    {
        if (!ModelState.IsValid)
            return View(model);

        var usuario = await ObtenerUsuarioActivoAsync(model.Username, _connectionString);

 var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
        var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
       var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

        if (usuario == null || usuario.Password != model.Password)
        {

            //COLOQUE EL SERVICIO DE LOGS EN LOS TRES CASOS POSIBLES
            try
            {
                await _bitacoraService.RegistrarAsync(
                    idUsuario: null,
                    idEmpresa: null,
                    accion: "LOGIN_FALLIDO",
                    mensaje: $"Intento fallido con usuario {model.Username}",
                    modulo: "SEGURIDA",
                    entidad: "Login",
                    entidadId: null,
                    resultado: "ERROR",
                    severidad: 3,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                    );
            }
            catch { }



            ModelState.AddModelError("", "Usuario o contraseña incorrectos");
            return View(model);


        }

        var empresas = await ObtenerEmpresasPorUsuarioAsync(usuario.UsuarioID);

        if (empresas.Count > 1)
        {
           

            try
            {
                await _bitacoraService.RegistrarAsync(
                    idUsuario: model.UsuarioID,
                    idEmpresa: null,
                    accion: "LOGIN_EMPRESAS_ENCONTRADAS",
                    mensaje: $"Usuario {usuario.UsuarioID} tiene {empresas.Count} empresas para elegir",
                    modulo: "SEGURIDAD",
                    entidad: "Login",
                    entidadId: usuario.UsuarioID.ToString(),
                    resultado: "OK",
                    severidad: 1,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                    );
            }
            catch (Exception)
            {

                throw;
            }

            // Varios empresas → mostrar selección
            model.Empresas = empresas;
            model.UsuarioID = usuario.UsuarioID;
            model.Username = "";
            model.Password = "";

            return View(model);
        }
        else if (empresas.Count == 1)
        {
            try
            {
                await _bitacoraService.RegistrarAsync(
                    idUsuario: usuario.UsuarioID,
                    idEmpresa: empresas[0].EmpresaID,
                    accion: "LOGIN_EXITOSO",
                    mensaje: $"Usuario{usuario.UsuarioID} inició sesión en empresa {empresas[0].EmpresaID} ",
                    modulo: "SEGURIDAD",
                    entidad: "Login",
                    entidadId: usuario.UsuarioID.ToString(),
                    resultado: "OK",
                    severidad: 4,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                    );
            }
            catch (Exception) { throw; }

            // Una empresa → continuar login
            return await CompletarLogin(usuario, empresas[0]);


            
        }
        else
        {
            ModelState.AddModelError("", "El usuario no tiene empresas asignadas");
            return View(model);
        }
    }

    // ---------- SELECCIÓN DE EMPRESA ----------
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

    // ---------- COMPLETAR LOGIN ----------
    private async Task<IActionResult> CompletarLogin(UsuarioModel usuario, EmpresaModel empresa)
    {

        // ✅ OBTENER EL RolID desde la base de datos
        int rolId = await ObtenerRolIdPorUsuarioAsync(usuario.UsuarioID);
        // Guardar en sesión
        HttpContext.Session.SetInt32("UsuarioID", usuario.UsuarioID);
        HttpContext.Session.SetString("Username", usuario.Username);
        HttpContext.Session.SetInt32("EmpresaID", empresa.EmpresaID);
        HttpContext.Session.SetString("EmpresaNombre", empresa.Nombre);
        HttpContext.Session.SetString("EmpresaLogo", string.IsNullOrEmpty(empresa.Logo) ? "default.jpg" : empresa.Logo);
        HttpContext.Session.SetString("ColorPrimario", string.IsNullOrEmpty(empresa.ColorPrimario) ? "#007bff" : empresa.ColorPrimario);
        HttpContext.Session.SetString("Rol", usuario.Rol);


        // ✅ AGREGAR ESTAS LÍNEAS - Variables que necesita Universidad NS
        HttpContext.Session.SetInt32("RolID", rolId);                    // ← NUEVO
        HttpContext.Session.SetInt32("EmpresaSeleccionada", empresa.EmpresaID); // ← NUEVO
        // Cargar menú
        var menuUsuario = await ObtenerMenuPorUsuarioAsync(usuario.UsuarioID);
        HttpContext.Session.SetString("MenuUsuario", JsonConvert.SerializeObject(menuUsuario));

        // Autenticación por cookies
        var claims = new List<Claim>
    {
        // Identidad humana
        new Claim(ClaimTypes.Name, usuario.Username ?? $"user:{usuario.UsuarioID}"),

        // 👇 MUY IMPORTANTE: estos dos como numéricos
        new Claim("UsuarioID", usuario.UsuarioID.ToString()),
        new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioID.ToString()),

        // Contexto empresa actual
        new Claim("EmpresaID", empresa.EmpresaID.ToString()),

        // Rol
        new Claim(ClaimTypes.Role, usuario.Rol ?? "Usuario"),
        new Claim("RolID", rolId.ToString())
    };


        TempData["MostrarBienvenida"] = "true";



        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        TempData["Bienvenida"] = $"Bienvenido, {usuario.Username}";
        return RedirectToAction("Index", "Menu");
    }

    // ---------- OBTENER USUARIO POR USERNAME ----------
    private async Task<UsuarioModel?> ObtenerUsuarioActivoAsync(string username, string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 
                u.UsuarioID, 
                u.Username, 
                u.Contrasena AS PasswordHash, 
                r.NombreRol AS Rol
            FROM Usuarios u
            INNER JOIN UsuariosEmpresas ue 
                ON u.UsuarioID = ue.UsuarioID
            INNER JOIN Roles r 
                ON u.RolID = r.RolID
            WHERE u.Username = @Username
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Username", username);

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

    // ---------- OBTENER USUARIO POR ID ----------
    private async Task<UsuarioModel?> ObtenerUsuarioActivoPorIdAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 
                u.UsuarioID, 
                u.Username, 
                u.Contrasena AS PasswordHash, 
                r.NombreRol AS Rol
            FROM Usuarios u
            INNER JOIN UsuariosEmpresas ue 
                ON u.UsuarioID = ue.UsuarioID
            INNER JOIN Roles r 
                ON u.RolID = r.RolID
            WHERE u.UsuarioID = @UsuarioID
              AND u.Activo = 1;";

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

    // ---------- OBTENER EMPRESAS POR USUARIO ----------
    private async Task<List<EmpresaModel>> ObtenerEmpresasPorUsuarioAsync(int usuarioId)
    {
        var empresas = new List<EmpresaModel>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT 
                e.EmpresaID, 
                e.Nombre, 
                e.Logo, 
                e.ColorPrimario
            FROM Empresas e
            INNER JOIN UsuariosEmpresas ue 
                ON e.EmpresaID = ue.EmpresaID
            WHERE ue.UsuarioID = @UsuarioID;";

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

    // ---------- OBTENER MENÚ POR USUARIO ----------
    private async Task<List<MenuModel>> ObtenerMenuPorUsuarioAsync(int usuarioId)
    {
        var listaMenu = new List<MenuModel>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT DISTINCT 
                m.MenuID, 
                m.Nombre AS NombreMenu,
                sm.UrlEnlace
            FROM Menus m
            INNER JOIN SubMenus sm ON m.MenuID = sm.MenuID
            INNER JOIN SubMenuAcciones sma ON sm.SubMenuID = sma.SubMenuID
            INNER JOIN PermisosPorRol pr ON sma.SubMenuAccionID = pr.SubMenuAccionID
            INNER JOIN Roles r ON pr.RolID = r.RolID
            INNER JOIN Usuarios u ON r.RolID = u.RolID
            INNER JOIN UsuariosEmpresas ue ON u.UsuarioID = ue.UsuarioID
            INNER JOIN Empresas e ON ue.EmpresaID = e.EmpresaID
            WHERE u.UsuarioID = @UsuarioID
            ORDER BY m.Nombre;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            listaMenu.Add(new MenuModel
            {
                MenuID = reader.GetInt32(0),
                Nombre = reader.GetString(1),
                Icono = "", // Ajustar si luego agregas el campo
                Url = reader.IsDBNull(2) ? "" : reader.GetString(2),
            });
        }
        return listaMenu;
    }

    // ---------- LOGOUT ----------
    [HttpGet]
    [AuditarAccion(Modulo = "SEGURIDAD", Entidad = "Login", Operacion = "LOGOUT", OmitirListas = false)]
  
    public async Task<IActionResult> Logout()
    {
        // Datos del middleware
        var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
        var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
        var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

        // Usuario actual
        int? idUsuario = null;
        if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid)) idUsuario = uid;
        else if (int.TryParse(User.FindFirst("UserID")?.Value, out uid)) idUsuario = uid;
        else if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid)) idUsuario = uid;
        else idUsuario = HttpContext.Session.GetInt32("UsuarioID"); // último recurso

        int? idEmpresa = int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid) ? eid : null;

        try
        {
            await _bitacoraService.RegistrarAsync(
                idUsuario: idUsuario,
                idEmpresa: idEmpresa,

                accion: "LOGOUT",
                mensaje: $"Usuario {idUsuario} cerró sesión",
                modulo: "SEGURIDAD",
                entidad: "Login",
                entidadId: idUsuario?.ToString(),
                resultado: "OK",
                severidad: 4,              // Auditoría
                solicitudId: solicitudId,
                ip: direccionIp,
                AgenteUsuario: agenteUsuario
            );
        }
        catch
        {
            // nunca romper el flujo de logout por bitácora
        }

        // Cerrar sesión
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();

        return RedirectToAction("Login");
    }


    // ✅ AGREGAR ESTE MÉTODO NUEVO al final de tu LoginController
    private async Task<int> ObtenerRolIdPorUsuarioAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
        SELECT TOP 1 u.RolID 
        FROM Usuarios u 
        WHERE u.UsuarioID = @UsuarioID";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        var result = await command.ExecuteScalarAsync();
        return result != null ? Convert.ToInt32(result) : 4; // Default: Autor/Editor para YOLGUINM
    }

    [HttpGet]
    public IActionResult VerificarSesion()
    {
        int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
        if (usuarioID == null)
            return Json(new { sesionActiva = false });

        return Json(new { sesionActiva = true });
    }

    public IActionResult Index()
    {

        
      

        if (HttpContext.Session.GetInt32("UsuarioID") != null)
        {


            return RedirectToAction("Index", "Menu"); // o "Home", según tu caso
        }

        return RedirectToAction("Login");
    }

}

