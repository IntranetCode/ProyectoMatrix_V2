// Usings para nuestro módulo
using ProyectoMatrix.Areas.AdminUsuarios.Interfaces;
using ProyectoMatrix.Areas.AdminUsuarios.Services;

// Usings existentes
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// --- Configuración de Servicios ---

// 1. Controladores, Vistas y Razor Pages
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
builder.Services.AddRazorPages();

// 2. AÑADIDO: Habilita la validación del lado del cliente en toda la aplicación
builder.Services.AddRazorPages().AddViewOptions(options =>
{
    options.HtmlHelperOptions.ClientValidationEnabled = true;
});


// 3. Registramos el DbContext (una sola vez)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 4. Registramos todos tus servicios
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<UniversidadServices>();
builder.Services.AddScoped<ServicioNotificaciones>();
builder.Services.AddScoped<BitacoraService>();

// 5. Configuración de Sesión y Autenticación
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });

// ... tu código de AddAuthorization si tienes ...

var app = builder.Build();

// --- Configuración del Pipeline de Peticiones ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// --- Configuración de Rutas (Endpoints) ---

// Ruta para las Areas
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Ruta por defecto
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Login}/{id?}");

// AÑADIDO: Habilita el mapeo para Razor Pages
app.MapRazorPages();

app.Run();