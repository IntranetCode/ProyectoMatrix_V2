using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Dapper;
using ProyectoMatrix.Models;

public class ContenidosBD
{
    private readonly string _connectionString;

    public ContenidosBD(string connectionString)
    {
        _connectionString = connectionString;
    }
    public async Task<List<ContenidoEducativo>> ObtenerContenidosPorAreaAsync(int areaId)
    {
        using var connection = new SqlConnection(_connectionString);
        string sql = @"
        SELECT 
            ContenidoID AS Id,
            AreaID,
            Titulo,
            Descripcion,
            TipoContenido,
            Categoria,
            FechaCreacion,
            CreadoPor,
            Visualizaciones,
            RutaArchivo,
            UrlVideo,
            Thumbnail,
            OrdenVisualizacion,
            Tags,
            TamanoArchivo,
            Extension,
            EsActivo
        FROM ContenidosEducativos
        WHERE EsActivo = 1 AND AreaID = @AreaId";

        var contenidos = await connection.QueryAsync<ContenidoEducativo>(sql, new { AreaId = areaId });
        return contenidos.ToList();
    }

    // ✅ Obtener todos los contenidos activos
    public async Task<List<ContenidoEducativo>> ObtenerContenidosAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT 
                ContenidoID AS Id,
                Titulo,
                Descripcion,
                TipoContenido,
                Categoria,
                FechaCreacion,
                CreadoPor,
                Visualizaciones,
                ArchivoRuta,
                UrlVideo,
                Thumbnail,
                OrdenVisualizacion,
                Tags,
                TamanoArchivo,
                Extension,
                EsActivo
            FROM ContenidoEducativo
            WHERE EsActivo = 1";

        var contenidos = await connection.QueryAsync<ContenidoEducativo>(sql);
        return contenidos.AsList();
    }

    // ✅ Registrar visualización (+1)
    public async Task RegistrarVisualizacionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            UPDATE ContenidosEducativos
            SET Visualizaciones = ISNULL(Visualizaciones, 0) + 1
            WHERE ContenidoID = @Id";

        await connection.ExecuteAsync(sql, new { Id = id });
    }
    public async Task<Curso> ObtenerCursoPorIdAsync(int id)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            var query = "SELECT * FROM Cursos WHERE CursoID = @Id";
            return await connection.QueryFirstOrDefaultAsync<Curso>(query, new { Id = id });
        }
    }

    // ✅ Obtener un solo contenido por ID (para PDF o video)
    public async Task<ContenidoEducativo> ObtenerContenidoPorIdAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT 
                ContenidoID AS Id,
                Titulo,
                Descripcion,
                TipoContenido,
                Categoria,
                FechaCreacion,
                CreadoPor,
                Visualizaciones,
                ArchivoRuta,
                UrlVideo,
                Thumbnail,
                OrdenVisualizacion,
                Tags,
                TamanoArchivo,
                Extension,
                EsActivo
                --AreaID
            FROM ContenidoEducativo
            WHERE ContenidoID = @Id AND EsActivo = 1";

        return await connection.QueryFirstOrDefaultAsync<ContenidoEducativo>(sql, new { Id = id });
    }

    // 🔄 Obtener el ID del colaborador a partir del ID de usuario
    public async Task<int> ObtenerColaboradorIdPorUsuario(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        string sql = "SELECT ColaboradorID FROM Colaboradores WHERE UsuarioID = @UsuarioID";
        return await connection.QuerySingleOrDefaultAsync<int>(sql, new { UsuarioID = usuarioId });
    }

    // 🔄 Obtener los cursos de un colaborador por nivel
    public async Task<List<ContenidoEducativo>> ObtenerProgresoPorColaboradorYNivel(int colaboradorId, string nivel)
    {
        using var connection = new SqlConnection(_connectionString);
        
        
        string sql = @"
        SELECT ce.*
        FROM ProgresoCurso pc
        INNER JOIN ContenidosEducativos ce ON pc.CursoID = ce.ContenidoID
        WHERE pc.ColaboradorID = @ColaboradorID AND pc.Nivel = @Nivel";
        
        var cursos = await connection.QueryAsync<ContenidoEducativo>(sql, new
        {
            ColaboradorID = colaboradorId,
            Nivel = nivel
        });

        return cursos.ToList();
    }
    public async Task<List<Curso>> ObtenerProgresoPorColaboradorYNivelc(int colaboradorId, string nivel)
    {
        using var connection = new SqlConnection(_connectionString);
        string sql = @"
        SELECT cu.*
        FROM ProgresoCurso pc
        --INNER JOIN ContenidosEducativos ce ON pc.CursoID = ce.ContenidoID
        INNER JOIN Cursos cu on pc.CursoID=cu.CursoID
        WHERE pc.ColaboradorID = @ColaboradorID AND pc.Nivel = @Nivel";
        

        var cursos = await connection.QueryAsync<Curso>(sql, new
        {
            ColaboradorID = colaboradorId,
            Nivel = nivel
        });

        return cursos.ToList();
    }

}
