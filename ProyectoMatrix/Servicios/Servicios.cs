using ProyectoMatrix.Models;
using System;
namespace ProyectoMatrix.Servicios
{
    public class Servicios
    {
        private readonly ApplicationDbContext _context;

        public Servicios(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UsuarioPerfilViewModel?> ObtenerPerfilPorUsernameAsync(string username)
        {
            var sql = """
                SELECT 
                  CONCAT(p.Nombre, ' ', p.ApellidoPaterno, ' ', p.ApellidoMaterno) AS NombreUsuario,
                  u.Username,
                  p.Correo,
                  p.Telefono,
                  r.NombreRol,
                  r.Descripcion AS DescripcionRol
                FROM Persona p
                LEFT JOIN Usuarios u ON p.PersonaID = u.PersonaID
                LEFT JOIN Roles r ON r.RolID = u.RolID
                WHERE u.Username = {0}
                LIMIT 1
                """;

            return await _context.PerfilUsuarioResults
                .FromSqlRaw(sql, username) 
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

    }
}
