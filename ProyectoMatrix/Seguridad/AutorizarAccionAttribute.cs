// Servicios/AutorizarAccionAttribute.cs bloquea o deja pasar al usuario

//PRUEBA
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using ProyectoMatrix.Servicios;
using System.Security.Claims;

namespace ProyectoMatrix.Seguridad  
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AutorizarAccionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string[] _subMenus;
        private readonly string _accion;

        // subMenus: admite "Comunicados|Ver Comunicados" o "Comunicados, Ver Comunicados"
        public AutorizarAccionAttribute(string subMenus, string accion)
        {
            _accion = accion;
            _subMenus = (subMenus ?? "")
                .Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var user = http.User;

            // No autenticado → login
            if (!(user?.Identity?.IsAuthenticated ?? false))
            { context.Result = new ChallengeResult(); return; }

            // UsuarioID
            if (!int.TryParse(user.FindFirstValue("UsuarioID"), out var usuarioId))
            { context.Result = new ChallengeResult(); return; }

            // ¿Tiene permiso en alguno de los submenús?
            var acceso = http.RequestServices.GetRequiredService<IServicioAcceso>();
            foreach (var sm in _subMenus)
                if (await acceso.TienePermisoAsync(usuarioId, sm, _accion))
                    return; // OK
                            // ----- SIN PERMISO -----
            bool isAjax =
                string.Equals(http.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) ||
                http.Request.Headers["Accept"].Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

            if (isAjax)
            {
                // Para llamadas AJAX: 403 con JSON (el front muestra el toast)
                context.Result = new JsonResult(new { ok = false, message = "No tienes permiso para realizar esta acción." })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            // mini HTML con alert y regreso en caso de que el usuario no tenga permisos de esa accion solo
            //muestre la alerta
            var html = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'></head>
<body>
<script>
  alert('No tienes permiso para realizar esta acción.');
  if (document.referrer) { window.location = document.referrer; } else { window.location = '/'; }
</script>
</body></html>";

            context.Result = new ContentResult { Content = html, ContentType = "text/html" };
        }
    }
}