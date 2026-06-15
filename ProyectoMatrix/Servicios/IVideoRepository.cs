using ProyectoMatrix.Models;

namespace ProyectoMatrix.Servicios
{
    public interface IVideoRepository
    {
        Task<IReadOnlyList<VideoItem>> GetAllAsync();
        Task<IReadOnlyList<VideoItem>> GetActiveAsync();
        Task<VideoItem?> GetByIdAsync(Guid id);
        Task AddAsync(VideoItem video);
        Task UpdateAsync(VideoItem video);
        Task DeleteAsync(Guid id);
    }
}