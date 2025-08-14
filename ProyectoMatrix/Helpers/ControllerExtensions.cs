// Archivo: Helpers/ControllerExtensions.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace ProyectoMatrix.Helpers
{
    public static class ControllerExtensions
    {
        public static bool TienePermiso(this Controller controller, string accion)
        {
            var rolId = controller.HttpContext.Session.GetInt32("RolID");
            return rolId.HasValue && UniversidadPermisosHelper.PermisosUniversidad.ValidarAccion(rolId.Value, accion);
        }

        public static int? GetRolUsuario(this Controller controller) =>
            controller.HttpContext.Session.GetInt32("RolID");

        public static bool EsAdministrador(this Controller controller)
        {
            var rolId = controller.GetRolUsuario();
            return rolId.HasValue && UniversidadPermisosHelper.PermisosUniversidad.EsAdministradorIntranet(rolId.Value);
        }
    }
}
