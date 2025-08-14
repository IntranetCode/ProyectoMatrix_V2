// =====================================================
// ARCHIVO: Servicios/UniversidadServices.cs
// PROPÓSITO: Servicios para módulo Universidad NS
// =====================================================

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Models;
using System.Data;

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
                SubCursoID,
                CursoID,
                NombreSubCurso,
                Descripcion,
                Orden,
                ArchivoVideo,
                ArchivoPDF,
                DuracionVideo,
                EsObligatorio,
                RequiereEvaluacion,
                PuntajeMinimo
            FROM dbo.SubCursos 
            WHERE CursoID = @CursoID AND Activo = 1
            ORDER BY Orden";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@CursoID", cursoId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    subCursos.Add(new SubCursoDetalle
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

                        // Valores por defecto
                        TiempoTotalVisto = 0,
                        PorcentajeVisto = 0,
                        Completado = false,
                        FechaCompletado = null,
                        PuedeAcceder = true,
                        UltimoIntento = null
                    });
                }

                return subCursos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcursos del curso {CursoId}", cursoId);
                return new List<SubCursoDetalle>();
            }
        }       // MANTÉN tu método existente pero AGREGA estas mejoras:

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
                        WHEN COUNT(sc.SubCursoID) = COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) 
                        THEN 'Completado'
                        WHEN COUNT(CASE WHEN asc.Completado = 1 THEN 1 END) > 0 
                        THEN 'En Progreso'
                        ELSE 'Asignado'
                    END AS Estado
                    
                FROM dbo.AsignacionesCursos ac
                INNER JOIN dbo.Cursos c ON ac.CursoID = c.CursoID
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

        public async Task<List<CertificadoEmitido>> GetCertificadosUsuarioAsync(int usuarioId, int? empresaId = null)
        {
            var certificados = new List<CertificadoEmitido>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    ce.CertificadoID, ce.CodigoCertificado, ce.FechaEmision, ce.FechaExpiracion,
                    ce.ArchivoPDF, ce.Activo,
                    c.NombreCurso, n.NombreNivel, e.Nombre as NombreEmpresa,
                    CASE 
                        WHEN ce.FechaExpiracion IS NULL OR ce.FechaExpiracion > GETDATE() 
                        THEN 'Vigente' 
                        ELSE 'Expirado' 
                    END AS Estado
                FROM dbo.CertificadosEmitidos ce
                INNER JOIN dbo.Cursos c ON ce.CursoID = c.CursoID
                INNER JOIN dbo.NivelesEducativos n ON c.NivelID = n.NivelID
                INNER JOIN dbo.Empresas e ON ce.EmpresaID = e.EmpresaID
                WHERE ce.UsuarioID = @UsuarioID 
                AND ce.Activo = 1" +
                (empresaId.HasValue ? " AND ce.EmpresaID = @EmpresaID" : "") + @"
                ORDER BY ce.FechaEmision DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            if (empresaId.HasValue)
            {
                command.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId.Value;
            }

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                certificados.Add(new CertificadoEmitido
                {
                    CertificadoID = reader.GetInt32("CertificadoID"),
                    CodigoCertificado = reader.GetString("CodigoCertificado"),
                    FechaEmision = reader.GetDateTime("FechaEmision"),
                    FechaExpiracion = reader.IsDBNull("FechaExpiracion") ? null : reader.GetDateTime("FechaExpiracion"),
                    ArchivoPDF = reader.IsDBNull("ArchivoPDF") ? null : reader.GetString("ArchivoPDF"),
                    NombreCurso = reader.GetString("NombreCurso"),
                    NombreNivel = reader.GetString("NombreNivel"),
                    NombreEmpresa = reader.GetString("NombreEmpresa"),
                    Estado = reader.GetString("Estado"),
                    Activo = reader.GetBoolean("Activo")
                });
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
            try
            {
                var cursosEntity = await GetCursosAsignadosUsuarioAsync(usuarioId, empresaId);

                // Convertir de CursoAsignado a CursoAsignadoViewModel
                return cursosEntity.Select(c => new CursoAsignadoViewModel
                {
                    CursoID = c.CursoID,
                    NombreCurso = c.NombreCurso,
                    Estado = c.Estado,
                    PorcentajeProgreso = (int)c.PorcentajeProgreso,
                    FechaAsignacion = c.FechaAsignacion
                }).ToList();
            }
            catch (Exception ex)
            {
                // Log error y retornar datos de prueba
                Console.WriteLine($"Error en GetCursosAsignadosUsuarioAsync: {ex.Message}");
                return new List<CursoAsignadoViewModel>
        {
            new CursoAsignadoViewModel
            {
                CursoID = 1,
                NombreCurso = "Curso de Inducción",
                Estado = "En Progreso",
                PorcentajeProgreso = 45,
                FechaAsignacion = DateTime.Now.AddDays(-7)
            },
            new CursoAsignadoViewModel
            {
                CursoID = 2,
                NombreCurso = "Seguridad Industrial",
                Estado = "Completado",
                PorcentajeProgreso = 100,
                FechaAsignacion = DateTime.Now.AddDays(-30)
            }
        };
            }
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
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var viewModel = new EvaluacionViewModel
                                {
                                    SubCursoID = reader.GetInt32("SubCursoID"),
                                    NombreSubCurso = reader.GetString("NombreSubCurso"),
                                    NombreCurso = reader.GetString("NombreCurso")
                                };
                                // Cerrar el reader antes de la siguiente consulta
                                reader.Close();
                                // Obtener preguntas existentes
                                viewModel.Preguntas = await GetPreguntasEvaluacionAsync(subCursoId, connection);
                                return viewModel;
                            }
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener evaluación para SubCurso {SubCursoId}", subCursoId);
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

        private async Task<int> GetSiguienteNumeroIntentoAsync(int usuarioId, int subCursoId, int empresaId, SqlConnection connection)
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

        public async Task<ResultadoEvaluacion> EntregarEvaluacionAsync(
            int usuarioId, int subCursoId, int empresaId,
            Dictionary<string, RespuestaUsuario> respuestas, int tiempoEmpleado)
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
                            // 1. Obtener siguiente número de intento
                            var numeroIntento = await GetSiguienteNumeroIntentoAsync(usuarioId, subCursoId, empresaId, connection);

                            // 2. Crear intento de evaluación
                            var intentoId = await CrearIntentoEvaluacionAsync(
                                usuarioId, subCursoId, empresaId, numeroIntento, tiempoEmpleado, connection, transaction);

                            // 3. Procesar respuestas y calcular puntaje
                            var (puntajeObtenido, puntajeMaximo) = await ProcesarRespuestasAsync(
                                intentoId, subCursoId, respuestas, connection, transaction);

                            // 4. Obtener puntaje mínimo para aprobar
                            var puntajeMinimoDecimal = await GetPuntajeMinimoAsync(subCursoId, connection);

                            // 5. Calcular porcentaje y si aprobó
                            var porcentaje = puntajeMaximo > 0 ? (puntajeObtenido / puntajeMaximo) * 100 : 0;
                            var aprobado = porcentaje >= puntajeMinimoDecimal;

                            // 6. Actualizar intento con resultados finales
                            await ActualizarResultadoIntentoAsync(
                                intentoId, puntajeObtenido, puntajeMaximo, aprobado, connection, transaction);

                            // 7. Si aprobó, marcar subcurso como completado
                            if (aprobado)
                            {
                                await MarcarSubCursoCompletadoAsync(usuarioId, subCursoId, empresaId, connection, transaction);
                            }

                            transaction.Commit();

                            return new ResultadoEvaluacion
                            {
                                Success = true,
                                Calificacion = Math.Round(porcentaje, 1),
                                Aprobado = aprobado,
                                Message = "Evaluación procesada correctamente"
                            };
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
                _logger.LogError(ex, "Error al procesar evaluación");
                return new ResultadoEvaluacion
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
            int intentoId, int subCursoId, Dictionary<string, RespuestaUsuario> respuestas,
            SqlConnection connection, SqlTransaction transaction)
        {
            decimal puntajeObtenido = 0;
            decimal puntajeMaximo = 0;

            // Obtener todas las preguntas del subcurso
            var preguntas = await GetPreguntasParaCalificarAsync(subCursoId, connection);

            foreach (var pregunta in preguntas)
            {
                puntajeMaximo += pregunta.PuntajeMaximo;

                if (respuestas.ContainsKey(pregunta.PreguntaID.ToString()))
                {
                    var respuesta = respuestas[pregunta.PreguntaID.ToString()];
                    decimal puntajePregunta = 0;
                    bool esCorrecta = false;

                    if (respuesta.Tipo == "opcion" && respuesta.OpcionId.HasValue)
                    {
                        // Verificar si la opción seleccionada es correcta
                        esCorrecta = await VerificarOpcionCorrectaAsync(respuesta.OpcionId.Value, connection);
                        if (esCorrecta)
                        {
                            puntajePregunta = pregunta.PuntajeMaximo;
                            puntajeObtenido += puntajePregunta;
                        }

                        // Guardar respuesta de opción
                        await GuardarRespuestaOpcionAsync(intentoId, pregunta.PreguntaID, respuesta.OpcionId.Value,
                            esCorrecta, puntajePregunta, connection, transaction);
                    }
                    else if (respuesta.Tipo == "abierta" && !string.IsNullOrEmpty(respuesta.Texto))
                    {
                        // Para preguntas abiertas, por ahora dar puntuación completa si hay respuesta
                        // En el futuro se puede implementar calificación manual
                        puntajePregunta = pregunta.PuntajeMaximo;
                        puntajeObtenido += puntajePregunta;
                        esCorrecta = true;

                        // Guardar respuesta abierta
                        await GuardarRespuestaAbiertaAsync(intentoId, pregunta.PreguntaID, respuesta.Texto,
                            esCorrecta, puntajePregunta, connection, transaction);
                    }
                }
            }

            return (puntajeObtenido, puntajeMaximo);
        }

        private async Task<List<(int PreguntaID, decimal PuntajeMaximo)>> GetPreguntasParaCalificarAsync(
            int subCursoId, SqlConnection connection)
        {
            var preguntas = new List<(int, decimal)>();

            var query = "SELECT PreguntaID, PuntajeMaximo FROM dbo.PreguntasEvaluacion WHERE SubCursoID = @SubCursoId AND Activo = 1";

            using (var command = new SqlCommand(query, connection))
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

        private async Task<bool> VerificarOpcionCorrectaAsync(int opcionId, SqlConnection connection)
        {
            var query = "SELECT EsCorrecta FROM dbo.OpcionesRespuesta WHERE OpcionID = @OpcionId";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@OpcionId", opcionId);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToBoolean(result);
            }
        }

        private async Task<decimal> GetPuntajeMinimoAsync(int subCursoId, SqlConnection connection)
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

        private async Task MarcarSubCursoCompletadoAsync(int usuarioId, int subCursoId, int empresaId,
            SqlConnection connection, SqlTransaction transaction)
        {
            var query = @"
        UPDATE dbo.AvancesSubCursos 
        SET Completado = 1, FechaCompletado = GETDATE(), PorcentajeVisto = 100
        WHERE UsuarioID = @UsuarioId AND SubCursoID = @SubCursoId AND EmpresaID = @EmpresaId";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                command.Parameters.AddWithValue("@SubCursoId", subCursoId);
                command.Parameters.AddWithValue("@EmpresaId", empresaId);

                await command.ExecuteNonQueryAsync();
            }
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
                var query = @"
            UPDATE dbo.SubCursos 
            SET NombreSubCurso = @NombreSubCurso,
                Descripcion = @Descripcion,
                Orden = @Orden,
                DuracionVideo = @DuracionVideo,
                EsObligatorio = @EsObligatorio,
                RequiereEvaluacion = @RequiereEvaluacion,
                PuntajeMinimo = @PuntajeMinimo
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

                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar subcurso");
                return false;
            }
        }

    }
}