// Usings para nuestro m�dulo
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Areas.AdminUsuarios.Services;


//Configuraci�n y conexi�n a base de datos derarrollo y productivo
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Controllers;
using ProyectoMatrix.Helpers;

using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;

using ProyectoMatrix.Servicios;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

// ? AGREGAR ESTAS L�NEAS PARA ARCHIVOS GRANDES
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 268435456; // 256 MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});


// ? AGREGAR CONFIGURACI�N DEL SERVIDOR
builder.WebHost.ConfigureKestrel(options =>
{
    // Escuchar en puerto 500 para todas las IPs
    //options.ListenAnyIP(5001);

    // ? AGREGAR L�MITES PARA KESTREL TAMBI�N
    options.Limits.MaxRequestBodySize = 268435456; // 256 MB
});

// ? AGREGAR CONFIGURACI�N DEL SERVIDOR
//builder.WebHost.ConfigureKestrel(options =>
//{
// Escuchar en puerto 500 para todas las IPs
// options.ListenAnyIP(500);
//});


// Obtener la cadena de conexi�n desde appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Registrar el contexto de la base de datos
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ? AGREGAR MVC Controllers // 1. Controladores, Vistas y Razor Pages
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

// Agregar servicios
builder.Services.AddRazorPages();



// 2. A�ADIDO: Habilita la validaci�n del lado del cliente en toda la aplicaci�n
builder.Services.AddRazorPages().AddViewOptions(options =>
{
    options.HtmlHelperOptions.ClientValidationEnabled = true;
});




// ? CONFIGURAR Session con opciones
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Agregar la autenticaci�n antes de construir la app
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });


builder.Services.AddAuthorization();





// 4. Registramos todos tus servicios
builder.Services.AddScoped<IUsuarioService, UsuarioService>();


// ? SERVICIOS Universidad NS
builder.Services.AddScoped<UniversidadServices>();


// Registrar el servicio de notificacionesa
builder.Services.AddScoped<ServicioNotificaciones>();


//Restra el servicio de Bitacora
builder.Services.AddScoped<BitacoraService>();

//Se agrega el servicio para la ruta NAS

builder.Services.AddScoped<RutaNas>();



builder.Services.AddDistributedMemoryCache();



builder.Services.AddHttpContextAccessor();


//Registrando el nuevo servicio creado que es sobre acceso

builder.Services.AddScoped<IServicioAcceso, ServicioAcceso>();

var app = builder.Build();

// Configurar el middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ? COMENTAR O QUITAR ESTA L�NEA para HTTP
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