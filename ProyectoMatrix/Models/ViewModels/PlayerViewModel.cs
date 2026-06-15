using ProyectoMatrix.Models;

namespace ProyectoMatrix.ViewModels;

public class PlayerViewModel
{
    public IReadOnlyList<VideoItem> Videos { get; set; } = Array.Empty<VideoItem>();
    public VideoItem? SelectedVideo { get; set; }
}
