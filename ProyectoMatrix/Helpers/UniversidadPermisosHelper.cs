// Helpers/UniversidadPermisosHelper.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using static ProyectoMatrix.Helpers.UniversidadPermisosHelper;

namespace ProyectoMatrix.Helpers
{
    public static class UniversidadPermisosHelper
    {
        public enum Rol
        {
            AdministradorIntranet = 1,
            AdministradorTI = 2,
            PropietarioContenido = 3,
            AutorEditor = 4,
            UsuarioFinal = 5,
            Auditor = 6,
            Test = 8
        }

        public static class Acciones
        {
            public const string CrearCurso = "crear_curso";
            public const string EditarCurso = "editar_curso";
            public const string EliminarCurso = "eliminar_curso";
            public const string AprobarCurso = "aprobar_curso";
            public const string AsignarCurso = "asignar_curso";
            public const string VerReportes = "ver_reportes";
            public const string ConfigurarSistema = "configurar_sistema";
            public const string GenerarCertificado = "generar_certificado";
            public const string SubirArchivos = "subir_archivos";
            public const string TomarCurso = "tomar_curso";
        }

        public static class PermisosUniversidad
        {
            // --- helpers de rol
            public static bool EsAdministradorIntranet(int rolId) => rolId == (int)Rol.AdministradorIntranet;
            public static bool EsAdministradorTI(int rolId) => rolId == (int)Rol.AdministradorTI;
            public static bool EsPropietarioContenido(int rolId) => rolId == (int)Rol.PropietarioContenido;
            public static bool EsAutorEditor(int rolId) => rolId == (int)Rol.AutorEditor;
            public static bool EsUsuarioFinal(int rolId) => rolId == (int)Rol.UsuarioFinal || rolId == (int)Rol.Test;
            public static bool EsAuditor(int rolId) => rolId == (int)Rol.Auditor;

            // --- permisos generales
            public static bool PuedeGestionarTodo(int rolId) =>
                EsAdministradorIntranet(rolId);

            public static bool PuedeConfigurarSistema(int rolId) =>
                EsAdministradorIntranet(rolId) || EsAdministradorTI(rolId);

            public static bool PuedeAprobarCursos(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId);

            public static bool PuedeCrearCursos(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId) || EsAutorEditor(rolId);

            public static bool PuedeTomarCursos(int rolId) =>
                true;

            public static bool PuedeVerReportes(int rolId) =>
                EsAdministradorIntranet(rolId) || EsAdministradorTI(rolId) || EsPropietarioContenido(rolId) || EsAuditor(rolId);

            public static bool PuedeAsignarCursos(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId) || EsAutorEditor(rolId);

            public static bool PuedeVerTodosUsuarios(int rolId) =>
                EsAdministradorIntranet(rolId) || EsAdministradorTI(rolId) || EsPropietarioContenido(rolId) || EsAuditor(rolId);

            public static bool PuedeEliminarCursos(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId);

            public static bool PuedeGenerarCertificados(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId);

            public static bool PuedeGestionarPlantillas(int rolId) =>
                EsAdministradorIntranet(rolId) || EsAdministradorTI(rolId);

            public static bool PuedeSubirArchivos(int rolId) =>
                EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId) || EsAutorEditor(rolId);

            // --- por módulo (ejemplos)
            public static class Dashboard
            {
                public static bool PuedeVerEstadisticasGenerales(int rolId) => PuedeVerReportes(rolId);
                public static bool PuedeVerTodosLosCursos(int rolId) => PuedeVerTodosUsuarios(rolId);
                public static bool PuedeVerNotificacionesAdmin(int rolId) => EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId);
            }

            public static class GestionCursos
            {
                public static bool PuedeCrearNiveles(int rolId) => EsAdministradorIntranet(rolId);
                public static bool PuedeEditarNiveles(int rolId) => EsAdministradorIntranet(rolId);
                public static bool PuedeEliminarNiveles(int rolId) => EsAdministradorIntranet(rolId);
                public static bool PuedeVerCursosBorradores(int rolId) => PuedeCrearCursos(rolId);
                public static bool PuedePublicarCursos(int rolId) => PuedeAprobarCursos(rolId);
            }

