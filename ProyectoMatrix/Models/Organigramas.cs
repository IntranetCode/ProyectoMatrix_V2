using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class OrganigramasIndexVm
    {
        public bool EsEditor { get; set; }

        public bool PuedeCrear { get; set; }

        public bool PuedeEditar { get; set; }

        public bool PuedeEliminar { get; set; }

        public string? AlertaSistema { get; set; }

        public List<OrganigramaListaVm> Organigramas { get; set; } = new();
    }

    public class OrganigramaListaVm
    {
        public int OrganigramaID { get; set; }

        public string Titulo { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public string Extension { get; set; } = string.Empty;

        public string MimeType { get; set; } = string.Empty;

        public string EmpresasAsignadas { get; set; } = string.Empty;

        public DateTime FechaCreacion { get; set; }

        public string CreadoPor { get; set; } = string.Empty;

        public bool EsPdf =>
            Extension.Equals("pdf", StringComparison.OrdinalIgnoreCase);

        public bool EsImagen =>
            Extension.Equals("png", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public class OrganigramaEditorVm
    {
        public int OrganigramaID { get; set; }

        [Required(ErrorMessage = "El título es obligatorio.")]
        [StringLength(150)]
        public string Titulo { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        [Display(Name = "Archivo")]
        public IFormFile? Archivo { get; set; }

        [Required(ErrorMessage = "Debe seleccionar al menos una empresa.")]
        public List<int> EmpresasSeleccionadas { get; set; } = new();

        public List<SelectListItem> Empresas { get; set; } = new();

        public string? ArchivoActual { get; set; }

        public string? ExtensionActual { get; set; }
    }
}