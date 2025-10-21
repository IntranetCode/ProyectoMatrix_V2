// =====================================================
// ARCHIVO: Servicios/UniversidadServices.cs
// PROPÓSITO: Servicios para módulo Universidad NS
// =====================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using System.Data;
using System.Linq;


namespace ProyectoMatrix.Servicios
{
    public class UniversidadServices
    {
        private readonly string _connectionString;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UniversidadServices> _logger;

        // ✅ CONSTRUCTOR CORREGIDO
        public UniversidadServices(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<UniversidadServices> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // =====================================================
        // SERVICIOS DE NIVELES EDUCATIVOS
        // =====================================================

        public async Task<List<NivelEducativo>> GetNivelesEducativosAsync()
        {
            var niveles = new List<NivelEducativo>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT NivelID, NombreNivel, Descripcion, Orden, ColorHex, Activo, FechaCreacion 
                FROM dbo.NivelesEducativos 
                WHERE Activo = 1 
                ORDER BY Orden";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                niveles.Add(new NivelEducativo
                {
                    NivelID = reader.GetInt32("NivelID"),
                    NombreNivel = reader.GetString("NombreNivel"),
                    Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                    Orden = reader.GetInt32("Orden"),
                    ColorHex = reader.IsDBNull("ColorHex") ? null : reader.GetString("ColorHex"),
                    Activo = reader.GetBoolean("Activo"),
                    FechaCreacion = reader.GetDateTime("FechaCreacion")
                });
            }

            return niveles;
        }

        // =====================================================
        // SERVICIOS DE CURSOS
        // =====================================================

        public async Task<List<CursoCompleto>> GetCursosPorNivelAsync(int nivelId, int? empresaId = null)
        {
            var cursos = new List<CursoCompleto>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    c.CursoID, c.NivelID, c.NombreCurso, c.Descripcion, 
                    c.Duracion, c.ImagenCurso, c.Activo, c.FechaCreacion,
                    n.NombreNivel, n.ColorHex,
                    COUNT(sc.SubCursoID) as TotalSubCursos
                FROM dbo.Cursos c
                INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                WHERE c.NivelID = @NivelID AND c.Activo = 1
                GROUP BY c.CursoID, c.NivelID, c.NombreCurso, c.Descripcion, 
                         c.Duracion, c.ImagenCurso, c.Activo, c.FechaCreacion,
                         n.NombreNivel, n.ColorHex
                ORDER BY c.FechaCreacion DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@NivelID", SqlDbType.Int).Value = nivelId;

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                cursos.Add(new CursoCompleto
                {
                    CursoID = reader.GetInt32("CursoID"),
                    NivelID = reader.GetInt32("NivelID"),
                    NombreCurso = reader.GetString("NombreCurso"),
                    Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                    Duracion = reader.IsDBNull("Duracion") ? null : reader.GetInt32("Duracion"),
                    ImagenCurso = reader.IsDBNull("ImagenCurso") ? null : reader.GetString("ImagenCurso"),
                    NombreNivel = reader.GetString("NombreNivel"),
                    ColorNivel = reader.IsDBNull("ColorHex") ? "#3b82f6" : reader.GetString("ColorHex"),
                    TotalSubCursos = reader.GetInt32("TotalSubCursos"),
                    Activo = reader.GetBoolean("Activo"),
                    FechaCreacion = reader.GetDateTime("FechaCreacion")
                });
            }

            return cursos;
        }

        public async Task<int> CrearCursoAsync(CrearCursoRequest request)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO dbo.Cursos (NivelID, NombreCurso, Descripcion, Duracion, ImagenCurso, CreadoPorUsuarioID)
                OUTPUT INSERTED.CursoID
                VALUES (@NivelID, @NombreCurso, @Descripcion, @Duracion, @ImagenCurso, @CreadoPorUsuarioID)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(new[]
            {
                new SqlParameter("@NivelID", request.NivelID),
                new SqlParameter("@NombreCurso", request.NombreCurso),
                new SqlParameter("@Descripcion", (object?)request.Descripcion ?? DBNull.Value),
                new SqlParameter("@Duracion", (object?)request.Duracion ?? DBNull.Value),
                new SqlParameter("@ImagenCurso", (object?)request.ImagenCurso ?? DBNull.Value),
                new SqlParameter("@CreadoPorUsuarioID", request.CreadoPorUsuarioID)
            });

            return (int)await command.ExecuteScalarAsync();
        }

        // =====================================================
        // SERVICIOS DE SUBCURSOS/VIDEOS
        // =====================================================

        public async Task<List<SubCursoDetalle>> GetSubCursosPorCursoAsync(int cursoId, int usuarioId, int empresaId)
        {
            try
            {
                var subCursos = new List<SubCursoDetalle>();

                var query = @"
           SELECT 
    sc.SubCursoID,
    sc.CursoID,
    sc.NombreSubCurso,
    sc.Descripcion,
    sc.Orden,
    sc.ArchivoVideo,
    sc.ArchivoPDF,
    sc.DuracionVideo,
    sc.EsObligatorio,
    sc.RequiereEvaluacion,
    sc.PuntajeMinimo,
    CAST(ISNULL(av.Completado, 0) AS bit) AS Completado,
    ISNULL(av.PorcentajeVisto, 0) AS PorcentajeVisto,
    av.FechaCompletado
FROM dbo.SubCursos sc
INNER JOIN dbo.Cursos c ON c.CursoID = sc.CursoID AND c.Activo = 1   -- 👈 agregar
LEFT JOIN dbo.AvancesSubCursos av
    ON sc.SubCursoID = av.SubCursoID 
    AND av.UsuarioID = @UsuarioId 
    AND av.EmpresaID = @EmpresaId
WHERE sc.CursoID = @CursoID 
  AND sc.Activo = 1
ORDER BY sc.Orden";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@CursoID", cursoId);
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    subCursos.Add(new SubCursoDetalle
                    {
                        SubCursoID = reader.GetInt32(reader.GetOrdinal("SubCursoID")),
                        CursoID = reader.GetInt32(reader.GetOrdinal("CursoID")),
                        NombreSubCurso = reader.GetString(reader.GetOrdinal("NombreSubCurso")),
                        Descripcion = reader.IsDBNull(reader.GetOrdinal("Descripcion"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("Descripcion")),
                        Orden = reader.GetInt32(reader.GetOrdinal("Orden")),
                        ArchivoVideo = reader.IsDBNull(reader.GetOrdinal("ArchivoVideo"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("ArchivoVideo")),
                        ArchivoPDF = reader.IsDBNull(reader.GetOrdinal("ArchivoPDF"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("ArchivoPDF")),
                        DuracionVideo = reader.IsDBNull(reader.GetOrdinal("DuracionVideo"))
                                        ? 0
                                        : reader.GetInt32(reader.GetOrdinal("DuracionVideo")),
                        EsObligatorio = reader.GetBoolean(reader.GetOrdinal("EsObligatorio")),
                        RequiereEvaluacion = reader.GetBoolean(reader.GetOrdinal("RequiereEvaluacion")),
                        PuntajeMinimo = reader.GetDecimal(reader.GetOrdinal("PuntajeMinimo")),

                        TiempoTotalVisto = 0,
                        PorcentajeVisto = reader.IsDBNull(reader.GetOrdinal("PorcentajeVisto"))
                            ? 0
                            : Convert.ToInt32(reader.GetDecimal(reader.GetOrdinal("PorcentajeVisto"))), // ✅ cambio aquí
                        Completado = reader.GetBoolean(reader.GetOrdinal("Completado")),
                        FechaCompletado = reader.IsDBNull(reader.GetOrdinal("FechaCompletado"))
                            ? null
                            : reader.GetDateTime(reader.GetOrdinal("FechaCompletado")),

                        PuedeAcceder = false,
                        UltimoIntento = null
                    });
                }

                // 🔑 Lógica de desbloqueo:
                var siguiente = subCursos
                    .Where(s => !s.Completado)
                    .OrderBy(s => s.Orden)
                    .FirstOrDefault();

                if (siguiente != null)
                    siguiente.PuedeAcceder = true;

                // 🔑 Los ya completados también se pueden seguir revisando
                foreach (var s in subCursos.Where(s => s.Completado))
                    s.PuedeAcceder = true;

                return subCursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcursos del curso {CursoId}", cursoId);
                return new List<SubCursoDetalle>();
            }
        }
        // MANTÉN tu método existente pero AGREGA estas mejoras:

        public async Task<int> CrearSubCursoAsync(CrearSubCursoRequest request)
        {
            try // ✅ AGREGAR try-catch
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO dbo.SubCursos 
                    (CursoID, NombreSubCurso, Descripcion, Orden, ArchivoVideo, ArchivoPDF, 
                     DuracionVideo, EsObligatorio, RequiereEvaluacion, PuntajeMinimo, Activo, FechaCreacion) -- ✅ AGREGAR estas columnas
                    OUTPUT INSERTED.SubCursoID
                    VALUES 
                    (@CursoID, @NombreSubCurso, @Descripcion, @Orden, @ArchivoVideo, @ArchivoPDF, 
                     @DuracionVideo, @EsObligatorio, @RequiereEvaluacion, @PuntajeMinimo, 1, GETDATE())";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddRange(new[]
                {
            new SqlParameter("@CursoID", request.CursoID),
            new SqlParameter("@NombreSubCurso", request.NombreSubCurso),
            new SqlParameter("@Descripcion", (object?)request.Descripcion ?? DBNull.Value),
            new SqlParameter("@Orden", request.Orden),
            new SqlParameter("@ArchivoVideo", (object?)request.ArchivoVideo ?? DBNull.Value),
            new SqlParameter("@ArchivoPDF", (object?)request.ArchivoPDF ?? DBNull.Value),
            new SqlParameter("@DuracionVideo", (object?)request.DuracionVideo ?? DBNull.Value),
            new SqlParameter("@EsObligatorio", request.EsObligatorio),
            new SqlParameter("@RequiereEvaluacion", request.RequiereEvaluacion),
            new SqlParameter("@PuntajeMinimo", request.PuntajeMinimo)
        });

                return (int)await command.ExecuteScalarAsync();
            }
            catch (Exception ex) // ✅ AGREGAR manejo de errores
            {
                Console.WriteLine($"Error en CrearSubCursoAsync: {ex.Message}");
                throw;
            }
        }
        // =====================================================
        // SERVICIOS DE PROGRESO Y AVANCES
        // =====================================================

        public async Task<bool> ActualizarProgresoVideoAsync(ActualizarProgresoRequest request)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                // Verificar si ya existe registro de avance
                var existeQuery = @"
                    SELECT AvanceSubCursoID 
                    FROM dbo.AvancesSubCursos 
                    WHERE UsuarioID = @UsuarioID AND SubCursoID = @SubCursoID AND EmpresaID = @EmpresaID";

                using var existeCommand = new SqlCommand(existeQuery, connection, transaction);
                existeCommand.Parameters.AddRange(new[]
                {
                    new SqlParameter("@UsuarioID", request.UsuarioID),
                    new SqlParameter("@SubCursoID", request.SubCursoID),
                    new SqlParameter("@EmpresaID", request.EmpresaID)
                });

                var avanceId = await existeCommand.ExecuteScalarAsync();

                string query;
                if (avanceId == null)
                {
                    // Crear nuevo registro
                    query = @"
                        INSERT INTO dbo.AvancesSubCursos 
                        (UsuarioID, SubCursoID, EmpresaID, InicioVisualizacion, TiempoTotalVisto, 
                         PorcentajeVisto, UltimaActividad)
                        VALUES 
                        (@UsuarioID, @SubCursoID, @EmpresaID, @InicioVisualizacion, @TiempoTotalVisto, 
                         @PorcentajeVisto, GETDATE())";
                }
                else
                {
                    // Actualizar existente
                    query = @"
                        UPDATE dbo.AvancesSubCursos 
                        SET TiempoTotalVisto = @TiempoTotalVisto,
                            PorcentajeVisto = @PorcentajeVisto,
                            FinVisualizacion = @FinVisualizacion,
                            Completado = @Completado,
                            FechaCompletado = @FechaCompletado,
                            UltimaActividad = GETDATE()
                        WHERE UsuarioID = @UsuarioID AND SubCursoID = @SubCursoID AND EmpresaID = @EmpresaID";
                }

                using var command = new SqlCommand(query, connection, transaction);
                command.Parameters.AddRange(new[]
                {
                    new SqlParameter("@UsuarioID", request.UsuarioID),
                    new SqlParameter("@SubCursoID", request.SubCursoID),
                    new SqlParameter("@EmpresaID", request.EmpresaID),
                    new SqlParameter("@TiempoTotalVisto", request.TiempoTotalVisto),
                    new SqlParameter("@PorcentajeVisto", request.PorcentajeVisto)
                });

                if (avanceId == null)
                {
                    command.Parameters.Add("@InicioVisualizacion", SqlDbType.DateTime).Value = DateTime.Now;
                }
                else
                {
                    command.Parameters.Add("@FinVisualizacion", SqlDbType.DateTime).Value =
                        request.PorcentajeVisto >= 95 ? DateTime.Now : (object)DBNull.Value;
                    command.Parameters.Add("@Completado", SqlDbType.Bit).Value = request.PorcentajeVisto >= 95;
                    command.Parameters.Add("@FechaCompletado", SqlDbType.DateTime).Value =
                        request.PorcentajeVisto >= 95 ? DateTime.Now : (object)DBNull.Value;
                }

                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // =====================================================
        // SERVICIOS DE ASIGNACIONES
        // =====================================================

        public async Task<List<CursoAsignado>> GetCursosAsignadosUsuarioAsync(int usuarioId, int empresaId)
        {
            var cursosAsignados = new List<CursoAsignado>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT DISTINCT
                    c.CursoID, c.NombreCurso, c.Descripcion, c.ImagenCurso,
                    n.NombreNivel, n.ColorHex,
                    ac.FechaAsignacion, ac.FechaLimite, ac.EsObligatorio,
                    
                    -- Progreso calculado
                    COUNT(sc.SubCursoID) as TotalSubCursos,
                    COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) as SubCursosCompletados,
                    CASE 
                        WHEN COUNT(sc.SubCursoID) > 0 
                        THEN CAST(COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) AS DECIMAL(5,2)) / COUNT(sc.SubCursoID) * 100 
                        ELSE 0 
                    END AS PorcentajeProgreso,
                    
                    -- Estado del curso
                    CASE
    WHEN COUNT(sc.SubCursoID) > 0
         AND COUNT(sc.SubCursoID) = COUNT(CASE WHEN asc.Completado = 1 THEN 1 END)
         THEN 'Completado'
    WHEN COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) > 0
         THEN 'En Progreso'
    ELSE 'Pendiente'            -- 👈 antes decía 'Asignado'
