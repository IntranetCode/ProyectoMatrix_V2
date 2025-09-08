using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

public sealed class BitacoraService
{
    private readonly string _cadenaConexion;

    public BitacoraService(IConfiguration config)
    {
        _cadenaConexion = config.GetConnectionString("DefaultConnection");
    }


    public async Task RegistrarAsync(
        int? idUsuario,
        int? idEmpresa,
        string accion,
        string mensaje = null,
        string resultado = null,   // NULL = usa default 'OK'
        byte? severidad = null     // NULL = usa default 1
    )
    {
        using var cn = new SqlConnection(_cadenaConexion);
        using var cmd = new SqlCommand("bitacora.InsertarRegistro", cn)
        { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@IdUsuario", (object?)idUsuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdEmpresa", (object?)idEmpresa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Accion", accion);
        cmd.Parameters.AddWithValue("@Mensaje", (object?)mensaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Resultado", (object?)resultado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Severidad", (object?)severidad ?? DBNull.Value);

        await cn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}