            public static class Asignaciones
            {
                public static bool PuedeAsignarPorEmpresa(int rolId) => EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId) || EsAutorEditor(rolId);
                public static bool PuedeAsignarPorDepartamento(int rolId) => EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId) || EsAutorEditor(rolId);
                public static bool PuedeAsignarIndividual(int rolId) => PuedeAsignarCursos(rolId);
                public static bool PuedeRevocarAsignaciones(int rolId) => EsAdministradorIntranet(rolId) || EsPropietarioContenido(rolId);
                public static bool PuedeVerHistorialAsignaciones(int rolId) => PuedeVerReportes(rolId);
            }

            // --- helpers de validación
            public static bool ValidarAccion(int rolId, string accion)
            {
                if (rolId <= 0) return false;
                switch (accion?.ToLowerInvariant())
                {
                    case Acciones.CrearCurso: return PuedeCrearCursos(rolId);
                    case Acciones.EditarCurso: return PuedeCrearCursos(rolId);
                    case Acciones.EliminarCurso: return PuedeEliminarCursos(rolId);
                    case Acciones.AprobarCurso: return PuedeAprobarCursos(rolId);
                    case Acciones.AsignarCurso: return PuedeAsignarCursos(rolId);
                    case Acciones.VerReportes: return PuedeVerReportes(rolId);
                    case Acciones.ConfigurarSistema: return PuedeConfigurarSistema(rolId);
                    case Acciones.GenerarCertificado: return PuedeGenerarCertificados(rolId);
                    case Acciones.SubirArchivos: return PuedeSubirArchivos(rolId);
                    case Acciones.TomarCurso: return PuedeTomarCursos(rolId);
                    default: return false;
                }
            }

            public static List<string> GetAccionesPermitidas(int rolId)
            {
                var acciones = new List<string>();
                if (PuedeTomarCursos(rolId)) acciones.Add(Acciones.TomarCurso);
                if (PuedeCrearCursos(rolId)) acciones.AddRange(new[] { Acciones.CrearCurso, Acciones.EditarCurso });
                if (PuedeEliminarCursos(rolId)) acciones.Add(Acciones.EliminarCurso);
                if (PuedeAprobarCursos(rolId)) acciones.Add(Acciones.AprobarCurso);
                if (PuedeAsignarCursos(rolId)) acciones.Add(Acciones.AsignarCurso);
                if (PuedeVerReportes(rolId)) acciones.Add(Acciones.VerReportes);
                if (PuedeConfigurarSistema(rolId)) acciones.Add(Acciones.ConfigurarSistema);
                if (PuedeGenerarCertificados(rolId)) acciones.Add(Acciones.GenerarCertificado);
                if (PuedeSubirArchivos(rolId)) acciones.Add(Acciones.SubirArchivos);
                return acciones;
            }

            public static string GetNivelAcceso(int rolId) => rolId switch
            {
                (int)Rol.AdministradorIntranet => "Administrador Total",
                (int)Rol.AdministradorTI => "Administrador Técnico",
                (int)Rol.PropietarioContenido => "Gestor de Contenidos",
                (int)Rol.AutorEditor => "Creador de Contenidos",
                (int)Rol.UsuarioFinal => "Usuario Estudiante",
                (int)Rol.Test
                => "Usuario Estudiante",
                (int)Rol.Auditor => "Auditor de Solo Lectura",
                _ => "Sin Acceso"
            };

            public static bool PuedeAccederModulo(int rolId, string modulo)
            {
                switch (modulo?.ToLowerInvariant())
                {
                    case "dashboard": return true;
                    case "mis_cursos": return PuedeTomarCursos(rolId);
                    case "certificados": return PuedeTomarCursos(rolId);
                    case "gestion_cursos": return PuedeCrearCursos(rolId);
                    case "asignaciones": return PuedeAsignarCursos(rolId);
                    case "reportes": return PuedeVerReportes(rolId);
                    case "configuracion": return PuedeConfigurarSistema(rolId);
                    case "administracion": return PuedeGestionarTodo(rolId);
                    default: return false;
                }
            }
        }



        // -------- Atributos + Filtro de autorización --------
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
        public class RequierePermisoAttribute : Attribute
        {
            public string Accion { get; }
            public RequierePermisoAttribute(string accion) => Accion = accion;
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
        public class RequiereRolAttribute : Attribute
        {
            public int[] RolesPermitidos { get; }
            public RequiereRolAttribute(params int[] rolesPermitidos) => RolesPermitidos = rolesPermitidos;
        }

        /// <summary>
        /// Filtro que ejecuta los atributos anteriores.
        /// Regístralo globalmente o por controlador.
        /// </summary>
        public class UniversidadAuthorizeFilter : IAuthorizationFilter
        {
            public void OnAuthorization(AuthorizationFilterContext context)
            {
                var http = context.HttpContext;
                var rolId = http.Session.GetInt32("RolID");

                // No autenticado / sin rol
                if (!rolId.HasValue || rolId.Value <= 0)
                {
                    context.Result = new ForbidResult();
                    return;
                }

                var endpoint = context.ActionDescriptor;
                var requiresPerm = context.Filters; // no se usa aquí

                // Lee atributos en acción/controlador
                var actionAttrs = context.ActionDescriptor.EndpointMetadata;
                // RequiereRol
                foreach (var meta in actionAttrs)
                {
                    if (meta is RequiereRolAttribute rr)
                    {
                        var ok = Array.Exists(rr.RolesPermitidos, r => r == rolId.Value);
                        if (!ok) { context.Result = new ForbidResult(); return; }
                    }
                    if (meta is RequierePermisoAttribute rp)
                    {
                        if (!PermisosUniversidad.ValidarAccion(rolId.Value, rp.Accion))
                        {
                            context.Result = new ForbidResult(); return;
                        }
                    }
                }
            }
        }
    }

    // -------- Extensiones para controladores (si usas MVC) --------

}
