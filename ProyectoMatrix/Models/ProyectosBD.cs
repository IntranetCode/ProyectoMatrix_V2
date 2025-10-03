using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Models;
using ProyectoMatrix.Seguridad;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Security.Policy;
using System.Threading.Tasks;

public class ProyectosBD 
{
    private readonly string _connectionString;

    public ProyectosBD(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Proyecto>> ObtenerProyectosPorEmpresaAsync(int empresaId)
    {
        var proyectos = new List<Proyecto>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                SELECT ProyectoID, NombreProyecto, Descripcion, CodigoProyecto, ArchivoRuta, 
                       FechaCreacion, FechaInicio, FechaFinPrevista, FechaFinReal, CreadoPor, 
                       ResponsableProyecto, EsActivo, EmpresaID, Tags, TamanoArchivo, Extension, 
                       Estado, Prioridad, Presupuesto, Progreso, Observaciones, Visualizaciones
                FROM [dbo].[Proyectos] 
                WHERE EmpresaID = @EmpresaID 
                ORDER BY FechaCreacion DESC";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        proyectos.Add(new Proyecto
                        {
                            ProyectoID = reader.GetInt32("ProyectoID"),
                            NombreProyecto = reader.GetString("NombreProyecto"),
                            Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                            CodigoProyecto = reader.IsDBNull("CodigoProyecto") ? null : reader.GetString("CodigoProyecto"),
                            ArchivoRuta = reader.IsDBNull("ArchivoRuta") ? null : reader.GetString("ArchivoRuta"),
                            FechaCreacion = reader.GetDateTime("FechaCreacion"),
                            FechaInicio = reader.IsDBNull("FechaInicio") ? null : reader.GetDateTime("FechaInicio"),
                            FechaFinPrevista = reader.IsDBNull("FechaFinPrevista") ? null : reader.GetDateTime("FechaFinPrevista"),
                            FechaFinReal = reader.IsDBNull("FechaFinReal") ? null : reader.GetDateTime("FechaFinReal"),
                            CreadoPor = reader.IsDBNull("CreadoPor") ? null : reader.GetString("CreadoPor"),
                            ResponsableProyecto = reader.IsDBNull("ResponsableProyecto") ? null : reader.GetString("ResponsableProyecto"),
                            EsActivo = reader.GetBoolean("EsActivo"),
                            EmpresaID = reader.GetInt32("EmpresaID"),
                            Tags = reader.IsDBNull("Tags") ? null : reader.GetString("Tags"),
                            TamanoArchivo = reader.IsDBNull("TamanoArchivo") ? 0 : reader.GetInt64("TamanoArchivo"),
                            Extension = reader.IsDBNull("Extension") ? null : reader.GetString("Extension"),
                            Estado = (EstadoProyecto)reader.GetInt32("Estado"),
                            Prioridad = (PrioridadProyecto)reader.GetInt32("Prioridad"),
                            Presupuesto = reader.IsDBNull("Presupuesto") ? null : reader.GetDecimal("Presupuesto"),
                            Progreso = reader.GetInt32("Progreso"),
                            Observaciones = reader.IsDBNull("Observaciones") ? null : reader.GetString("Observaciones"),
                            Visualizaciones = reader.GetInt32("Visualizaciones")
                        });
                    }
                }
            }
        }

        return proyectos;
    }

    public async Task<Proyecto?> ObtenerProyectoPorIdAsync(int proyectoId, int empresaId)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                SELECT ProyectoID, NombreProyecto, Descripcion, CodigoProyecto, ArchivoRuta, 
                       FechaCreacion, FechaInicio, FechaFinPrevista, FechaFinReal, CreadoPor, 
                       ResponsableProyecto, EsActivo, EmpresaID, Tags, TamanoArchivo, Extension, 
                       Estado, Prioridad, Presupuesto, Progreso, Observaciones, Visualizaciones, Ubicacion,
                       Cliente, Tipo
                FROM [dbo].[Proyectos] 
                WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyectoId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new Proyecto
                        {
                            ProyectoID = reader.GetInt32("ProyectoID"),
                            NombreProyecto = reader.GetString("NombreProyecto"),
                            Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                            CodigoProyecto = reader.IsDBNull("CodigoProyecto") ? null : reader.GetString("CodigoProyecto"),
                            ArchivoRuta = reader.IsDBNull("ArchivoRuta") ? null : reader.GetString("ArchivoRuta"),
                            FechaCreacion = reader.GetDateTime("FechaCreacion"),
                            FechaInicio = reader.IsDBNull("FechaInicio") ? null : reader.GetDateTime("FechaInicio"),
                            FechaFinPrevista = reader.IsDBNull("FechaFinPrevista") ? null : reader.GetDateTime("FechaFinPrevista"),
                            FechaFinReal = reader.IsDBNull("FechaFinReal") ? null : reader.GetDateTime("FechaFinReal"),
                            CreadoPor = reader.IsDBNull("CreadoPor") ? null : reader.GetString("CreadoPor"),
                            ResponsableProyecto = reader.IsDBNull("ResponsableProyecto") ? null : reader.GetString("ResponsableProyecto"),
                            EsActivo = reader.GetBoolean("EsActivo"),
                            EmpresaID = reader.GetInt32("EmpresaID"),
                            Tags = reader.IsDBNull("Tags") ? null : reader.GetString("Tags"),
                            TamanoArchivo = reader.IsDBNull("TamanoArchivo") ? 0 : reader.GetInt64("TamanoArchivo"),
                            Extension = reader.IsDBNull("Extension") ? null : reader.GetString("Extension"),
                            Estado = (EstadoProyecto)reader.GetInt32("Estado"),
                            Prioridad = (PrioridadProyecto)reader.GetInt32("Prioridad"),
                            Presupuesto = reader.IsDBNull("Presupuesto") ? null : reader.GetDecimal("Presupuesto"),
                            Progreso = reader.GetInt32("Progreso"),
                            Observaciones = reader.IsDBNull("Observaciones") ? null : reader.GetString("Observaciones"),
                            Visualizaciones = reader.GetInt32("Visualizaciones"),
                            Ubicacion = reader.IsDBNull("Ubicacion") ? null :  reader.GetString("Ubicacion"),
                            Cliente = reader.IsDBNull("Cliente") ? null : reader.GetString("Cliente"),
                            Tipo = reader.IsDBNull("Tipo") ? null : reader.GetString("Tipo")
                        };
                    }
                }
            }
        }

        return null;
    }

    public async Task<int> CrearProyectoAsync(Proyecto proyecto)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                INSERT INTO Proyectos (NombreProyecto, Descripcion, CodigoProyecto, ArchivoRuta, 
                                     FechaCreacion, FechaInicio, FechaFinPrevista, CreadoPor, 
                                     ResponsableProyecto, EsActivo, EmpresaID, Tags, TamanoArchivo, 
                                     Extension, Estado, Prioridad, Presupuesto, Progreso, Observaciones, Visualizaciones,Ubicacion,
                       Cliente, Tipo)
                VALUES (@NombreProyecto, @Descripcion, @CodigoProyecto, @ArchivoRuta, @FechaCreacion, 
                        @FechaInicio, @FechaFinPrevista, @CreadoPor, @ResponsableProyecto, @EsActivo, 
                        @EmpresaID, @Tags, @TamanoArchivo, @Extension, @Estado, @Prioridad, 
                        @Presupuesto, @Progreso, @Observaciones, 0 , @Ubicacion , @Cliente , @Tipo);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@NombreProyecto", proyecto.NombreProyecto);
                command.Parameters.AddWithValue("@Descripcion", (object)proyecto.Descripcion ?? DBNull.Value);
                command.Parameters.AddWithValue("@CodigoProyecto", (object)proyecto.CodigoProyecto ?? DBNull.Value);
                command.Parameters.AddWithValue("@ArchivoRuta", (object)proyecto.ArchivoRuta ?? DBNull.Value);
                command.Parameters.AddWithValue("@FechaCreacion", proyecto.FechaCreacion);
                command.Parameters.AddWithValue("@FechaInicio", (object)proyecto.FechaInicio ?? DBNull.Value);
                command.Parameters.AddWithValue("@FechaFinPrevista", (object)proyecto.FechaFinPrevista ?? DBNull.Value);
                command.Parameters.AddWithValue("@CreadoPor", (object)proyecto.CreadoPor ?? DBNull.Value);
                command.Parameters.AddWithValue("@ResponsableProyecto", (object)proyecto.ResponsableProyecto ?? DBNull.Value);
                command.Parameters.AddWithValue("@EsActivo", proyecto.EsActivo);
                command.Parameters.AddWithValue("@EmpresaID", proyecto.EmpresaID);
                command.Parameters.AddWithValue("@Tags", (object)proyecto.Tags ?? DBNull.Value);
                command.Parameters.AddWithValue("@TamanoArchivo", proyecto.TamanoArchivo);
                command.Parameters.AddWithValue("@Extension", (object)proyecto.Extension ?? DBNull.Value);
                command.Parameters.AddWithValue("@Estado", (int)proyecto.Estado);
                command.Parameters.AddWithValue("@Prioridad", (int)proyecto.Prioridad);
                command.Parameters.AddWithValue("@Presupuesto", (object)proyecto.Presupuesto ?? DBNull.Value);
                command.Parameters.AddWithValue("@Progreso", proyecto.Progreso);
                command.Parameters.AddWithValue("@Observaciones", (object)proyecto.Observaciones ?? DBNull.Value);
                command.Parameters.AddWithValue("@Ubicacion", (object)proyecto.Ubicacion ?? DBNull.Value);
                command.Parameters.AddWithValue("@Cliente", (object)proyecto.Cliente ?? DBNull.Value);
                command.Parameters.AddWithValue("@Tipo", (object)proyecto.Tipo ?? DBNull.Value) ;

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }
    }

    public async Task<bool> ActualizarProyectoAsync(Proyecto proyecto)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                UPDATE Proyectos SET 
                    NombreProyecto = @NombreProyecto,
                    Descripcion = @Descripcion,
                    CodigoProyecto = @CodigoProyecto,
                    ArchivoRuta = @ArchivoRuta,
                    FechaInicio = @FechaInicio,
                    FechaFinPrevista = @FechaFinPrevista,
                    FechaFinReal = @FechaFinReal,
                    ResponsableProyecto = @ResponsableProyecto,
                    Tags = @Tags,
                    TamanoArchivo = @TamanoArchivo,
                    Extension = @Extension,
                    Estado = @Estado,
                    Prioridad = @Prioridad,
                    Presupuesto = @Presupuesto,
                    Progreso = @Progreso,
                    Observaciones = @Observaciones,
                    Ubicacion = @Ubicacion,
                    Cliente = @Cliente,
                    Tipo = @Tipo
                WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyecto.ProyectoID);
                command.Parameters.AddWithValue("@EmpresaID", proyecto.EmpresaID);
                command.Parameters.AddWithValue("@NombreProyecto", proyecto.NombreProyecto);
                command.Parameters.AddWithValue("@Descripcion", (object)proyecto.Descripcion ?? DBNull.Value);
                command.Parameters.AddWithValue("@CodigoProyecto", (object)proyecto.CodigoProyecto ?? DBNull.Value);
                command.Parameters.AddWithValue("@ArchivoRuta", (object)proyecto.ArchivoRuta ?? DBNull.Value);
                command.Parameters.AddWithValue("@FechaInicio", (object)proyecto.FechaInicio ?? DBNull.Value);
                command.Parameters.AddWithValue("@FechaFinPrevista", (object)proyecto.FechaFinPrevista ?? DBNull.Value);
                command.Parameters.AddWithValue("@FechaFinReal", (object)proyecto.FechaFinReal ?? DBNull.Value);
                command.Parameters.AddWithValue("@ResponsableProyecto", (object)proyecto.ResponsableProyecto ?? DBNull.Value);
                command.Parameters.AddWithValue("@Tags", (object)proyecto.Tags ?? DBNull.Value);
                command.Parameters.AddWithValue("@TamanoArchivo", proyecto.TamanoArchivo);
                command.Parameters.AddWithValue("@Extension", (object)proyecto.Extension ?? DBNull.Value);
                command.Parameters.AddWithValue("@Estado", (int)proyecto.Estado);
                command.Parameters.AddWithValue("@Prioridad", (int)proyecto.Prioridad);
                command.Parameters.AddWithValue("@Presupuesto", (object)proyecto.Presupuesto ?? DBNull.Value);
                command.Parameters.AddWithValue("@Progreso", proyecto.Progreso);
                command.Parameters.AddWithValue("@Observaciones", (object)proyecto.Observaciones ?? DBNull.Value);
                command.Parameters.AddWithValue("@Ubicacion", (object)proyecto.Ubicacion ?? DBNull.Value);
                command.Parameters.AddWithValue("@Cliente", (object)proyecto.Cliente ?? DBNull.Value);
                command.Parameters.AddWithValue("@Tipo", (object)proyecto.Tipo ?? DBNull.Value);

                int filasAfectadas = await command.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
        }
    }

    public async Task<bool> RegistrarVisualizacionProyectoAsync(int proyectoId)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = "UPDATE Proyectos SET Visualizaciones = Visualizaciones + 1 WHERE ProyectoID = @ProyectoID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyectoId);
                int filasAfectadas = await command.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
        }
    }

    public async Task<bool> ActualizarProgresoAsync(int proyectoId, int progreso, int empresaId)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                UPDATE Proyectos SET 
                    Progreso = @Progreso,
                    Estado = CASE 
                        WHEN @Progreso = 100 THEN 3  -- Completado
                        WHEN @Progreso > 0 THEN 1    -- EnProgreso
                        ELSE Estado 
                    END
                WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyectoId);
                command.Parameters.AddWithValue("@Progreso", Math.Max(0, Math.Min(100, progreso)));
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                int filasAfectadas = await command.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
        }
    }

    public async Task<bool> CambiarEstadoAsync(int proyectoId, EstadoProyecto estado, int empresaId)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                UPDATE Proyectos SET 
                    Estado = @Estado,
                    FechaFinReal = CASE 
                        WHEN @Estado = 3 AND FechaFinReal IS NULL THEN GETDATE() -- Completado
                        WHEN @Estado != 3 THEN NULL 
                        ELSE FechaFinReal 
                    END
                WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyectoId);
                command.Parameters.AddWithValue("@Estado", (int)estado);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                int filasAfectadas = await command.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
        }
    }



    public async Task<bool> EliminarProyectoAsync(int proyectoId, int empresaId)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = "UPDATE Proyectos " +
                "SET EsActivo = 0 WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProyectoID", proyectoId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                int filasAfectadas = await command.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
        }
    }






    public async Task<List<Proyecto>> BuscarProyectosAsync(int empresaId, string termino)
    {
        var proyectos = new List<Proyecto>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = @"
                SELECT ProyectoID, NombreProyecto, Descripcion, CodigoProyecto, ArchivoRuta, 
                       FechaCreacion, FechaInicio, FechaFinPrevista, FechaFinReal, CreadoPor, 
                       ResponsableProyecto, EsActivo, EmpresaID, Tags, TamanoArchivo, Extension, 
                       Estado, Prioridad, Presupuesto, Progreso, Observaciones, Visualizaciones,
                       Ubicacion, Cliente, Tipo
                 FROM [dbo].[Proyectos]
                WHERE EmpresaID = @EmpresaID 
                    AND EsActivo = 1
                    AND (NombreProyecto LIKE @Termino 
                         OR Descripcion LIKE @Termino 
                         OR CodigoProyecto LIKE @Termino 
                         OR Tags LIKE @Termino)
                ORDER BY FechaCreacion DESC";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EmpresaID", empresaId);
                command.Parameters.AddWithValue("@Termino", $"%{termino}%");

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        proyectos.Add(new Proyecto
                        {
                            ProyectoID = reader.GetInt32("ProyectoID"),
                            NombreProyecto = reader.GetString("NombreProyecto"),
                            Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                            CodigoProyecto = reader.IsDBNull("CodigoProyecto") ? null : reader.GetString("CodigoProyecto"),
                            ArchivoRuta = reader.IsDBNull("ArchivoRuta") ? null : reader.GetString("ArchivoRuta"),
                            FechaCreacion = reader.GetDateTime("FechaCreacion"),
                            FechaInicio = reader.IsDBNull("FechaInicio") ? null : reader.GetDateTime("FechaInicio"),
                            FechaFinPrevista = reader.IsDBNull("FechaFinPrevista") ? null : reader.GetDateTime("FechaFinPrevista"),
                            FechaFinReal = reader.IsDBNull("FechaFinReal") ? null : reader.GetDateTime("FechaFinReal"),
                            CreadoPor = reader.IsDBNull("CreadoPor") ? null : reader.GetString("CreadoPor"),
                            ResponsableProyecto = reader.IsDBNull("ResponsableProyecto") ? null : reader.GetString("ResponsableProyecto"),
                            EsActivo = reader.GetBoolean("EsActivo"),
                            EmpresaID = reader.GetInt32("EmpresaID"),
                            Tags = reader.IsDBNull("Tags") ? null : reader.GetString("Tags"),
                            TamanoArchivo = reader.IsDBNull("TamanoArchivo") ? 0 : reader.GetInt64("TamanoArchivo"),
                            Extension = reader.IsDBNull("Extension") ? null : reader.GetString("Extension"),
                            Estado = (EstadoProyecto)reader.GetInt32("Estado"),
                            Prioridad = (PrioridadProyecto)reader.GetInt32("Prioridad"),
                            Presupuesto = reader.IsDBNull("Presupuesto") ? null : reader.GetDecimal("Presupuesto"),
                            Progreso = reader.GetInt32("Progreso"),
                            Observaciones = reader.IsDBNull("Observaciones") ? null : reader.GetString("Observaciones"),
                            Visualizaciones = reader.GetInt32("Visualizaciones"),
                            Ubicacion = reader.IsDBNull("Ubicacion") ? null : reader.GetString("Ubicacion"),
                            Cliente = reader.IsDBNull("Cliente") ? null : reader.GetString ("Cliente"),
                            Tipo = reader.IsDBNull("Tipo") ? null :  reader.GetString ("Tipo")
                        });
                    }
                }
            }
        }

        return proyectos;
    }

   


    public async Task ActualizarRutaArchivoAsync(int proyectoId, string? ruta, string? extension, long? tamano, int empresaId)
    {
        using var con = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand("dbo.Proyecto_ActualizarArchivo", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@ProyectoID", proyectoId);
        cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
        cmd.Parameters.AddWithValue("@ArchivoRuta", (object?)ruta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Extension", (object?)extension ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TamanoArchivo", (object?)tamano ?? DBNull.Value);

        await con.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }


    //eSTE Metodo es para listar las carpetas dentro de un proyecto
    public async Task<List<CarpetaDto>> ListarCarpetasProyectoAsync(int proyectoId)
    {
        var lista = new List<CarpetaDto>();

        using var con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        // Leemos todas las carpetas activas del proyecto
        var sql = @"
        SELECT CarpetaID, CarpetaPadreID, Nombre, RutaRelativa
        FROM dbo.Carpetas
        WHERE ProyectoID = @ProyectoID AND Activa = 1
        ORDER BY RutaRelativa ASC;";

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@ProyectoID", proyectoId);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        while (await rd.ReadAsync())
        {
            var bruta = rd["RutaRelativa"] as string ?? "";
            // Normalizar
            var limpia = bruta.StartsWith("/") ? bruta.Substring(1) : bruta;

            lista.Add(new CarpetaDto
            {
                CarpetaID = (int)rd["CarpetaID"],
                CarpetaPadreID = rd["CarpetaPadreID"] == DBNull.Value ? null : (int?)rd["CarpetaPadreID"],
                Nombre = (string)rd["Nombre"],
                RutaRelativa = limpia, 
                Nivel = string.IsNullOrEmpty(limpia) ? 0 : limpia.Count(c => c == '/')
            });
        }

        return lista;
    }


    //Metodo para obtener la ruta de la carpeta 
    public async Task<string?> ObtenerRutaRelativaCarpetaAsync(int proyectoId, int carpetaId)
    {
        using var con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        var sql = @"
        SELECT RutaRelativa
        FROM dbo.Carpetas
        WHERE ProyectoID = @ProyectoID AND CarpetaID = @CarpetaID AND Activa = 1;";

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@ProyectoID", proyectoId);
        cmd.Parameters.AddWithValue("@CarpetaID", carpetaId);

        var obj = await cmd.ExecuteScalarAsync();
        if (obj == null || obj == DBNull.Value) return null;

        var bruta = (string)obj;
        return bruta.StartsWith("/") ? bruta.Substring(1) : bruta; // normalizada
    }


    //Metodo para guardar borradores para el apartado de cargar proyectos


    public async Task<int> GuardarBorradorAsync(Proyecto modelo, int empresaId, int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (modelo.ProyectoID <= 0)
        {
            // INSERT
            const string sql = @"
INSERT INTO Proyectos
(
    NombreProyecto, Descripcion, CodigoProyecto, ArchivoRuta, TamanoArchivo, Extension,
    FechaCreacion, FechaInicio, FechaFinPrevista, FechaFinReal, CreadoPor, ResponsableProyecto,
    EsActivo, EmpresaID, Tags, Estado, Prioridad, Presupuesto, Progreso, Observaciones,
    Visualizaciones, Ubicacion, Cliente, Tipo
)
VALUES
(
    @NombreProyecto, @Descripcion, @CodigoProyecto, @ArchivoRuta, @TamanoArchivo, @Extension,
    @FechaCreacion, @FechaInicio, @FechaFinPrevista, @FechaFinReal, @CreadoPor, @ResponsableProyecto,
    1, @EmpresaID, @Tags, @Estado, @Prioridad, @Presupuesto, @Progreso, @Observaciones,
    @Visualizaciones, @Ubicacion, @Cliente, @Tipo
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var cmd = new SqlCommand(sql, connection);
            // comunes
            SetCommonProyectoParams(cmd, modelo, empresaId, isInsert: true, usuarioId: usuarioId);
            // obligatorios de insert
            cmd.Parameters.AddWithValue("@FechaCreacion", (object)(modelo.FechaCreacion == default ? DateTime.UtcNow : modelo.FechaCreacion));

            var id = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            return id;
        }
        else
        {
            // UPDATE
            const string sql = @"
UPDATE Proyectos SET
    NombreProyecto      = @NombreProyecto,
    Descripcion         = @Descripcion,
    CodigoProyecto      = @CodigoProyecto,
    ArchivoRuta         = @ArchivoRuta,
    TamanoArchivo       = @TamanoArchivo,
    Extension           = @Extension,
    FechaInicio         = @FechaInicio,
    FechaFinPrevista    = @FechaFinPrevista,
    FechaFinReal        = @FechaFinReal,
    ResponsableProyecto = @ResponsableProyecto,
    Tags                = @Tags,
    Estado              = @Estado,
    Prioridad           = @Prioridad,
    Presupuesto         = @Presupuesto,
    Progreso            = @Progreso,
    Observaciones       = @Observaciones,
    Visualizaciones     = @Visualizaciones,
    Ubicacion           = @Ubicacion,
    Cliente             = @Cliente,
    Tipo                = @Tipo
WHERE ProyectoID = @ProyectoID AND EmpresaID = @EmpresaID;
SELECT @ProyectoID;";

            using var cmd = new SqlCommand(sql, connection);
            // comunes
            SetCommonProyectoParams(cmd, modelo, empresaId, isInsert: false);
            // clave
            cmd.Parameters.AddWithValue("@ProyectoID", modelo.ProyectoID);

            var id = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            return id;
        }
    }


    private static void SetCommonProyectoParams(SqlCommand cmd, Proyecto m, int empresaId, bool isInsert, int? usuarioId = null)
    {
        // requeridos
        cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
        cmd.Parameters.AddWithValue("@NombreProyecto", (object?)m.NombreProyecto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado", (int)m.Estado);

        // opcionales (null-safe)
        cmd.Parameters.AddWithValue("@Descripcion", (object?)m.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CodigoProyecto", (object?)m.CodigoProyecto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArchivoRuta", (object?)m.ArchivoRuta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TamanoArchivo", (object?)m.TamanoArchivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Extension", (object?)m.Extension ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaInicio", (object?)m.FechaInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFinPrevista", (object?)m.FechaFinPrevista ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaFinReal", (object?)m.FechaFinReal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ResponsableProyecto", (object?)m.ResponsableProyecto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tags", (object?)m.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Presupuesto", (object?)m.Presupuesto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Progreso", (object?)m.Progreso ?? 0);
        cmd.Parameters.AddWithValue("@Observaciones", (object?)m.Observaciones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Visualizaciones", (object?)m.Visualizaciones ?? 0);
        cmd.Parameters.AddWithValue("@Ubicacion", (object?)m.Ubicacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Cliente", (object?)m.Cliente ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tipo", (object?)m.Tipo ?? DBNull.Value);
        cmd.Parameters.Add("@Prioridad", SqlDbType.Int).Value = (int)m.Prioridad;


        // CreadoPor solo tiene sentido en INSERT (es nvarchar(150) en tu tabla)
        if (isInsert)
        {
            // Si en el modelo ya traes CreadoPor úsalo; si no, escribe un fallback con el ID
            var creadoPor =  m.CreadoPor;
            cmd.Parameters.AddWithValue("@CreadoPor", (object)creadoPor ?? DBNull.Value);
        }
        else
        {
            // En UPDATE no tocamos FechaCreacion ni CreadoPor
            cmd.Parameters.AddWithValue("@CreadoPor", DBNull.Value);
        }
    }


}