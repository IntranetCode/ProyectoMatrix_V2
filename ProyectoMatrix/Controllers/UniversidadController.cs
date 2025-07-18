/*
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

public class UniversidadController : Controller
{
    private readonly ContenidosBD _contenidosBD;
    private readonly AreasBD _areasBD;

    public UniversidadController(IConfiguration config)
    {
        // Inicialización de los servicios de base de datos
        var connectionString = config.GetConnectionString("DefaultConnection");
        _contenidosBD = new ContenidosBD(connectionString);
        _areasBD = new AreasBD(connectionString);
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? areaId = null, CategoriaContenido? categoria = null, string busqueda = null)
    {
        // Obtener todas las áreas y contenidos activos
        var areas = await _areasBD.ObtenerAreasAsync();
        var contenidos = await _contenidosBD.ObtenerContenidosAsync();

        // Construir el ViewModel base
        var viewModel = new UniversidadViewModel
        {
            Areas = areas,
            TodosLosContenidos = contenidos,
            AreaSeleccionadaId = areaId,
            CategoriaSeleccionada = categoria,
            BusquedaTexto = busqueda
        };

        // Aplicar filtros
        var filtrados = contenidos.Where(c => c.EsActivo);

        if (areaId.HasValue)
        {
            filtrados = filtrados.Where(c => c.AreaID == areaId.Value);
        }

        if (categoria.HasValue)
        {
            var categoriaTexto = categoria.Value.ToString();
            filtrados = filtrados.Where(c => c.Categoria != null &&
                                             c.Categoria.Equals(categoriaTexto, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            filtrados = filtrados.Where(c =>
                (!string.IsNullOrEmpty(c.Titulo) && c.Titulo.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Descripcion) && c.Descripcion.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Tags) && c.Tags.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
            );
        }

        // Asignar resultados filtrados al ViewModel
        viewModel.ContenidosFiltrados = filtrados
            .OrderBy(c => c.OrdenVisualizacion)
            .ThenByDescending(c => c.FechaCreacion)
            .ToList();

        // Contadores por categoría
        viewModel.ContadorPorCategoria = contenidos
            .Where(c => c.EsActivo)
            .GroupBy(c => c.Categoria)
            .ToDictionary(g => g.Key ?? "Sin categoría", g => g.Count());

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> RegistrarVisualizacion([FromBody] VisualizacionModel model)
    {
        if (model?.Id > 0)
        {
            await _contenidosBD.RegistrarVisualizacionAsync(model.Id);
            return Ok(new { success = true });
        }

        return BadRequest(new { success = false, message = "ID inválido" });
    }

    public async Task<IActionResult> VerPDF(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync()).FirstOrDefault(c => c.ContenidoID == id);
        if (contenido == null || string.IsNullOrEmpty(contenido.RutaArchivo))
            return NotFound();

        // Armar la ruta absoluta a partir de wwwroot
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.RutaArchivo.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/pdf");
    }
    public async Task<IActionResult> DescargarArchivo(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync())
            .FirstOrDefault(c => c.ContenidoID == id);

        if (contenido == null || string.IsNullOrEmpty(contenido.RutaArchivo))
            return NotFound();

        // Convertir la ruta relativa en una ruta física absoluta
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.RutaArchivo.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(contenido.RutaArchivo);

        return File(fileBytes, "application/octet-stream", fileName);
    }

    [HttpGet]
    public async Task<JsonResult> ContenidoPorArea(int areaId)
    {
        var contenidos = await _contenidosBD.ObtenerContenidosPorAreaAsync(areaId); // Asegúrate de tener este método en tu clase ContenidosBD
        return Json(contenidos);
    }
}
*/
//segundo cambio
/*
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class UniversidadController : Controller
{
    private readonly ContenidosBD _contenidosBD;
    private readonly AreasBD _areasBD;

    public UniversidadController(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        _contenidosBD = new ContenidosBD(connectionString);
        _areasBD = new AreasBD(connectionString);
    }

    [HttpGet]
    public async Task<IActionResult> IndexColaborador(string nivel = "JUNIOR")
    {
        var rol = HttpContext.Session.GetString("Rol");
        int? usuarioId = HttpContext.Session.GetInt32("UsuarioID");

        if (usuarioId == null)
            return RedirectToAction("Login", "Menu");

        int colaboradorId = await _contenidosBD.ObtenerColaboradorIdPorUsuario(usuarioId.Value);
        var cursos = await _contenidosBD.ObtenerProgresoPorColaboradorYNivelc(colaboradorId, nivel);

        var viewModel = new UniversidadNivelViewModel
        {
            NivelSeleccionado = nivel,
            Cursos = cursos
        };

        return View(viewModel); // Vista IndexColaborador.cshtml
    }

    [HttpPost]
    public IActionResult IndexColaboradorPost(string nivel)
    {
        return RedirectToAction("IndexColaborador", new { nivel });
    }


    [HttpGet]
    public async Task<IActionResult> Index(int? areaId = null, CategoriaContenido? categoria = null, string busqueda = null)
    {
        var areas = await _areasBD.ObtenerAreasAsync();
        var contenidos = await _contenidosBD.ObtenerContenidosAsync();

        var viewModel = new UniversidadViewModel
        {
            Areas = areas,
            TodosLosContenidos = contenidos,
            AreaSeleccionadaId = areaId,
            CategoriaSeleccionada = categoria,
            BusquedaTexto = busqueda
        };

        var filtrados = contenidos.Where(c => c.EsActivo);

        if (areaId.HasValue)
            filtrados = filtrados.Where(c => c.AreaID == areaId.Value);

        if (categoria.HasValue)
        {
            var categoriaTexto = categoria.Value.ToString();
            filtrados = filtrados.Where(c => c.Categoria != null &&
                                             c.Categoria.Equals(categoriaTexto, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            filtrados = filtrados.Where(c =>
                (!string.IsNullOrEmpty(c.Titulo) && c.Titulo.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Descripcion) && c.Descripcion.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Tags) && c.Tags.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
            );
        }

        viewModel.ContenidosFiltrados = filtrados
            .OrderBy(c => c.OrdenVisualizacion)
            .ThenByDescending(c => c.FechaCreacion)
            .ToList();

        viewModel.ContadorPorCategoria = contenidos
            .Where(c => c.EsActivo)
            .GroupBy(c => c.Categoria)
            .ToDictionary(g => g.Key ?? "Sin categoría", g => g.Count());

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> RegistrarVisualizacion([FromBody] VisualizacionModel model)
    {
        if (model?.Id > 0)
        {
            await _contenidosBD.RegistrarVisualizacionAsync(model.Id);
            return Ok(new { success = true });
        }

        return BadRequest(new { success = false, message = "ID inválido" });
    }

    public async Task<IActionResult> VerPDF(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync()).FirstOrDefault(c => c.ContenidoID == id);
        if (contenido == null || string.IsNullOrEmpty(contenido.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/pdf");
    }

    public async Task<IActionResult> DescargarArchivo(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync())
            .FirstOrDefault(c => c.ContenidoID == id);

        if (contenido == null || string.IsNullOrEmpty(contenido.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(contenido.ArchivoRuta);

        return File(fileBytes, "application/octet-stream", fileName);
    }

    [HttpGet]
    public async Task<JsonResult> ContenidoPorArea(int areaId)
    {
        var contenidos = await _contenidosBD.ObtenerContenidosPorAreaAsync(areaId);
        return Json(contenidos);
    }
    public async Task<IActionResult> ComenzarCurso(int id)
    {
        var curso = await _contenidosBD.ObtenerCursoPorIdAsync(id);
        if (curso == null) return NotFound();

        return View("ComenzarCurso", curso);
    }

    public async Task<IActionResult> VerContenido(int id)
    {
        var curso = await _contenidosBD.ObtenerContenidoPorIdAsync(id);
        if (curso == null) return NotFound();

        return View(curso);
    }
    /*public async Task<IActionResult> VerContenido(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync()).FirstOrDefault(c => c.ContenidoID == id);
        if (contenido == null || string.IsNullOrEmpty(contenido.RutaArchivo))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.RutaArchivo.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/pdf");
    }*/
