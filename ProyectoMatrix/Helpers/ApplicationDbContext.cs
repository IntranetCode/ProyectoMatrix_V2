using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ModelUsuarios;

// Este es el namespace donde residen tus modelos y el DbContext
namespace ProyectoMatrix.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
        {
        }

        public DbSet<UsuarioPerfilViewModel> PerfilUsuarioResults => Set<UsuarioPerfilViewModel>();



        // --- DbSets para el módulo de Usuarios y Auditoría ---
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Persona> Personas { get; set; }
        public DbSet<Rol> Roles { get; set; }
        public DbSet<Empresa> Empresas { get; set; }
        public DbSet<UsuariosEmpresas> UsuariosEmpresas { get; set; }
        public DbSet<AuditoriaUsuario> AuditoriasUsuarios { get; set; }
        public DbSet<V_InformacionUsuarioCompleta> InformacionUsuariosCompletos { get; set; }

        // --- AÑADIDOS PARA EL MÓDULO DE PERMISOS ---
        public DbSet<Menu> Menus { get; set; }
        public DbSet<SubMenu> SubMenus { get; set; }
        public DbSet<Permiso> Permisos { get; set; }
        // ---------------------------------------------

        // DbSets para el módulo de Comunicados
        public DbSet<Comunicado> Comunicados { get; set; }
        public DbSet<ComunicadoEmpresa> ComunicadoEmpresas { get; set; }

        // DbSets para el módulo de Webinars
        public DbSet<Webinar> Webinars { get; set; }
        public DbSet<WebinarEmpresa> WebinarsEmpresas { get; set; }

        // DbSets para el módulo de Notificaciones
        public DbSet<Notificacion> Notificaciones { get; set; }
        public DbSet<NotificacionLectura> NotificacionLecturas { get; set; }
        public DbSet<NotificacionEmpresas> NotificacionEmpresas { get; set; }
        public DbSet<PermisosPorRol> PermisosPorRol { get; set; }
        public DbSet<SubMenuAcciones> SubMenuAcciones { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- CONFIGURACIONES MÓDULO USUARIOS ---
            modelBuilder.Entity<AuditoriaUsuario>().HasNoKey();
            modelBuilder.Entity<Persona>().ToTable("Persona");

            modelBuilder.Entity<V_InformacionUsuarioCompleta>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("V_InformacionUsuarioCompleta");
            });

            // --- CONFIGURACIONES MÓDULO COMUNICADOS ---
            modelBuilder.Entity<ComunicadoEmpresa>().ToTable("ComunicadosEmpresas");
            modelBuilder.Entity<ComunicadoEmpresa>().HasKey(ce => new { ce.ComunicadoID, ce.EmpresaID });

            modelBuilder.Entity<ComunicadoEmpresa>()
                .HasOne(ce => ce.Comunicado)
                .WithMany(c => c.ComunicadosEmpresas)
                .HasForeignKey(ce => ce.ComunicadoID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ComunicadoEmpresa>()
                .HasOne(ce => ce.Empresa)
                .WithMany(e => e.ComunicadoEmpresas)
                .HasForeignKey(ce => ce.EmpresaID)
                .OnDelete(DeleteBehavior.Cascade);

            // --- CONFIGURACIONES MÓDULO WEBINARS ---
            modelBuilder.Entity<WebinarEmpresa>().HasKey(we => new { we.WebinarID, we.EmpresaID });

            modelBuilder.Entity<WebinarEmpresa>()
                .HasOne(we => we.Webinar)
                .WithMany(w => w.WebinarsEmpresas)
                .HasForeignKey(we => we.WebinarID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WebinarEmpresa>()
                .HasOne(we => we.Empresa)
                .WithMany(e => e.WebinarsEmpresas)
                .HasForeignKey(we => we.EmpresaID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Webinar>()
                .Property(w => w.FechaCreacion)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .ValueGeneratedOnAdd();

            // --- CONFIGURACIONES MÓDULO NOTIFICACIONES ---
            modelBuilder.Entity<NotificacionLectura>()
                .HasIndex(x => new { x.NotificacionId, x.UsuarioId }).IsUnique();

            modelBuilder.Entity<NotificacionLectura>()
                .HasOne(x => x.Notificacion)
                .WithMany()
                .HasForeignKey(x => x.NotificacionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NotificacionEmpresas>()
                .HasIndex(x => new { x.EmpresaId, x.NotificacionId });

            modelBuilder.Entity<NotificacionEmpresas>()
                .HasOne(x => x.Notificacion)
                .WithMany()
                .HasForeignKey(x => x.NotificacionId)
                .OnDelete(DeleteBehavior.Cascade);


            base.OnModelCreating(modelBuilder);

            // Configurar el ViewModel como sin clave (keyless)
            modelBuilder.Entity<UsuarioPerfilViewModel>().HasNoKey();

            modelBuilder.Entity<UsuarioPerfilViewModel>().HasNoKey();
        }
    }
}