using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ProyectoMatrix.Models.Universidad;
using ProyectoMatrix.Models.ViewModels;

namespace ProyectoMatrix.Models  
{
    public class UniversidadReportesRepository
    {
        private readonly string _connectionString;

        public UniversidadReportesRepository(string connectionString)
        {
            _connectionString = connectionString
                ?? throw new ArgumentNullException(nameof(connectionString));
        }


        //Este metodo es para obtener todos los datos de tos los cursos creados , de manera general, 
       
        public List<CursoResumenViewModel> ObtenerResumenCursos()
        {
            var resultados = new List<CursoResumenViewModel>();

            const string sql = @"
        SELECT 
            CursoID,
            NombreCurso,
            UsuariosAsignados,
            NoIniciados,
            EnProgreso,
            Aprobados,
            Reprobados,
            PorcentajeAprobacion
        FROM dbo.vw_Universidad_ReporteCursos_Resumen
        ORDER BY NombreCurso;";

            using (var conexion = new SqlConnection(_connectionString))
            using (var comando = new SqlCommand(sql, conexion))
            {
                comando.CommandType = CommandType.Text;
                conexion.Open();

                using (var lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        var item = new CursoResumenViewModel
                        {
                            CursoId = lector.GetInt32(lector.GetOrdinal("CursoID")),
                            NombreCurso = lector["NombreCurso"] as string ?? string.Empty,

                            UsuariosAsignados = lector["UsuariosAsignados"] != DBNull.Value
                                ? Convert.ToInt32(lector["UsuariosAsignados"])
                                : 0,

                            NoIniciados = lector["NoIniciados"] != DBNull.Value
                                ? Convert.ToInt32(lector["NoIniciados"])
                                : 0,

                            EnProgreso = lector["EnProgreso"] != DBNull.Value
                                ? Convert.ToInt32(lector["EnProgreso"])
                                : 0,

                            Aprobados = lector["Aprobados"] != DBNull.Value
                                ? Convert.ToInt32(lector["Aprobados"])
                                : 0,

                            Reprobados = lector["Reprobados"] != DBNull.Value
                                ? Convert.ToInt32(lector["Reprobados"])
                                : 0,

                            PorcentajeAprobacion = lector["PorcentajeAprobacion"] != DBNull.Value
                                ? Convert.ToDecimal(lector["PorcentajeAprobacion"])
                                : 0m
                        };

                        resultados.Add(item);
                    }
                }
            }

            return resultados;
        }

        //El siguiente metodo es para obtener detalles de un curso

        public List<DetalleCursoViewModel> ObtenerDetalleCurso(int cursoId)
        {
            var resultados = new List<DetalleCursoViewModel>();

            const string sql = @"
        SELECT
            AsignacionID,
            CursoID,
            NombreCurso,
            UsuarioID,
            Username,
            NombreUsuario,
            EmpresaID,
            DepartamentoID,
            EstadoCurso,
            CalificacionFinal,
            IntentosTotales,
            IntentosAprobados,
            IntentosNoAprobados,
            FechaAsignacion,
            FechaInicioCurso,
            FechaTerminoCurso,
            FechaUltimaActividad
        FROM dbo.vw_Universidad_ReporteCursos_Detalle
        WHERE CursoID = @CursoID
        ORDER BY EstadoCurso, UsuarioID;";

            using (var conexion = new SqlConnection(_connectionString))
            using (var comando = new SqlCommand(sql, conexion))
            {
                comando.CommandType = CommandType.Text;
                comando.Parameters.AddWithValue("@CursoID", cursoId);

                conexion.Open();

                using (var lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        var item = new DetalleCursoViewModel
                        {
                            AsignacionId = Convert.ToInt32(lector["AsignacionID"]),

                            CursoId = Convert.ToInt32(lector["CursoID"]),
                            NombreCurso = lector["NombreCurso"] as string ?? string.Empty,

                            UsuarioId = Convert.ToInt32(lector["UsuarioID"]),

                            Username = lector["Username"] as string ?? string.Empty,
                            NombreUsuario = lector["NombreUsuario"] as string,

                            EmpresaId = lector["EmpresaID"] != DBNull.Value
                                ? (int?)Convert.ToInt32(lector["EmpresaID"])
                                : null,

                            DepartamentoId = lector["DepartamentoID"] != DBNull.Value
                                ? (int?)Convert.ToInt32(lector["DepartamentoID"])
                                : null,

                            EstadoCurso = lector["EstadoCurso"] as string ?? string.Empty,

                            CalificacionFinal = lector["CalificacionFinal"] != DBNull.Value
                                ? (decimal?)Convert.ToDecimal(lector["CalificacionFinal"])
                                : null,

                            IntentosTotales = lector["IntentosTotales"] != DBNull.Value
                                ? (int?)Convert.ToInt32(lector["IntentosTotales"])
                                : null,

                            IntentosAprobados = lector["IntentosAprobados"] != DBNull.Value
                                ? (int?)Convert.ToInt32(lector["IntentosAprobados"])
                                : null,

                            IntentosNoAprobados = lector["IntentosNoAprobados"] != DBNull.Value
                                ? (int?)Convert.ToInt32(lector["IntentosNoAprobados"])
                                : null,

                            FechaAsignacion = Convert.ToDateTime(lector["FechaAsignacion"]),

                            FechaInicioCurso = lector["FechaInicioCurso"] != DBNull.Value
                                ? (DateTime?)Convert.ToDateTime(lector["FechaInicioCurso"])
                                : null,

                            FechaTerminoCurso = lector["FechaTerminoCurso"] != DBNull.Value
                                ? (DateTime?)Convert.ToDateTime(lector["FechaTerminoCurso"])
                                : null,

                            FechaUltimaActividad = lector["FechaUltimaActividad"] != DBNull.Value
                                ? (DateTime?)Convert.ToDateTime(lector["FechaUltimaActividad"])
                                : null
                        };

                        resultados.Add(item);
                    }
                }
            }

