using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProyectoMatrix.Servicios;
using System.Security.Claims;

namespace ProyectoMatrix.Seguridad
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AutorizarAccionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _subMenu;
        private readonly string? _accion;

        public AutorizarAccionAttribute(string subMenu, string? accion = null)
        {
            _subMenu = subMenu;
            _accion = accion;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var user = http.User;
            if (!(user?.Identity?.IsAuthenticated ?? false))
            { context.Result = new ChallengeResult(); return; }

            if (!int.TryParse(user.FindFirstValue("UsuarioID"), out var usuarioId))
            { context.Result = new ChallengeResult(); return; }

            var acceso = http.RequestServices.GetRequiredService<IServicioAcceso>();
            var ok = await acceso.TienePermisoAsync(usuarioId, _subMenu, _accion);
            if (ok) { return; }

            // 403 estándar
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
