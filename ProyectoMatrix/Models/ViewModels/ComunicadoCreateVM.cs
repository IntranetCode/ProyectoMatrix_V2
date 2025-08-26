using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ProyectoMatrix.Models
{
    public class ComunicadoCreateVM
    {
        
        public string NombreComunicado { get; set; } = string.Empty;
        public string? Descripcion { get; set; } = string.Empty;
        public bool EsPublico { get; set; } = true;
        public int ComunicadoID { get; set; }
        public int? UsuarioCreadorID { get; set; }
        // Fecha de creación se establece automáticamente al crear el comunicado    CAMBIAR POR TODAY
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public IFormFile? ImagenFile { get; set; }
        public int[] EmpresasSeleccionadas { get; set; } = Array.Empty<int>();

        public IEnumerable<Empresa> Empresas { get; set; } = new List<Empresa>();
    }
}
