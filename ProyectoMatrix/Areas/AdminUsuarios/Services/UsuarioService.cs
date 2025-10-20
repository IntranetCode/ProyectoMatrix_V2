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

        // ========= NUEVO: sin usar tabla Permisos; usa la TVF para armar SubMenuIDs efectivos (global/NULL) =========
        public async Task<UsuarioEdicionDTO?> ObtenerParaEditarAsync(int usuarioId)
        {
            var usuario = await _context.Usuarios
                .Include(u => u.Persona)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UsuarioID == usuarioId);

            if (usuario == null || usuario.Persona == null) return null;

            // Empresas del usuario
            var empresasIds = await _context.UsuariosEmpresas
                .Where(ue => ue.UsuarioID == usuarioId)
                .Select(ue => ue.EmpresaID)
                .ToListAsync();

            // Historial
            var historial = await ObtenerHistorialAsync(usuarioId);

            // Submenus EFECTIVOS (global = NULL) desde TVF
            var subMenusEfectivos = await ObtenerSubMenusEfectivosAsync(usuarioId, null);

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
                SubMenuIDs = subMenusEfectivos,   // <- ya NO viene de tabla Permisos
                HistorialDeCambios = historial
            };
        }

        // Lee SubMenuIDs efectivos usando la TVF fn_PermisosEfectivosUsuario
        private async Task<List<int>> ObtenerSubMenusEfectivosAsync(int usuarioId, int? empresaId)
        {
            var lista = new List<int>();
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT fe.SubMenuID
FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID) AS fe
WHERE fe.TienePermiso = 1;";
            cmd.CommandType = CommandType.Text;

            var pU = cmd.CreateParameter(); pU.ParameterName = "@UsuarioID"; pU.Value = usuarioId; cmd.Parameters.Add(pU);
            var pE = cmd.CreateParameter(); pE.ParameterName = "@EmpresaID"; pE.Value = (object?)empresaId ?? DBNull.Value; cmd.Parameters.Add(pE);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(rd.GetInt32(0));
            }
            return lista.Distinct().OrderBy(x => x).ToList();
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

            // NOTA: mantenemos SubMenuIDs para no romper el SP existente;
            // si luego deprecamos el parámetro en el SP, quitamos esta sección.
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

            // Igual que arriba: mantenemos por compatibilidad con el SP actual
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

        // Puedes mantener esta función si tu FN_UsuarioTienePermiso ya usa la lógica nueva.
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
            => await VerificarPermisoAsync(usuarioId, null, subMenuId);  // global por defecto

        public async Task<bool> VerificarPermisoAsync(int usuarioId, int? empresaId, int subMenuId)
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1 fe.TienePermiso
FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID) AS fe
WHERE fe.SubMenuID = @SubMenuID;";
            cmd.CommandType = CommandType.Text;

            var pU = cmd.CreateParameter(); pU.ParameterName = "@UsuarioID"; pU.Value = usuarioId; cmd.Parameters.Add(pU);
            var pE = cmd.CreateParameter(); pE.ParameterName = "@EmpresaID"; pE.Value = (object?)empresaId ?? DBNull.Value; cmd.Parameters.Add(pE);
            var pS = cmd.CreateParameter(); pS.ParameterName = "@SubMenuID"; pS.Value = subMenuId; cmd.Parameters.Add(pS);

            var resultObj = await cmd.ExecuteScalarAsync();
            return (resultObj is bool b && b);
        }

        // ========================= OVERRIDES =========================

        public async Task<List<OverrideItemDto>> ListarOverridesAsync(int usuarioId, int? empresaId)
        {
            var result = new List<OverrideItemDto>();
            var conn = _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_Overrides_ListarUsuario";
            cmd.CommandType = CommandType.StoredProcedure;

            var pU = cmd.CreateParameter(); pU.ParameterName = "@UsuarioID"; pU.Value = usuarioId; cmd.Parameters.Add(pU);
            var pE = cmd.CreateParameter(); pE.ParameterName = "@EmpresaID"; pE.Value = (object?)empresaId ?? DBNull.Value; cmd.Parameters.Add(pE);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var item = new OverrideItemDto
                {
                    SubMenuID = rd.GetInt32(rd.GetOrdinal("SubMenuID")),
                    Nombre = rd.GetString(rd.GetOrdinal("Nombre")),
                    Estado = rd.GetInt32(rd.GetOrdinal("Estado")),          // -1, 0, 1
                    PermisoEfectivo = rd.GetBoolean(rd.GetOrdinal("PermisoEfectivo"))
                };
                result.Add(item);
            }
            return result;
        }

        public async Task GuardarOverridesAsync(int usuarioId, int? empresaId, IEnumerable<OverrideItemDto> items)
        {


            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var tx = await (conn as SqlConnection)!.BeginTransactionAsync();

            try
            {
                foreach (var it in items ?? Enumerable.Empty<OverrideItemDto>())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx as SqlTransaction;
                    cmd.CommandText = "dbo.sp_Override_Upsert";
                    cmd.CommandType = CommandType.StoredProcedure;

                    var pU = cmd.CreateParameter(); pU.ParameterName = "@UsuarioID"; pU.Value = usuarioId; cmd.Parameters.Add(pU);
                    var pE = cmd.CreateParameter(); pE.ParameterName = "@EmpresaID"; pE.Value = (object?)empresaId ?? DBNull.Value; cmd.Parameters.Add(pE);
                    var pS = cmd.CreateParameter(); pS.ParameterName = "@SubMenuID"; pS.Value = it.SubMenuID; cmd.Parameters.Add(pS);
                    var pT = cmd.CreateParameter(); pT.ParameterName = "@Estado"; pT.Value = it.Estado; cmd.Parameters.Add(pT);

                    await cmd.ExecuteNonQueryAsync();
                }

                await (tx as SqlTransaction)!.CommitAsync();
            }
            catch
            {
                await (tx as SqlTransaction)!.RollbackAsync();
                throw;
            }
        }

        // ========= Chequeo de menú con empresaId =========
        public async Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int? empresaId, int menuId)
        {
            var subMenuIdsDelMenu = await _context.SubMenus
                .Where(sm => sm.MenuID == menuId)
                .Select(sm => sm.SubMenuID)
                .ToListAsync();

            if (!subMenuIdsDelMenu.Any())
                return false;

            foreach (var subMenuId in subMenuIdsDelMenu)
            {
                if (await VerificarPermisoAsync(usuarioId, empresaId, subMenuId))
                    return true;
            }
            return false;
        }

        public async Task<string> GetMenuHomeUrlAsync(int menuId)
        {
            // 1) Preferir el SubMenú cuyo Nombre empiece por "Ver ..."
            var url = await _context.SubMenus
                .Where(sm => sm.MenuID == menuId && sm.Nombre.StartsWith("Ver"))
                .OrderBy(sm => sm.SubMenuID)
                .Select(sm => sm.UrlEnlace)
                .FirstOrDefaultAsync();

            // 2) Fallback por convención: /Index o /Entrada
            if (string.IsNullOrWhiteSpace(url))
            {
                url = await _context.SubMenus
                    .Where(sm => sm.MenuID == menuId &&
                        (sm.UrlEnlace.EndsWith("/Index") || sm.UrlEnlace.EndsWith("/Entrada")))
                    .Select(sm => sm.UrlEnlace)
                    .FirstOrDefaultAsync();
            }

            // 3) Último fallback
            return string.IsNullOrWhiteSpace(url) ? "/" : url;
        }




        // Wrapper para compatibilidad (global)
        public async Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int menuId)
            => await VerificarPermisoParaMenuAsync(usuarioId, (int?)null, menuId);
    }



}
