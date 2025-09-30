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
    private readonly IWebHostEnvironment _env;

    public ProyectosController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        var connectionString = config.GetConnectionString("DefaultConnection");
        _proyectosBD = new ProyectosBD(connectionString);
        _env = env;
    }


    [HttpGet]
    [AutorizarAccion ("Proyectos", "Ver")]
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

    [AutorizarAccion("Proyectos", "Crear")]
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
    [AutorizarAccion("Proyectos", "Crear")]
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
            proyecto.EsActivo = true;

            // Se llenan despues
            ModelState.Remove(nameof(proyecto.CreadoPor));
            ModelState.Remove(nameof(proyecto.ArchivoRuta));
            ModelState.Remove(nameof(proyecto.Extension));


            if (!ModelState.IsValid)
            {
                ViewBag.ModelErrors = ModelState
                    .Where(kv => kv.Value.Errors.Count > 0)
                    .Select(kv => $"{kv.Key}: {string.Join(" | ", kv.Value.Errors.Select(e => e.ErrorMessage))}")
                    .ToList();
                return View(proyecto);
            }

            int proyectoIdNuevo = await _proyectosBD.CrearProyectoAsync(proyecto);

            //Crear carpeta raiz en el NAS 
            string nombreCarpeta = ObtenerNombreCarpetaProyecto(proyectoIdNuevo, proyecto.NombreProyecto);
            string rutaBaseNas = ObtenerRutaBaseProyectos();
            string rutaProyecto = Path.Combine(rutaBaseNas, nombreCarpeta);

            try
            {
                if (!Directory.Exists(rutaProyecto))
                    Directory.CreateDirectory(rutaProyecto);
            }
            catch 
            {
                TempData["Warn"] = "El proyecto se creó, pero no se pudo crear la carpeta en el NAS. Verifique permisos/ruta.";
            }

         
                // Manejar archivo si se subió uno

                if (archivo != null && archivo.Length > 0)
            {

                string carpetaDestino = Path.Combine(rutaProyecto, "Documentos");
                if (!Directory.Exists(carpetaDestino)) Directory.CreateDirectory(carpetaDestino);

                string ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant();
                string nombreSeguro = $"{Guid.NewGuid()}{ext}";
                string rutaArchivoFisico = Path.Combine(carpetaDestino, nombreSeguro);

                using (var fs = new FileStream(rutaArchivoFisico, FileMode.Create))
                    await archivo.CopyToAsync(fs);

                // 1) llena propiedades en memoria (si tu entidad las tiene)
                proyecto.Extension = ext;
                proyecto.TamanoArchivo = archivo.Length;
                // guarda una RUTA RELATIVA (recomendado) para UI / descargas
                proyecto.ArchivoRuta = Path.Combine("Documentos", nombreSeguro).Replace("\\", "/");

                // 2) persiste en BD (UPDATE)
                await _proyectosBD.ActualizarRutaArchivoAsync(
        proyectoIdNuevo,
        proyecto.ArchivoRuta,
        proyecto.Extension,
        proyecto.TamanoArchivo,
        empresaId.Value
    );

            }

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
    [AutorizarAccion("Proyectos", "Editar")]
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




    //Se modificara este controlador para que los cambios al editar un proyecto se guarde correctamente

    [HttpPost]
    [AutorizarAccion("Proyectos", "Editar")]
    public async Task<IActionResult> Editar(int id,Proyecto proyecto, IFormFile archivo)
    {
        ViewBag.TituloNavbar = "Editar Proyecto";
        ViewBag.LogoNavbar = "logo-proyectos.png";

        //Verifica que usuario es el que esta iniciando sesion

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        string username = HttpContext.Session.GetString("Username");
        if (empresaId == null || string.IsNullOrEmpty(username))
            return RedirectToAction("Login", "Login");

        // Manda a traer el proyecto que le corresponde al usuario mediante su id y emoresaid (con los datos antes de editar)
        var actual = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (actual == null) return NotFound();

        // Campos posibles a editar en proyecto
        actual.NombreProyecto = proyecto.NombreProyecto;
        actual.CodigoProyecto = proyecto.CodigoProyecto;
        actual.Descripcion = proyecto.Descripcion;
        actual.Estado = proyecto.Estado;
        actual.Prioridad = proyecto.Prioridad;
        actual.Progreso = proyecto.Progreso;
        actual.FechaInicio = proyecto.FechaInicio;
        actual.FechaFinPrevista = proyecto.FechaFinPrevista;
        actual.ResponsableProyecto = proyecto.ResponsableProyecto;
        actual.Presupuesto = proyecto.Presupuesto;
        actual.Tags = proyecto.Tags;
        actual.Observaciones = proyecto.Observaciones;

        //Si se sube un archivo se guarda ese, de lo contrario se conserva el que ya estaba
        if (archivo != null && archivo.Length > 0)
        {
            var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "proyectos", "documentos");
            if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

            var unique = $"{Guid.NewGuid()}_{archivo.FileName}";
            var path = Path.Combine(uploads, unique);
            using (var fs = new FileStream(path, FileMode.Create))
                await archivo.CopyToAsync(fs);

            actual.ArchivoRuta = $"/proyectos/documentos/{unique}";
            actual.TamanoArchivo = archivo.Length;
            actual.Extension = Path.GetExtension(archivo.FileName);
        }
        // Si no hay archivo se consrva

        //  Aseguramos a que empresa pertenece
        actual.EmpresaID = empresaId.Value;   
       

        //  Validar despues de completar el modelo
        ModelState.Clear();
        TryValidateModel(actual);
        if (!ModelState.IsValid) return View(actual);

        // Guardar cambbios
        await _proyectosBD.ActualizarProyectoAsync(actual);
        TempData["Exito"] = "Proyecto actualizado exitosamente.";
        return RedirectToAction("Detalle", new { id = actual.ProyectoID });
    }




    [HttpGet]
    [AutorizarAccion("Proyectos", "Ver")]
    public async Task<IActionResult> VerArchivo(int id)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null) return NotFound("Proyecto no encontrado.");

        if (string.IsNullOrWhiteSpace(proyecto.ArchivoRuta))
            return NotFound("El proyecto no tiene archivo asociado.");

        string carpetaProyecto = ObtenerNombreCarpetaProyecto(proyecto.ProyectoID, proyecto.NombreProyecto);




        string baseNas = ObtenerRutaBaseProyectos(); // p.ej. "\\\\192.168.1.50\\Proyectos"
                                                     // Normaliza la ruta relativa que se guardo, p.ej. "Documentos/xxx.pdf"
        string relativa = proyecto.ArchivoRuta.Replace("~/", "").TrimStart('/', '\\')
                              .Replace('/', Path.DirectorySeparatorChar)
                              .Replace('\\', Path.DirectorySeparatorChar);

        //  Path físico final en NAS
        string carpetaFisicaProyecto = Path.Combine(baseNas, carpetaProyecto);
        string rutaArchivoFisico = Path.GetFullPath(Path.Combine(carpetaFisicaProyecto, relativa));

        // Defensa básica: el archivo debe estar dentro de la carpeta del proyecto en NAS
        string raizEsperada = Path.GetFullPath(carpetaFisicaProyecto) + Path.DirectorySeparatorChar;
        if (!rutaArchivoFisico.StartsWith(raizEsperada, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Ruta inválida.");

        if (!System.IO.File.Exists(rutaArchivoFisico))
            return NotFound("Archivo no encontrado en el repositorio.");

        //  Content-Type y stream
        string contentType = ObtenerMimeTypePorExtension(Path.GetExtension(rutaArchivoFisico));
        var fs = new FileStream(rutaArchivoFisico, FileMode.Open, FileAccess.Read, FileShare.Read);

        return File(fs, contentType, Path.GetFileName(rutaArchivoFisico), enableRangeProcessing: true);
    }

    private static string ObtenerMimeTypePorExtension(string? ext)
    {
        ext = (ext ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };
    }




    public async Task<IActionResult> DescargarArchivo(int id)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null || string.IsNullOrEmpty(proyecto.ArchivoRuta))
            return NotFound();

        var p = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (p == null || string.IsNullOrEmpty(p.ArchivoRuta)) return NotFound();



        //Normalizar la ruta antes de combinar
        var rel = p.ArchivoRuta.Replace("~/", "").TrimStart('/', '\\');
        var root = _env.WebRootPath;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return BadRequest("Ruta inválida.");
        if (!System.IO.File.Exists(full)) return NotFound("Archivo no encontrado en el servidor.");

        var contentType = GetContentType(full);
        var downloadName = Path.GetFileName(full); 
        return PhysicalFile(full, contentType, fileDownloadName: downloadName);
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
    [AutorizarAccion("Proyectos", "Editar")]
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

    //Se agregara un nuevo metodo para eliminar un proyecto

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Proyectos", "Eliminar")]
    public async Task<IActionResult> Eliminar(int id, string? returnUrl)
    {
        // Obtén empresaId del claim o sesión
        int empresaId = int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid) ? eid : 0;

        bool ok = await _proyectosBD.EliminarProyectoAsync(id, empresaId);

        TempData[ok ? "Exito" : "Error"] =
            ok ? "Proyecto eliminado correctamente." : "No se pudo eliminar el proyecto.";

        // Si quieres volver a la misma página:
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
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

    //Se separa el metodo para poderlo reutilizar en descargar y en ver acrchivo

    private static string GetContentType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pdf": return "application/pdf";
            case ".doc": return "application/msword";
            case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case ".xls": return "application/vnd.ms-excel";
            case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            default: return "application/octet-stream";
        }
    }


    //Nuevo metodo para el controlador oara contruir la ruta fisica de proyectos desde appsetting.jason

    private string ObtenerRutaBaseProyectos()
    {

        // Lee el valor de appsettings.json -> "Rutas:ProyectosNAS"
        string? ruta = _config["Ruta:NAS"];

        if (string.IsNullOrWhiteSpace(ruta))
            throw new InvalidOperationException("Configura RUTAS en appsettings.json (Rutas:ProyectosNAS).");

        return ruta;
    } 
    //Metodo para obtener el nombre de unac arpeta raiz
    private string ObtenerNombreCarpetaProyecto (int proyectoId, string nombreProyecto)
    {
        //Quitar los caracteres invalidos para Windows
        var nombre = string.Concat(nombreProyecto
            .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));

        //Reemplazar espacios por guiones
        nombre= nombre.Replace (" ", "-").ToUpperInvariant();

        return $"PROYECTO-{proyectoId}-{nombre}";
    }

    //Metodo para crear fisicamente la carpeta en el NADS

    private string CrearCarpetaRaizProyecto(int proyectoId, string codigoProyecto = null)
    {
        var rutaBase = ObtenerRutaBaseProyectos();
        var nombreCarpetaProyecto = ObtenerNombreCarpetaProyecto(proyectoId, codigoProyecto);
        var rutaFisicaProyecto = Path.Combine(rutaBase, nombreCarpetaProyecto);

        if (!Directory.Exists(rutaFisicaProyecto))
            Directory.CreateDirectory(rutaFisicaProyecto);

        return rutaFisicaProyecto;
    }

}
