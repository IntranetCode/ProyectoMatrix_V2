using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System;

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

        [HttpGet("Contar")]
        public async Task<IActionResult> Contar()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioId")?? HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaId")?? HttpContext.Session.GetInt32("EmpresaID");
            if (usuarioId == null && empresaId == null) return Json(new { total = 0 });

            var ahora = DateTime.UtcNow;

            var total = await _context.Notificaciones
              .Where(n => n.FechaEliminacion == null && n.FechaExpiracion > ahora)
              .Where(n =>
                   (n.UsuarioId == null && n.EmpresaId == null
                        && !_context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id))      // GLOBAL
                || (empresaId != null && (
                        _context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id && ne.EmpresaId == empresaId)
                     || n.EmpresaId == empresaId))                                                  // EMPRESA
                || (usuarioId != null && n.UsuarioId == usuarioId)                                  // USUARIO
              )
              .Where(n => !_context.NotificacionLecturas
                       .Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId))
              .CountAsync();

            return Json(new { total });
        }



        [HttpGet("Listar")]
        public async Task<IActionResult> Listar(int pagina = 1, int tamanio = 10, bool soloNoLeidas = false)
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaId") ?? HttpContext.Session.GetInt32("EmpresaID");
            if (usuarioId == null && empresaId == null)
                return Json(new { total = 0, pagina, tamanio, items = Array.Empty<object>() });

            var ahora = DateTime.UtcNow;

            var q = _context.Notificaciones
              .Where(n => n.FechaEliminacion == null && n.FechaExpiracion > ahora)
              .Where(n =>
                   (n.UsuarioId == null && n.EmpresaId == null
                        && !_context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id))      // GLOBAL
                || (empresaId != null && (
                        _context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id && ne.EmpresaId == empresaId)
                     || n.EmpresaId == empresaId))                                                  // EMPRESA
                || (usuarioId != null && n.UsuarioId == usuarioId)                                  // USUARIO
              );

            if (soloNoLeidas)
                q = q.Where(n => !_context.NotificacionLecturas
                            .Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId));

            q = q.OrderByDescending(n => n.FechaCreacion);

            var total = await q.CountAsync();

            var items = await q.Skip((pagina - 1) * tamanio).Take(tamanio)
              .Select(n => new {
                  n.Id,
                  n.Titulo,
                  n.Mensaje,
                  n.Tipo,
                  n.IdOrigen,
                  n.TablaOrigen,
                  EsLeida = _context.NotificacionLecturas
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
            var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaId") ?? HttpContext.Session.GetInt32("EmpresaID");
            if (usuarioId == null && empresaId == null) return BadRequest();

            var aplica = await _context.Notificaciones.AnyAsync(n =>
                n.Id == id && (
                    (n.UsuarioId == null && n.EmpresaId == null
                         && !_context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id))    // GLOBAL
                  || (empresaId != null && (
                         _context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id && ne.EmpresaId == empresaId)
                      || n.EmpresaId == empresaId))                                                // EMPRESA
                  || (usuarioId != null && n.UsuarioId == usuarioId)                               // USUARIO
                )
            );
            if (!aplica) return NotFound();

            var ya = await _context.NotificacionLecturas
                .AnyAsync(l => l.NotificacionId == id && l.UsuarioId == usuarioId);
            if (!ya)
            {
                _context.NotificacionLecturas.Add(new NotificacionLectura
                {
                    NotificacionId = id,
                    UsuarioId = usuarioId!.Value,
                    FechaLeida = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            return Ok(new { ok = true });
        }




        [HttpPost("MarcarTodasLeidas")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarTodasLeidas()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? HttpContext.Session.GetInt32("UsuarioID");
            var empresaId = HttpContext.Session.GetInt32("EmpresaId") ?? HttpContext.Session.GetInt32("EmpresaID");
            if (usuarioId == null && empresaId == null) return BadRequest();

            var ahora = DateTime.UtcNow;

            // Notis que aplican a este usuario (GLOBAL / EMPRESA / USUARIO) y aún NO leídas por él
            var ids = await _context.Notificaciones
                .Where(n => n.FechaEliminacion == null && n.FechaExpiracion > ahora)
                .Where(n =>
                     (n.UsuarioId == null && n.EmpresaId == null
                        && !_context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id))      // GLOBAL
                  || (empresaId != null && (
                         _context.NotificacionEmpresas.Any(ne => ne.NotificacionId == n.Id && ne.EmpresaId == empresaId)
                      || n.EmpresaId == empresaId))                                                  // EMPRESA (compat)
                  || (usuarioId != null && n.UsuarioId == usuarioId)                                 // USUARIO
                )
                .Where(n => !_context.NotificacionLecturas.Any(l => l.NotificacionId == n.Id && l.UsuarioId == usuarioId))
                .Select(n => n.Id)
                .ToListAsync();

            foreach (var id in ids)
                _context.NotificacionLecturas.Add(new NotificacionLectura
                {
                    NotificacionId = id,
                    UsuarioId = usuarioId!.Value,
                    FechaLeida = DateTime.UtcNow
                });

            await _context.SaveChangesAsync();
            return Ok(new { ok = true, total = ids.Count });
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
            var notif = new Models.Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = usuarioId,
                EmpresaId = empresaId,
                FechaCreacion = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
            };

            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();

            return Ok(new { ok = true, notif.Id });
        }

       



    }
}