END AS Estado

                    
                FROM dbo.AsignacionesCursos ac
                INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                LEFT JOIN dbo.AvancesSubCursos asc ON sc.SubCursoID = asc.SubCursoID 
                    AND asc.UsuarioID = @UsuarioID AND asc.EmpresaID = @EmpresaID
                WHERE ac.Activo = 1
                AND (
                    -- Asignación individual
                    (ac.TipoAsignacionID = 1 AND ac.UsuarioID = @UsuarioID AND ac.EmpresaID = @EmpresaID)
                    OR
                    -- Asignación por empresa
                    (ac.TipoAsignacionID = 3 AND ac.EmpresaID = @EmpresaID 
                     AND EXISTS (SELECT 1 FROM dbo.UsuarioEmpresas ue WHERE ue.UsuarioID = @UsuarioID AND ue.EmpresaID = @EmpresaID AND ue.Activo = 1))
                    OR
                    -- Asignación por departamento
                    (ac.TipoAsignacionID = 2 AND ac.DepartamentoID IN 
                     (SELECT ed.DepartamentoID FROM dbo.EmpleadoDepartamentos ed WHERE ed.UsuarioID = @UsuarioID AND ed.EmpresaID = @EmpresaID AND ed.Activo = 1))
                )
                GROUP BY c.CursoID, c.NombreCurso, c.Descripcion, c.ImagenCurso,
                         n.NombreNivel, n.ColorHex,
                         ac.FechaAsignacion, ac.FechaLimite, ac.EsObligatorio
                ORDER BY ac.FechaAsignacion DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(new[]
            {
                new SqlParameter("@UsuarioID", usuarioId),
                new SqlParameter("@EmpresaID", empresaId)
            });

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                cursosAsignados.Add(new CursoAsignado
                {
                    CursoID = reader.GetInt32("CursoID"),
                    NombreCurso = reader.GetString("NombreCurso"),
                    Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                    ImagenCurso = reader.IsDBNull("ImagenCurso") ? null : reader.GetString("ImagenCurso"),
                    NombreNivel = reader.GetString("NombreNivel"),
                    ColorNivel = reader.IsDBNull("ColorHex") ? "#3b82f6" : reader.GetString("ColorHex"),
                    FechaAsignacion = reader.GetDateTime("FechaAsignacion"),
                    FechaLimite = reader.IsDBNull("FechaLimite") ? null : reader.GetDateTime("FechaLimite"),
                    EsObligatorio = reader.GetBoolean("EsObligatorio"),
                    TotalSubCursos = reader.GetInt32("TotalSubCursos"),
                    SubCursosCompletados = reader.GetInt32("SubCursosCompletados"),
                    PorcentajeProgreso = reader.GetDecimal("PorcentajeProgreso"),
                    Estado = reader.GetString("Estado")
                });
            }

            return cursosAsignados;
        }

        // =====================================================
        // SERVICIOS DE CERTIFICADOS
        // =====================================================

        public async Task<List<CertificadoViewModel>> GetCertificadosUsuarioAsync(int usuarioId, int? empresaId)
        {
            var certificados = new List<CertificadoViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = @"
            SELECT ce.CertificadoID, ce.CursoID, c.NombreCurso, ce.FechaEmision, 
                   ce.CodigoCertificado, ce.ArchivoPDF
            FROM CertificadosEmitidos ce
            INNER JOIN Cursos c ON ce.CursoID = c.CursoID
            WHERE ce.UsuarioID = @UsuarioID AND ce.EmpresaID = @EmpresaID AND ce.Activo = 1";

                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@EmpresaID", (object?)empresaId ?? DBNull.Value);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            certificados.Add(new CertificadoViewModel
                            {
                                CertificadoID = reader.GetInt32(0),
                                CursoID = reader.GetInt32(1),
                                NombreCurso = reader.GetString(2),
                                FechaEmision = reader.GetDateTime(3),
                                CodigoCertificado = reader.GetString(4),
                                ArchivoPDF = reader.IsDBNull(5) ? null : reader.GetString(5)
                            });
                        }
                    }
                }
            }

            return certificados;
        }


        // AGREGAR estos métodos al FINAL de tu UniversidadServices.cs
        // (después del último método GetCertificadosUsuarioAsync)

        // =====================================================
        // MÉTODOS WRAPPER PARA COMPATIBILIDAD CON CONTROLLER
        // =====================================================

        // SI NO TIENES este método en tu UniversidadServices.cs, AGRÉGALO:

        public async Task<List<NivelEducativoViewModel>> GetNivelesEducativosViewModelAsync()
        {
            try
            {
                var niveles = new List<NivelEducativoViewModel>();

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT NivelID, NombreNivel, Descripcion, Orden, ColorHex 
                    FROM dbo.NivelesEducativos 
                    WHERE Activo = 1 
                    ORDER BY Orden";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    niveles.Add(new NivelEducativoViewModel
                    {
                        NivelID = reader.GetInt32("NivelID"),
                        NombreNivel = reader.GetString("NombreNivel"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        ColorHex = reader.IsDBNull("ColorHex") ? "#3b82f6" : reader.GetString("ColorHex"),
                        Orden = reader.GetInt32("Orden")
                    });
                }

                return niveles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en GetNivelesEducativosViewModelAsync: {ex.Message}");
                return new List<NivelEducativoViewModel>
        {
            new NivelEducativoViewModel
            {
                NivelID = 1,
                NombreNivel = "Básico",
                Descripcion = "Nivel básico de capacitación",
                ColorHex = "#3b82f6",
                Orden = 1
            },
            new NivelEducativoViewModel
            {
                NivelID = 2,
                NombreNivel = "Intermedio",
                Descripcion = "Nivel intermedio de capacitación",
                ColorHex = "#f59e0b",
                Orden = 2
            },
            new NivelEducativoViewModel
            {
                NivelID = 3,
                NombreNivel = "Avanzado",
                Descripcion = "Nivel avanzado de capacitación",
                ColorHex = "#ef4444",
                Orden = 3
            }
        };
            }
        }
        public async Task<List<CursoAsignadoViewModel>> GetCursosAsignadosUsuarioViewModelAsync(int usuarioId, int empresaId)
        {
            var cursos = new List<CursoAsignadoViewModel>();

            var query = @"
                SELECT 
                    ac.AsignacionID,
                    ac.CursoID,
                    c.NombreCurso,
                    c.Descripcion,
                    c.Duracion as DuracionHoras,
                    c.ImagenCurso,
                    n.NombreNivel,
                    n.ColorHex as ColorNivel,
                    ac.FechaAsignacion,
                    ac.FechaLimite,
                    ac.EsObligatorio,
                    ac.Comentarios as Observaciones,

                    -- Total subcursos
                    (SELECT COUNT(*) 
                     FROM SubCursos sc 
                     WHERE sc.CursoID = ac.CursoID AND sc.Activo = 1) as TotalSubCursos,

                    -- Subcursos completados por el usuario
                    (SELECT COUNT(*) 
                     FROM AvancesSubCursos av 
                     INNER JOIN SubCursos sc ON av.SubCursoID = sc.SubCursoID
                     WHERE av.UsuarioID = ac.UsuarioID 
                       AND av.EmpresaID = ac.EmpresaID
                       AND sc.CursoID = ac.CursoID
                       AND av.Completado = 1) as SubCursosCompletados
                FROM dbo.AsignacionesCursos ac
                INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                WHERE ac.UsuarioID = @UsuarioID 
                  AND ac.EmpresaID = @EmpresaID 
                  AND ac.Activo = 1
                  AND c.Activo = 1
                ORDER BY ac.FechaAsignacion DESC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);
            command.Parameters.AddWithValue("@EmpresaID", empresaId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var totalSubCursos = reader.GetInt32(reader.GetOrdinal("TotalSubCursos"));
                var completados = reader.GetInt32(reader.GetOrdinal("SubCursosCompletados"));

                var porcentaje = totalSubCursos > 0
                    ? (decimal)completados * 100 / totalSubCursos
                    : 0;

                var estado = "Pendiente";
                if (completados == totalSubCursos && totalSubCursos > 0)
                    estado = "Completado";
                else if (completados > 0)
                    estado = "En Progreso";

                cursos.Add(new CursoAsignadoViewModel
                {
                    CursoID = reader.GetInt32(reader.GetOrdinal("CursoID")),
                    NombreCurso = reader.GetString(reader.GetOrdinal("NombreCurso")),
                    Descripcion = reader.IsDBNull(reader.GetOrdinal("Descripcion")) ? "" : reader.GetString(reader.GetOrdinal("Descripcion")),
                    NombreNivel = reader.GetString(reader.GetOrdinal("NombreNivel")),
                    ColorNivel = reader.GetString(reader.GetOrdinal("ColorNivel")),
                    TotalSubCursos = totalSubCursos,
                    SubCursosCompletados = completados,
                    PorcentajeProgreso = (int)Math.Round(porcentaje, 0),
                    Estado = estado,
                    EstadoClass = estado switch
                    {
                        "Completado" => "badge bg-success",
                        "En Progreso" => "badge bg-warning",
                        _ => "badge bg-secondary"
                    }
                });
            }

            return cursos;
        }


        public async Task<List<CertificadoUsuarioViewModel>> GetCertificadosUsuarioViewModelAsync(int usuarioId, int? empresaId = null)
        {
            try
            {
                var certificadosEntity = await GetCertificadosUsuarioAsync(usuarioId, empresaId);

                // Convertir de CertificadoEmitido a CertificadoUsuarioViewModel
                return certificadosEntity.Select(cert => new CertificadoUsuarioViewModel
                {
                    CertificadoID = cert.CertificadoID,
                    NombreCurso = cert.NombreCurso,
                    CodigoCertificado = cert.CodigoCertificado,
                    FechaEmision = cert.FechaEmision,
                    Estado = cert.Estado,
                    TieneArchivo = !string.IsNullOrEmpty(cert.ArchivoPDF),
                    ArchivoPDF = cert.ArchivoPDF
                }).ToList();
            }
            catch (Exception ex)
            {
                // Log error y retornar datos de prueba
                Console.WriteLine($"Error en GetCertificadosUsuarioAsync: {ex.Message}");
                return new List<CertificadoUsuarioViewModel>
        {
            new CertificadoUsuarioViewModel
            {
                CertificadoID = 1,
                NombreCurso = "Inducción Corporativa",
                CodigoCertificado = "NS-IND-001",
                FechaEmision = DateTime.Now.AddDays(-20),
                Estado = "Vigente",
                TieneArchivo = false
            }
        };
            }
        }

        public async Task<List<SubCursoDetalle>> GetSubCursosPorCursoViewModelAsync(int cursoId, int usuarioId, int empresaId)
        {
            try
            {
                var subCursosEntity = await GetSubCursosPorCursoAsync(cursoId, usuarioId, empresaId);

                // Convertir de SubCursoDetalle a SubCursoDetalleViewModel
                return subCursosEntity.Select(sc => new SubCursoDetalle
                {
                    SubCursoID = sc.SubCursoID,
                    CursoID = sc.CursoID,
                    NombreSubCurso = sc.NombreSubCurso,
                    Descripcion = sc.Descripcion,
                    Orden = sc.Orden,
                    PuedeAcceder = sc.PuedeAcceder,
                    Completado = sc.Completado,
                    PorcentajeVisto = (int)sc.PorcentajeVisto
                }).ToList();
            }
            catch (Exception ex)
            {
                // Log error y retornar lista vacía
                Console.WriteLine($"Error en GetSubCursosPorCursoAsync: {ex.Message}");
                return new List<SubCursoDetalle>();
            }
        }

        public async Task<CursoCompleto?> GetCursoPorIdAsync(int cursoId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        c.CursoID, c.NivelID, c.NombreCurso, c.Descripcion, 
                        c.Duracion, c.ImagenCurso, c.Activo, c.FechaCreacion,
                        n.NombreNivel, n.ColorHex,
                        COUNT(sc.SubCursoID) as TotalSubCursos
                    FROM dbo.Cursos c
                    INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                    LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                    WHERE c.CursoID = @CursoID
                    GROUP BY c.CursoID, c.NivelID, c.NombreCurso, c.Descripcion, 
                             c.Duracion, c.ImagenCurso, c.Activo, c.FechaCreacion,
                             n.NombreNivel, n.ColorHex";

                using var command = new SqlCommand(query, connection);
                command.Parameters.Add("@CursoID", SqlDbType.Int).Value = cursoId;

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new CursoCompleto
                    {
                        CursoID = reader.GetInt32("CursoID"),
                        NivelID = reader.GetInt32("NivelID"),
                        NombreCurso = reader.GetString("NombreCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        Duracion = reader.IsDBNull("Duracion") ? null : reader.GetInt32("Duracion"),
                        ImagenCurso = reader.IsDBNull("ImagenCurso") ? null : reader.GetString("ImagenCurso"),
                        NombreNivel = reader.GetString("NombreNivel"),
                        ColorNivel = reader.IsDBNull("ColorHex") ? "#3b82f6" : reader.GetString("ColorHex"),
                        TotalSubCursos = reader.GetInt32("TotalSubCursos"),
                        Activo = reader.GetBoolean("Activo"),
                        FechaCreacion = reader.GetDateTime("FechaCreacion")
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en GetCursoPorIdAsync: {ex.Message}");
                return null;
            }
        }

        // =====================================================
        // MÉTODO MEJORADO PARA CREAR SUBCURSO
        // =====================================================
        public async Task<EstadisticasUniversidadViewModel> GetEstadisticasAdministrativasAsync()
        {
            try
            {
                var estadisticas = new EstadisticasUniversidadViewModel();

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // ✅ CORRECTO: dbo.Usuarios
                    using (var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Usuarios WHERE Activo = 1", connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        estadisticas.TotalUsuariosActivos = Convert.ToInt32(result ?? 0);
                    }

                    // ✅ CORRECTO: dbo.Cursos
                    using (var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Cursos WHERE Activo = 1", connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        estadisticas.TotalCursosCreados = Convert.ToInt32(result ?? 0);
                    }

                    // ✅ CORRECTO: dbo.CertificadosEmitidos
                    var inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    using (var command = new SqlCommand("SELECT COUNT(*) FROM dbo.CertificadosEmitidos WHERE FechaEmision >= @FechaInicio", connection))
                    {
                        command.Parameters.AddWithValue("@FechaInicio", inicioMes);
                        var result = await command.ExecuteScalarAsync();
                        estadisticas.CertificadosEmitidosMes = Convert.ToInt32(result ?? 0);
                    }
                }

                return estadisticas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas administrativas");
                return new EstadisticasUniversidadViewModel();
            }
        }

        public async Task<List<CursoCompleto>> GetTodosCursosAsync()
        {
            try
            {
                var cursos = new List<CursoCompleto>();

                var query = @"
                    SELECT 
                        c.CursoID,
                        c.NivelID,
                        c.NombreCurso,
                        c.Descripcion,
                        c.Duracion,
                        c.ImagenCurso,
                        c.Activo,
                        c.FechaCreacion,
                        ne.NombreNivel,
                        ISNULL(ne.ColorHex, '#3b82f6') as ColorNivel,
                        (SELECT COUNT(*) FROM dbo.SubCursos sc WHERE sc.CursoID = c.CursoID) as TotalSubCursos
                    FROM dbo.Cursos c
                    INNER JOIN dbo.NivelesEducativos ne ON c.NivelID = ne.NivelID
                    WHERE c.Activo = 1
                    ORDER BY c.FechaCreacion DESC";

                _logger.LogInformation("🔍 Ejecutando consulta de cursos...");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            int contador = 0;
                            while (await reader.ReadAsync())
                            {
                                contador++;
                                var cursoId = reader.GetInt32("CursoID");
                                var nombreCurso = reader.GetString("NombreCurso");

                                _logger.LogInformation("📖 Leyendo curso #{Contador}: ID={CursoId}, Nombre={Nombre}",
                                    contador, cursoId, nombreCurso);

                                cursos.Add(new CursoCompleto
                                {
                                    CursoID = cursoId,
                                    NivelID = reader.GetInt32("NivelID"),
                                    NombreCurso = nombreCurso,
                                    Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                                    Duracion = reader.IsDBNull("Duracion") ? null : reader.GetInt32("Duracion"),
                                    ImagenCurso = reader.IsDBNull("ImagenCurso") ? null : reader.GetString("ImagenCurso"),
                                    Activo = reader.GetBoolean("Activo"),
                                    FechaCreacion = reader.GetDateTime("FechaCreacion"),
                                    NombreNivel = reader.GetString("NombreNivel"),
                                    ColorNivel = reader.GetString("ColorNivel"),
                                    TotalSubCursos = reader.GetInt32("TotalSubCursos")
                                });
                            }

                            _logger.LogInformation("✅ Total cursos leídos: {Total}", cursos.Count);
                        }
                    }
                }

                return cursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los cursos");
                return new List<CursoCompleto>();
            }
        }

        public async Task<EvaluacionViewModel?> GetEvaluacionViewModelAsync(int subCursoId)
        {
            try
            {
                _logger.LogInformation("🔍 DEBUG GetEvaluacionViewModelAsync - SubCursoId: {SubCursoId}", subCursoId);

                var query = @"
                SELECT 
                    sc.SubCursoID,
                    sc.NombreSubCurso,
                    c.NombreCurso
                FROM dbo.SubCursos sc
                INNER JOIN dbo.Cursos c ON sc.CursoID = c.CursoID
                WHERE sc.SubCursoID = @SubCursoId AND sc.Activo = 1";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("🔍 Conexión abierta, ejecutando consulta...");

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                _logger.LogInformation("🔍 SubCurso encontrado en BD");

                                var viewModel = new EvaluacionViewModel
                                {
                                    SubCursoID = reader.GetInt32("SubCursoID"),
                                    NombreSubCurso = reader.GetString("NombreSubCurso"),
                                    NombreCurso = reader.GetString("NombreCurso"),
                                    Preguntas = new List<PreguntaEvaluacion>(), // TEMPORAL: Sin cargar preguntas
                                    PuedeEditarEvaluacion = true
                                };

                                _logger.LogInformation("🔍 ViewModel creado exitosamente - Nombre: {Nombre}", viewModel.NombreSubCurso);
                                return viewModel;
                            }
                            else
                            {
                                _logger.LogWarning("🔍 SubCurso NO encontrado en BD con ID: {SubCursoId}", subCursoId);
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔍 ERROR en GetEvaluacionViewModelAsync para SubCurso {SubCursoId}", subCursoId);
                return null;
            }
        }

        public async Task<TomarEvaluacionViewModel?> GetTomarEvaluacionViewModelAsync(int subCursoId, int usuarioId, int empresaId)
        {
            try
            {
                var query = @"
                SELECT 
                    sc.SubCursoID,
                    sc.NombreSubCurso,
                    sc.PuntajeMinimo,
                    c.NombreCurso
                FROM dbo.SubCursos sc
                INNER JOIN dbo.Cursos c ON sc.CursoID = c.CursoID
                WHERE sc.SubCursoID = @SubCursoId AND sc.Activo = 1 AND sc.RequiereEvaluacion = 1";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var viewModel = new TomarEvaluacionViewModel
                                {
                                    SubCursoID = reader.GetInt32("SubCursoID"),
                                    NombreSubCurso = reader.GetString("NombreSubCurso"),
                                    NombreCurso = reader.GetString("NombreCurso"),
                                    PuntajeMinimoAprobacion = reader.GetDecimal("PuntajeMinimo"),
                                    TiempoLimiteMinutos = 30 // Valor por defecto
                                };

                                reader.Close();

                                // Obtener preguntas
                                viewModel.Preguntas = await GetPreguntasEvaluacionAsync(subCursoId, connection);

                                // Obtener último intento
                                viewModel.UltimoIntento = await GetUltimoIntentoAsync(usuarioId, subCursoId, empresaId, connection);

                                // Calcular número de intento
                                viewModel.NumeroIntento = await GetSiguienteNumeroIntentoAsync(usuarioId, subCursoId, empresaId, connection);

                                return viewModel;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener evaluación para tomar");
                return null;
            }
        }

        public async Task<bool> CrearEvaluacionAsync(CrearEvaluacionRequest request)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Eliminar preguntas existentes
                            await EliminarPreguntasExistentesAsync(request.SubCursoID, connection, transaction);

                            // Crear nuevas preguntas
                            foreach (var pregunta in request.Preguntas)
                            {
                                var preguntaId = await CrearPreguntaAsync(request.SubCursoID, pregunta, connection, transaction);

                                // Crear opciones
                                foreach (var opcion in pregunta.Opciones)
                                {
                                    await CrearOpcionAsync(preguntaId, opcion, connection, transaction);
                                }
                            }

                            transaction.Commit();
                            return true;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear evaluación");
                return false;
            }
        }

        private async Task<List<PreguntaEvaluacion>> GetPreguntasEvaluacionAsync(int subCursoId, SqlConnection connection)
        {
            var preguntas = new List<PreguntaEvaluacion>();

            var query = @"
            SELECT PreguntaID, TextoPregunta, TipoPregunta, Orden, PuntajeMaximo
            FROM dbo.PreguntasEvaluacion
            WHERE SubCursoID = @SubCursoId AND Activo = 1
            ORDER BY Orden";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var pregunta = new PreguntaEvaluacion
                        {
                            PreguntaID = reader.GetInt32("PreguntaID"),
                            SubCursoID = subCursoId,
                            TextoPregunta = reader.GetString("TextoPregunta"),
                            TipoPregunta = reader.GetString("TipoPregunta"),
                            Orden = reader.GetInt32("Orden"),
                            PuntajeMaximo = reader.GetDecimal("PuntajeMaximo")
                        };

                        preguntas.Add(pregunta);
                    }
                }
            }

            // Obtener opciones para cada pregunta
            foreach (var pregunta in preguntas)
            {
                pregunta.Opciones = await GetOpcionesPreguntaAsync(pregunta.PreguntaID, connection);
            }

            return preguntas;
        }

        private async Task<List<OpcionRespuesta>> GetOpcionesPreguntaAsync(int preguntaId, SqlConnection connection)
        {
            var opciones = new List<OpcionRespuesta>();

            var query = @"
                SELECT OpcionID, TextoOpcion, EsCorrecta, Orden
                FROM dbo.OpcionesRespuesta
                WHERE PreguntaID = @PreguntaId AND Activo = 1
                ORDER BY Orden";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@PreguntaId", preguntaId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        opciones.Add(new OpcionRespuesta
                        {
                            OpcionID = reader.GetInt32("OpcionID"),
                            PreguntaID = preguntaId,
                            TextoOpcion = reader.GetString("TextoOpcion"),
                            EsCorrecta = reader.GetBoolean("EsCorrecta"),
                            Orden = reader.GetInt32("Orden")
                        });
                    }
                }
            }

            return opciones;
        }

        private async Task<IntentoEvaluacion?> GetUltimoIntentoAsync(int usuarioId, int subCursoId, int empresaId, SqlConnection connection)
        {
            var query = @"
                SELECT TOP 1 IntentoID, NumeroIntento, FechaInicio, FechaFin, 
                       PuntajeObtenido, PuntajeMaximo, Aprobado, TiempoEmpleado
                FROM dbo.IntentosEvaluacion
                WHERE UsuarioID = @UsuarioId AND SubCursoID = @SubCursoId AND EmpresaID = @EmpresaId
                ORDER BY FechaInicio DESC";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new IntentoEvaluacion
                        {
                            IntentoID = reader.GetInt32("IntentoID"),
                            UsuarioID = usuarioId,
                            SubCursoID = subCursoId,
                            EmpresaID = empresaId,
                            NumeroIntento = reader.GetInt32("NumeroIntento"),
                            FechaInicio = reader.GetDateTime("FechaInicio"),
                            FechaFin = reader.IsDBNull("FechaFin") ? null : reader.GetDateTime("FechaFin"),
                            PuntajeObtenido = reader.IsDBNull("PuntajeObtenido") ? null : reader.GetDecimal("PuntajeObtenido"),
                            PuntajeMaximo = reader.IsDBNull("PuntajeMaximo") ? null : reader.GetDecimal("PuntajeMaximo"),
                            Aprobado = reader.GetBoolean("Aprobado"),
                            TiempoEmpleado = reader.IsDBNull("TiempoEmpleado") ? null : reader.GetInt32("TiempoEmpleado")
                        };
                    }
                }
            }

            return null;
        }

        // Versión con transaction (para EntregarEvaluacionAsync)
        private async Task<int> GetSiguienteNumeroIntentoAsync(
            int usuarioId, int subCursoId, int empresaId,
            SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                SELECT ISNULL(MAX(NumeroIntento), 0) + 1
                FROM dbo.IntentosEvaluacion
                WHERE UsuarioID = @UsuarioId AND SubCursoID = @SubCursoId AND EmpresaID = @EmpresaId";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }

        // Versión sin transaction (para GetTomarEvaluacionViewModelAsync)
        private async Task<int> GetSiguienteNumeroIntentoAsync(
            int usuarioId, int subCursoId, int empresaId,
            SqlConnection connection)
        {
            var query = @"
        SELECT ISNULL(MAX(NumeroIntento), 0) + 1
        FROM dbo.IntentosEvaluacion
        WHERE UsuarioID = @UsuarioId AND SubCursoID = @SubCursoId AND EmpresaID = @EmpresaId";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }


        private async Task EliminarPreguntasExistentesAsync(int subCursoId, SqlConnection connection, SqlTransaction transaction)
        {
            // Primero eliminar opciones
            var deleteOpciones = @"
                DELETE op FROM dbo.OpcionesRespuesta op
                INNER JOIN dbo.PreguntasEvaluacion pe ON op.PreguntaID = pe.PreguntaID
                WHERE pe.SubCursoID = @SubCursoId";

            using (var command = new SqlCommand(deleteOpciones, connection, transaction))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                await command.ExecuteNonQueryAsync();
            }

            // Luego eliminar preguntas
            var deletePreguntas = "DELETE FROM dbo.PreguntasEvaluacion WHERE SubCursoID = @SubCursoId";

            using (var command = new SqlCommand(deletePreguntas, connection, transaction))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task<int> CrearPreguntaAsync(int subCursoId, CrearPreguntaRequest pregunta, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                INSERT INTO dbo.PreguntasEvaluacion (SubCursoID, TextoPregunta, TipoPregunta, Orden, PuntajeMaximo)
                VALUES (@SubCursoId, @TextoPregunta, @TipoPregunta, @Orden, @PuntajeMaximo);
                SELECT SCOPE_IDENTITY();";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@TextoPregunta", pregunta.TextoPregunta);
                command.Parameters.AddWithValue("@TipoPregunta", pregunta.TipoPregunta);
                command.Parameters.AddWithValue("@Orden", 1); // Simplificado por ahora
                command.Parameters.AddWithValue("@PuntajeMaximo", pregunta.PuntajeMaximo);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }

        private async Task CrearOpcionAsync(int preguntaId, CrearOpcionRequest opcion, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                INSERT INTO dbo.OpcionesRespuesta (PreguntaID, TextoOpcion, EsCorrecta, Orden)
                VALUES (@PreguntaId, @TextoOpcion, @EsCorrecta, @Orden)";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@PreguntaId", preguntaId);
                command.Parameters.AddWithValue("@TextoOpcion", opcion.TextoOpcion);
                command.Parameters.AddWithValue("@EsCorrecta", opcion.EsCorrecta);
                command.Parameters.AddWithValue("@Orden", 1); // Simplificado por ahora

                await command.ExecuteNonQueryAsync();
            }
        }

        // =====================================================
        // ENTREGAR EVALUACIÓN
        // =====================================================

        public async Task<ResultadoEvaluacionDto> EntregarEvaluacionAsync(
            int usuarioId, int subCursoId, int empresaId,
            Dictionary<int, RespuestaDto> respuestas, int tiempoEmpleado)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Obtener siguiente número de intento ✅
                            var numeroIntento = await GetSiguienteNumeroIntentoAsync(
                                usuarioId, subCursoId, empresaId, connection, transaction);

                            // 2. Crear intento de evaluación ✅
                            var intentoId = await CrearIntentoEvaluacionAsync(
                                usuarioId, subCursoId, empresaId, numeroIntento, tiempoEmpleado, connection, transaction);

                            // 3. Procesar respuestas y calcular puntaje ✅
                            var (puntajeObtenido, puntajeMaximo) = await ProcesarRespuestasAsync(
                                intentoId, subCursoId, respuestas, connection, transaction);

                            // 4. Obtener puntaje mínimo para aprobar ✅
                            var puntajeMinimoDecimal = await GetPuntajeMinimoAsync(
                                subCursoId, connection, transaction);

                            // 5. Calcular porcentaje y si aprobó ✅
                            var porcentaje = puntajeMaximo > 0 ? (puntajeObtenido / puntajeMaximo) * 100 : 0;
                            var aprobado = porcentaje >= puntajeMinimoDecimal;

                            // 6. Actualizar intento con resultados finales ✅
                            await ActualizarResultadoIntentoAsync(
                                intentoId, puntajeObtenido, puntajeMaximo, aprobado, connection, transaction);

                            // 7. Si aprobó, marcar subcurso como completado (incluye lógica de certificados)
                            if (aprobado)
                            {
                                try
                                {
                                    // MarcarSubCursoCompletadoAsync ya maneja toda la lógica de certificados
                                    // incluyendo verificación de curso completo y generación automática
                                    await MarcarSubCursoCompletadoAsync(
                                        usuarioId, subCursoId, empresaId, connection, transaction);
                                }
                                catch (Exception certEx)
                                {
                                    // 🚨 Ojo: no tiramos la transacción completa si falla el certificado
                                    _logger.LogError(certEx,
                                        "Error al marcar subcurso completado en EntregarEvaluacionAsync. Usuario={UsuarioId}, SubCurso={SubCursoId}",
                                        usuarioId, subCursoId);
                                }
                            }

                            await transaction.CommitAsync();

                            return new ResultadoEvaluacionDto
                            {
                                Success = true,
                                Calificacion = Math.Round(porcentaje, 1),
                                Aprobado = aprobado,
                                Message = "Evaluación procesada correctamente"
                            };
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                await transaction.RollbackAsync();
                            }
                            catch (InvalidOperationException)
                            {
                                // La transacción ya fue rollback automáticamente
                                _logger.LogWarning("La transacción ya había sido terminada automáticamente");
                            }

                            _logger.LogError(ex, "Error en EntregarEvaluacionAsync. Usuario={UsuarioId}, SubCurso={SubCursoId}",
                                usuarioId, subCursoId);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar evaluación");
                return new ResultadoEvaluacionDto
                {
                    Success = false,
                    Message = "Error al procesar la evaluación"
                };
            }
        }



        // =====================================================
        // MÉTODOS AUXILIARES PRIVADOS
        // =====================================================

        private async Task<int> CrearIntentoEvaluacionAsync(
            int usuarioId, int subCursoId, int empresaId, int numeroIntento, int tiempoEmpleado,
            SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                INSERT INTO dbo.IntentosEvaluacion 
                (UsuarioID, SubCursoID, EmpresaID, NumeroIntento, FechaInicio, TiempoEmpleado)
                VALUES (@UsuarioId, @SubCursoId, @EmpresaId, @NumeroIntento, GETDATE(), @TiempoEmpleado);
                SELECT SCOPE_IDENTITY();";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);
                command.Parameters.AddWithValue("@NumeroIntento", numeroIntento);
                command.Parameters.AddWithValue("@TiempoEmpleado", tiempoEmpleado);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
        }

        private async Task<(decimal puntajeObtenido, decimal puntajeMaximo)> ProcesarRespuestasAsync(
            int intentoId,
            int subCursoId,
            Dictionary<int, RespuestaDto> respuestas,   // 👈 ahora con int en la llave
            SqlConnection connection,
            SqlTransaction transaction)
        {
            decimal puntajeObtenido = 0;
            decimal puntajeMaximo = 0;

            // Obtener todas las preguntas del subcurso
            var preguntas = await GetPreguntasParaCalificarAsync(subCursoId, connection, transaction);

            foreach (var pregunta in preguntas)
            {
                puntajeMaximo += pregunta.PuntajeMaximo;

                if (respuestas.ContainsKey(pregunta.PreguntaID))   // 👈 ya no ToString()
                {
                    var respuesta = respuestas[pregunta.PreguntaID];
                    decimal puntajePregunta = 0;
                    bool esCorrecta = false;

                    if (respuesta.Tipo == "opcion" && respuesta.OpcionId.HasValue)
                    {
                        // Verificar si la opción seleccionada es correcta
                        esCorrecta = await VerificarOpcionCorrectaAsync(respuesta.OpcionId.Value, connection, transaction);
                        if (esCorrecta)
                        {
                            puntajePregunta = pregunta.PuntajeMaximo;
                            puntajeObtenido += puntajePregunta;
                        }

                        // Guardar respuesta de opción
                        await GuardarRespuestaOpcionAsync(
                            intentoId,
                            pregunta.PreguntaID,
                            respuesta.OpcionId.Value,
                            esCorrecta,
                            puntajePregunta,
                            connection,
                            transaction
                        );
                    }
                    else if (respuesta.Tipo == "abierta" && !string.IsNullOrEmpty(respuesta.Texto))
                    {
                        // Para preguntas abiertas, dar puntaje completo si respondió algo
                        puntajePregunta = pregunta.PuntajeMaximo;
                        puntajeObtenido += puntajePregunta;
                        esCorrecta = true;

                        // Guardar respuesta abierta
                        await GuardarRespuestaAbiertaAsync(
                            intentoId,
                            pregunta.PreguntaID,
                            respuesta.Texto,
                            esCorrecta,
                            puntajePregunta,
                            connection,
                            transaction
                        );
                    }
                }
            }

            return (puntajeObtenido, puntajeMaximo);
        }


        private async Task<List<(int PreguntaID, decimal PuntajeMaximo)>> GetPreguntasParaCalificarAsync(
            int subCursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var preguntas = new List<(int, decimal)>();

            var query = "SELECT PreguntaID, PuntajeMaximo FROM dbo.PreguntasEvaluacion WHERE SubCursoID = @SubCursoId AND Activo = 1";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        preguntas.Add((reader.GetInt32("PreguntaID"), reader.GetDecimal("PuntajeMaximo")));
                    }
                }
            }

            return preguntas;
        }

        // 🔸 Con Transaction
        private async Task<bool> VerificarOpcionCorrectaAsync(
            int opcionId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = "SELECT EsCorrecta FROM dbo.OpcionesRespuesta WHERE OpcionID = @OpcionId";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@OpcionId", opcionId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToBoolean(result);
            }
        }

        // 🔸 Sin Transaction
        private async Task<bool> VerificarOpcionCorrectaAsync(
            int opcionId, SqlConnection connection)
        {
            var query = "SELECT EsCorrecta FROM dbo.OpcionesRespuesta WHERE OpcionID = @OpcionId";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@OpcionId", opcionId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToBoolean(result);
            }
        }

        // 🔸 Con Transaction
        private async Task<decimal> GetPuntajeMinimoAsync(
            int subCursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = "SELECT PuntajeMinimo FROM dbo.SubCursos WHERE SubCursoID = @SubCursoId";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 70); // 70 por defecto si es NULL
            }
        }

        // 🔸 Sin Transaction
        private async Task<decimal> GetPuntajeMinimoAsync(
            int subCursoId, SqlConnection connection)
        {
            var query = "SELECT PuntajeMinimo FROM dbo.SubCursos WHERE SubCursoID = @SubCursoId";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 70);
            }
        }


        private async Task GuardarRespuestaOpcionAsync(int intentoId, int preguntaId, int opcionId,
            bool esCorrecta, decimal puntaje, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                INSERT INTO dbo.RespuestasEvaluacion 
                (IntentoID, PreguntaID, OpcionSeleccionadaID, EsCorrecta, PuntajeObtenido)
                VALUES (@IntentoId, @PreguntaId, @OpcionId, @EsCorrecta, @Puntaje)";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@IntentoId", intentoId);
                command.Parameters.AddWithValue("@PreguntaId", preguntaId);
                command.Parameters.AddWithValue("@OpcionId", opcionId);
                command.Parameters.AddWithValue("@EsCorrecta", esCorrecta);
                command.Parameters.AddWithValue("@Puntaje", puntaje);

                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task GuardarRespuestaAbiertaAsync(int intentoId, int preguntaId, string texto,
            bool esCorrecta, decimal puntaje, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                INSERT INTO dbo.RespuestasEvaluacion 
                (IntentoID, PreguntaID, TextoRespuesta, EsCorrecta, PuntajeObtenido)
                VALUES (@IntentoId, @PreguntaId, @Texto, @EsCorrecta, @Puntaje)";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@IntentoId", intentoId);
                command.Parameters.AddWithValue("@PreguntaId", preguntaId);
                command.Parameters.AddWithValue("@Texto", texto);
                command.Parameters.AddWithValue("@EsCorrecta", esCorrecta);
                command.Parameters.AddWithValue("@Puntaje", puntaje);

                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task ActualizarResultadoIntentoAsync(int intentoId, decimal puntajeObtenido,
            decimal puntajeMaximo, bool aprobado, SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                UPDATE dbo.IntentosEvaluacion 
                SET FechaFin = GETDATE(), 
                    PuntajeObtenido = @PuntajeObtenido,
                    PuntajeMaximo = @PuntajeMaximo,
                    Aprobado = @Aprobado
                WHERE IntentoID = @IntentoId";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@IntentoId", intentoId);
                command.Parameters.AddWithValue("@PuntajeObtenido", puntajeObtenido);
                command.Parameters.AddWithValue("@PuntajeMaximo", puntajeMaximo);
                command.Parameters.AddWithValue("@Aprobado", aprobado);

                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task MarcarSubCursoCompletadoAsync(
            int usuarioId, int subCursoId, int empresaId,
            SqlConnection connection, SqlTransaction transaction)
        {
            // 1️⃣ Marcar subcurso como completado
            var query = @"
    UPDATE dbo.AvancesSubCursos 
    SET Completado = 1, 
        FechaCompletado = GETDATE(), 
        PorcentajeVisto = 100
    WHERE UsuarioID = @UsuarioId AND SubCursoID = @SubCursoId AND EmpresaID = @EmpresaId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.AvancesSubCursos
        (UsuarioID, SubCursoID, EmpresaID, Completado, FechaCompletado, PorcentajeVisto)
        VALUES (@UsuarioId, @SubCursoId, @EmpresaId, 1, GETDATE(), 100)
    END;";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                await command.ExecuteNonQueryAsync();
            }

            // 2️⃣ Obtener el curso al que pertenece este subcurso
            int cursoId;
            using (var cmd = new SqlCommand("SELECT CursoID FROM SubCursos WHERE SubCursoID = @SubCursoId", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@SubCursoId", subCursoId);
                cursoId = (int)await cmd.ExecuteScalarAsync();
            }

            // 3️⃣ Contar subcursos totales del curso
            int totalSubcursos;
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM SubCursos WHERE CursoID = @CursoId", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@CursoId", cursoId);
                totalSubcursos = (int)await cmd.ExecuteScalarAsync();
            }

            // 4️⃣ Contar subcursos aprobados
            int subcursosAprobados;
            var aprobadosQuery = @"
        SELECT COUNT(DISTINCT ie.SubCursoID)
        FROM IntentosEvaluacion ie
        INNER JOIN SubCursos s ON ie.SubCursoID = s.SubCursoID
        WHERE ie.UsuarioID = @UsuarioId AND ie.Aprobado = 1 AND s.CursoID = @CursoId";
            using (var cmd = new SqlCommand(aprobadosQuery, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@UsuarioId", usuarioId);
                cmd.Parameters.AddWithValue("@CursoId", cursoId);
                subcursosAprobados = (int)await cmd.ExecuteScalarAsync();
            }

            // 5️⃣ Si aprobó todo → generar certificado
            if (subcursosAprobados == totalSubcursos)
            {
                var codigoCertificado = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();

                // Buscar plantilla activa
                int plantillaId = 1;
                using (var cmd = new SqlCommand(@"
            SELECT TOP 1 PlantillaID
            FROM PlantillasCertificados
            WHERE EsPorDefecto = 1 AND Activo = 1
            ORDER BY FechaCreacion DESC;", connection, transaction))
                {
                    var val = await cmd.ExecuteScalarAsync();
                    if (val != null && val != DBNull.Value)
                        plantillaId = Convert.ToInt32(val);
                }

                // Evitar duplicados
                int existe;
                using (var cmd = new SqlCommand(@"
            SELECT COUNT(*) 
            FROM CertificadosEmitidos 
            WHERE UsuarioID=@UsuarioID AND CursoID=@CursoID AND EmpresaID=@EmpresaID AND Activo=1", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    cmd.Parameters.AddWithValue("@CursoID", cursoId);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                    existe = (int)await cmd.ExecuteScalarAsync();
                }

                if (existe == 0)
                {
                    // 🔥 Generar PDF físico
                    var nombreArchivo = $"Certificado_{codigoCertificado}.pdf";
                    var rutaArchivo = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/certificados", nombreArchivo);

                    // Crear carpeta si no existe
                    var carpeta = Path.GetDirectoryName(rutaArchivo);
                    if (!Directory.Exists(carpeta))
                        Directory.CreateDirectory(carpeta);

                    // Obtener datos usuario (JOIN con Persona) y curso
                    string nombreUsuario = "USUARIO";
                    string nombreCurso = "CURSO";

                    using (var cmdUsr = new SqlCommand(@"
                SELECT p.Nombre + ' ' + p.ApellidoPaterno + ' ' + p.ApellidoMaterno
                FROM Usuarios u
                INNER JOIN Persona p ON u.PersonaID = p.PersonaID
                WHERE u.UsuarioID = @UsuarioID", connection, transaction))
                    {
                        cmdUsr.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        var val = await cmdUsr.ExecuteScalarAsync();
                        if (val != null) nombreUsuario = val.ToString();
                    }

                    using (var cmdCurso = new SqlCommand("SELECT NombreCurso FROM Cursos WHERE CursoID=@CursoID", connection, transaction))
                    {
                        cmdCurso.Parameters.AddWithValue("@CursoID", cursoId);
                        var val = await cmdCurso.ExecuteScalarAsync();
                        if (val != null) nombreCurso = val.ToString();
                    }

                    // Logo
                    var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Imagenes/logo.png");
                    var logo = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : new byte[0];

                    var certificadoPdf = new CertificadoDocument(nombreUsuario, nombreCurso, DateTime.Now, logo);
                    certificadoPdf.GeneratePdf(rutaArchivo);

                    // 7️⃣ Insertar en BD con ArchivoPDF
                    var insertCertificado = @"
                INSERT INTO CertificadosEmitidos
                    (UsuarioID, CursoID, FechaEmision, PlantillaID, CodigoCertificado, EmpresaID, Activo, ArchivoPDF)
                VALUES
                    (@UsuarioID, @CursoID, GETDATE(), @PlantillaID, @CodigoCertificado, @EmpresaID, 1, @ArchivoPDF);";

                    using (var cmdInsert = new SqlCommand(insertCertificado, connection, transaction))
                    {
                        cmdInsert.Parameters.AddWithValue("@UsuarioID", usuarioId);
                        cmdInsert.Parameters.AddWithValue("@CursoID", cursoId);
                        cmdInsert.Parameters.AddWithValue("@PlantillaID", plantillaId);
                        cmdInsert.Parameters.AddWithValue("@CodigoCertificado", codigoCertificado);
                        cmdInsert.Parameters.AddWithValue("@EmpresaID", empresaId);
                        cmdInsert.Parameters.AddWithValue("@ArchivoPDF", nombreArchivo);

                        await cmdInsert.ExecuteNonQueryAsync();
                    }

                    _logger.LogInformation("✅ Certificado generado en PDF. Usuario={UsuarioId}, Curso={CursoId}, Archivo={Archivo}",
                        usuarioId, cursoId, nombreArchivo);
                }
            }
        }




        public async Task<CertificadoEmitido?> GetCertificadoAsync(int certificadoId, int usuarioId)
        {
            CertificadoEmitido? cert = null;
            string query = @"
        SELECT CertificadoID, CodigoCertificado, ArchivoPDF
        FROM CertificadosEmitidos
        WHERE CertificadoID = @CertificadoID AND UsuarioID = @UsuarioID";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@CertificadoID", certificadoId);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            cert = new CertificadoEmitido
                            {
                                CertificadoID = reader.GetInt32(0),
                                CodigoCertificado = reader.GetString(1),
                                ArchivoPDF = reader.IsDBNull(2) ? null : reader.GetString(2)
                            };
                        }
                    }
                }
            }

            return cert;
        }





        // =====================================================
        // MÉTODOS PARA EDITAR SUBCURSO
        // =====================================================

        public async Task<SubCursoDetalle?> GetSubCursoPorIdAsync(int subCursoId)
        {
            try
            {
                var query = @"
                    SELECT 
                        SubCursoID, CursoID, NombreSubCurso, Descripcion, Orden,
                        ArchivoVideo, ArchivoPDF, DuracionVideo, EsObligatorio,
                        RequiereEvaluacion, PuntajeMinimo
                    FROM dbo.SubCursos 
                    WHERE SubCursoID = @SubCursoId AND Activo = 1";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new SubCursoDetalle
                                {
                                    SubCursoID = reader.GetInt32("SubCursoID"),
                                    CursoID = reader.GetInt32("CursoID"),
                                    NombreSubCurso = reader.GetString("NombreSubCurso"),
                                    Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                                    Orden = reader.GetInt32("Orden"),
                                    ArchivoVideo = reader.IsDBNull("ArchivoVideo") ? null : reader.GetString("ArchivoVideo"),
                                    ArchivoPDF = reader.IsDBNull("ArchivoPDF") ? null : reader.GetString("ArchivoPDF"),
                                    DuracionVideo = reader.IsDBNull("DuracionVideo") ? null : reader.GetInt32("DuracionVideo"),
                                    EsObligatorio = reader.GetBoolean("EsObligatorio"),
                                    RequiereEvaluacion = reader.GetBoolean("RequiereEvaluacion"),
                                    PuntajeMinimo = reader.GetDecimal("PuntajeMinimo")
                                };
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcurso {SubCursoId}", subCursoId);
                return null;
            }
        }

        public async Task<string> GetNombreCursoPorSubCursoAsync(int subCursoId)
        {
            try
            {
                var query = @"
                    SELECT c.NombreCurso
                    FROM dbo.Cursos c
                    INNER JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID
                    WHERE sc.SubCursoID = @SubCursoId";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                        var result = await command.ExecuteScalarAsync();
                        return result?.ToString() ?? "Curso";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener nombre de curso");
                return "Curso";
            }
        }

        public async Task<bool> ActualizarSubCursoAsync(int subCursoId, CrearSubCursoRequest request)
        {
            try
            {
                Console.WriteLine($"DEBUG: Iniciando actualización SubCurso {subCursoId}");
                Console.WriteLine($"DEBUG: ArchivoVideo a actualizar: {request.ArchivoVideo ?? "NULL"}");
                Console.WriteLine($"DEBUG: ArchivoPDF a actualizar: {request.ArchivoPDF ?? "NULL"}");
                var query = @"
                    UPDATE dbo.SubCursos 
                    SET NombreSubCurso = @NombreSubCurso,
                        Descripcion = @Descripcion,
                        Orden = @Orden,
                        DuracionVideo = @DuracionVideo,
                        EsObligatorio = @EsObligatorio,
                        RequiereEvaluacion = @RequiereEvaluacion,
                        PuntajeMinimo = @PuntajeMinimo,
                        ArchivoVideo = @ArchivoVideo,
                        ArchivoPDF = @ArchivoPDF  
                    WHERE SubCursoID = @SubCursoId";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                        command.Parameters.AddWithValue("@NombreSubCurso", request.NombreSubCurso);
                        command.Parameters.AddWithValue("@Descripcion", request.Descripcion ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Orden", request.Orden);
                        command.Parameters.AddWithValue("@DuracionVideo", request.DuracionVideo ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@EsObligatorio", request.EsObligatorio);
                        command.Parameters.AddWithValue("@RequiereEvaluacion", request.RequiereEvaluacion);
                        command.Parameters.AddWithValue("@PuntajeMinimo", request.PuntajeMinimo);
                        command.Parameters.AddWithValue("@ArchivoVideo", request.ArchivoVideo ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ArchivoPDF", request.ArchivoPDF ?? (object)DBNull.Value);

                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        Console.WriteLine($"DEBUG: Filas afectadas: {rowsAffected}");
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR en ActualizarSubCursoAsync: {ex.Message}");
                _logger.LogError(ex, "Error al actualizar subcurso");
                return false;
            }
        }


        ////////////////////////////////////////MODIFIACION A APARTIR DE AQUI
        public async Task<List<CursoSimpleViewModel>> GetCursosActivosParaAsignacionAsync()
        {
            try
            {
                var cursos = new List<CursoSimpleViewModel>();

                var query = @"
                    SELECT c.CursoID, c.NombreCurso, c.Descripcion, ne.NombreNivel
                    FROM dbo.Cursos c
                    INNER JOIN dbo.NivelesEducativos ne ON c.NivelID = ne.NivelID
                    WHERE c.Activo = 1
                    ORDER BY ne.Orden, c.NombreCurso";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    cursos.Add(new CursoSimpleViewModel
                    {
                        Id = reader.GetInt32("CursoID"),
                        NombreCurso = reader.GetString("NombreCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        NombreNivel = reader.GetString("NombreNivel")
                    });
                }

                return cursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cursos activos para asignación");
                return new List<CursoSimpleViewModel>();
            }
        }

        public async Task<List<EmpresaViewModel>> GetEmpresasActivasAsync()
        {
            try
            {
                var empresas = new List<EmpresaViewModel>();

                var query = @"
                    SELECT EmpresaID, Nombre
                    FROM dbo.Empresas 
                    WHERE Activa = 1 
                    ORDER BY Nombre";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    empresas.Add(new EmpresaViewModel
                    {
                        Id = reader.GetInt32("EmpresaID"),
                        NombreEmpresa = reader.GetString("Nombre")
                    });
                }

                return empresas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener empresas activas");
                return new List<EmpresaViewModel>();
            }
        }

        public async Task<List<DepartamentoViewModel>> GetDepartamentosPorEmpresaAsync(int idEmpresa)
        {
            try
            {
                var departamentos = new List<DepartamentoViewModel>();

                var query = @"
                    SELECT DISTINCT d.Id, d.NombreDepartamento
                    FROM dbo.Departamentos d
                    INNER JOIN dbo.EmpleadoDepartamentos ed ON d.Id = ed.IdDepartamento
                    INNER JOIN dbo.Usuarios u ON ed.IdEmpleado = u.Id
                    INNER JOIN dbo.UsuariosEmpresas ue ON u.Id = ue.IdUsuario
                    WHERE ue.IdEmpresa = @IdEmpresa AND u.Activo = 1 AND d.Activo = 1
                    ORDER BY d.NombreDepartamento";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    departamentos.Add(new DepartamentoViewModel
                    {
                        Id = reader.GetInt32("Id"),
                        NombreDepartamento = reader.GetString("NombreDepartamento"),
                        IdEmpresa = idEmpresa
                    });
                }

                return departamentos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener departamentos por empresa");
                return new List<DepartamentoViewModel>();
            }
        }

        public async Task<List<UsuarioAsignacionViewModel>> GetTodosLosUsuariosActivosAsync(int? CursoID = null)
        {
            try
            {
                var usuarios = new List<UsuarioAsignacionViewModel>();

                var query = @"
                    SELECT DISTINCT 
                        u.Id,
                        u.NombreCompleto,
                        u.Email,
                        e.Nombre AS NombreEmpresa,
                        d.NombreDepartamento,
                        CASE 
                            WHEN EXISTS (
                                SELECT 1 FROM dbo.AsignacionesCursos ac 
                                WHERE ac.IdUsuario = u.Id AND ac.CursoID = @CursoID AND ac.Activo = 1
                            ) THEN 1 ELSE 0 
                        END as YaTieneCurso
                    FROM dbo.Usuarios u
                    INNER JOIN dbo.UsuariosEmpresas ue ON u.Id = ue.IdUsuario
                    INNER JOIN dbo.Empresas e ON ue.IdEmpresa = e.Id
                    LEFT JOIN dbo.EmpleadoDepartamentos ed ON u.Id = ed.IdEmpleado
                    LEFT JOIN dbo.Departamentos d ON ed.IdDepartamento = d.Id
                    WHERE u.Activo = 1 AND ue.Activo = 1
                    ORDER BY e.Nombre, d.NombreDepartamento, u.NombreCompleto";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@CursoID", CursoID ?? (object)DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    usuarios.Add(new UsuarioAsignacionViewModel
                    {
                        Id = reader.GetInt32("Id"),
                        NombreCompleto = reader.GetString("NombreCompleto"),
                        Email = reader.IsDBNull("Email") ? "" : reader.GetString("Email"),
                        NombreEmpresa = reader.GetString("NombreEmpresa"),
                        NombreDepartamento = reader.IsDBNull("NombreDepartamento") ? "Sin departamento" : reader.GetString("NombreDepartamento"),
                        YaTieneCurso = reader.GetBoolean("YaTieneCurso")
                    });
                }

                return usuarios;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los usuarios activos");
                return new List<UsuarioAsignacionViewModel>();
            }
        }

        public async Task<List<UsuarioAsignacionViewModel>> GetUsuariosPorEmpresaAsync(int idEmpresa, int? CursoID = null)
        {
            try
            {
                var usuarios = new List<UsuarioAsignacionViewModel>();
                /*
                var query = @"
                    SELECT DISTINCT 
                        u.UsuarioID as Id,
                        CONCAT(p.Nombre, ' ', p.ApellidoPaterno, ' ', p.ApellidoMaterno) AS NombreCompleto, 
                        p.Correo AS Email,
                        e.Nombre AS NombreEmpresa,
                        d.NombreDepartamento,
                        CASE 
                            WHEN EXISTS (
                                SELECT 1 FROM dbo.AsignacionesCursos ac 
                                WHERE ac.IdUsuario = u.UsuarioID AND ac.IdCurso = @IdCurso AND ac.Activo = 1
                            ) THEN 1 ELSE 0 
                        END as YaTieneCurso
                    FROM dbo.Usuarios u
                    INNER JOIN dbo.UsuariosEmpresas ue ON u.UsuarioID = ue.UsuarioID
                    INNER JOIN dbo.Empresas e ON ue.EmpresaID = e.EmpresaID
                    INNER JOIN dbo.Persona p ON u.PersonaID = p.PersonaID
                    LEFT JOIN dbo.EmpleadoDepartamentos ed ON u.PersonaID = ed.IdEmpleado
                    LEFT JOIN dbo.Departamentos d ON ed.IdDepartamento = d.DepartamentoID
                    WHERE u.Activo = 1 AND ue.Activo = 1 AND ue.IdEmpresa = @IdEmpresa
                    ORDER BY e.Nombre, d.NombreDepartamento, NombreCompleto;";
                */
                var query = @"
                    SELECT DISTINCT 
                        u.UsuarioID as Id,
                        CONCAT(p.Nombre, ' ', p.ApellidoPaterno, ' ', p.ApellidoMaterno) AS NombreCompleto, 
                        p.Correo AS Email,
                        e.Nombre AS NombreEmpresa,
                        'Sin departamento' AS NombreDepartamento,  -- placeholder
                        CAST(
                            CASE 
                                WHEN EXISTS (
                                    SELECT 1 
                                    FROM dbo.AsignacionesCursos ac 
                                    WHERE ac.UsuarioID = u.UsuarioID 
                                      AND ac.CursoID = @CursoID                                      AND ac.Activo = 1
                                ) 
                                THEN 1 ELSE 0 
                            END 
                        AS BIT) as YaTieneCurso
                    FROM dbo.Usuarios u
                    INNER JOIN dbo.UsuariosEmpresas ue ON u.UsuarioID = ue.UsuarioID
                    INNER JOIN dbo.Empresas e ON ue.EmpresaID = e.EmpresaID
                    INNER JOIN dbo.Persona p ON u.PersonaID = p.PersonaID
                    WHERE u.Activo = 1 
                      AND ue.Activo = 1 
                      AND ue.EmpresaID = @IdEmpresa
                    ORDER BY e.Nombre, NombreCompleto;";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                command.Parameters.AddWithValue("@CursoID", CursoID ?? (object)DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    usuarios.Add(new UsuarioAsignacionViewModel
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        NombreCompleto = reader.GetString(reader.GetOrdinal("NombreCompleto")),
                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? "" : reader.GetString(reader.GetOrdinal("Email")),
                        NombreEmpresa = reader.GetString(reader.GetOrdinal("NombreEmpresa")),
                        NombreDepartamento = reader.IsDBNull(reader.GetOrdinal("NombreDepartamento")) ? "Sin departamento" : reader.GetString(reader.GetOrdinal("NombreDepartamento")),
                        YaTieneCurso = reader.GetBoolean(reader.GetOrdinal("YaTieneCurso"))
                    });
                }

                return usuarios;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener usuarios por empresa");
                return new List<UsuarioAsignacionViewModel>();
            }
        }

        public async Task<List<UsuarioAsignacionViewModel>> GetUsuariosPorDepartamentoAsync(int idDepartamento, int? CursoID = null)
        {
            try
            {
                var usuarios = new List<UsuarioAsignacionViewModel>();

                var query = @"
                    SELECT DISTINCT 
                        u.Id,
                        u.NombreCompleto,
                        u.Email,
                        e.Nombre AS NombreEmpresa,
                        d.NombreDepartamento,
                        CASE 
                            WHEN EXISTS (
                                SELECT 1 FROM dbo.AsignacionesCursos ac 
                                WHERE ac.IdUsuario = u.Id AND ac.CursoID = @CursoID AND ac.Activo = 1
                            ) THEN 1 ELSE 0 
                        END as YaTieneCurso
                    FROM dbo.Usuarios u
                    INNER JOIN dbo.EmpleadoDepartamentos ed ON u.Id = ed.IdEmpleado
                    INNER JOIN dbo.Departamentos d ON ed.IdDepartamento = d.Id
                    INNER JOIN dbo.UsuariosEmpresas ue ON u.Id = ue.IdUsuario
                    INNER JOIN dbo.Empresas e ON ue.IdEmpresa = e.EmpresaID
                    WHERE u.Activo = 1 AND d.Id = @IdDepartamento
                    ORDER BY u.NombreCompleto";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdDepartamento", idDepartamento);
                command.Parameters.AddWithValue("@CursoID", CursoID ?? (object)DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    usuarios.Add(new UsuarioAsignacionViewModel
                    {
                        Id = reader.GetInt32("Id"),
                        NombreCompleto = reader.GetString("NombreCompleto"),
                        Email = reader.IsDBNull("Email") ? "" : reader.GetString("Email"),
                        NombreEmpresa = reader.GetString("NombreEmpresa"),
                        NombreDepartamento = reader.GetString("NombreDepartamento"),
                        YaTieneCurso = reader.GetBoolean("YaTieneCurso")
                    });
                }

                return usuarios;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener usuarios por departamento");
                return new List<UsuarioAsignacionViewModel>();
            }
        }

        public async Task<ResultadoAsignacionMasiva> AsignarCursoMasivoAsync(
            int CursoID,
            List<int> usuariosSeleccionados,
            int usuarioCreador,
            DateTime? fechaLimite,
            string? observaciones)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();

                try
                {
                    int usuariosAsignados = 0;
                    int usuariosOmitidos = 0;

                    foreach (var usuarioId in usuariosSeleccionados)
                    {
                        // Verificar si ya tiene el curso asignado
                        var yaAsignado = await VerificarCursoYaAsignadoAsync(usuarioId, CursoID, connection, transaction);

                        if (!yaAsignado)
                        {
                            await AsignarCursoIndividualAsync(
                                usuarioId, CursoID, usuarioCreador, fechaLimite, observaciones,
                                connection, transaction);
                            usuariosAsignados++;
                        }
                        else
                        {
                            usuariosOmitidos++;
                        }
                    }

                    await transaction.CommitAsync();

                    return new ResultadoAsignacionMasiva
                    {
                        Exito = true,
                        Mensaje = "Asignación completada exitosamente",
                        UsuariosAsignados = usuariosAsignados,
                        UsuariosOmitidos = usuariosOmitidos
                    };
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en asignación masiva");
                return new ResultadoAsignacionMasiva
                {
                    Exito = false,
                    Mensaje = $"Error al asignar curso: {ex.Message}",
                    UsuariosAsignados = 0,
                    UsuariosOmitidos = 0
                };
            }
        }

        private async Task<bool> VerificarCursoYaAsignadoAsync(int usuarioId, int cursoId,
            SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
                SELECT COUNT(*) 
                FROM dbo.AsignacionesCursos 
                WHERE UsuarioID = @UsuarioId AND CursoID = @CursoId AND Activo = 1";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
            command.Parameters.AddWithValue("@CursoId", cursoId);

            var count = (int)await command.ExecuteScalarAsync();
            return count > 0;
        }

        private async Task AsignarCursoIndividualAsync(int usuarioId, int cursoId, int usuarioCreador,
            DateTime? fechaLimite, string? observaciones,
            SqlConnection connection, SqlTransaction transaction)
        {
            // 1. Obtener empresa del usuario destino
            var queryEmpresa = @"
                SELECT TOP 1 EmpresaID 
                FROM dbo.UsuariosEmpresas 
                WHERE UsuarioID = @UsuarioId AND Activo = 1";



            int empresaId;
            using (var cmdEmpresa = new SqlCommand(queryEmpresa, connection, transaction))
            {
                cmdEmpresa.Parameters.AddWithValue("@UsuarioId", usuarioId);
                var result = await cmdEmpresa.ExecuteScalarAsync();

                if (result == null || result == DBNull.Value)
                    throw new Exception($"El usuario {usuarioId} no tiene empresa asignada en UsuariosEmpresas.");

                empresaId = (int)result;
            }

            // 2. Insertar la asignación con esa empresa
            var query = @"
                INSERT INTO dbo.AsignacionesCursos 
                (UsuarioID, CursoID, AsignadoPorUsuarioID, EmpresaID, TipoAsignacionID, 
                 FechaAsignacion, FechaLimite, EsObligatorio, Comentarios, Activo)
                VALUES 
                (@UsuarioId, @CursoId, @UsuarioCreador, @EmpresaId, 1, 
                 GETDATE(), @FechaLimite, 1, @Observaciones, 1)";
            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
            command.Parameters.AddWithValue("@CursoId", cursoId);
            command.Parameters.AddWithValue("@UsuarioCreador", usuarioCreador);
            command.Parameters.AddWithValue("@EmpresaId", empresaId);
            command.Parameters.AddWithValue("@FechaLimite", fechaLimite ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Observaciones", observaciones ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        public async Task<List<AsignacionRecienteViewModel>> GetAsignacionesRecientesAsync()
        {
            try
            {
                var asignaciones = new List<AsignacionRecienteViewModel>();

                var query = @"
                    SELECT 
                        c.NombreCurso,
                        COUNT(ac.UsuarioID) as CantidadUsuarios,
                        MAX(ac.FechaAsignacion) as FechaAsignacion,
                        ac.FechaLimite,
                        u.Username as AsignadoPor,  -- o puedes unir con Persona para nombre completo
                        e.Nombre AS NombreEmpresa
                    FROM dbo.AsignacionesCursos ac
                    INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                    INNER JOIN dbo.Usuarios u ON ac.AsignadoPorUsuarioID = u.UsuarioID
                    INNER JOIN dbo.Empresas e ON ac.EmpresaID = e.EmpresaID
                    WHERE ac.FechaAsignacion >= DATEADD(day, -30, GETDATE()) AND ac.Activo = 1
                    GROUP BY c.NombreCurso, ac.FechaLimite, u.Username, e.Nombre, ac.CursoID
                    ORDER BY MAX(ac.FechaAsignacion) DESC";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    asignaciones.Add(new AsignacionRecienteViewModel
                    {
                        NombreCurso = reader.GetString("NombreCurso"),
                        CantidadUsuarios = reader.GetInt32("CantidadUsuarios"),
                        FechaAsignacion = reader.GetDateTime("FechaAsignacion"),
                        FechaLimite = reader.IsDBNull("FechaLimite") ? null : reader.GetDateTime("FechaLimite"),
                        AsignadoPor = reader.GetString("AsignadoPor"),
                        NombreEmpresa = reader.GetString("NombreEmpresa")
                    });
                }

                return asignaciones;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener asignaciones recientes");
                return new List<AsignacionRecienteViewModel>();
            }
        }


        // =====================================================
        // AGREGAR ESTOS MÉTODOS AL FINAL DE UniversidadServices.cs
        // =====================================================

        /// <summary>
        /// Obtiene los cursos asignados a un usuario específico con información detallada
        /// </summary>
        public async Task<List<MiCursoViewModel>> ObtenerMisCursosAsync(int usuarioId, int empresaId)
        {
            try
            {
                var misCursos = new List<MiCursoViewModel>();

                var query = @"
                    SELECT DISTINCT
                        ac.AsignacionID,  -- CAMBIO: AsignacionCursoID → AsignacionID
                        ac.CursoID,
                        c.NombreCurso as TituloCurso,
                        c.Descripcion,
                        c.Duracion as DuracionHoras,
                        c.ImagenCurso,
                        n.NombreNivel,
                        n.ColorHex as ColorNivel,
                        ac.FechaAsignacion,
                        ac.FechaLimite,
                        ac.EsObligatorio,
                        ac.Comentarios as Observaciones,
                
                        -- Cálculo de progreso
                        COUNT(sc.SubCursoID) as TotalSubCursos,
                        COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) as SubCursosCompletados,  -- CAMBIO: asc → avs
                        CASE 
                            WHEN COUNT(sc.SubCursoID) > 0 
                            THEN CAST(COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) AS DECIMAL(5,2)) / COUNT(sc.SubCursoID) * 100 
                            ELSE 0 
                        END AS Progreso,
                
                        -- Estado del curso
                        CASE
                            WHEN COUNT(sc.SubCursoID) = COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) 
                                 AND COUNT(sc.SubCursoID) > 0
                            THEN 'Completado'
                            WHEN COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) > 0 
                            THEN 'En Progreso'
                            WHEN ac.FechaLimite IS NOT NULL AND ac.FechaLimite < GETDATE()
                            THEN 'Vencido'
                            ELSE 'Pendiente'
                        END AS Estado,
                
                        -- Fechas de inicio y finalización
                        MIN(avs.InicioVisualizacion) as FechaInicio,  -- CAMBIO: asc → avs
                        MAX(CASE WHEN avs.Completado = 1 THEN avs.FechaCompletado END) as FechaFinalizacion,
                
                        -- Verificar si está vencido
                        CASE 
                            WHEN ac.FechaLimite IS NOT NULL AND ac.FechaLimite < GETDATE() 
                                 AND NOT (COUNT(sc.SubCursoID) = COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) AND COUNT(sc.SubCursoID) > 0)
                            THEN 1
                            ELSE 0
                        END as EstaVencido
                
                    FROM dbo.AsignacionesCursos ac
                    INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                    INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                    LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                    LEFT JOIN dbo.AvancesSubCursos avs ON sc.SubCursoID = avs.SubCursoID   -- CAMBIO: asc → avs
                        AND avs.UsuarioID = @UsuarioID AND avs.EmpresaID = @EmpresaID
                    WHERE ac.UsuarioID = @UsuarioID 
                        AND ac.EmpresaID = @EmpresaID 
                        AND ac.Activo = 1
                        AND c.Activo = 1
                    GROUP BY 
                        ac.AsignacionID, ac.CursoID, c.NombreCurso, c.Descripcion,   -- CAMBIO: AsignacionCursoID → AsignacionID
                        c.Duracion, c.ImagenCurso, n.NombreNivel, n.ColorHex,
                        ac.FechaAsignacion, ac.FechaLimite, ac.EsObligatorio, ac.Comentarios
                    ORDER BY 
                        CASE 
                            WHEN ac.FechaLimite IS NOT NULL AND ac.FechaLimite < GETDATE() THEN 1
                            ELSE 2
                        END,
                        ac.FechaAsignacion DESC";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    misCursos.Add(new MiCursoViewModel
                    {
                        AsignacionCursoID = reader.GetInt32("AsignacionID"),  // CAMBIO: usar AsignacionID
                        CursoID = reader.GetInt32("CursoID"),
                        TituloCurso = reader.GetString("TituloCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        DuracionHoras = reader.IsDBNull("DuracionHoras") ? null : reader.GetInt32("DuracionHoras"),
                        ImagenCurso = reader.IsDBNull("ImagenCurso") ? null : reader.GetString("ImagenCurso"),
                        NombreNivel = reader.GetString("NombreNivel"),
                        ColorNivel = reader.IsDBNull("ColorNivel") ? "#3b82f6" : reader.GetString("ColorNivel"),
                        FechaAsignacion = reader.GetDateTime("FechaAsignacion"),
                        FechaLimite = reader.IsDBNull("FechaLimite") ? null : reader.GetDateTime("FechaLimite"),
                        FechaInicio = reader.IsDBNull("FechaInicio") ? null : reader.GetDateTime("FechaInicio"),
                        FechaFinalizacion = reader.IsDBNull("FechaFinalizacion") ? null : reader.GetDateTime("FechaFinalizacion"),
                        Progreso = reader.GetDecimal("Progreso"),
                        Estado = reader.GetString("Estado"),
                        EsObligatorio = reader.GetBoolean("EsObligatorio"),
                        Observaciones = reader.IsDBNull("Observaciones") ? null : reader.GetString("Observaciones"),
                        TotalSubCursos = reader.GetInt32("TotalSubCursos"),
                        SubCursosCompletados = reader.GetInt32("SubCursosCompletados"),
                        EstaVencido = reader.GetBoolean("EstaVencido")
                    });
                }

                _logger.LogInformation("Obtenidos {Count} cursos para usuario {UsuarioId}", misCursos.Count, usuarioId);
                return misCursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener mis cursos para usuario {UsuarioId}", usuarioId);
                return new List<MiCursoViewModel>();
            }
        }

        /// <summary>
        /// Obtiene estadísticas de progreso del usuario
        /// </summary>
        public async Task<EstadisticasProgresoUsuarioViewModel> ObtenerEstadisticasProgresoUsuarioAsync(int usuarioId, int empresaId)
        {
            try
            {
                var estadisticas = new EstadisticasProgresoUsuarioViewModel();

                var query = @"
                    SELECT 
                        COUNT(DISTINCT ac.CursoID) as TotalCursosAsignados,
                        COUNT(DISTINCT CASE 
                            WHEN COUNT(sc.SubCursoID) = COUNT(CASE WHEN avs.Completado = 1 THEN 1 END)   -- CAMBIO: asc → avs
                                 AND COUNT(sc.SubCursoID) > 0
                            THEN ac.CursoID 
                        END) as CursosCompletados,
                        COUNT(DISTINCT CASE 
                            WHEN COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) > 0   -- CAMBIO: asc → avs
                                 AND NOT (COUNT(sc.SubCursoID) = COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) AND COUNT(sc.SubCursoID) > 0)
                            THEN ac.CursoID 
                        END) as CursosEnProgreso,
                        COUNT(DISTINCT CASE 
                            WHEN ac.FechaLimite IS NOT NULL AND ac.FechaLimite < GETDATE()
                                 AND NOT (COUNT(sc.SubCursoID) = COUNT(CASE WHEN avs.Completado = 1 THEN 1 END) AND COUNT(sc.SubCursoID) > 0)
                            THEN ac.CursoID 
                        END) as CursosVencidos
                    FROM dbo.AsignacionesCursos ac
                    INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                    LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                    LEFT JOIN dbo.AvancesSubCursos avs ON sc.SubCursoID = avs.SubCursoID   -- CAMBIO: asc → avs
                        AND avs.UsuarioID = @UsuarioID AND avs.EmpresaID = @EmpresaID
                    WHERE ac.UsuarioID = @UsuarioID 
                        AND ac.EmpresaID = @EmpresaID 
                        AND ac.Activo = 1
                        AND c.Activo = 1
                    GROUP BY ac.CursoID, ac.FechaLimite";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    estadisticas.TotalCursosAsignados = reader.GetInt32("TotalCursosAsignados");
                    estadisticas.CursosCompletados = reader.GetInt32("CursosCompletados");
                    estadisticas.CursosEnProgreso = reader.GetInt32("CursosEnProgreso");
                    estadisticas.CursosVencidos = reader.GetInt32("CursosVencidos");
                }

                // Calcular porcentaje de progreso general
                if (estadisticas.TotalCursosAsignados > 0)
                {
                    estadisticas.PorcentajeProgresoGeneral = Math.Round(
                        (decimal)estadisticas.CursosCompletados / estadisticas.TotalCursosAsignados * 100, 1);
                }

                return estadisticas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas de progreso para usuario {UsuarioId}", usuarioId);
                return new EstadisticasProgresoUsuarioViewModel();
            }
        }


        /// <summary>
        /// Obtiene los certificados disponibles para un usuario
        /// </summary>
        public async Task<List<CertificadoDisponibleViewModel>> ObtenerCertificadosDisponiblesAsync(int usuarioId, int empresaId)
        {
            try
            {
                var certificados = new List<CertificadoDisponibleViewModel>();

                var query = @"
                    SELECT DISTINCT
                        c.CursoID,
                        c.NombreCurso,
                        c.Descripcion,
                        n.NombreNivel,
                        ac.FechaAsignacion,
                        MAX(CASE WHEN asc.Completado = 1 THEN asc.FechaCompletado END) as FechaCompletado,
                        -- Verificar si ya tiene certificado emitido
                        CASE 
                            WHEN EXISTS (
                                SELECT 1 FROM dbo.CertificadosEmitidos ce 
                                WHERE ce.UsuarioID = @UsuarioID 
                                  AND ce.CursoID = c.CursoID 
                                  AND ce.EmpresaID = @EmpresaID
                                  AND ce.Activo = 1
                            ) THEN 1 ELSE 0 
                        END as YaTieneCertificado,
                        -- Verificar si el curso está completado
                        CASE
                            WHEN COUNT(sc.SubCursoID) = COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) 
                                 AND COUNT(sc.SubCursoID) > 0
                            THEN 1 ELSE 0
                        END AS CursoCompletado
                    FROM dbo.AsignacionesCursos ac
                    INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID AND c.Activo = 1
                    INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                    LEFT JOIN dbo.SubCursos sc ON c.CursoID = sc.CursoID AND sc.Activo = 1
                    LEFT JOIN dbo.AvancesSubCursos asc ON sc.SubCursoID = asc.SubCursoID 
                        AND asc.UsuarioID = @UsuarioID AND asc.EmpresaID = @EmpresaID
                    WHERE ac.UsuarioID = @UsuarioID 
                        AND ac.EmpresaID = @EmpresaID 
                        AND ac.Activo = 1
                        AND c.Activo = 1
                    GROUP BY 
                        c.CursoID, c.NombreCurso, c.Descripcion, n.NombreNivel, ac.FechaAsignacion
                    HAVING 
                        -- Solo cursos completados
                        COUNT(sc.SubCursoID) = COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) 
                        AND COUNT(sc.SubCursoID) > 0
                    ORDER BY MAX(CASE WHEN asc.Completado = 1 THEN asc.FechaCompletado END) DESC";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    certificados.Add(new CertificadoDisponibleViewModel
                    {
                        CursoID = reader.GetInt32("CursoID"),
                        NombreCurso = reader.GetString("NombreCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        NombreNivel = reader.GetString("NombreNivel"),
                        FechaCompletado = reader.IsDBNull("FechaCompletado") ? null : reader.GetDateTime("FechaCompletado"),
                        YaTieneCertificado = reader.GetBoolean("YaTieneCertificado"),
                        PuedeGenerarCertificado = reader.GetBoolean("CursoCompletado") && !reader.GetBoolean("YaTieneCertificado")
                    });
                }

                return certificados;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener certificados disponibles para usuario {UsuarioId}", usuarioId);
                return new List<CertificadoDisponibleViewModel>();
            }
        }

        /// <summary>
        /// Verifica si un usuario puede acceder a un curso específico
        /// </summary>
        public async Task<bool> UsuarioPuedeAccederCursoAsync(int usuarioId, int cursoId, int empresaId)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*)
