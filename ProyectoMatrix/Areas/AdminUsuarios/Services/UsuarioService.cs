using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Areas.AdminUsuarios.DTOs;
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;
using System.Data;
using System.Linq;

namespace ProyectoMatrix.Areas.AdminUsuarios.Services
{
    public class UsuarioService : IUsuarioService
    {
        private readonly ApplicationDbContext _context;

        public UsuarioService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<V_InformacionUsuarioCompleta>> ObtenerTodosAsync(bool? activos, string? filtroCampo, string? terminoBusqueda)
        {
            var query = _context.InformacionUsuariosCompletos.AsQueryable();
            if (activos.HasValue)
            {
                query = query.Where(u => u.Activo == activos.Value);
            }
            if (!string.IsNullOrWhiteSpace(terminoBusqueda))
            {
                var busquedaLower = terminoBusqueda.ToLower();
                switch (filtroCampo)
                {
                    case "Nombre": query = query.Where(u => u.Nombre.ToLower().Contains(busquedaLower)); break;
                    case "ApellidoPaterno": query = query.Where(u => u.ApellidoPaterno.ToLower().Contains(busquedaLower)); break;
                    case "Correo": query = query.Where(u => u.Correo != null && u.Correo.ToLower().Contains(busquedaLower)); break;
                    case "Username": query = query.Where(u => u.Username.ToLower().Contains(busquedaLower)); break;
                    default:
                        query = query.Where(u =>
                            u.Username.ToLower().Contains(busquedaLower) ||
                            u.Nombre.ToLower().Contains(busquedaLower) ||
                            u.ApellidoPaterno.ToLower().Contains(busquedaLower) ||
                            (u.Correo != null && u.Correo.ToLower().Contains(busquedaLower))
                        );
                        break;
                }
            }
            return await query.OrderBy(u => u.Nombre).ToListAsync();
        }

        // ✅ ESTE ES EL MÉTODO CON LA LÓGICA ACTUALIZADA
        public async Task<UsuarioEdicionDTO?> ObtenerParaEditarAsync(int usuarioId)
        {
            var usuario = await _context.Usuarios
                .Include(u => u.Persona)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UsuarioID == usuarioId);

            if (usuario == null || usuario.Persona == null) return null;

            // 1. Obtiene los SubMenuID que vienen del ROL del usuario.
            var permisosDelRolIds = await _context.PermisosPorRol
                .Where(pr => pr.RolID == usuario.RolID)
                .Join(_context.SubMenuAcciones,
                      pr => pr.SubMenuAccionID,
                      sma => sma.SubMenuAccionID,
                      (pr, sma) => sma.SubMenuID)
                .Distinct()
                .ToListAsync();

            // 2. Obtiene los SubMenuID asignados DIRECTAMENTE al usuario en la tabla Permisos.
            var permisosDirectosIds = await _context.Permisos
                .Where(p => p.UsuarioID == usuarioId)
                .Select(p => p.SubMenuID)
                .Distinct()
                .ToListAsync();

            // 3. Combina ambas listas para obtener los permisos efectivos.
            var permisosEfectivosIds = permisosDelRolIds.Union(permisosDirectosIds).ToList();

            // Se obtiene el resto de la información como antes
            var empresasIds = await _context.UsuariosEmpresas
                .Where(ue => ue.UsuarioID == usuarioId)
                .Select(ue => ue.EmpresaID)
                .ToListAsync();

            var historial = await ObtenerHistorialAsync(usuarioId);

            return new UsuarioEdicionDTO
            {
                UsuarioID = usuario.UsuarioID,
                Nombre = usuario.Persona.Nombre,
                ApellidoPaterno = usuario.Persona.ApellidoPaterno,
                ApellidoMaterno = usuario.Persona.ApellidoMaterno,
                Correo = usuario.Persona.Correo,
                Telefono = usuario.Persona.Telefono,
                RolID = usuario.RolID,
                Activo = usuario.Activo,
                EmpresasIDs = empresasIds,
                SubMenuIDs = permisosEfectivosIds, // Se usa la lista combinada.
                HistorialDeCambios = historial
            };
        }

