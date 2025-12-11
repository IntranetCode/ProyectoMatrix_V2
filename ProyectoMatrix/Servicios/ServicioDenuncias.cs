using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Models;

namespace ProyectoMatrix.Servicios
{
    public class ServiciosDenuncias
    {
        private readonly string _cs;
        private readonly ServicioNotificaciones _notificaciones;
        private readonly ILogger<ServiciosDenuncias> _logger;

        public ServiciosDenuncias(
            IConfiguration cfg,
            ServicioNotificaciones notificaciones,
            ILogger<ServiciosDenuncias> logger)
        {
            _cs = cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection");
            _notificaciones = notificaciones;
            _logger = logger;
        }

        
        // Crea una denuncia anónima:
        // 1) Inserta en BD vía SP sp_DenunciasAnonimas_Insertar
        // 2) Envía correo a RH (sin UsuarioId)
       
        public async Task<int> CrearDenunciaAsync(DenunciaAnonimaCreateVm model, int usuarioId)
        {
            int denunciaId;

            const string storedProc = "sp_DenunciasAnonimas_Insertar";


            await using (var conn = new SqlConnection(_cs))
            await using (var cmd = new SqlCommand(storedProc, conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@UsuarioId", SqlDbType.Int)
                {
                    Value = usuarioId
                });

                cmd.Parameters.Add(new SqlParameter("@TipoDenuncia", SqlDbType.NVarChar, 50)
                {
                    Value = model.TipoDenuncia
                });

                cmd.Parameters.Add(new SqlParameter("@DepartamentoAfectado", SqlDbType.NVarChar, 150)
                {
                    Value = model.DepartamentoAfectado
                });

                cmd.Parameters.Add(new SqlParameter("@Descripcion", SqlDbType.NVarChar)
                {
                    Value = model.Descripcion
                });

                cmd.Parameters.Add(new SqlParameter("@FechaHechos", SqlDbType.Date)
                {
                    Value = (object?)model.FechaHechos ?? DBNull.Value
                });

                cmd.Parameters.Add(new SqlParameter("@LugarHechos", SqlDbType.NVarChar, 200)
                {
                    Value = string.IsNullOrWhiteSpace(model.LugarHechos)
                        ? DBNull.Value
                        : model.LugarHechos
                });

                var outParam = new SqlParameter("@DenunciaId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(outParam);

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();

                denunciaId = (int)outParam.Value;
            }

            // 2) Enviar correo a RH SIN UsuarioId
            try
            {
                await _notificaciones.EnviarCorreoDenunciaAnonimaAsync(model);
                _logger.LogInformation("Correo de denuncia anónima enviado. DenunciaId={DenunciaId}", denunciaId);
            }
            catch (Exception ex)
            {
                // No queremos romper la UX del usuario si falla el correo
                _logger.LogError(ex,
                    "Error enviando correo de denuncia anónima. DenunciaId={DenunciaId}",
                    denunciaId);
            }

            return denunciaId;
        }
    }
}
