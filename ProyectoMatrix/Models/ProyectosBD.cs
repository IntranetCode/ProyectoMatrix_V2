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

    //Se creó un nuevo metodo para mandar a llamar al procedimiento almacenado de Carpetas

    public async Task<(int CarpetaID, string RutaRelativa)> CrearCarpetaAsync(
        int proyectoId,
        int? carpetaPadreId,
        string nombreCarpeta,
        int? usuarioCreadorId)
    {
        using var conexion = new SqlConnection(_connectionString);
        await conexion.OpenAsync();

        using var comando = new SqlCommand("dbo.sp_CrearCarpeta", conexion);
        comando.CommandType = CommandType.StoredProcedure;

        //Parametros de entradda 
        comando.Parameters.Add("@ProyectoID", SqlDbType.Int).Value = proyectoId;
        comando.Parameters.Add("@CarpetaPadreID", SqlDbType.Int).Value = (object)carpetaPadreId ?? DBNull.Value;
        comando.Parameters.Add("@NombreCarpeta", SqlDbType.NVarChar, 200).Value = nombreCarpeta;
        comando.Parameters.Add("@UsuarioCreadorID", SqlDbType.Int).Value=(object)usuarioCreadorId ?? DBNull.Value;

        //Parametos de salida
        var parametroRuta = comando.Parameters.Add("@RutaRelativaOut", SqlDbType.NVarChar, 1000);
        parametroRuta.Direction = ParameterDirection.Output;

        var parametroId = comando.Parameters.Add("@CarpetaIDOut", SqlDbType.Int);
        parametroId.Direction = ParameterDirection.Output;


        //Ejecutar

        try
        {
            await comando.ExecuteNonQueryAsync();

            int idCarpeta = (int)parametroId.Value;
            string rutaRelativa = parametroRuta.Value?.ToString() ?? string.Empty;

            return (idCarpeta, rutaRelativa);

        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            throw new InvalidOperationException("Ya existe una carpeta creada con ese nombre", ex);
        }

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
 
}