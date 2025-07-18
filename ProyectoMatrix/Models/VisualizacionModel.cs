using System.Text.Json.Serialization;

namespace ProyectoMatrix.Models
{
    public class VisualizacionModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }
}
