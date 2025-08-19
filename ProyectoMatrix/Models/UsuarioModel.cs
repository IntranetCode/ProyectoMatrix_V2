using System;
using System.Collections.Generic;

namespace ProyectoMatrix.Models
{
    public class UsuarioModel
    {
        public int UsuarioID { get; set; }
        public int PersonaID { get; set; }
        public int EmpresaID { get; set; }
        public string Username { get; set; }

        // Contraseña hasheada en la base de datos
        public string Password { get; set; } = string.Empty;

        // Datos de la persona
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;

        // Datos de la empresa
        public string NombreEmpresa { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;

        // Rol del usuario (Ej. "Administrador", "Colaborador")
        public string Rol { get; set; } = string.Empty;

        // Lista de empresas asociadas (para mostrar modal si hay más de una)
        public List<EmpresaModel> Empresas { get; set; } = new List<EmpresaModel>();
    }

    public class EmpresaModel
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
    }
}
