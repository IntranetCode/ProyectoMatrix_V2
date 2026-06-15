using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{ 

public enum VideoType
{
    [Display(Name = "Transmisión en vivo")]
    Hls,

    [Display(Name = "Video guardado")]
    Mp4,

    [Display(Name = "Reproductor externo")]
    Iframe
}

public class VideoQualityOption
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class VideoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Escribe el nombre del contenido.")]
    [StringLength(120, ErrorMessage = "El nombre no puede superar 120 caracteres.")]
    [Display(Name = "Nombre público")]
    public string Title { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La descripción no puede superar 500 caracteres.")]
    [Display(Name = "Descripción corta")]
    public string? Description { get; set; }

    [StringLength(80, ErrorMessage = "La categoría no puede superar 80 caracteres.")]
    [Display(Name = "Categoría")]
    public string? Category { get; set; }

    [StringLength(30, ErrorMessage = "La duración no puede superar 30 caracteres.")]
    [Display(Name = "Duración o estado")]
    public string? DurationLabel { get; set; }

    [Url(ErrorMessage = "Usa un enlace válido que comience con http o https.")]
    [StringLength(2000, ErrorMessage = "El enlace de portada es demasiado largo.")]
    [Display(Name = "Imagen de portada")]
    public string? PosterUrl { get; set; }

    [Required(ErrorMessage = "Selecciona cómo se reproducirá.")]
    [Display(Name = "Tipo de contenido")]
    public VideoType Type { get; set; } = VideoType.Hls;

    [Required(ErrorMessage = "Agrega el enlace principal.")]
    [Url(ErrorMessage = "Usa un enlace válido que comience con http o https.")]
    [StringLength(2000, ErrorMessage = "El enlace principal es demasiado largo.")]
    [Display(Name = "Enlace .m3u8 principal")]
    public string Url { get; set; } = string.Empty;

    [Url(ErrorMessage = "Usa un enlace válido que comience con http o https.")]
    [StringLength(2000, ErrorMessage = "El enlace de calidad alta es demasiado largo.")]
    [Display(Name = "Enlace calidad alta")]
    public string? Url1080p { get; set; }

    [Url(ErrorMessage = "Usa un enlace válido que comience con http o https.")]
    [StringLength(2000, ErrorMessage = "El enlace de calidad media es demasiado largo.")]
    [Display(Name = "Enlace calidad media")]
    public string? Url720p { get; set; }

    [Url(ErrorMessage = "Usa un enlace válido que comience con http o https.")]
    [StringLength(2000, ErrorMessage = "El enlace de calidad ligera es demasiado largo.")]
    [Display(Name = "Enlace calidad ligera")]
    public string? Url480p { get; set; }

    [Display(Name = "Visible para usuarios")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Mostrar primero")]
    public bool IsFeatured { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string GetAudienceSourceName() => Type switch
    {
        VideoType.Hls => "En vivo",
        VideoType.Mp4 => "Disponible",
        VideoType.Iframe => "Reproductor propio",
        _ => "Listo"
    };

    public IReadOnlyList<VideoQualityOption> GetQualityOptions()
    {
        var options = new List<VideoQualityOption>
        {
            new() { Key = "auto", Label = "Automática", Url = Url }
        };

        AddOption(options, "high", "Alta", Url1080p);
        AddOption(options, "medium", "Media", Url720p);
        AddOption(options, "light", "Ligera", Url480p);

        return options;
    }

    private static void AddOption(List<VideoQualityOption> options, string key, string label, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            options.Add(new VideoQualityOption
            {
                Key = key,
                Label = label,
                Url = url
            });
        }
    }
}
}