            return resultados;
        }


        //Nuevo metodo para visualizar intentos de evaluación por usuarios

        public List<IntentoEvaluacionViewModel> ObtenerIntentosPorUsuarioCurso(int cursoId, int usuarioId)
        {
            var resultados = new List<IntentoEvaluacionViewModel>();

            const string sql = @"
        SELECT
            ie.IntentoID,
            ie.UsuarioID,
            sc.CursoID,
            ie.SubCursoID,
            sc.NombreSubCurso,
            ie.NumeroIntento,
            ie.FechaInicio,
            ie.FechaFin,
            ie.PuntajeObtenido,
            ie.PuntajeMaximo,
            ie.Aprobado,
            ie.TiempoEmpleado
        FROM dbo.IntentosEvaluacion AS ie
        INNER JOIN dbo.SubCursos AS sc
            ON sc.SubCursoID = ie.SubCursoID
           AND sc.Activo = 1
        WHERE
            ie.Activo = 1
            AND sc.CursoID = @CursoID
            AND ie.UsuarioID = @UsuarioID
        ORDER BY
            sc.Orden,        -- si tienes columna Orden
            ie.NumeroIntento;";

            using (var conexion = new SqlConnection(_connectionString))
            using (var comando = new SqlCommand(sql, conexion))
            {
                comando.CommandType = CommandType.Text;
                comando.Parameters.AddWithValue("@CursoID", cursoId);
                comando.Parameters.AddWithValue("@UsuarioID", usuarioId);

                conexion.Open();

                using (var lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        var item = new IntentoEvaluacionViewModel
                        {
                            IntentoId = Convert.ToInt32(lector["IntentoID"]),
                            UsuarioId = Convert.ToInt32(lector["UsuarioID"]),
                            CursoId = Convert.ToInt32(lector["CursoID"]),
                            SubCursoId = Convert.ToInt32(lector["SubCursoID"]),
                            NombreSubCurso = lector["NombreSubCurso"] as string ?? string.Empty,

                            NumeroIntento = Convert.ToInt32(lector["NumeroIntento"]),

                            FechaInicio = Convert.ToDateTime(lector["FechaInicio"]),
                            FechaFin = Convert.ToDateTime(lector["FechaFin"]),

                            PuntajeObtenido = Convert.ToDecimal(lector["PuntajeObtenido"]),
                            PuntajeMaximo = Convert.ToDecimal(lector["PuntajeMaximo"]),
                            Aprobado = Convert.ToBoolean(lector["Aprobado"]),

                            TiempoEmpleado = Convert.ToInt32(lector["TiempoEmpleado"])
                        };

                        resultados.Add(item);
                    }
                }
            }

            return resultados;
        }




    }
}
