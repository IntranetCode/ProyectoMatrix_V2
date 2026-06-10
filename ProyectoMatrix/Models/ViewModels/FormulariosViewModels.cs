using System.Text.Json.Serialization;

namespace ProyectoMatrix.ViewModels.Formularios
{
    public class FormularioPlantillaViewModel
    {
        public int IdFormulario { get; set; }

        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? Categoria { get; set; }
        public string Modulo { get; set; } = "Logistica";

        public bool Activo { get; set; } = true;
        public bool EsPlantillaBase { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }

        public string? DatosFijosPdfJson { get; set; }

        public List<FormularioCampoViewModel> Campos { get; set; } = new();
    }

    public class FormularioCampoViewModel
    {
        [JsonPropertyName("clave")]
        public string Clave { get; set; } = string.Empty;

        [JsonPropertyName("etiqueta")]
        public string Etiqueta { get; set; } = string.Empty;

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = "texto";

        [JsonPropertyName("obligatorio")]
        public bool Obligatorio { get; set; }

        [JsonPropertyName("copiable")]
        public bool Copiable { get; set; } = true;

        [JsonPropertyName("placeholder")]
        public string? Placeholder { get; set; }

        [JsonPropertyName("opciones")]
        public List<string> Opciones { get; set; } = new();
    }

    public class FormularioRespuestaViewModel
    {
        public int IdRespuesta { get; set; }
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;

        public int UsuarioID { get; set; }

        public string Estado { get; set; } = "Borrador";

        public Dictionary<string, string?> Valores { get; set; } = new();

        public int? RespuestaOrigenID { get; set; }

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }

        public DateTime FechaRegistro { get; set; }
    }

    public class FormularioLlenadoViewModel
    {
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? Categoria { get; set; }

        public List<FormularioCampoViewModel> Campos { get; set; } = new();

        public Dictionary<string, string?> Valores { get; set; } = new();

        public int? RespuestaOrigenID { get; set; }

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }
    }

    public class FormularioRespuestaResumenViewModel
    {
        public int IdRespuesta { get; set; }
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;
        public string? Categoria { get; set; }
        public string Modulo { get; set; } = "Logistica";

        public int UsuarioID { get; set; }

        public string? Folio { get; set; }
        public string? Factura { get; set; }
        public string? Cliente { get; set; }
        public string? Proyecto { get; set; }
        public string? Solicitante { get; set; }
        public string? Departamento { get; set; }
        public string? CentroCosto { get; set; }
        public string? FechaCarga { get; set; }
        public string? HorarioCarga { get; set; }
        public string? DireccionRecoleccion { get; set; }
        public string? DestinoPrincipal { get; set; }
        public string? Fletero { get; set; }
        public string? CostoFlete { get; set; }

        public string Estado { get; set; } = "Borrador";

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }
        public int? RespuestaOrigenID { get; set; }

        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaActualizacion { get; set; }
    }

    public class FormularioRespuestaDetalleViewModel
    {
        public int IdRespuesta { get; set; }

        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;

        public string? Categoria { get; set; }

        public DateTime FechaRegistro { get; set; }

        public List<FormularioRespuestaCampoValorViewModel> Campos { get; set; } = new();
    }

    public class FormularioRespuestaCampoValorViewModel
    {
        public string Clave { get; set; } = string.Empty;

        public string Etiqueta { get; set; } = string.Empty;

        public string Tipo { get; set; } = "texto";

        public string? Valor { get; set; }
    }

}