using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ProyectoMatrix.Servicios
{
    public class ServicioAcceso : IServicioAcceso
    {
        private readonly string _connStr;
        private readonly IHttpContextAccessor _http;

        public ServicioAcceso(IConfiguration configuration, IHttpContextAccessor http)
        {
            _connStr = configuration.GetConnectionString("DefaultConnection");
            _http = http;
        }

        // Overload de conveniencia: obtiene EmpresaID de sesión o claims
        public Task<bool> TienePermisoAsync(int usuarioId, string subMenu, string? accion = null)
        {
            int? empresaId = _http.HttpContext?.Session.GetInt32("EmpresaID");
            if (empresaId is null)
            {
                var claim = _http.HttpContext?.User?.FindFirst("EmpresaID")?.Value;
                if (int.TryParse(claim, out var e)) empresaId = e;
            }
            return TienePermisoAsync(usuarioId, empresaId, subMenu, accion);
        }

        // Núcleo
        public async Task<bool> TienePermisoAsync(int usuarioId, int? empresaId, string subMenu, string? accion = null)
        {
            using var cn = new SqlConnection(_connStr);
            await cn.OpenAsync();

            const string sql = @"
DECLARE @SubId INT = (SELECT TOP 1 SubMenuID FROM dbo.SubMenus WHERE Nombre=@s);
IF @SubId IS NULL
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END

-- 1) Permiso EFECTIVO (rol ± overrides)
IF NOT EXISTS(
    SELECT 1
    FROM dbo.fn_PermisosEfectivosUsuario(@u,@e) f
    WHERE f.SubMenuID = @SubId AND f.TienePermiso = 1
)
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END

-- 2) Si no se pide validar acción concreta, con el permiso efectivo alcanza
IF (@accion IS NULL OR LTRIM(RTRIM(@accion)) = '')
BEGIN
    SELECT CAST(1 AS bit);
    RETURN;
END

-- 3) Validar acción por rol (opcional)
SELECT CAST(CASE WHEN EXISTS(
    SELECT 1
    FROM dbo.Usuarios u
    JOIN dbo.PermisosPorRol pr        ON pr.RolID = u.RolID AND pr.Activo=1
    JOIN dbo.SubMenuAcciones sma      ON sma.SubMenuAccionID = pr.SubMenuAccionID AND sma.SubMenuID = @SubId
    JOIN dbo.Acciones a               ON a.AccionID = sma.AccionID AND a.Nombre = @accion
    WHERE u.UsuarioID = @u
) THEN 1 ELSE 0 END AS bit);";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@u", usuarioId);
            var pE = cmd.Parameters.Add("@e", SqlDbType.Int);
            pE.Value = (object?)empresaId ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@s", subMenu);
            cmd.Parameters.AddWithValue("@accion", (object?)accion ?? DBNull.Value);

            var r = await cmd.ExecuteScalarAsync();
            return r is bool b && b;
        }
    }
}