/*

}*/
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class UniversidadController : Controller
{
    private readonly ContenidosBD _contenidosBD;
    private readonly AreasBD _areasBD;

    public UniversidadController(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        _contenidosBD = new ContenidosBD(connectionString);
        _areasBD = new AreasBD(connectionString);
    }

    [HttpGet]
    public async Task<IActionResult> IndexColaborador(string nivel = "JUNIOR")
    {
        // ===== AGREGAR ESTAS LÍNEAS PARA EL NAVBAR DINÁMICO =====
        ViewBag.TituloNavbar = "Universidad NS Group";
        ViewBag.LogoNavbar = "logo-universidad-ns.png";
        // ===== FIN DE LÍNEAS AGREGADAS =====

        var rol = HttpContext.Session.GetString("Rol");
        int? usuarioId = HttpContext.Session.GetInt32("UsuarioID");

        if (usuarioId == null)
            return RedirectToAction("Login", "Menu");

        int colaboradorId = await _contenidosBD.ObtenerColaboradorIdPorUsuario(usuarioId.Value);
        var cursos = await _contenidosBD.ObtenerProgresoPorColaboradorYNivelc(colaboradorId, nivel);

        var viewModel = new UniversidadNivelViewModel
        {
            NivelSeleccionado = nivel,
            Cursos = cursos
        };

        return View(viewModel); // Vista IndexColaborador.cshtml
    }

    [HttpPost]
    public IActionResult IndexColaboradorPost(string nivel)
    {
        return RedirectToAction("IndexColaborador", new { nivel });
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? areaId = null, CategoriaContenido? categoria = null, string busqueda = null)
    {
        // ===== AGREGAR ESTAS LÍNEAS PARA EL NAVBAR DINÁMICO =====
        ViewBag.TituloNavbar = "Universidad NS Group";
        ViewBag.LogoNavbar = "logo-universidad-ns.png";
        // ===== FIN DE LÍNEAS AGREGADAS =====

        var areas = await _areasBD.ObtenerAreasAsync();
        var contenidos = await _contenidosBD.ObtenerContenidosAsync();

        var viewModel = new UniversidadViewModel
        {
            Areas = areas,
            TodosLosContenidos = contenidos,
            AreaSeleccionadaId = areaId,
            CategoriaSeleccionada = categoria,
            BusquedaTexto = busqueda
        };

        var filtrados = contenidos.Where(c => c.EsActivo);

        if (areaId.HasValue)
            filtrados = filtrados.Where(c => c.AreaID == areaId.Value);

        if (categoria.HasValue)
        {
            var categoriaTexto = categoria.Value.ToString();
            filtrados = filtrados.Where(c => c.Categoria != null &&
                                             c.Categoria.Equals(categoriaTexto, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            filtrados = filtrados.Where(c =>
                (!string.IsNullOrEmpty(c.Titulo) && c.Titulo.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Descripcion) && c.Descripcion.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Tags) && c.Tags.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
            );
        }

        viewModel.ContenidosFiltrados = filtrados
            .OrderBy(c => c.OrdenVisualizacion)
            .ThenByDescending(c => c.FechaCreacion)
            .ToList();

        viewModel.ContadorPorCategoria = contenidos
            .Where(c => c.EsActivo)
            .GroupBy(c => c.Categoria)
            .ToDictionary(g => g.Key ?? "Sin categoría", g => g.Count());

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> RegistrarVisualizacion([FromBody] VisualizacionModel model)
    {
        if (model?.Id > 0)
        {
            await _contenidosBD.RegistrarVisualizacionAsync(model.Id);
            return Ok(new { success = true });
        }

        return BadRequest(new { success = false, message = "ID inválido" });
    }

    public async Task<IActionResult> VerPDF(int id)
    {
        // ===== AGREGAR ESTAS LÍNEAS PARA EL NAVBAR DINÁMICO =====
        ViewBag.TituloNavbar = "Universidad NS Group";
        ViewBag.LogoNavbar = "logo-universidad-ns.png";
        // ===== FIN DE LÍNEAS AGREGADAS =====

        var contenido = (await _contenidosBD.ObtenerContenidosAsync()).FirstOrDefault(c => c.ContenidoID == id);
        if (contenido == null || string.IsNullOrEmpty(contenido.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/pdf");
    }

    public async Task<IActionResult> DescargarArchivo(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync())
            .FirstOrDefault(c => c.ContenidoID == id);

        if (contenido == null || string.IsNullOrEmpty(contenido.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(contenido.ArchivoRuta);

        return File(fileBytes, "application/octet-stream", fileName);
    }

    [HttpGet]
    public async Task<JsonResult> ContenidoPorArea(int areaId)
    {
        var contenidos = await _contenidosBD.ObtenerContenidosPorAreaAsync(areaId);
        return Json(contenidos);
    }

    public async Task<IActionResult> ComenzarCurso(int id)
    {
        // ===== AGREGAR ESTAS LÍNEAS PARA EL NAVBAR DINÁMICO =====
        ViewBag.TituloNavbar = "Universidad NS Group";
        ViewBag.LogoNavbar = "logo-universidad-ns.png";
        // ===== FIN DE LÍNEAS AGREGADAS =====

        var curso = await _contenidosBD.ObtenerCursoPorIdAsync(id);
        if (curso == null) return NotFound();

        return View("ComenzarCurso", curso);
    }

    public async Task<IActionResult> VerContenido(int id)
    {
        // ===== AGREGAR ESTAS LÍNEAS PARA EL NAVBAR DINÁMICO =====
        ViewBag.TituloNavbar = "Universidad NS Group";
        ViewBag.LogoNavbar = "logo-universidad-ns.png";
        // ===== FIN DE LÍNEAS AGREGADAS =====

        var curso = await _contenidosBD.ObtenerContenidoPorIdAsync(id);
        if (curso == null) return NotFound();

        return View(curso);
    }

    /*public async Task<IActionResult> VerContenido(int id)
    {
        var contenido = (await _contenidosBD.ObtenerContenidosAsync()).FirstOrDefault(c => c.ContenidoID == id);
        if (contenido == null || string.IsNullOrEmpty(contenido.RutaArchivo))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", contenido.RutaArchivo.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/pdf");
    }*/
}