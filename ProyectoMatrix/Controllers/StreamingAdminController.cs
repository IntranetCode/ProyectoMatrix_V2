using Microsoft.AspNetCore.Mvc;
using ProyectoMatrix.Models;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class StreamingAdminController : Controller
    {
        private readonly IVideoRepository _repository;

        public StreamingAdminController(IVideoRepository repository)
        {
            _repository = repository;
        }

        public async Task<IActionResult> Index()
        {
            var videos = await _repository.GetAllAsync();
            return View(videos);
        }

        public IActionResult Create()
        {
            return View(new VideoItem
            {
                Type = VideoType.Hls,
                IsActive = true
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VideoItem video)
        {
            if (!ModelState.IsValid)
            {
                return View(video);
            }

            await _repository.AddAsync(video);
            TempData["Message"] = "Transmisión agregada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(Guid id)
        {
            var video = await _repository.GetByIdAsync(id);
            return video is null ? NotFound() : View(video);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, VideoItem video)
        {
            if (id != video.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(video);
            }

            await _repository.UpdateAsync(video);
            TempData["Message"] = "Transmisión actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(Guid id)
        {
            var video = await _repository.GetByIdAsync(id);
            return video is null ? NotFound() : View(video);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            await _repository.DeleteAsync(id);
            TempData["Message"] = "Transmisión eliminada correctamente.";
            return RedirectToAction(nameof(Index));
        }
    }
}