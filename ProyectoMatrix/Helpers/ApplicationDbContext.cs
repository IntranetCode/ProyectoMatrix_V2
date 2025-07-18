using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : base(options)
    {
    }

    //public DbSet<TuEntidad> TuTabla { get; set; } // Reemplaza con tu entidad
}