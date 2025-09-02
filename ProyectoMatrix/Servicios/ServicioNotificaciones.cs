using ProyectoMatrix.Models;

namespace ProyectoMatrix.Servicios
{
    public class ServicioNotificaciones
    {

        private readonly ApplicationDbContext _context;

        public ServicioNotificaciones(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task EmitirCursoAsignado(int idCurso,string nombreCurso, int usuarioId)
        {
            var notif = new Notificacion
            {
                Tipo = "CursoAsignado",
                Titulo = "Nuevo Curso Asignado",
                Mensaje = $"Se te ha asignado un nuevo curso con nombre: {nombreCurso}",
                IdOrigen = idCurso,
                TablaOrigen = "Cursos",
                UsuarioId = usuarioId,
               
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
               
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirWebinarAsignado(int idWebinar, string nombreWebinar,DateTime fecha, int usuarioId)
        {
            var notif = new Notificacion
            {
                Tipo = "WebinarAsignado",
                Titulo = "Nuevo Webinar Asignado",
                Mensaje = $"Se te ha asignado un nuevo webinar con nombre: {nombreWebinar}-{fecha:dd/MM/yyyy HH:mm}",
                IdOrigen = idWebinar,
                TablaOrigen = "Webinars",
                UsuarioId = usuarioId,
               
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
               
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirComunicadoEmpresa(int idComunicado, string tituloComunicado, int empresaId)
        {
            var notif = new Models.Notificacion
            {
                Tipo = "Comunicado_Nuevo",
                Titulo = "Nuevo Comunicado",
                Mensaje = tituloComunicado,
                IdOrigen = idComunicado,
                TablaOrigen = "Comunicados",
                EmpresaId = empresaId,
               
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
               
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirComunicadoUsuarios(int idComunicado, string tituloComunicado, IEnumerable<int> usuarios)
        {
           foreach (var uid in usuarios)
                _context.Notificaciones.Add(new Models.Notificacion
                {
                    Tipo = "Comunicado_Nuevo",
                    Titulo = "Nuevo Comunicado",
                    Mensaje = tituloComunicado,
                    IdOrigen = idComunicado,
                    TablaOrigen = "Comunicados",
                    UsuarioId = uid,
                   
                    FechaExpiracion = DateTime.UtcNow.AddDays(30),
                   
                });
          
           
            await _context.SaveChangesAsync();
        }

        public async Task EmitirGlobal(string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen)
        {
            var n= new Notificacion
            {
                Tipo = tipo,Titulo = titulo,Mensaje = mensaje,
                IdOrigen = idOrigen,TablaOrigen = tablaOrigen,
                UsuarioId = null,EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
FechaCreacion= DateTime.UtcNow
            };
            _context.Notificaciones.Add(n);
            await _context.SaveChangesAsync();
        }


        public async Task EmitirParaEmpresas (string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen, IEnumerable<int> empresas)
        {
            var n = new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = null,
                EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
                FechaCreacion = DateTime.UtcNow
            };
            _context.Notificaciones.Add(n);
            await _context.SaveChangesAsync();

            foreach(var eid in empresas.Distinct())
            
                _context.NotificacionEmpresas.Add(new NotificacionEmpresas
                {
                    NotificacionId = n.Id,
                    EmpresaId = eid
                });
                await _context.SaveChangesAsync();
            
        }

        public async Task EmitirUsuario(string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen, int usuarioId)
        {
           _context.Notificaciones.Add( new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = usuarioId,
                EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
                FechaCreacion = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }
}
