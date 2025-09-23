// Servicios/AutorizarAccionAttribute.cs bloquea o deja pasar al usuario
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using ProyectoMatrix.Servicios;

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
            var user = context.HttpContext.User;
            if (!(user?.Identity?.IsAuthenticated ?? false))
            {
                context.Result = new ChallengeResult(); // a Login
                return;
            }

            var usuarioIdStr = user.FindFirstValue("UsuarioID");
            if (!int.TryParse(usuarioIdStr, out var usuarioId))
            {
                context.Result = new ChallengeResult();
                return;
            }

            var acceso = context.HttpContext.RequestServices.GetRequiredService<IServicioAcceso>();

            // Autoriza si CUALQUIER submenú concede la acción (OR)
            foreach (var sm in _subMenus)
            {
                if (await acceso.TienePermisoAsync(usuarioId, sm, _accion))
                    return; // permitido
            }

            context.Result = new ForbidResult(); // logueado pero sin permiso → 403 / AccessDenied
        }
    }
}