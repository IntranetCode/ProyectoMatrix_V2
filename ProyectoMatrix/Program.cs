//Configuración y conexión a base de datos
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Controllers;
using ProyectoMatrix.Servicios;

using Microsoft.AspNetCore.Server.IIS;

using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

// ? AGREGAR ESTAS LÍNEAS PARA ARCHIVOS GRANDES
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 268435456; // 256 MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});


builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 268435456; // 256 MB
});

// ? AGREGAR CONFIGURACIÓN DEL SERVIDOR
builder.WebHost.ConfigureKestrel(options =>
{
    // Escuchar en puerto 500 para todas las IPs
    //options.ListenAnyIP(5001);

    // ? AGREGAR LÍMITES PARA KESTREL TAMBIÉN
    options.Limits.MaxRequestBodySize = 268435456; // 256 MB
});

// ? AGREGAR CONFIGURACIÓN DEL SERVIDOR
//builder.WebHost.ConfigureKestrel(options =>
//{
    // Escuchar en puerto 500 para todas las IPs
   // options.ListenAnyIP(500);
//});

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



builder.Services.AddAuthorization();



// Agregar la autenticación antes de construir la app
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });





// Registrar el servicio de notificacionesa
builder.Services.AddScoped<ServicioNotificaciones>();


//Restra el servicio de Bitacora
builder.Services.AddScoped<BitacoraService>();

builder.Services.AddDistributedMemoryCache();

//Registrando el nuevo servicio creado que es sobre acceso

builder.Services.AddScoped<IServicioAcceso, ServicioAcceso>();

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


app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ? Session DEBE ir antes de Authentication
app.UseSession();
app.UseAuthentication();
app.UseMiddleware<MiddlewareContextoSolicitud>();

app.UseAuthorization();


app.MapControllers();

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