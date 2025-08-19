using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Servicios;

var builder = WebApplication.CreateBuilder(args);

// ? AGREGAR CONFIGURACIÓN DEL SERVIDOR
builder.WebHost.ConfigureKestrel(options =>
{
    // Escuchar en puerto 500 para todas las IPs
   // options.ListenAnyIP(500);
});

// Obtener la cadena de conexión desde appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Registrar el contexto de la base de datos
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ? AGREGAR MVC Controllers
builder.Services.AddControllersWithViews();

// Agregar servicios
builder.Services.AddRazorPages();

// ? CONFIGURAR Session con opciones
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ? SERVICIOS Universidad NS
builder.Services.AddScoped<UniversidadServices>();

// Agregar la autenticación antes de construir la app
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });

var app = builder.Build();

// Configurar el middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ? COMENTAR O QUITAR ESTA LÍNEA para HTTP
// app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

// ? Session DEBE ir antes de Authentication
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ? MAPEAR Controllers ANTES de RazorPages
app.MapControllerRoute(
    name: "universidad",
    pattern: "Universidad/{action=Index}/{id?}",
    defaults: new { controller = "Universidad" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Login}/{id?}");

app.MapRazorPages();

app.Run();