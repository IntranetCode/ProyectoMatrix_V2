using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    [Route("[controller]")]
    public class NotificacionesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ServicioNotificaciones _servicio;

        public NotificacionesController(ApplicationDbContext context, ServicioNotificaciones servicio)
        {
            _context = context;
            _servicio = servicio;
        }

        // Helpers
        private (int? usuarioId, int? empresaId) GetIds()
        {
            int? uid = null, eid = null;

            // 1) Claims (preferido)
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claim, out var cid)) uid = cid;

            // 2) Sesión (preferir las variantes con "ID")
            var uID = HttpContext.Session.GetInt32("UsuarioID");
            var uId = HttpContext.Session.GetInt32("UsuarioId");
            var eID = HttpContext.Session.GetInt32("EmpresaID");
            var eId = HttpContext.Session.GetInt32("EmpresaId");

            if (uid == null) uid = uID ?? uId;
            eid = eID ?? eId;

            // Normalizar: si ambas existen y son distintas, sobrescribe la vieja
            if (uID != null && uId != null && uID != uId)
                HttpContext.Session.SetInt32("UsuarioId", uID.Value);
            if (eID != null && eId != null && eID != eId)
                HttpContext.Session.SetInt32("EmpresaId", eID.Value);

            return (uid, eid);
        }

        private IQueryable<Notificacion> QueryVisiblesPara(int? usuarioId, int? empresaId)
        {
            var ahora = DateTime.UtcNow;
            return _context.Notificaciones
                .Where(n => n.FechaEliminacion == null && n.FechaExpiracion > ahora)
                .Where(n =>
                       (n.UsuarioId == null && n.EmpresaId == null
                            && !_context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id)) // GLOBAL
                    || (empresaId != null && (
                            _context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id && ne.EmpresaId == empresaId)
                         || n.EmpresaId == empresaId))                                           // EMPRESA
                    || (usuarioId != null && n.UsuarioId == usuarioId)                            // USUARIO
                );
        }

        [HttpGet("Contar")]
        public async Task<IActionResult> Contar()
        {
            var (usuarioId, empresaId) = GetIds();
            if (usuarioId == null && empresaId == null) return Json(new { total = 0 });

            var visibles = QueryVisiblesPara(usuarioId, empresaId);

            // Si no hay usuario, no se puede saber "leídas"; cuenta todas visibles
            var total = (usuarioId == null)
                ? await visibles.CountAsync()
                : await visibles.CountAsync(n =>
                        !_context.NotificacionLecturas.Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId));

            return Json(new { total });
        }

        [HttpGet("Listar")]
        public async Task<IActionResult> Listar(int pagina = 1, int tamanio = 12, bool soloNoLeidas = false)
        {
            var (usuarioId, empresaId) = GetIds();
            if (usuarioId == null && empresaId == null)
                return Json(new { total = 0, pagina, tamanio, items = Array.Empty<object>() });

            var q = QueryVisiblesPara(usuarioId, empresaId);

            if (soloNoLeidas && usuarioId != null)
            {
                q = q.Where(n => !_context.NotificacionLecturas
                                  .Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId));
            }

            q = q.OrderByDescending(n => n.FechaCreacion);

            var total = await q.CountAsync();

            var items = await q.Skip((pagina - 1) * tamanio).Take(tamanio)
                .Select(n => new
                {
                   n.Id,
                    n.Titulo,
                    n.Mensaje,
                    n.Tipo,
                    n.IdOrigen,
                    n.TablaOrigen,
                    EsLeida = usuarioId != null && _context.NotificacionLecturas
                                  .Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId),
                    n.FechaCreacion
                })
                .ToListAsync();

           return Json(new { total, pagina, tamanio, items });
        }

        [HttpPost("MarcarLeida/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarLeida(int id)
        {
            var (usuarioId, empresaId) = GetIds();
            if (usuarioId == null) return Unauthorized();

            var visible = await QueryVisiblesPara(usuarioId, empresaId).AnyAsync(n => n.Id == id);
            if (!visible) return NotFound();
            var ya = await _context.NotificacionLecturas
                .AnyAsync(l => l.NotificacionId == id && l.UsuarioId == usuarioId);
            if (!ya)
            {
                _context.NotificacionLecturas.Add(new NotificacionLectura
                {
                    NotificacionId = id,
                    UsuarioId = usuarioId.Value,
                    FechaLeida = DateTime.UtcNow
                });
                try { await _context.SaveChangesAsync(); } catch { /* índice UNIQUE */ }
            }
            return Json(new { ok = true });
        }

        [HttpPost("MarcarTodasLeidas")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarTodasLeidas()
        {
            var (usuarioId, empresaId) = GetIds();
            if (usuarioId == null) return Unauthorized();

            var visiblesIds = await QueryVisiblesPara(usuarioId, empresaId)
                .Select(n => n.Id)
                .ToListAsync();

            if (visiblesIds.Count == 0)
                return Json(new { ok = true, total = 0 });

            var yaLeidos = await _context.NotificacionLecturas
               .Where(l => l.UsuarioId == usuarioId && visiblesIds.Contains(l.NotificacionId))
                .Select(l => l.NotificacionId)
                .ToListAsync();

            var faltantes = visiblesIds.Except(yaLeidos).ToList();
            if (faltantes.Count > 0)
            {
                _context.NotificacionLecturas.AddRange(
                    faltantes.Select(id => new NotificacionLectura
                    {
                        NotificacionId = id,
                        UsuarioId = usuarioId.Value,
                        FechaLeida = DateTime.UtcNow
                    })
                );
                await _context.SaveChangesAsync();
            }

            return Json(new { ok = true, total = faltantes.Count });
        }

        [HttpPost("Crear")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(
            [FromForm] string tipo,
            [FromForm] string titulo,
            [FromForm] string? mensaje,
            [FromForm] int idOrigen,
            [FromForm] string tablaOrigen,
            [FromForm] int? usuarioId,
            [FromForm] int? empresaId)
        {
            var notif = new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = usuarioId,
                EmpresaId = empresaId,
                FechaCreacion = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddDays(30)
            };

            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();

            return Json(new { ok = true, id = notif.Id });
        }
    }
}