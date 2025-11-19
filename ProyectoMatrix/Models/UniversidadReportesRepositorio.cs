using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using ProyectoMatrix.Models.Universidad; 

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

    }
}