FROM dbo.AsignacionesCursos ac
INNER JOIN dbo.Cursos c ON c.CursoID = ac.CursoID AND c.Activo = 1
WHERE ac.UsuarioID = @UsuarioID
  AND ac.CursoID  = @CursoID
  AND ac.EmpresaID = @EmpresaID
  AND ac.Activo = 1";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@CursoID", cursoId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                var count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar acceso al curso {CursoId} para usuario {UsuarioId}", cursoId, usuarioId);
                return false;
            }
        }

        public enum SoftDeleteResult
        {
            Success,
            AlreadyInactive,
            NotFound,
            Error
        }

        public async Task<SoftDeleteResult> EliminarSubCursoAsync(int subCursoId, int usuarioId, string? motivo)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // 1) Intentar inactivar si está activo
                const string sqlUpdate = @"
UPDATE dbo.SubCursos
SET Activo = 0
WHERE SubCursoID = @Id AND Activo = 1;";

                using (var cmd = new SqlCommand(sqlUpdate, connection))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = subCursoId;
                    var rows = await cmd.ExecuteNonQueryAsync();
                    if (rows > 0)
                        return SoftDeleteResult.Success; // se inactivó
                }

                // 2) Si no afectó filas, verificar si existe y ya estaba inactivo
                const string sqlEstado = "SELECT Activo FROM dbo.SubCursos WHERE SubCursoID = @Id;";
                using (var cmd2 = new SqlCommand(sqlEstado, connection))
                {
                    cmd2.Parameters.Add("@Id", SqlDbType.Int).Value = subCursoId;
                    var estado = await cmd2.ExecuteScalarAsync();

                    if (estado is null)
                        return SoftDeleteResult.NotFound;

                    var activo = Convert.ToBoolean(estado);
                    return activo ? SoftDeleteResult.Error : SoftDeleteResult.AlreadyInactive;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al inactivar subcurso {SubCursoId}", subCursoId);
                return SoftDeleteResult.Error;
            }
        }




        /// <summary>
        /// Verifica si un usuario tiene asignado un curso específico
        /// </summary>
        public async Task<bool> VerificarAccesoCursoAsync(int usuarioId, int cursoId, int empresaId)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) 
                    FROM dbo.AsignacionesCursos ac
                    WHERE ac.UsuarioID = @UsuarioID 
                        AND ac.CursoID = @CursoID 
                        AND ac.EmpresaID = @EmpresaID 
                        AND ac.Activo = 1";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@CursoID", cursoId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                var count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar acceso al curso");
                return false;
            }
        }

        /// <summary>
        /// Registra que un usuario ha iniciado un subcurso
        /// </summary>
        public async Task<bool> RegistrarInicioSubCursoAsync(int usuarioId, int subCursoId, int empresaId)
        {
            try
            {
                // Verificar si ya existe registro
                var existeQuery = @"
                    SELECT AvanceSubCursoID 
                    FROM dbo.AvancesSubCursos 
                    WHERE UsuarioID = @UsuarioID 
                        AND SubCursoID = @SubCursoID 
                        AND EmpresaID = @EmpresaID";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var existeCommand = new SqlCommand(existeQuery, connection);
                existeCommand.Parameters.AddWithValue("@UsuarioID", usuarioId);
                existeCommand.Parameters.AddWithValue("@SubCursoID", subCursoId);
                existeCommand.Parameters.AddWithValue("@EmpresaID", empresaId);

                var avanceId = await existeCommand.ExecuteScalarAsync();

                if (avanceId == null)
                {
                    // Crear nuevo registro
                    var insertQuery = @"
                        INSERT INTO dbo.AvancesSubCursos 
                        (UsuarioID, SubCursoID, EmpresaID, InicioVisualizacion, TiempoTotalVisto, 
                         PorcentajeVisto, UltimaActividad, Completado)
                        VALUES 
                        (@UsuarioID, @SubCursoID, @EmpresaID, GETDATE(), 0, 0, GETDATE(), 0)";

                    using var insertCommand = new SqlCommand(insertQuery, connection);
                    insertCommand.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    insertCommand.Parameters.AddWithValue("@SubCursoID", subCursoId);
                    insertCommand.Parameters.AddWithValue("@EmpresaID", empresaId);

                    await insertCommand.ExecuteNonQueryAsync();
                }
                else
                {
                    // Actualizar fecha de última actividad
                    var updateQuery = @"
                        UPDATE dbo.AvancesSubCursos 
                        SET UltimaActividad = GETDATE()
                        WHERE UsuarioID = @UsuarioID 
                            AND SubCursoID = @SubCursoID 
                            AND EmpresaID = @EmpresaID";

                    using var updateCommand = new SqlCommand(updateQuery, connection);
                    updateCommand.Parameters.AddWithValue("@UsuarioID", usuarioId);
                    updateCommand.Parameters.AddWithValue("@SubCursoID", subCursoId);
                    updateCommand.Parameters.AddWithValue("@EmpresaID", empresaId);

                    await updateCommand.ExecuteNonQueryAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar inicio de subcurso");
                return false;
            }
        }

        /// <summary>
        /// Obtiene un subcurso específico con información del curso padre
        /// </summary>
        public async Task<SubCursoDetalle?> ObtenerSubCursoConCursoAsync(int subCursoId, int usuarioId, int empresaId)
        {
            try
            {
                var query = @"
                    SELECT 
                        sc.SubCursoID,
                        sc.CursoID,
                        sc.NombreSubCurso,
                        sc.Descripcion,
                        sc.Orden,
                        sc.ArchivoVideo,
                        sc.ArchivoPDF,
                        sc.DuracionVideo,
                        sc.EsObligatorio,
                        sc.RequiereEvaluacion,
                        sc.PuntajeMinimo,
                        c.NombreCurso,
                        -- Información de progreso del usuario
                        ISNULL(avs.TiempoTotalVisto, 0) as TiempoTotalVisto,
                        ISNULL(avs.PorcentajeVisto, 0) as PorcentajeVisto,
                        ISNULL(avs.Completado, 0) as Completado,
                        avs.FechaCompletado
                    FROM dbo.SubCursos sc
                    INNER JOIN dbo.Cursos c ON sc.CursoID = c.CursoID
                    LEFT JOIN dbo.AvancesSubCursos avs ON sc.SubCursoID = avs.SubCursoID 
                        AND avs.UsuarioID = @UsuarioID 
                        AND avs.EmpresaID = @EmpresaID
                    WHERE sc.SubCursoID = @SubCursoID 
                        AND sc.Activo = 1";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@SubCursoID", subCursoId);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new SubCursoDetalle
                    {
                        SubCursoID = reader.GetInt32("SubCursoID"),
                        CursoID = reader.GetInt32("CursoID"),
                        NombreSubCurso = reader.GetString("NombreSubCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        Orden = reader.GetInt32("Orden"),
                        ArchivoVideo = reader.IsDBNull("ArchivoVideo") ? null : reader.GetString("ArchivoVideo"),
                        ArchivoPDF = reader.IsDBNull("ArchivoPDF") ? null : reader.GetString("ArchivoPDF"),
                        DuracionVideo = reader.IsDBNull("DuracionVideo") ? null : reader.GetInt32("DuracionVideo"),
                        EsObligatorio = reader.GetBoolean("EsObligatorio"),
                        RequiereEvaluacion = reader.GetBoolean("RequiereEvaluacion"),
                        PuntajeMinimo = reader.GetDecimal("PuntajeMinimo"),

                        // Información de progreso
                        TiempoTotalVisto = reader.GetInt32("TiempoTotalVisto"),
                        PorcentajeVisto = reader.GetDecimal("PorcentajeVisto"),
                        Completado = reader.GetBoolean("Completado"),
                        FechaCompletado = reader.IsDBNull("FechaCompletado") ? null : reader.GetDateTime("FechaCompletado"),

                        // Por defecto puede acceder
                        PuedeAcceder = true
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcurso con información del curso");
                return null;
            }
        }

        /// <summary>
        /// Mejora el método GetSubCursosPorCursoAsync existente para incluir información de progreso real
        /// </summary>
        public async Task<List<SubCursoDetalle>> GetSubCursosPorCursoConProgresoAsync(int cursoId, int usuarioId, int empresaId)
        {
            try
            {
                var subCursos = new List<SubCursoDetalle>();

                var query = @"
                    SELECT 
                        sc.SubCursoID,
                        sc.CursoID,
                        sc.NombreSubCurso,
                        sc.Descripcion,
                        sc.Orden,
                        sc.ArchivoVideo,
                        sc.ArchivoPDF,
                        sc.DuracionVideo,
                        sc.EsObligatorio,
                        sc.RequiereEvaluacion,
                        sc.PuntajeMinimo,
                        c.NombreCurso,
                
                        -- Información de progreso del usuario
                        ISNULL(avs.TiempoTotalVisto, 0) as TiempoTotalVisto,
                        ISNULL(avs.PorcentajeVisto, 0) as PorcentajeVisto,
                        ISNULL(avs.Completado, 0) as Completado,
                        avs.FechaCompletado,
                        avs.InicioVisualizacion
                
                    FROM dbo.SubCursos sc
                    INNER JOIN dbo.Cursos c ON sc.CursoID = c.CursoID
                    LEFT JOIN dbo.AvancesSubCursos avs ON sc.SubCursoID = avs.SubCursoID 
                        AND avs.UsuarioID = @UsuarioID 
                        AND avs.EmpresaID = @EmpresaID
                    WHERE sc.CursoID = @CursoID 
                        AND sc.Activo = 1
                    ORDER BY sc.Orden";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@CursoID", cursoId);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@EmpresaID", empresaId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var subcurso = new SubCursoDetalle
                    {
                        SubCursoID = reader.GetInt32("SubCursoID"),
                        CursoID = reader.GetInt32("CursoID"),
                        NombreSubCurso = reader.GetString("NombreSubCurso"),
                        Descripcion = reader.IsDBNull("Descripcion") ? null : reader.GetString("Descripcion"),
                        Orden = reader.GetInt32("Orden"),
                        ArchivoVideo = reader.IsDBNull("ArchivoVideo") ? null : reader.GetString("ArchivoVideo"),
                        ArchivoPDF = reader.IsDBNull("ArchivoPDF") ? null : reader.GetString("ArchivoPDF"),
                        DuracionVideo = reader.IsDBNull("DuracionVideo") ? null : reader.GetInt32("DuracionVideo"),
                        EsObligatorio = reader.GetBoolean("EsObligatorio"),
                        RequiereEvaluacion = reader.GetBoolean("RequiereEvaluacion"),
                        PuntajeMinimo = reader.GetDecimal("PuntajeMinimo"),

                        // Información de progreso REAL
                        TiempoTotalVisto = reader.GetInt32("TiempoTotalVisto"),
                        PorcentajeVisto = reader.GetDecimal("PorcentajeVisto"),
                        Completado = reader.GetBoolean("Completado"),
                        FechaCompletado = reader.IsDBNull("FechaCompletado") ? null : reader.GetDateTime("FechaCompletado"),

                        // Control de acceso (por ahora todos pueden acceder)
                        PuedeAcceder = true,
                        UltimoIntento = null
                    };

                    subCursos.Add(subcurso);
                }

                return subCursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcursos con progreso del curso {CursoId}", cursoId);
                return new List<SubCursoDetalle>();
            }
        }
        public async Task<int> GetTiempoEstudioUsuarioAsync(int usuarioId, int empresaId)
        {
            // Sumamos minutos de estudio en contenidos + minutos de evaluaciones.
            // ⚠️ Si TU columna AvancesSubCursos.TiempoTotalVisto está en SEGUNDOS,
            // cambia la primera subconsulta para dividir por 60 (ver versión B más abajo).

            const string sql = @"
        SELECT
            ISNULL((
                SELECT SUM(avs.TiempoTotalVisto)
                FROM dbo.AvancesSubCursos avs
                WHERE avs.UsuarioID = @UsuarioID AND avs.EmpresaID = @EmpresaID
            ), 0)
            +
            ISNULL((
                SELECT SUM(ie.TiempoEmpleado)
                FROM dbo.IntentosEvaluacion ie
                WHERE ie.UsuarioID = @UsuarioID AND ie.EmpresaID = @EmpresaID
            ), 0) AS MinutosTotales;";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@EmpresaID", empresaId);

            var result = await cmd.ExecuteScalarAsync();
            var minutos = (result == null || result == DBNull.Value) ? 0 : Convert.ToInt32(result);
            return minutos; // <- Estadisticas.TiempoTotalEstudio usa minutos y se formatea a hh:mm
        }



        private async Task<int> GetCursoIdBySubCursoIdAsync(int subCursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var sql = "SELECT CursoID FROM SubCursos WHERE SubCursoID = @SubCursoID";
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@SubCursoID", subCursoId);
            return (int)await cmd.ExecuteScalarAsync();
        }

        private async Task<int> GetTotalSubcursosAsync(int cursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var sql = "SELECT COUNT(*) FROM SubCursos WHERE CursoID = @CursoID";
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@CursoID", cursoId);
            return (int)await cmd.ExecuteScalarAsync();
        }

        private async Task<int> GetSubcursosAprobadosAsync(int usuarioId, int cursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var sql = @"
                SELECT COUNT(DISTINCT ie.SubCursoID)
                FROM IntentosEvaluacion ie
                INNER JOIN SubCursos s ON ie.SubCursoID = s.SubCursoID
                WHERE ie.UsuarioID = @UsuarioID AND ie.Aprobado = 1 AND s.CursoID = @CursoID";
            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@CursoID", cursoId);
            return (int)await cmd.ExecuteScalarAsync();
        }


        private async Task<string> GetNombreUsuarioAsync(int usuarioId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = "SELECT Nombre FROM Usuarios WHERE UsuarioID = @UsuarioID";
            using (var cmd = new SqlCommand(query, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Alumno";
            }
        }

        private async Task<string> GetNombreCursoAsync(int cursoId, SqlConnection connection, SqlTransaction transaction)
        {
            var query = "SELECT NombreCurso FROM Cursos WHERE CursoID = @CursoID";
            using (var cmd = new SqlCommand(query, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@CursoID", cursoId);
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Curso";
            }
        }


        public async Task<SoftDeleteResult> EliminarCursoAsync(int cursoId, int usuarioId, string? motivo)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var tx = connection.BeginTransaction();

                // 0) ¿Existe?
                const string sqlExiste = "SELECT Activo FROM dbo.Cursos WHERE CursoID = @CursoID;";
                using (var cmd0 = new SqlCommand(sqlExiste, connection, tx))
                {
                    cmd0.Parameters.Add("@CursoID", SqlDbType.Int).Value = cursoId;
                    var estado = await cmd0.ExecuteScalarAsync();
                    if (estado == null) { tx.Rollback(); return SoftDeleteResult.NotFound; }

                    var activo = Convert.ToBoolean(estado);
                    if (!activo) { tx.Commit(); return SoftDeleteResult.AlreadyInactive; }
                }

                // 1) Inactivar subcursos del curso
                const string sqlSub = @"
UPDATE dbo.SubCursos
SET Activo = 0
WHERE CursoID = @CursoID AND Activo = 1;";
                using (var cmd1 = new SqlCommand(sqlSub, connection, tx))
                {
                    cmd1.Parameters.Add("@CursoID", SqlDbType.Int).Value = cursoId;
                    await cmd1.ExecuteNonQueryAsync();
                }

                // 2) (Opcional) Inactivar asignaciones del curso
                const string sqlAsig = @"
UPDATE dbo.AsignacionesCursos
SET Activo = 0
WHERE CursoID = @CursoID AND Activo = 1;";
                using (var cmd2 = new SqlCommand(sqlAsig, connection, tx))
                {
                    cmd2.Parameters.Add("@CursoID", SqlDbType.Int).Value = cursoId;
                    await cmd2.ExecuteNonQueryAsync();
                }

                // 3) Inactivar el curso
                const string sqlCurso = @"
UPDATE dbo.Cursos
SET Activo = 0
WHERE CursoID = @CursoID AND Activo = 1;";
                int afectados;
                using (var cmd3 = new SqlCommand(sqlCurso, connection, tx))
                {
                    cmd3.Parameters.Add("@CursoID", SqlDbType.Int).Value = cursoId;
                    afectados = await cmd3.ExecuteNonQueryAsync();
                }

                if (afectados == 0) { tx.Rollback(); return SoftDeleteResult.Error; }

                tx.Commit();
                return SoftDeleteResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar (inactivar) curso {CursoId}", cursoId);
                return SoftDeleteResult.Error;
            }
        }
            // using System.Data; using Dapper; etc. según tu capa de datos



            // ======= Prerrequisitos evaluación (video/pdf) =======
