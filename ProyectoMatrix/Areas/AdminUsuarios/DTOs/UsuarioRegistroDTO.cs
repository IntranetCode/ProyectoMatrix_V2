using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Areas.AdminUsuarios.DTOs
{
    public class UsuarioRegistroDTO
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        public string ApellidoPaterno { get; set; }

        public string? ApellidoMaterno { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "El formato del correo no es válido.")]
        public string Correo { get; set; }

        public string? Telefono { get; set; }

        public string? Direccion { get; set; }

        public DateTime? FechaNacimiento { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "El rol es obligatorio.")]
        public int RolID { get; set; }

        [Required(ErrorMessage = "Debe seleccionar al menos una empresa.")]
        public List<int> EmpresasIDs { get; set; } = new List<int>();

        // AÑADIDO
        public List<int> SubMenuIDs { get; set; } = new List<int>();
    }
}