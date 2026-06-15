using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Servicios;
using ProyectoMatrix.ViewModels;

namespace ProyectoMatrix.Controllers
{
    public class StreamingController : Controller
    {
        private readonly IVideoRepository _repository;

        public StreamingController(IVideoRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        public async Task<IActionResult> Index(Guid? id)
        {
            var videos = await _repository.GetActiveAsync();

            var selectedVideo = id.HasValue
                ? videos.FirstOrDefault(video => video.Id == id.Value)
                : videos.FirstOrDefault(video => video.IsFeatured) ?? videos.FirstOrDefault();

            return View(new PlayerViewModel
            {
                Videos = videos,
                SelectedVideo = selectedVideo
            });
        }

        [HttpGet]
        public IActionResult Index2()
        {
            return View();
        }

    }
}