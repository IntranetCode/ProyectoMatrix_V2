using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProyectoMatrix.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Areas.AdminUsuarios.DTOs
{
    public class UsuarioEdicionDTO
    {
        [Required]
        public int UsuarioID { get; set; }

        public string Nombre { get; set; }

        public string ApellidoPaterno { get; set; }

        public string? ApellidoMaterno { get; set; }

        public string Correo { get; set; }

        public string? Telefono { get; set; }

        public int RolID { get; set; }

        public bool Activo { get; set; }

        public List<int> EmpresasIDs { get; set; } = new List<int>();

        // AÑADIDO
        public List<int> SubMenuIDs { get; set; } = new List<int>();

        [ValidateNever]
        public IEnumerable<AuditoriaUsuario> HistorialDeCambios { get; set; }
    }
}