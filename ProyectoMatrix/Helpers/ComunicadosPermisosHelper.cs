using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace ProyectoMatrix.Helpers
{
    public static class ComunicadosPermisosHelper
    {
        public enum RolU
        {
            AdministradorIntranet = 1,
            AdministradorTI = 2,
            PropietarioContenido = 3,
            AutorEditor = 4,
            UsuarioFinal = 5,
            Auditor = 6
        }

        public static class Acciones
        {
            public const string CrearComunicado = "crear_comunicado";
            public const string EditarComunicado = "editar_comunicado";
            public const string EliminarComunicado = "eliminar_comunicado";
            public const string AprobarCurso = "aprobar_curso";
            public const string AsignarCurso = "asignar_curso";
            public const string VerReportes = "ver_reportes";
            public const string ConfigurarSistema = "configurar_sistema";
            public const string GenerarCertificado = "generar_certificado";
            public const string SubirArchivos = "subir_archivos";
            public const string TomarCurso = "tomar_curso";
        }

    }
}
