using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;
using ProyectoMatrix.Seguridad;



public class ProyectosController : Controller
{
    private readonly ProyectosBD _proyectosBD;
    private readonly IConfiguration _config;

    public ProyectosController(IConfiguration config)
    {
        _config = config;
        var connectionString = config.GetConnectionString("DefaultConnection");
        _proyectosBD = new ProyectosBD(connectionString);
    }

    public static class PermisosProyectos
    {
        public const string SubMenu = "Proyectos";
        public const string Ver = "Ver";
        public const string Crear = "Crear";
        public const string Editar = "Editar";
        public const string Eliminar = "Eliminar";
    }



    [HttpGet]
    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Ver)]
    public async Task<IActionResult> Index(EstadoProyecto? estado = null, PrioridadProyecto? prioridad = null, string busqueda = null)
    {
        // Configurar navbar dinámico
        ViewBag.TituloNavbar = "Gestión de Proyectos";
        ViewBag.LogoNavbar = "logo_proyectos.png";

        // Verificar sesión de usuario
        var rol = HttpContext.Session.GetString("Rol");
        int? usuarioId = HttpContext.Session.GetInt32("UsuarioID");
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");

        if (usuarioId == null || empresaId == null)
            return RedirectToAction("Login", "Login");

        // Obtener proyectos de la empresa
        var proyectos = await _proyectosBD.ObtenerProyectosPorEmpresaAsync(empresaId.Value);

        var viewModel = new ProyectosViewModel
        {
            TodosLosProyectos = proyectos,
            EstadoSeleccionado = estado,
            PrioridadSeleccionada = prioridad,
            BusquedaTexto = busqueda,
            EmpresaID = empresaId.Value
        };

        // Aplicar filtros
        var filtrados = proyectos.Where(p => p.EsActivo);

        if (estado.HasValue)
            filtrados = filtrados.Where(p => p.Estado == estado.Value);

        if (prioridad.HasValue)
            filtrados = filtrados.Where(p => p.Prioridad == prioridad.Value);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            filtrados = filtrados.Where(p =>
                (!string.IsNullOrEmpty(p.NombreProyecto) && p.NombreProyecto.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.Descripcion) && p.Descripcion.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.CodigoProyecto) && p.CodigoProyecto.Contains(busqueda, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.Tags) && p.Tags.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
            );
        }

        viewModel.ProyectosFiltrados = filtrados
            .OrderByDescending(p => p.FechaCreacion)
            .ToList();

        // Contadores para estadísticas
        viewModel.ContadorPorEstado = proyectos
            .Where(p => p.EsActivo)
            .GroupBy(p => p.Estado)
            .ToDictionary(g => g.Key, g => g.Count());

        viewModel.ContadorPorPrioridad = proyectos
            .Where(p => p.EsActivo)
            .GroupBy(p => p.Prioridad)
            .ToDictionary(g => g.Key, g => g.Count());

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        ViewBag.TituloNavbar = "Detalle del Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null)
            return NotFound();

        // Registrar visualización
        await _proyectosBD.RegistrarVisualizacionProyectoAsync(id);

        return View(proyecto);
    }


    [HttpGet]

    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Crear)]
    public IActionResult Crear()
    {
        ViewBag.TituloNavbar = "Crear Nuevo Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = new Proyecto
        {
            EmpresaID = empresaId.Value,
            FechaCreacion = DateTime.Now,
            Estado = EstadoProyecto.Planificacion,
            Prioridad = PrioridadProyecto.Media,
            EsActivo = true,
            Progreso = 0
        };

        return View(proyecto);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Crear)]
    public async Task<IActionResult> Crear(Proyecto proyecto, IFormFile archivo)
    {
        ViewBag.TituloNavbar = "Crear Nuevo Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        string username = HttpContext.Session.GetString("Username");

        if (empresaId == null || string.IsNullOrEmpty(username))
            return RedirectToAction("Login", "Login");

        

        try
        {
            proyecto.EmpresaID = empresaId.Value;
            proyecto.CreadoPor = username;
            proyecto.FechaCreacion = DateTime.Now;

            // Manejar archivo si se subió uno
            if (archivo != null && archivo.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "proyectos", "documentos");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}_{archivo.FileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await archivo.CopyToAsync(fileStream);
                }

                proyecto.ArchivoRuta = $"/proyectos/documentos/{uniqueFileName}";
                proyecto.TamanoArchivo = archivo.Length;
                proyecto.Extension = Path.GetExtension(archivo.FileName);
                proyecto.EsActivo = true;
            }

            ModelState.Clear();                    
            TryValidateModel(proyecto);

            if (!ModelState.IsValid)
            {
                // (Opcional) Para depurar, muestra los errores reales:
                ViewBag.ModelErrors = ModelState
                    .Where(kv => kv.Value.Errors.Count > 0)
                    .Select(kv => $"{kv.Key}: {string.Join(" | ", kv.Value.Errors.Select(e => e.ErrorMessage))}")
                    .ToList();

                return View(proyecto); // el file input se vacía por seguridad del navegador
            }

            // 3) Guardar
            await _proyectosBD.CrearProyectoAsync(proyecto);

            TempData["Exito"] = "Proyecto creado exitosamente.";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error al crear el proyecto: {ex.Message}");
            return View(proyecto);
        }
    }

    [HttpGet]
    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Editar)]
    public async Task<IActionResult> Editar(int id)
    {
        ViewBag.TituloNavbar = "Editar Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null)
            return NotFound();

        return View(proyecto);
    }

    [HttpPost]
    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Editar)]
    public async Task<IActionResult> Editar(Proyecto proyecto, IFormFile archivo)
    {
        ViewBag.TituloNavbar = "Editar Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        if (!ModelState.IsValid)
            return View(proyecto);

        try
        {
            // Manejar nuevo archivo si se subió uno
            if (archivo != null && archivo.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "proyectos", "documentos");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}_{archivo.FileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await archivo.CopyToAsync(fileStream);
                }

                proyecto.ArchivoRuta = $"/proyectos/documentos/{uniqueFileName}";
                proyecto.TamanoArchivo = archivo.Length;
                proyecto.Extension = Path.GetExtension(archivo.FileName);
            }

            await _proyectosBD.ActualizarProyectoAsync(proyecto);
            TempData["Exito"] = "Proyecto actualizado exitosamente.";
            return RedirectToAction("Detalle", new { id = proyecto.ProyectoID });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error al actualizar el proyecto: {ex.Message}");
            return View(proyecto);
        }
    }

    public async Task<IActionResult> VerArchivo(int id)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null || string.IsNullOrEmpty(proyecto.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", proyecto.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var extension = Path.GetExtension(proyecto.ArchivoRuta).ToLower();

        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

        return File(fileBytes, contentType);
    }

    public async Task<IActionResult> DescargarArchivo(int id)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null || string.IsNullOrEmpty(proyecto.ArchivoRuta))
            return NotFound();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", proyecto.ArchivoRuta.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo no encontrado en el servidor.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(proyecto.ArchivoRuta);

        return File(fileBytes, "application/octet-stream", fileName);
    }

    [HttpPost]
    public async Task<IActionResult> ActualizarProgreso([FromBody] ActualizarProgresoModel model)
    {
        try
        {
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId == null)
                return Unauthorized();

            await _proyectosBD.ActualizarProgresoAsync(model.ProyectoId, model.Progreso, empresaId.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }


    [HttpPost]
    [AutorizarAccion(PermisosProyectos.SubMenu, PermisosProyectos.Editar)]
    public async Task<IActionResult> CambiarEstado([FromBody] CambiarEstadoModel model)
    {
        try
        {
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId == null)
                return Unauthorized();

            await _proyectosBD.CambiarEstadoAsync(model.ProyectoId, model.Estado, empresaId.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // Modelos para requests AJAX
    public class ActualizarProgresoModel
    {
        public int ProyectoId { get; set; }
        public int Progreso { get; set; }
    }

    public class CambiarEstadoModel
    {
        public int ProyectoId { get; set; }
        public EstadoProyecto Estado { get; set; }
    }
}