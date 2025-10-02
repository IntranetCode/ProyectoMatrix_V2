using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ViewModels;
using ProyectoMatrix.Seguridad;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;



public class ProyectosController : Controller
{
    private readonly ProyectosBD _proyectosBD;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly RutaNas _rutaNas;

    public ProyectosController(IConfiguration config, IWebHostEnvironment env, RutaNas rutaNas)
    {
        _config = config;
        var connectionString = config.GetConnectionString("DefaultConnection");
        _proyectosBD = new ProyectosBD(connectionString);
        _env = env;
        _rutaNas = rutaNas;
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
    [AutorizarAccion("Proyectos", "Ver")]
    public async Task<IActionResult> Detalle(int id, int? carpetaId = null)
    {
        ViewBag.TituloNavbar = "Detalle del Proyecto";
        ViewBag.LogoNavbar = "logo_proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null)
            return NotFound();

        // Registrar visualización
        await _proyectosBD.RegistrarVisualizacionProyectoAsync(id);


        //Ruta relativa
        string rutaRelativa = await ResolverRutaRelativaCarpetaAsync(id, carpetaId);

        //Ruta fisica en NAS
        string rutaBaseProyecto = _rutaNas.CrearCarpetaRaizProyecto(proyecto.ProyectoID, proyecto.NombreProyecto);
        string rutaFisicaActual = Path.Combine(rutaBaseProyecto, rutaRelativa.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!Directory.Exists(rutaFisicaActual)) Directory.CreateDirectory(rutaFisicaActual);


        //Cargar el arbol de las carpetas desde bd
        var carpetasDb = await _proyectosBD.ListarCarpetasProyectoAsync(id);
        var carpetasVm = carpetasDb.Select(c => new CarpetaNodoVM
        {
            CarpetaId = c.CarpetaID,
            CarpetaPadreId = c.CarpetaPadreID,
            Nombre = c.Nombre,
            RutaRelativa = c.RutaRelativa, // asegúrate que tu SP la devuelva
            Nivel = c.Nivel                 // o calcúlalo si no viene
        }).ToList();


        //Listar los archivos dentro de un nivel
        var archivosVm = Directory.EnumerateFiles(rutaFisicaActual)
        .Select(p => new FileInfo(p))
        .OrderByDescending(f => f.CreationTimeUtc)
        .Select(f => new ArchivoVM
        {
            Nombre = f.Name,
            RutaRelativa = Path.Combine(rutaRelativa, f.Name).Replace("\\", "/"),
            Tamano = f.Length,
            Fecha = f.CreationTime,
            Extension = f.Extension?.ToLowerInvariant() ?? ""
        })
        .ToList();


        var vm = new ProyectoDetalleVm
        {
            Proyecto = proyecto,
            CarpetaSeleccionadaId = carpetaId,
            Carpetas = carpetasVm,
            Archivos = archivosVm
        };


        return View(vm);
    }




    [HttpGet]

    [AutorizarAccion("Proyectos", "Crear")]
    public IActionResult Crear()
    {
        ViewBag.TituloNavbar = "Crear Nuevo Proyecto";
        ViewBag.LogoNavbar = "logo_proyectos.png";

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
        ViewBag.LogoNavbar = "logo_proyectos.png";

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

            // Se llenan despues una vez adjuntado el archivo
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

            //Insertar proyecto en la base de fatos
           int proyectoIdNuevo = await _proyectosBD.CrearProyectoAsync(proyecto);

            //Crear carpeta raiz en el NAS 
            string rutaProyecto;

            try
            {
                rutaProyecto = _rutaNas.CrearCarpetaRaizProyecto(proyectoIdNuevo, proyecto.NombreProyecto);

            }
            catch 
            {
                TempData["Warn"] = "El proyecto se creó, pero no se pudo crear la carpeta en el NAS. Verifique permisos/ruta.";
                rutaProyecto = null!;
            }

            //Se crean subcarpetas base 

            var subcarpetasBase = new[] { "Documentos", "Planos", "Fotos" };
           

            if (!string.IsNullOrEmpty(rutaProyecto))
            {
                // físico
                foreach (var sub in subcarpetasBase)
                {
                    var rutaFisica = Path.Combine(rutaProyecto, sub);
                    if (!Directory.Exists(rutaFisica))
                        Directory.CreateDirectory(rutaFisica);
                }
            }

            int? usuarioCreadorId = HttpContext.Session.GetInt32("UsuarioID"); // si lo manejas
            foreach (var sub in subcarpetasBase)
            {
                try
                {
                    await _proyectosBD.CrearCarpetaAsync(
                        proyectoId: proyectoIdNuevo,
                        carpetaPadreId: null,
                        nombreCarpeta: sub,
                        usuarioCreadorId: usuarioCreadorId
                    );
                }
                catch (InvalidOperationException)
                {
                    // Ya existe

                }
            }

            // Manejar archivo si se subió uno por defecto se guardaran en Documentos
            if (archivo != null && archivo.Length > 0 && !string.IsNullOrEmpty(rutaProyecto))
            {
                string carpetaDestino = Path.Combine(rutaProyecto, "Documentos");
                if (!Directory.Exists(carpetaDestino))
                    Directory.CreateDirectory(carpetaDestino);

                string ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
                string nombreSeguro = $"{Guid.NewGuid()}{ext}";
                string rutaArchivoFisico = Path.Combine(carpetaDestino, nombreSeguro);

                using (var fs = new FileStream(rutaArchivoFisico, FileMode.Create))
                    await archivo.CopyToAsync(fs);

                // Metadatos en memoria
                proyecto.Extension = ext;
                proyecto.TamanoArchivo = archivo.Length;
                proyecto.ArchivoRuta = Path.Combine("Documentos", nombreSeguro).Replace("\\", "/");

                // Persistir metadatos (UPDATE)
                await _proyectosBD.ActualizarRutaArchivoAsync(
                    proyectoId: proyectoIdNuevo,
                    ruta: proyecto.ArchivoRuta,
                    extension: proyecto.Extension,
                    tamano: proyecto.TamanoArchivo,
                    empresaId: empresaId.Value
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
        ViewBag.LogoNavbar = "logo_proyectos.png";

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
        ViewBag.LogoNavbar = "logo_proyectos.png";

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
            var raiz = GetNasRaizProyecto(actual);
            var carpetaDestino = Path.Combine(raiz, "Documentos");
            if (!Directory.Exists(carpetaDestino)) Directory.CreateDirectory(carpetaDestino);

            string ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
            string nombreSeguro = $"{Guid.NewGuid()}{ext}";
            string rutaArchivoFisico = Path.Combine(carpetaDestino, nombreSeguro);

            using (var fs = new FileStream(rutaArchivoFisico, FileMode.Create))
                await archivo.CopyToAsync(fs);

            actual.ArchivoRuta = Path.Combine("Documentos", nombreSeguro).Replace("\\", "/");
            actual.TamanoArchivo = archivo.Length;
            actual.Extension = ext;
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
    public async Task<IActionResult> VerArchivo(int id, string? rutaRelativa = null)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null) return NotFound("Proyecto no encontrado.");

        var rel = NormalizarRutaRelativa(
            string.IsNullOrWhiteSpace(rutaRelativa) ? proyecto.ArchivoRuta : rutaRelativa
        );
        if (string.IsNullOrEmpty(rel)) return NotFound("El proyecto no tiene archivo asociado.");

        var full = CombinarNas(proyecto, rel);
        if (!System.IO.File.Exists(full)) return NotFound("Archivo no encontrado en el NAS.");

        var contentType = GetContentType(full);
        return PhysicalFile(full, contentType, enableRangeProcessing: true);
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
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            _ => "application/octet-stream"
        };
    }

    // Iconos para cada tió de archivo reutilizando el helper de obtenermimeporextension
    private static string IconoPorExt(string nombreOExtension)
    {
        // Acepta ".pdf" o "archivo.pdf"
        var ext = Path.GetExtension(nombreOExtension ?? string.Empty).ToLowerInvariant();

        // Usa tu helper de MIME (si no reconoce, dará octet-stream)
        var mime = ObtenerMimeTypePorExtension(ext);

        // Mapea por MIME cuando aplica
        if (mime == "application/pdf") return "fas fa-file-pdf text-danger";
        if (mime == "application/msword" ||
            mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return "fas fa-file-word text-primary";
        if (mime == "application/vnd.ms-excel" ||
            mime == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            return "fas fa-file-excel text-success";
        if (mime == "image/jpeg" || mime == "image/png" ||
            mime == "image/gif" || mime == "image/webp") return "fas fa-file-image";

        // Fallbacks por extensión para tipos que tu MIME actual no cubre
        return ext switch
        {
            ".ppt" or ".pptx" => "fas fa-file-powerpoint text-warning",
            ".zip" or ".rar" => "fas fa-file-archive",
            ".txt" or ".md" => "fas fa-file-lines",
            _ => "fas fa-file"
        };
    }



    [HttpGet]
    [AutorizarAccion("Proyectos", "Ver")]
    public async Task<IActionResult> DescargarArchivo(int id, string? rutaRelativa = null)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null) return NotFound();

        var rel = NormalizarRutaRelativa(
            string.IsNullOrWhiteSpace(rutaRelativa) ? proyecto.ArchivoRuta : rutaRelativa
        );
        if (string.IsNullOrEmpty(rel)) return NotFound("No hay archivo para descargar.");

        var full = CombinarNas(proyecto, rel);
        if (!System.IO.File.Exists(full)) return NotFound("Archivo no encontrado en el NAS.");

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




    // Si no pasan carpetaId, usamos "Documentos" como raíz.
    private async Task<string> ResolverRutaRelativaCarpetaAsync(int proyectoId, int? carpetaId)
    {
        if (!carpetaId.HasValue) return "Documentos";

        var ruta = await _proyectosBD.ObtenerRutaRelativaCarpetaAsync(proyectoId, carpetaId.Value);
        return string.IsNullOrWhiteSpace(ruta) ? "Documentos" : ruta;
    }



    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [ValidateAntiForgeryToken]
    // Si quieres permitir a editores subir, cambia a Editar:
    [AutorizarAccion("Proyectos", "Crear")]
    public async Task<IActionResult> SubirArchivos()
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return Unauthorized();

        var proyectoId = int.Parse(Request.Form["proyectoId"]);
        var rutaDestino = NormalizarRutaRelativa(Request.Form["rutaDestino"].FirstOrDefault() ?? "Documentos");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(proyectoId, empresaId.Value);
        if (proyecto == null) return NotFound();

        var carpeta = CombinarNas(proyecto, rutaDestino);
        if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);

        foreach (var file in Request.Form.Files)
        {
            if (file.Length <= 0) continue;
            var safe = Path.GetFileName(file.FileName);
            var destino = Path.Combine(carpeta, safe);

            if (System.IO.File.Exists(destino))
            {
                var baseName = Path.GetFileNameWithoutExtension(safe);
                var ext = Path.GetExtension(safe);
                var i = 1;
                do { destino = Path.Combine(carpeta, $"{baseName} ({i++}){ext}"); }
                while (System.IO.File.Exists(destino));
            }

            using var fs = System.IO.File.Create(destino);
            await file.CopyToAsync(fs);
        }
        return Json(new { ok = true });
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


    private string NormalizarRutaRelativa(string? ruta)
    {
        ruta ??= "";
        ruta = ruta.Replace('\\', '/').Trim('/');
        if (ruta.Contains("..")) throw new InvalidOperationException("Ruta inválida.");
        return ruta;
    }

    private string GetNasRaizProyecto(Proyecto p)
    {
        // Usa tu helper y crea si no existe:
        return _rutaNas.CrearCarpetaRaizProyecto(p.ProyectoID, p.NombreProyecto);
    }

    private string CombinarNas(Proyecto p, string rutaRelNormalizada)
    {
        var raiz = GetNasRaizProyecto(p); // .../Proyectos/PRY-123-NOMBRE
        return Path.Combine(raiz, rutaRelNormalizada.Replace('/', Path.DirectorySeparatorChar));
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Proyectos", "Ver")]
    public async Task<IActionResult> ListarArchivos([FromBody] ListarReq req)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return Unauthorized();

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(req.ProyectoId, empresaId.Value);
        if (proyecto == null) return NotFound();

        var rutaRel = NormalizarRutaRelativa(req.Ruta ?? "Documentos");
        var carpeta = CombinarNas(proyecto, rutaRel);
        if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);

        var carpetas = Directory.GetDirectories(carpeta)
            .Select(d => new DirectoryInfo(d))
            .Select(di => new ArchivoItemVm
            {
                Nombre = di.Name,
                RutaRelativa = string.IsNullOrEmpty(rutaRel) ? di.Name : $"{rutaRel}/{di.Name}",
                EsCarpeta = true,
                TamanoBytes = 0,
                UltimaMod = di.LastWriteTimeUtc,
                IconoCss = "fas fa-folder"
            });

        var archivos = Directory.GetFiles(carpeta)
            .Select(f => new FileInfo(f))
            .Select(fi => new ArchivoItemVm
            {
                Nombre = fi.Name,
                RutaRelativa = string.IsNullOrEmpty(rutaRel) ? fi.Name : $"{rutaRel}/{fi.Name}",
                EsCarpeta = false,
                TamanoBytes = fi.Length,
                UltimaMod = fi.LastWriteTimeUtc,
                IconoCss = IconoPorExt(fi.Extension)
            });

        var crumbs = new System.Collections.Generic.List<object> { new { nombre = "Raíz", ruta = "Documentos" } };
        var partes = rutaRel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acum = "";
        foreach (var p in partes) { acum = string.IsNullOrEmpty(acum) ? p : $"{acum}/{p}"; crumbs.Add(new { nombre = p, ruta = acum }); }

        return Json(new
        {
            ok = true,
            ruta = rutaRel,
            breadcrumb = crumbs,
            items = carpetas.Concat(archivos)
                .OrderByDescending(x => x.EsCarpeta)
                .ThenBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase)
        });
    }


 

    public record EliminarReq(int ProyectoId, string RutaRelativa);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Proyectos", "Eliminar")]
    public async Task<IActionResult> EliminarArchivo([FromBody] EliminarReq req)
    {
        var empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId is null) return Unauthorized();

        var p = await _proyectosBD.ObtenerProyectoPorIdAsync(req.ProyectoId, empresaId.Value);
        if (p is null) return NotFound();

        var baseProyecto = _rutaNas.CrearCarpetaRaizProyecto(p.ProyectoID, p.NombreProyecto);
        var full = Path.Combine(baseProyecto, req.RutaRelativa.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        else if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        else return NotFound();

        return Json(new { ok = true });
    }



    //Metodo para crear una nueva carpeta en el gestor de archivos

    [HttpPost]
    [AutorizarAccion("Proyectos", "Crear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearCarpeta([FromBody] CrearCarpetaDto dto)
    {
        try
        {
            // Sesión/empresa
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId is null) return Json(false);

            // Datos mínimos
            if (dto is null || string.IsNullOrWhiteSpace(dto.Nombre)) return Json(false);

            // Proyecto válido y perteneciente a la empresa
            var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(dto.ProyectoId, empresaId.Value);
            if (proyecto is null) return Json(false);

            // Normalizar la ruta relativa destino
            var rutaRelativa = NormalizarRutaRelativa(
                string.IsNullOrWhiteSpace(dto.RutaPadre) ? proyecto.ArchivoRuta : dto.RutaPadre
            );

            if (rutaRelativa is null) return Json(false);

            // Validar nombre de carpeta 
            var nombre = dto.Nombre.Trim();
            if (nombre == "." || nombre == "..") return Json(false);
            if (nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Json(false);
            if (nombre.Contains(Path.DirectorySeparatorChar) || nombre.Contains(Path.AltDirectorySeparatorChar))
                return Json(false);

            // Combinar con NAS 
            var rutaPadreCompleta = CombinarNas(proyecto, rutaRelativa);
            if (string.IsNullOrWhiteSpace(rutaPadreCompleta)) return Json(false);

            // Garantizar que la ruta padre exista
            if (!Directory.Exists(rutaPadreCompleta))
            {
                
                Directory.CreateDirectory(rutaPadreCompleta);
            }

            // Ruta final a crear
            var rutaNuevaCarpeta = Path.Combine(rutaPadreCompleta, nombre);
            if (Directory.Exists(rutaNuevaCarpeta))
                return Json(new { ok = false, error = "La carpeta ya existe" });

            Directory.CreateDirectory(rutaNuevaCarpeta);
            return Json(new { ok = true }); 
        }
        catch
        {
            // aqui porner el servicio de bitacroa
            return Json(false);
        }
    }




}
