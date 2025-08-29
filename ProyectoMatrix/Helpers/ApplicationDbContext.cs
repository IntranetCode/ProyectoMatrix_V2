using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : base(options)
    {
    }

    //public DbSet<TuEntidad> TuTabla { get; set; } // Reemplaza con tu entidad

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Empresa> Empresas { get; set; }
    public DbSet<UsuariosEmpresas> UsuariosEmpresas { get; set; }

    public DbSet<Comunicado> Comunicados { get; set; }
    public DbSet<ComunicadoEmpresa> ComunicadoEmpresas { get; set; }

    public DbSet<Webinar> Webinars { get; set; }

    public DbSet<WebinarEmpresa> WebinarsEmpresas { get; set; }

    //  public DbSet<Nivel> NivelesEducativos { get; set; } // Asegúrate de tener una entidad Nivel

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ComunicadoEmpresa>().ToTable("ComunicadosEmpresas");

        base.OnModelCreating(modelBuilder);

        // Relación muchos a muchos entre Comunicado y Empresa
        modelBuilder.Entity<ComunicadoEmpresa>()
            .HasKey(ce => new { ce.ComunicadoID, ce.EmpresaID });

        modelBuilder.Entity<ComunicadoEmpresa>()
            .HasOne(ce => ce.Comunicado)
            .WithMany(c => c.ComunicadosEmpresas)
            .HasForeignKey(ce => ce.ComunicadoID)
            .OnDelete(DeleteBehavior.Cascade); // ← Corregido

        modelBuilder.Entity<ComunicadoEmpresa>()
            .HasOne(ce => ce.Empresa)
            .WithMany(e => e.ComunicadoEmpresas)
            .HasForeignKey(ce => ce.EmpresaID)
            .OnDelete(DeleteBehavior.Cascade); 

        
    modelBuilder.Entity<WebinarEmpresa>()
        .HasKey(we => new { we.WebinarID, we.EmpresaID });

 //Configuracion de relaciones webinar empresa
    modelBuilder.Entity<WebinarEmpresa>()
        .HasOne(we => we.Webinar)
        .WithMany(w => w.WebinarsEmpresas)
        .HasForeignKey(we => we.WebinarID)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WebinarEmpresa>()
        .HasOne(we => we.Empresa)
        .WithMany (e => e.WebinarsEmpresas)
        .HasForeignKey(we => we.EmpresaID)
        .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Webinar>()
            .Property(w => w.FechaCreacion)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();


    }

}