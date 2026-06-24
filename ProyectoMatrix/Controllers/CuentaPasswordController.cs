using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace ProyectoMatrix.Controllers
{
    public class CuentaPasswordController : Controller
    {
        private readonly string _connectionString;

        public CuentaPasswordController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> CambiarPasswordInicial()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioPendienteCambioPasswordID");

            if (!usuarioId.HasValue)
            {
                return RedirectToAction("Login", "Login");
            }

            var debeCambiar = await DebeCambiarPasswordAsync(usuarioId.Value);
            if (!debeCambiar)
            {
                HttpContext.Session.Remove("UsuarioPendienteCambioPasswordID");
                return RedirectToAction("Login", "Login");
            }

            return View(new CambioPasswordInicialViewModel
            {
                UsuarioID = usuarioId.Value
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarPasswordInicial(CambioPasswordInicialViewModel viewModel)
        {
            var usuarioIdSesion = HttpContext.Session.GetInt32("UsuarioPendienteCambioPasswordID");

            if (!usuarioIdSesion.HasValue || usuarioIdSesion.Value != viewModel.UsuarioID)
            {
                return RedirectToAction("Login", "Login");
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var passwordActual = await ObtenerPasswordActualAsync(viewModel.UsuarioID);
            if (passwordActual == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Login");
            }

            if (string.Equals(passwordActual, viewModel.NuevaPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(viewModel.NuevaPassword), "La nueva contraseña debe ser diferente a la contraseña temporal.");
                return View(viewModel);
            }

            await ActualizarPasswordInicialAsync(viewModel.UsuarioID, viewModel.NuevaPassword);

            HttpContext.Session.Clear();
            TempData["SuccessMessage"] = "Contraseña actualizada correctamente. Ingresa nuevamente con tu nueva contraseña.";

            return RedirectToAction("Login", "Login");
        }

        private async Task<bool> DebeCambiarPasswordAsync(int usuarioId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP 1
                    CASE
                        WHEN ISNULL(DebeCambiarPassword, 0) = 1 THEN 1
                        WHEN FechaUltimoCambioPassword IS NULL THEN 1
                        WHEN FechaUltimoCambioPassword <= DATEADD(MONTH, -2, GETDATE()) THEN 1
                        ELSE 0
                    END AS DebeCambiar
                FROM Usuarios
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await command.ExecuteScalarAsync();
            return result != null && result != DBNull.Value && Convert.ToBoolean(result);
        }

        private async Task<string?> ObtenerPasswordActualAsync(int usuarioId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP 1 Contrasena
                FROM Usuarios
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToString(result);
        }

        private async Task ActualizarPasswordInicialAsync(int usuarioId, string nuevaPassword)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE Usuarios
                SET Contrasena = @NuevaPassword,
                    DebeCambiarPassword = 0,
                    FechaUltimoCambioPassword = GETDATE()
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);
            command.Parameters.AddWithValue("@NuevaPassword", nuevaPassword);

            await command.ExecuteNonQueryAsync();
        }
    }
}