public async Task<PrereqEvaluacionDto> GetPrerequisitosEvaluacionAsync(int subCursoId, int usuarioId, int empresaId)
        {
            var dto = new PrereqEvaluacionDto();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1) Saber si el subcurso tiene video/pdf
            var qSub = @"
        SELECT ArchivoVideo, ArchivoPDF
        FROM dbo.SubCursos
        WHERE SubCursoID = @SubCursoID AND Activo = 1";
            using (var cmd = new SqlCommand(qSub, connection))
            {
                cmd.Parameters.AddWithValue("@SubCursoID", subCursoId);
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    dto.TieneVideo = !r.IsDBNull(r.GetOrdinal("ArchivoVideo")) && !string.IsNullOrWhiteSpace(r.GetString(r.GetOrdinal("ArchivoVideo")));
                    dto.TienePDF = !r.IsDBNull(r.GetOrdinal("ArchivoPDF")) && !string.IsNullOrWhiteSpace(r.GetString(r.GetOrdinal("ArchivoPDF")));
                }
            }

            // 2) Leer avance del usuario (video% y PdfVisto)
            var qAv = @"
        SELECT TOP 1 
               ISNULL(PorcentajeVisto,0) AS PorcentajeVisto,
               ISNULL(PdfVisto, 0)       AS PdfVisto
        FROM dbo.AvancesSubCursos
        WHERE UsuarioID = @UsuarioID AND EmpresaID = @EmpresaID AND SubCursoID = @SubCursoID
        ORDER BY UltimaActividad DESC";
            using (var cmd2 = new SqlCommand(qAv, connection))
            {
                cmd2.Parameters.AddWithValue("@UsuarioID", usuarioId);
                cmd2.Parameters.AddWithValue("@EmpresaID", empresaId);
                cmd2.Parameters.AddWithValue("@SubCursoID", subCursoId);

                using var r2 = await cmd2.ExecuteReaderAsync();
                if (await r2.ReadAsync())
                {
                    dto.PorcentajeVideoVisto = Convert.ToInt32(r2["PorcentajeVisto"]);
                    dto.PdfVisto = Convert.ToBoolean(r2["PdfVisto"]);
                }
                else
                {
                    dto.PorcentajeVideoVisto = 0;
                    dto.PdfVisto = false;
                }
            }

            return dto;
        }

        public async Task<bool> MarcarPdfVistoAsync(int usuarioId, int empresaId, int subCursoId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // UPSERT: si existe, actualiza; si no, inserta
            var upsert = @"
MERGE dbo.AvancesSubCursos AS T
USING (SELECT @UsuarioID AS UsuarioID, @EmpresaID AS EmpresaID, @SubCursoID AS SubCursoID) AS S
    ON (T.UsuarioID = S.UsuarioID AND T.EmpresaID = S.EmpresaID AND T.SubCursoID = S.SubCursoID)
WHEN MATCHED THEN
    UPDATE SET PdfVisto = 1, UltimaActividad = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (UsuarioID, EmpresaID, SubCursoID, InicioVisualizacion, TiempoTotalVisto, PorcentajeVisto, PdfVisto, UltimaActividad)
    VALUES (@UsuarioID, @EmpresaID, @SubCursoID, GETDATE(), 0, 0, 1, GETDATE());";

            using var cmd = new SqlCommand(upsert, connection);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
            cmd.Parameters.AddWithValue("@SubCursoID", subCursoId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> RegistrarProgresoVideoAsync(int usuarioId, int empresaId, int subCursoId, int porcentaje)
        {
            porcentaje = Math.Max(0, Math.Min(100, porcentaje));
            var req = new ActualizarProgresoRequest
            {
                UsuarioID = usuarioId,
                EmpresaID = empresaId,
                SubCursoID = subCursoId,
                TiempoTotalVisto = 0,        // si no mides tiempo real aún
                PorcentajeVisto = porcentaje
            };
            return await ActualizarProgresoVideoAsync(req);
        }
    } 
} // <-- FIN namespace ProyectoMatrix.Servicios
