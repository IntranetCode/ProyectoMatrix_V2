using System.Text.Json;
using System.Text.Json.Serialization;
using ProyectoMatrix.Models;

namespace ProyectoMatrix.Servicios
{
    public class JsonVideoRepository : IVideoRepository
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public JsonVideoRepository(IWebHostEnvironment environment)
        {
            var dataFolder = Path.Combine(environment.ContentRootPath, "Data");
            Directory.CreateDirectory(dataFolder);
            _filePath = Path.Combine(dataFolder, "videos.json");

            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "[]");
            }
        }

        public async Task<IReadOnlyList<VideoItem>> GetAllAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var videos = await ReadAllInternalAsync();
                return videos
                    .OrderByDescending(video => video.CreatedAtUtc)
                    .ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IReadOnlyList<VideoItem>> GetActiveAsync()
        {
            var videos = await GetAllAsync();
            return videos
                .Where(video => video.IsActive)
                .ToList();
        }

        public async Task<VideoItem?> GetByIdAsync(Guid id)
        {
            var videos = await GetAllAsync();
            return videos.FirstOrDefault(video => video.Id == id);
        }

        public async Task AddAsync(VideoItem video)
        {
            await _lock.WaitAsync();
            try
            {
                var videos = await ReadAllInternalAsync();
                video.Id = video.Id == Guid.Empty ? Guid.NewGuid() : video.Id;
                video.CreatedAtUtc = DateTime.UtcNow;
                videos.Add(video);
                await WriteAllInternalAsync(videos);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task UpdateAsync(VideoItem video)
        {
            await _lock.WaitAsync();
            try
            {
                var videos = await ReadAllInternalAsync();
                var index = videos.FindIndex(item => item.Id == video.Id);

                if (index < 0)
                {
                    throw new InvalidOperationException("La transmisión no existe.");
                }

                video.CreatedAtUtc = videos[index].CreatedAtUtc;
                videos[index] = video;
                await WriteAllInternalAsync(videos);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            await _lock.WaitAsync();
            try
            {
                var videos = await ReadAllInternalAsync();
                videos.RemoveAll(video => video.Id == id);
                await WriteAllInternalAsync(videos);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<List<VideoItem>> ReadAllInternalAsync()
        {
            await using var stream = File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0)
            {
                return new List<VideoItem>();
            }

            var videos = await JsonSerializer.DeserializeAsync<List<VideoItem>>(stream, _jsonOptions);
            return videos ?? new List<VideoItem>();
        }

        private async Task WriteAllInternalAsync(List<VideoItem> videos)
        {
            await using var stream = File.Open(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, videos, _jsonOptions);
        }
    }
}