// Servicios/AutorizarAccionAttribute.cs bloquea o deja pasar al usuario
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Seguridad
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AutorizarAccionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _subMenu;
        private readonly string _accion;

        public AutorizarAccionAttribute(string subMenu, string accion)
        {
            _subMenu = subMenu;
            _accion = accion;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (!user.Identity?.IsAuthenticated ?? true)
            {
                context.Result = new ForbidResult();
                return;
            }

            var usuarioIdStr = user.FindFirstValue("UsuarioID");
            if (!int.TryParse(usuarioIdStr, out var usuarioId))
            {
                context.Result = new ForbidResult();
                return;
            }

            var servicio = context.HttpContext.RequestServices.GetRequiredService<IServicioAcceso>();
            var ok = await servicio.TienePermisoAsync(usuarioId, _subMenu, _accion);
            if (!ok) context.Result = new ForbidResult();
        }
    }
}
