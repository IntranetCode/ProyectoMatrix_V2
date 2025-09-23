using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;

//Este servicio implementa IservicioAcceso, contiene la loguca de consultar a la bd
//para saber si un usuario tiene una accion asignada 
//va a devolcer true o false dependiendo si tiene el  permiso

namespace ProyectoMatrix.Servicios
{
    public class ServicioAcceso : IServicioAcceso
    {
        private readonly string _connStr;

        public ServicioAcceso (IConfiguration configuracion)
        {
            _connStr = configuracion.GetConnectionString("DefaultConnection");
        }

        public async Task<bool> TienePermisoAsync(int usuarioId, string subMenu,string accion )
        {
            using var conn = new SqlConnection ( _connStr );
            await conn.OpenAsync();

            using var cmd = new SqlCommand ( @"
            SELECT TOP 1 1
             FROM PermisosPorRol pr
             JOIN SubMenuAcciones sma ON sma.SubMenuAccionID =  pr.SubMenuAccionID AND pr.Activo=1
             JOIN SubMenus sm ON sm.SubMenuID = sma.SubMenuID AND sm.Activo=1
             JOIN Acciones a ON a.AccionID =  sma.AccionID
             JOIN Usuarios ur ON ur.RolID = pr.RolID AND ur.UsuarioID= @u AND ur.Activo=1
              WHERE sm.Nombre=@s AND a.Nombre=@a; ", conn );

            cmd.Parameters.AddWithValue ("@u", usuarioId);
            cmd.Parameters.AddWithValue("@s", subMenu);
            cmd.Parameters.AddWithValue("@a", accion);


            var r = await cmd.ExecuteScalarAsync();
            return r != null;
        }
    }
}
