using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Controllers;
using ProyectoMatrix.Servicios;
using System.Security.Claims;

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

builder.Services.AddScoped<AsignacionesController>();

builder.Services.AddAuthorization();



// Agregar la autenticación antes de construir la app
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });



builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("GestionComunicados", policy =>
        policy.RequireAssertion(ctx =>
        {
            // por nombre de rol:
            var r = ctx.User.FindFirst(ClaimTypes.Role)?.Value;
            if (r == "Administrador de Intranet" || r == "Propietario de Contenido" || r == "Autor/Editor de Contenido")
                return true;

            // o por RolID:
            var rid = ctx.User.FindFirst("RolID")?.Value;
            return rid == "1" || rid == "3" || rid == "4";
        }));
});
//CONFIGURAAR PARA QUE ACEPTE ARCHIVOS GRANDES, EN ESTE CASO VIDEOS

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104_857_600; // 100 MB
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



app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    await next();
});



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