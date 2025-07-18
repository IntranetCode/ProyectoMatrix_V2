using System;

namespace ProyectoMatrix.Models
{
    public class UsuarioModel
    {
        public int UsuarioID { get; set; }
        public int PersonaID { get; set; }
        public int EmpresaID { get; set; }
        public string Username { get; set; }

        // Hasheado en la base de datos, para verificar con PasswordHasher
        public string PasswordHash { get; set; } = string.Empty;

        // Datos de la persona
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;

        // Datos de la empresa
        public string NombreEmpresa { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;

        // Nuevo: Rol del usuario (Ej. "Administrador", "Colaborador")
        public string Rol { get; set; } = string.Empty;
    }
}