        public async Task RegistrarAsync(UsuarioRegistroDTO nuevoUsuario)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(nuevoUsuario.Password);
            var empresasTable = new DataTable();
            empresasTable.Columns.Add("ID", typeof(int));
            if (nuevoUsuario.EmpresasIDs != null)
            {
                foreach (var empresaId in nuevoUsuario.EmpresasIDs) { empresasTable.Rows.Add(empresaId); }
            }
            var empresasParam = new SqlParameter
            {
                ParameterName = "@EmpresasIDs",
                SqlDbType = SqlDbType.Structured,
                Value = empresasTable,
                TypeName = "dbo.ListaDeEnteros"
            };
            var subMenusTable = new DataTable();
            subMenusTable.Columns.Add("ID", typeof(int));
            if (nuevoUsuario.SubMenuIDs != null)
            {
                foreach (var subMenuId in nuevoUsuario.SubMenuIDs) { subMenusTable.Rows.Add(subMenuId); }
            }
            var subMenusParam = new SqlParameter
            {
                ParameterName = "@SubMenuIDs",
                SqlDbType = SqlDbType.Structured,
                Value = subMenusTable,
                TypeName = "dbo.ListaDeEnteros"
            };
            var parameters = new object[]
            {
                new SqlParameter("@Nombre", nuevoUsuario.Nombre),
                new SqlParameter("@ApellidoPaterno", nuevoUsuario.ApellidoPaterno),
                new SqlParameter("@Correo", nuevoUsuario.Correo),
                new SqlParameter("@Username", nuevoUsuario.Username),
                new SqlParameter("@ContrasenaHash", passwordHash),
                new SqlParameter("@RolID", nuevoUsuario.RolID),
                new SqlParameter("@ApellidoMaterno", (object)nuevoUsuario.ApellidoMaterno ?? DBNull.Value),
                new SqlParameter("@Telefono", (object)nuevoUsuario.Telefono ?? DBNull.Value),
                empresasParam,
                subMenusParam
            };
            await _context.Database.ExecuteSqlRawAsync(
                "EXEC sp_RegistrarUsuario @Nombre, @ApellidoPaterno, @Correo, @Username, @ContrasenaHash, @RolID, @ApellidoMaterno, @Telefono, @EmpresasIDs, @SubMenuIDs",
                parameters);
        }

        public async Task ActualizarAsync(UsuarioEdicionDTO usuario)
        {
            var empresasTable = new DataTable();
            empresasTable.Columns.Add("ID", typeof(int));
            if (usuario.EmpresasIDs != null)
            {
                foreach (var empresaId in usuario.EmpresasIDs) { empresasTable.Rows.Add(empresaId); }
            }
            var empresasParam = new SqlParameter
            {
                ParameterName = "@EmpresasIDs",
                SqlDbType = SqlDbType.Structured,
                Value = empresasTable,
                TypeName = "dbo.ListaDeEnteros"
            };
            var subMenusTable = new DataTable();
            subMenusTable.Columns.Add("ID", typeof(int));
            if (usuario.SubMenuIDs != null)
            {
                foreach (var subMenuId in usuario.SubMenuIDs) { subMenusTable.Rows.Add(subMenuId); }
            }
            var subMenusParam = new SqlParameter
            {
                ParameterName = "@SubMenuIDs",
                SqlDbType = SqlDbType.Structured,
                Value = subMenusTable,
                TypeName = "dbo.ListaDeEnteros"
            };
            var parameters = new object[]
            {
                new SqlParameter("@UsuarioID", usuario.UsuarioID),
                new SqlParameter("@Nombre", usuario.Nombre),
                new SqlParameter("@ApellidoPaterno", usuario.ApellidoPaterno),
                new SqlParameter("@Correo", usuario.Correo),
                new SqlParameter("@RolID", usuario.RolID),
                new SqlParameter("@Activo", usuario.Activo),
                new SqlParameter("@ApellidoMaterno", (object)usuario.ApellidoMaterno ?? DBNull.Value),
                new SqlParameter("@Telefono", (object)usuario.Telefono ?? DBNull.Value),
                empresasParam,
                subMenusParam
            };
            await _context.Database.ExecuteSqlRawAsync(
                "EXEC sp_ActualizarUsuario @UsuarioID, @Nombre, @ApellidoPaterno, @Correo, @RolID, @Activo, @ApellidoMaterno, @Telefono, @EmpresasIDs, @SubMenuIDs",
                parameters);
        }

        public async Task DarDeBajaAsync(int usuarioId)
        {
            var parameter = new SqlParameter("@UsuarioID", usuarioId);
            await _context.Database.ExecuteSqlRawAsync("EXEC sp_DarDeBajaUsuario @UsuarioID", parameter);
        }

        public async Task<IEnumerable<AuditoriaUsuario>> ObtenerHistorialAsync(int usuarioId)
        {
            var parameter = new SqlParameter("@UsuarioID", usuarioId);
            var historial = await _context.AuditoriasUsuarios
                .FromSqlRaw("EXEC sp_ObtenerAuditoriaDeUsuario @UsuarioID", parameter)
                .ToListAsync();
            return historial ?? new List<AuditoriaUsuario>();
        }

        public async Task<bool> TienePermisoAsync(int usuarioId, string nombreAccion)
        {
            var usuarioIdParam = new SqlParameter("@UsuarioID", usuarioId);
            var accionParam = new SqlParameter("@NombreAccion", nombreAccion);
            var resultParam = new SqlParameter { ParameterName = "@Result", SqlDbType = SqlDbType.Bit, Direction = ParameterDirection.Output };
            await _context.Database.ExecuteSqlRawAsync("SET @Result = dbo.FN_UsuarioTienePermiso(@UsuarioID, @NombreAccion)", resultParam, usuarioIdParam, accionParam);
            return (bool)resultParam.Value;
        }

        public async Task<List<MenuViewModel>> ObtenerMenusConSubMenusAsync()
        {
            return await _context.Menus
                .Include(m => m.SubMenus)
                .OrderBy(m => m.Nombre)
                .Select(m => new MenuViewModel
                {
                    MenuID = m.MenuID,
                    Nombre = m.Nombre,
                    SubMenus = m.SubMenus.Select(sm => new SubMenuViewModel
                    {
                        SubMenuID = sm.SubMenuID,
                        Nombre = sm.Nombre
                    }).ToList()
                }).ToListAsync();
        }

        public async Task<bool> VerificarPermisoAsync(int usuarioId, int subMenuId)
        {
            // 1. Primero, busca un permiso DIRECTO en la tabla Permisos.
            //    Si existe, el usuario tiene acceso y no necesitamos buscar más.
            bool tienePermisoDirecto = await _context.Permisos
                .AnyAsync(p => p.UsuarioID == usuarioId && p.SubMenuID == subMenuId);

            if (tienePermisoDirecto)
            {
                return true; // Permiso concedido por asignación directa
            }

            // 2. Si no hay permiso directo, busca el permiso heredado del ROL.
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return false; // El usuario no existe
            }

            bool tienePermisoPorRol = await _context.PermisosPorRol
                .Where(pr => pr.RolID == usuario.RolID)
                .Join(_context.SubMenuAcciones,
                      pr => pr.SubMenuAccionID,
                      sma => sma.SubMenuAccionID,
                      (pr, sma) => sma.SubMenuID)
                .AnyAsync(id => id == subMenuId);

            return tienePermisoPorRol; // Devuelve true si el rol lo permite, sino false
        }

        public async Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int menuId)
        {
            // Obtiene todos los SubMenuID que pertenecen al Menu principal
            var subMenuIdsDelMenu = await _context.SubMenus
                .Where(sm => sm.MenuID == menuId)
                .Select(sm => sm.SubMenuID)
                .ToListAsync();

            if (!subMenuIdsDelMenu.Any())
            {
                return false; // El menú no tiene submenús, no se puede dar acceso
            }

            // Comprueba si el usuario tiene permiso para CUALQUIERA de esos submenús
            foreach (var subMenuId in subMenuIdsDelMenu)
            {
                if (await VerificarPermisoAsync(usuarioId, subMenuId))
                {
                    return true; // Encontramos un permiso, concedemos acceso al menú principal
                }
            }

            return false; // No se encontró ningún permiso para ningún submenú
        }

    }
}