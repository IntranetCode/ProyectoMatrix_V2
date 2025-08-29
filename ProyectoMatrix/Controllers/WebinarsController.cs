using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoMatrix.Models;

namespace ProyectoMatrix.Controllers
{
    [Authorize]
    public class WebinarsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public WebinarsController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }


        private int? GetUsuarioId()
            => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

        private int? GetEmpresaId()
            => int.TryParse(User.FindFirst("EmpresaID")?.Value, out var id) ? id : (int?)null;

        private bool EsGestor()
            => User.IsInRole("Autor/Editor de Contenido")
            || User.IsInRole("Administrador de Intranet")
            || User.IsInRole("Propietario de Contenido");

        private List<SelectListItem> GetEmpresasSelect()
            => _db.Empresas
                 .Select(e => new SelectListItem { Value = e.EmpresaID.ToString(), Text = e.Nombre })
                 .ToList();

        private async Task<string?> GuardarImagenAsync(IFormFile? archivo)
        {
            if (archivo == null || archivo.Length == 0) return null;

            var carpeta = Path.Combine(_env.WebRootPath, "uploads", "webinars");
            Directory.CreateDirectory(carpeta);

            var nombre = $"{Guid.NewGuid()}{Path.GetExtension(archivo.FileName)}";
            var rutaFisica = Path.Combine(carpeta, nombre);

            using (var fs = new FileStream(rutaFisica, FileMode.Create))
                await archivo.CopyToAsync(fs);

            // ruta relativa para BD
            return $"/uploads/webinars/{nombre}";
        }

        private void EliminarImagenFisica(string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa)) return;
            var ruta = Path.Combine(_env.WebRootPath, rutaRelativa.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(ruta))
                System.IO.File.Delete(ruta);
        }

        private async Task<string> GetDirigidoAAsync(int webinarId, bool esPublico)
        {
            if (esPublico) return "Todas";
            var nombres = await _db.WebinarsEmpresas
                .Where(we => we.WebinarID == webinarId)
                .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => e.Nombre)
                .OrderBy(n => n)
                .ToListAsync();
            return nombres.Count == 0 ? "-" : string.Join(", ", nombres);
        }

        


        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }


        [HttpGet]
        [AllowAnonymous] // opcional
        public async Task<IActionResult> Lista()
        {
            var empresaId = GetEmpresaId();
            var ahora = DateTime.Now;

            var baseQ = _db.Webinars.AsNoTracking().Where(w =>
                w.EsPublico ||
                (empresaId.HasValue &&
                 _db.WebinarsEmpresas.Any(we => we.WebinarID == w.WebinarID && we.EmpresaID == empresaId.Value))
            );


            //PARA HACER DESTACADO EL PROXIMO
            var proximo = await baseQ
                .Where(w => w.FechaInicio > ahora)
                .OrderBy(w => w.FechaInicio)
                .FirstOrDefaultAsync(); 

            WebinarListItemVm? destacadoVm = null;
            if(proximo != null)
            {
                destacadoVm = new WebinarListItemVm
                {
                    WebinarID = proximo.WebinarID,
                    Titulo = proximo.Titulo,
                    Descripcion = proximo.Descripcion,
                    FechaInicio = proximo.FechaInicio,
                    FechaFin = proximo.FechaFin,
                    UrlTeams = proximo.UrlTeams,
                    Imagen = proximo.Imagen,
                    DirigidoA = proximo.EsPublico ? "Todas" : await GetDirigidoAAsync(proximo.WebinarID, proximo.EsPublico)
                };
                ViewBag.Destacado = destacadoVm;
            }
            else
            {
                ViewBag.Destacado = null;
            }

                var lista = await baseQ
                    .OrderBy(w => w.FechaInicio)
                    .Select(w => new WebinarListItemVm
                    {
                        WebinarID = w.WebinarID,
                        Titulo = w.Titulo,
                        Descripcion = w.Descripcion,
                        FechaInicio = w.FechaInicio,
                        FechaFin = w.FechaFin,
                        UrlTeams = w.UrlTeams,
                        Imagen = w.Imagen,
                        DirigidoA = w.EsPublico ? "Todas" : ""
                    })
                    .ToListAsync();

            var idsNoPublicos = lista.Where(x => x.DirigidoA == "").Select(x => x.WebinarID).ToList();
            if (idsNoPublicos.Count > 0)
            {
                var empresasPorWebinar = await _db.WebinarsEmpresas
                    .Where(we => idsNoPublicos.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new { we.WebinarID, e.Nombre })
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g => g.Key, g => string.Join(", ", g.Select(x => x.Nombre).OrderBy(n => n)));

                foreach (var item in lista.Where(x => x.DirigidoA == ""))
                    item.DirigidoA = empresasPorWebinar.TryGetValue(item.WebinarID, out var nombres) ? nombres : "-";
            }

            return View("~/Views/Lider/LiderLista.cshtml", lista);
        }


        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpGet]
        public async Task<IActionResult> MisWebinars()
        {
            var userId = GetUsuarioId();
            if (!userId.HasValue) return Forbid();

            var lista = await _db.Webinars
                .AsNoTracking()
                .Where(w => w.UsuarioCreadorID == userId.Value)
                .OrderByDescending(w => w.FechaCreacion)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID = w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico ? "Todas" : ""
                })
                .ToListAsync();
            var ids = lista.Where(x => x.DirigidoA == "").Select(x => x.WebinarID).ToList();
            if (ids.Count > 0)
            {
                var map = await _db.WebinarsEmpresas
                    .Where(we => ids.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new { we.WebinarID, e.Nombre })
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g => g.Key, g => string.Join(", ", g.Select(x => x.Nombre).OrderBy(n => n)));
                foreach (var x in lista.Where(x => x.DirigidoA == ""))
                    x.DirigidoA = map.TryGetValue(x.WebinarID, out var noms) ? noms : "-";
            }

            return View("~/Views/Lider/LiderLista.cshtml", lista); // si prefieres, crea otra vista
        }


        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var w = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id.Value);
            if (w == null) return NotFound();

            if (!w.EsPublico && !EsGestor())
            {
                var empresaId = GetEmpresaId();
                var asignado = empresaId.HasValue && await _db.WebinarsEmpresas
                    .AnyAsync(we => we.WebinarID == w.WebinarID && we.EmpresaID == empresaId.Value);
                if (!asignado) return Forbid();
            }

            return View(w);
        }

        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpGet]
        public IActionResult CrearWebinar()
        {
            ViewBag.Empresas = GetEmpresasSelect();
            return View("~/Views/Lider/CrearWebinar.cshtml");
        }

        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearWebinar(Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
                return View("~/Views/Lider/CrearWebinar.cshtml", model);
            }

            var rutaRel = await GuardarImagenAsync(imagenFile);
            if (!string.IsNullOrWhiteSpace(rutaRel))
                model.Imagen = rutaRel;

            model.UsuarioCreadorID = GetUsuarioId();

            _db.Webinars.Add(model);
            await _db.SaveChangesAsync();

            if (!model.EsPublico && empresasSeleccionadas is { Length: > 0 })
            {
                var relaciones = empresasSeleccionadas.Select(eid => new WebinarEmpresa
                {
                    WebinarID = model.WebinarID,
                    EmpresaID = eid
                });
                _db.WebinarsEmpresas.AddRange(relaciones);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(MisWebinars));
        }


        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpGet]
        public async Task<IActionResult> Editar(int? id, string? returnUrl)
        {
            if (id == null) return NotFound();
            var w = await _db.Webinars.FindAsync(id.Value);
            if (w == null) return NotFound();

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }

            var seleccionadas = await _db.WebinarsEmpresas
                .Where(we => we.WebinarID == w.WebinarID)
                .Select(we => we.EmpresaID)
                .ToListAsync();


            var empresas = await _db.Empresas
                .OrderBy(e => e.Nombre)
               .Select(e => new { e.EmpresaID, e.Nombre })
               .ToListAsync();

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                ? Url.Action("GestionarWebinar", "Webinars")
                : returnUrl;
            return View("~/Views/Lider/EditarWebinar.cshtml", w);
        }

        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(
    int id, Webinar model, int[]? empresasSeleccionadas, IFormFile? imagenFile, string? returnUrl)
        {
            if (id != model.WebinarID) return NotFound();

            // Validaciones...
            if (model.FechaFin < model.FechaInicio)
         ModelState.AddModelError("FechaFin", "La fecha de fin no puede ser menor que la fecha de inicio.");
            if (!ModelState.IsValid)
            {
                ViewBag.Empresas = GetEmpresasSelect();
         ViewBag.ReturnUrl = returnUrl ?? Url.Action("GestionarWebinar", "Webinars");
                return View("~/Views/Lider/EditarWebinar.cshtml", model);
            }

            //  Recupera el original para conservar campos
            var original = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id);
            if (original == null) return NotFound();

            // Preserva estos campos
            model.UsuarioCreadorID = original.UsuarioCreadorID;
            model.FechaCreacion = original.FechaCreacion;

            // Imagen
            var nuevaRuta = await GuardarImagenAsync(imagenFile);
            model.Imagen = !string.IsNullOrWhiteSpace(nuevaRuta) ? nuevaRuta : original.Imagen;

            

            
            _db.Update(model);
            _db.Entry(model).Property(x => x.UsuarioCreadorID).IsModified = false;
            _db.Entry(model).Property(x => x.FechaCreacion).IsModified = false;
            await _db.SaveChangesAsync();

            // Reasignar empresas 
            var actuales = _db.WebinarsEmpresas.Where(we => we.WebinarID == id);
            _db.WebinarsEmpresas.RemoveRange(actuales);
            if (!model.EsPublico && empresasSeleccionadas is { Length: > 0 })
            {
                var relaciones = empresasSeleccionadas.Select(eid => new WebinarEmpresa
                {
                    WebinarID = id,
                    EmpresaID = eid
                });
                await _db.WebinarsEmpresas.AddRangeAsync(relaciones);
            }
            await _db.SaveChangesAsync();

            return Redirect(returnUrl ?? Url.Action("GestionarWebinar", "Webinars")!);
        }


        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpGet]
        public async Task<IActionResult> GestionarWebinar()
        {
             var userId = GetUsuarioId();
            if (!userId.HasValue) return Forbid();

           var lista = await _db.Webinars
                .AsNoTracking()
                .Where(w => w.UsuarioCreadorID== userId.Value )
                .OrderByDescending(w => w.FechaCreacion)
                .Select(w => new WebinarListItemVm
                {
                    WebinarID= w.WebinarID,
                    Titulo = w.Titulo,
                    Descripcion = w.Descripcion,
                    FechaInicio = w.FechaInicio,
                    FechaFin = w.FechaFin,
                    UrlTeams = w.UrlTeams,
                    Imagen = w.Imagen,
                    DirigidoA = w.EsPublico? "Todas" : ""
                })
                .ToListAsync();
            var  idsNoPublicos = lista.Where(x => x.DirigidoA == "").Select(  x => x.WebinarID).ToList();
            if (idsNoPublicos.Count > 0)
            {
                var empresasPorWebinar = await _db.WebinarsEmpresas
                    .Where(we => idsNoPublicos.Contains(we.WebinarID))
                    .Join(_db.Empresas, we => we.EmpresaID, e => e.EmpresaID, (we, e) => new {we.WebinarID, e.Nombre})
                    .GroupBy(x => x.WebinarID)
                    .ToDictionaryAsync(g  => g .Key, g => string.Join(",", g.Select(x => x.Nombre).OrderBy(n  => n)));
                foreach (var item in lista.Where(x => x.DirigidoA == ""))
                    item.DirigidoA = empresasPorWebinar.TryGetValue(item.WebinarID, out var nombres) ? nombres : "-";
            }
            return View("~/Views/Lider/GestionarWebinar.cshtml",lista);
        }

    
        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpGet]
        public async Task<IActionResult> Eliminar(int? id, string? returnUrl)
        {
            if (id == null) return NotFound();

            var w = await _db.Webinars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebinarID == id.Value);
            if (w == null) return NotFound();

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                ? Url.Action("GestionarWebinar","Webinars")
                : returnUrl;

            return View("~/Views/Lider/GestionarWebinar.cshtml",w); // Views/Webinars/Delete.cshtml (o tu ruta)
        }

        [Authorize(Roles = "Autor/Editor de Contenido,Administrador de Intranet,Propietario de Contenido")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id, string? returnUrl)
        {
            var w = await _db.Webinars.FindAsync(id);
            if (w == null) 
                return Redirect( returnUrl ?? Url.Action("GestionarWebinar","Webinars")!);

            if (!User.IsInRole("Administrador de Intranet") && !User.IsInRole("Propietario de Contenido"))
            {
                var userId = GetUsuarioId();
                if (!userId.HasValue || w.UsuarioCreadorID != userId.Value) return Forbid();
            }

            EliminarImagenFisica(w.Imagen);

            _db.Webinars.Remove(w);
            await _db.SaveChangesAsync();
            return Redirect (returnUrl ?? Url.Action("GestionarWebinar", "Webinars")!);
        }
    }

   

    public class WebinarGestionVm
    {
        public Webinar Webinar { get; set; } = default!;
        public string DirigidoA { get; set; } = "-";
    }
}