using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AuditarAccionAttribute : Attribute, IAsyncActionFilter
{
    public string Modulo { get; init; } = "";
    public string Entidad { get; init; } = "";
  
    public bool OmitirListas { get; init; } = true;

    private static readonly HashSet<string> AccionesDeLista = new(StringComparer.OrdinalIgnoreCase)
    { "Index", "Lista", "Gestionar" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var metodo = http.Request.Method.ToUpperInvariant();
        var nombreAccion = context.ActionDescriptor.RouteValues.TryGetValue("action", out var act) ? act ?? "" : "";

       
        var nombre = nombreAccion.ToLowerInvariant();

        string accion = metodo switch
        {
            "DELETE" => "ELIMINAR",
            "PUT" or "PATCH" => "EDITAR",
            "POST" => nombre.Contains("elimin") || nombre.Contains("borr") || nombre.Contains("remov")
                            ? "ELIMINAR"
                            : nombre.Contains("edit") || nombre.Contains("actualiz")
                                ? "EDITAR"
                                : "CREAR",
            _ => "VER"
        };
        // Detectar si es lista o detalle (solo en GET)
        bool esLista = accion == "VER" && AccionesDeLista.Contains(nombreAccion);

        // IdEntidad si viene en parámetros
        string? idEntidad = context.ActionArguments
            .Where(kv => kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value?.ToString())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        // Si es lista y se decidió omitir, solo ejecutar la acción y salir
        if (esLista && OmitirListas)
        {
            await next();
            return;
        }

        // Ajustar nombres cuando es VER
        if (accion == "VER")
            accion = esLista ? "VER_LISTA" : (idEntidad is null ? "VER" : "VER_DETALLE");

        var ejecutado = await next();
        var resultado = (ejecutado.Exception != null && !ejecutado.ExceptionHandled) ? "ERROR" : "OK";

        // Severidad sugerida
        byte severidad = resultado == "ERROR"
            ? (byte)3
            : accion.StartsWith("VER", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)4;

        string? mensaje = resultado == "ERROR" ? ejecutado.Exception?.Message : null;

        // === Datos de contexto para auditoría ===
        var solicitudId = http.Items["SolicitudId"]?.ToString();
        var ip = (http.Items["DireccionIp"]?.ToString())
                            ?? http.Connection.RemoteIpAddress?.ToString();
        var agenteUsuario = http.Items["AgenteUsuario"]?.ToString();

        try
        {
            var bitacora = http.RequestServices.GetRequiredService<BitacoraService>();

            // Ids de usuario/empresa (claims; si no existen, quedan en null)
            int? idUsuario = int.TryParse(http.User.FindFirst("UsuarioID")?.Value, out var uid) ? uid : null;
            int? idEmpresa = int.TryParse(http.User.FindFirst("EmpresaID")?.Value, out var eid) ? eid : null;

            await bitacora.RegistrarAsync(
                idUsuario: idUsuario,
                idEmpresa: idEmpresa,
                accion: accion,
                mensaje: mensaje,
                modulo: string.IsNullOrWhiteSpace(Modulo)
                            ? (context.RouteData.Values["controller"]?.ToString()?.ToUpperInvariant() ?? "")
                            : Modulo,
                entidad: string.IsNullOrWhiteSpace(Entidad) ? "Entidad" : Entidad,
                entidadId: esLista ? null : idEntidad,   // en listas no guardamos Id específico
                resultado: resultado,
                severidad: severidad,
                solicitudId: solicitudId,
                ip: ip,
               AgenteUsuario: agenteUsuario
            );
        }
        catch
        {
            // Nunca romper el flujo por fallas de bitácora
        }
    }
}
