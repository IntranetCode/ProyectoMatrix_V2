using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Dapper;
using ProyectoMatrix.Models;

public class AreasBD
{
    private readonly string _connectionString;

    public AreasBD(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Area>> ObtenerAreasAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"SELECT AreaID, Nombre, Icono, ColorHex, Activo
                       FROM Areas
                       WHERE Activo = 1";

        var areas = await connection.QueryAsync<Area>(sql);
        return areas.AsList();
    }
}
