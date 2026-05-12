using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ProyectoMatrix.Helpers;
using ProyectoMatrix.Models;
using ProyectoMatrix.Models.ViewModels;
using ProyectoMatrix.Seguridad;

public class ProyectosController : Controller
{
    private readonly ProyectosBD _proyectosBD;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly RutaNas _rutaNas;          // Usamos solo ObtenerNombreCarpetaProyecto
    private readonly ISftpStorage _sftp;        // Servicio SFTP con métodos en español

    public ProyectosController(IConfiguration config, IWebHostEnvironment env, RutaNas rutaNas, ISftpStorage sftp)
    {
        _config = config;
        var connectionString = config.GetConnectionString("DefaultConnection");
        _proyectosBD = new ProyectosBD(connectionString);
        _env = env;
        _rutaNas = rutaNas;
        _sftp = sftp;
    }

    // ==========================
    // LISTADO / DASHBOARD
    // ==========================
    [HttpGet]
    [AutorizarAccion("Ver Proyectos", "Ver")]
    public async Task<IActionResult> Index(EstadoProyecto? estado = null, PrioridadProyecto? prioridad = null, string busqueda = null)
    {
        ViewBag.TituloNavbar = "Gestión de Proyectos";
        ViewBag.LogoNavbar = "logo_proyectos.png";

        int? usuarioId = HttpContext.Session.GetInt32("UsuarioID");
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (usuarioId == null || empresaId == null)
            return RedirectToAction("Login", "Login");

        var proyectos = await _proyectosBD.ObtenerProyectosPorEmpresaAsync(empresaId.Value);

        var viewModel = new ProyectosViewModel
        {
            TodosLosProyectos = proyectos,
            EstadoSeleccionado = estado,
            PrioridadSeleccionada = prioridad,
            BusquedaTexto = busqueda,
            EmpresaID = empresaId.Value
        };

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

    // ==========================
    // DETALLE (Gestor de archivos vía SFTP)
    // ==========================
    [HttpGet]
    [AutorizarAccion("Ver Proyectos", "Ver")]
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

        await _proyectosBD.RegistrarVisualizacionProyectoAsync(id);

        string rutaRelativa = await ResolverRutaRelativaCarpetaAsync(id, carpetaId);

        var baseProyecto = SftpBaseProyecto(proyecto);
        var remActual = string.IsNullOrWhiteSpace(rutaRelativa)
            ? baseProyecto
            : JoinPosix(baseProyecto, rutaRelativa);

        // Árbol de carpetas (desde BD)
        var carpetasDb = await _proyectosBD.ListarCarpetasProyectoAsync(id);
        var carpetasVm = carpetasDb.Select(c => new CarpetaNodoVM
        {
            CarpetaId = c.CarpetaID,
            CarpetaPadreId = c.CarpetaPadreID,
            Nombre = c.Nombre,
            RutaRelativa = c.RutaRelativa,
            Nivel = c.Nivel
        }).ToList();

        // Archivos en la carpeta actual (SFTP)
        var archivosVm = _sftp.Listar(remActual)
            .Where(x => !x.EsCarpeta)
            .OrderByDescending(x => x.UltimaModUtc)
            .Select(x => new ArchivoVM
            {
                Nombre = x.Nombre,
                RutaRelativa = string.IsNullOrWhiteSpace(rutaRelativa) ? x.Nombre : $"{rutaRelativa}/{x.Nombre}",
                Tamano = x.Tamano,
                Fecha = x.UltimaModUtc.ToLocalTime(),
                Extension = Path.GetExtension(x.Nombre)?.ToLowerInvariant() ?? ""
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

    // ==========================
    // CREAR
    // ==========================
    [HttpGet]
    [AutorizarAccion("Crear Proyectos", "Crear")]
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
    [AutorizarAccion("Crear Proyectos", "Crear")]
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

            // Campos que completamos después si hay archivo
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

            // Guardar en BD
            int proyectoIdNuevo = await _proyectosBD.CrearProyectoAsync(proyecto);

            // Subida opcional del archivo principal a SFTP
            if (archivo != null && archivo.Length > 0)
            {
                var ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
                var nombreSeguro = $"{Guid.NewGuid()}{ext}";

                // Asegurar carpeta base del proyecto en SFTP y subir
                var refProyecto = new Proyecto { ProyectoID = proyectoIdNuevo, NombreProyecto = proyecto.NombreProyecto };
                var baseRemota = SftpBaseProyecto(refProyecto);
                _sftp.AsegurarDirectorio(baseRemota);

                using (var fs = archivo.OpenReadStream())
                {
                    _sftp.SubirStream(fs, JoinPosix(baseRemota, nombreSeguro));
                }

                // Metadatos y actualización en BD
                proyecto.Extension = ext;
                proyecto.TamanoArchivo = archivo.Length;
                proyecto.ArchivoRuta = nombreSeguro;

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

    // ==========================
    // EDITAR
    // ==========================
    [HttpGet]
    [AutorizarAccion("Editar proyecto", "Editar")]
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

    [HttpPost]
    [AutorizarAccion("Editar proyecto", "Editar")]
    public async Task<IActionResult> Editar(int id, Proyecto proyecto, IFormFile archivo)
    {
        ViewBag.TituloNavbar = "Editar Proyecto";
        ViewBag.LogoNavbar = "logo_proyectos.png";

        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        string username = HttpContext.Session.GetString("Username");
        if (empresaId == null || string.IsNullOrEmpty(username))
            return RedirectToAction("Login", "Login");

        var actual = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (actual == null) return NotFound();

        // Campos editables
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

        // Archivo principal (opcional)
        if (archivo != null && archivo.Length > 0)
        {
            var ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant() ?? "";
            var nombreSeguro = $"{Guid.NewGuid()}{ext}";

            var baseRemota = SftpBaseProyecto(actual);
            _sftp.AsegurarDirectorio(baseRemota);

            using (var fs = archivo.OpenReadStream())
            {
                _sftp.SubirStream(fs, JoinPosix(baseRemota, nombreSeguro));
            }

            actual.ArchivoRuta = nombreSeguro;
            actual.TamanoArchivo = archivo.Length;
            actual.Extension = ext;
        }

        actual.EmpresaID = empresaId.Value;

        ModelState.Clear();
        TryValidateModel(actual);
        if (!ModelState.IsValid) return View(actual);

        await _proyectosBD.ActualizarProyectoAsync(actual);
        TempData["Exito"] = "Proyecto actualizado exitosamente.";
        return RedirectToAction("Detalle", new { id = actual.ProyectoID });
    }

    // ==========================
    // AJAX: progreso / estado
    // ==========================
    [HttpPost]
    public async Task<IActionResult> ActualizarProgreso([FromBody] ActualizarProgresoModel model)
    {
        try
        {
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId == null) return Unauthorized();

            await _proyectosBD.ActualizarProgresoAsync(model.ProyectoId, model.Progreso, empresaId.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [AutorizarAccion("Editar proyecto", "Editar")]
    public async Task<IActionResult> CambiarEstado([FromBody] CambiarEstadoModel model)
    {
        try
        {
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId == null) return Unauthorized();

            await _proyectosBD.CambiarEstadoAsync(model.ProyectoId, model.Estado, empresaId.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ==========================
    // ELIMINAR PROYECTO
    // ==========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Eliminar Proyectos", "Eliminar")]
    public async Task<IActionResult> Eliminar(int id, string? returnUrl)
    {
        int empresaId = int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid) ? eid : 0;
        bool ok = await _proyectosBD.EliminarProyectoAsync(id, empresaId);

        TempData[ok ? "Exito" : "Error"] =
            ok ? "Proyecto eliminado correctamente." : "No se pudo eliminar el proyecto.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    // ==========================
    // LISTAR ARCHIVOS (AJAX)
    // ==========================
    public record ListarReq(int ProyectoId, string Ruta);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Ver Proyectos", "Ver")]
    public async Task<IActionResult> ListarArchivos([FromBody] ListarReq req)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return Unauthorized();

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(req.ProyectoId, empresaId.Value);
        if (proyecto == null) return NotFound();

        var rutaRel = NormalizarRutaRelativa(req.Ruta);
        if (string.IsNullOrWhiteSpace(rutaRel)) rutaRel = "";

        var rem = string.IsNullOrEmpty(rutaRel)
            ? SftpBaseProyecto(proyecto)
            : JoinPosix(SftpBaseProyecto(proyecto), rutaRel);

        var listado = _sftp.Listar(rem).ToList();

        var carpetas = listado.Where(i => i.EsCarpeta).Select(i => new ArchivoItemVm
        {
            Nombre = i.Nombre,
            RutaRelativa = string.IsNullOrEmpty(rutaRel) ? i.Nombre : $"{rutaRel}/{i.Nombre}",
            EsCarpeta = true,
            TamanoBytes = 0,
            UltimaMod = i.UltimaModUtc,
            IconoCss = "fas fa-folder"
        });

        var archivos = listado.Where(i => !i.EsCarpeta).Select(i => new ArchivoItemVm
        {
            Nombre = i.Nombre,
            RutaRelativa = string.IsNullOrEmpty(rutaRel) ? i.Nombre : $"{rutaRel}/{i.Nombre}",
            EsCarpeta = false,
            TamanoBytes = i.Tamano,
            UltimaMod = i.UltimaModUtc,
            IconoCss = IconoPorExt(i.Nombre)
        });

        var crumbs = new List<object> { new { nombre = "Raíz", ruta = "" } };
        var partes = rutaRel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acum = "";
        foreach (var p in partes)
        {
            acum = string.IsNullOrEmpty(acum) ? p : $"{acum}/{p}";
            crumbs.Add(new { nombre = p, ruta = acum });
        }

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

    // ==========================
    // CREAR CARPETA (AJAX)
    // ==========================
    public class CrearCarpetaDto
    {
        public int ProyectoId { get; set; }
        public string RutaPadre { get; set; }
        public string Nombre { get; set; }
    }

    [HttpPost]
    [AutorizarAccion("Crear Proyectos", "Crear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearCarpeta([FromBody] CrearCarpetaDto dto)
    {
        try
        {
            int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId is null) return Json(new { ok = false, error = "Sesión inválida." });

            if (dto is null || string.IsNullOrWhiteSpace(dto.Nombre))
                return Json(new { ok = false, error = "Nombre requerido." });

            var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(dto.ProyectoId, empresaId.Value);
            if (proyecto is null) return Json(new { ok = false, error = "Proyecto no encontrado." });

            var rutaRel = NormalizarRutaRelativa(dto.RutaPadre);
            if (string.IsNullOrWhiteSpace(rutaRel)) rutaRel = ""; // raíz

            var nombre = dto.Nombre.Trim();
            if (nombre is "." or "..") return Json(new { ok = false, error = "Nombre inválido." });
            if (nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                nombre.Contains(Path.DirectorySeparatorChar) ||
                nombre.Contains(Path.AltDirectorySeparatorChar))
                return Json(new { ok = false, error = "Nombre inválido." });

            var parent = string.IsNullOrEmpty(rutaRel)
                ? SftpBaseProyecto(proyecto)
                : JoinPosix(SftpBaseProyecto(proyecto), rutaRel);

            var existe = _sftp.Listar(parent).Any(x => x.EsCarpeta &&
                           string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (existe) return Json(new { ok = false, error = "La carpeta ya existe." });

            var nuevaRuta = JoinPosix(parent, nombre);
            _sftp.AsegurarDirectorio(nuevaRuta);

            var nuevaRutaRel = string.IsNullOrEmpty(rutaRel) ? nombre : $"{rutaRel}/{nombre}";
            return Json(new { ok = true, ruta = nuevaRutaRel });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ==========================
    // SUBIR ARCHIVOS (AJAX)
    // ==========================
    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Crear Proyectos", "Crear")] // o "Editar"
    public async Task<IActionResult> SubirArchivos()
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return Unauthorized();

        if (!int.TryParse(Request.Form["proyectoId"], out var proyectoId))
            return BadRequest("proyectoId inválido.");

        var rutaDestinoRaw = Request.Form["rutaDestino"].FirstOrDefault();
        var rutaRelativa = NormalizarRutaRelativa(rutaDestinoRaw);
        if (string.IsNullOrWhiteSpace(rutaRelativa)) rutaRelativa = "";

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(proyectoId, empresaId.Value);
        if (proyecto == null) return NotFound("Proyecto no encontrado.");

        var baseProyecto = SftpBaseProyecto(proyecto);
        var remCarpeta = string.IsNullOrEmpty(rutaRelativa) ? baseProyecto : JoinPosix(baseProyecto, rutaRelativa);
        _sftp.AsegurarDirectorio(remCarpeta);

        // Pre-carga para colisiones
        var existentes = _sftp.Listar(remCarpeta).Select(i => i.Nombre)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Request.Form.Files)
        {
            if (file.Length <= 0) continue;

            var original = Path.GetFileName(file.FileName);
            var destinoNombre = original;

            if (existentes.Contains(destinoNombre))
            {
                var baseName = Path.GetFileNameWithoutExtension(destinoNombre);
                var ext = Path.GetExtension(destinoNombre);
                var i = 1;
                string candidato;
                do
                {
                    candidato = $"{baseName} ({i++}){ext}";
                } while (existentes.Contains(candidato));
                destinoNombre = candidato;
            }

            using var fs = file.OpenReadStream();
            _sftp.SubirStream(fs, JoinPosix(remCarpeta, destinoNombre));
            existentes.Add(destinoNombre);
        }

        return Json(new { ok = true });
    }

    // ==========================
    // VER / DESCARGAR ARCHIVO
    // ==========================
    [HttpGet]
    [AutorizarAccion("Ver Proyectos", "Ver")]
    public async Task<IActionResult> VerArchivo(int id, string? rutaRelativa = null)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null) return NotFound("Proyecto no encontrado.");

        var rel = NormalizarRutaRelativa(string.IsNullOrWhiteSpace(rutaRelativa) ? proyecto.ArchivoRuta : rutaRelativa);
        if (string.IsNullOrEmpty(rel)) return NotFound("El proyecto no tiene archivo asociado.");

        var remote = JoinPosix(SftpBaseProyecto(proyecto), rel);
        var bytes = _sftp.DescargarBytes(remote);

        var contentType = ObtenerMimeTypePorExtension(Path.GetExtension(rel));
        return File(bytes, contentType);
    }

    [HttpGet]
    [AutorizarAccion("Ver Proyectos", "Ver")]
    public async Task<IActionResult> DescargarArchivo(int id, string? rutaRelativa = null)
    {
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null) return RedirectToAction("Login", "Login");

        var proyecto = await _proyectosBD.ObtenerProyectoPorIdAsync(id, empresaId.Value);
        if (proyecto == null) return NotFound();

        var rel = NormalizarRutaRelativa(string.IsNullOrWhiteSpace(rutaRelativa) ? proyecto.ArchivoRuta : rutaRelativa);
        if (string.IsNullOrEmpty(rel)) return NotFound("No hay archivo para descargar.");

        var remote = JoinPosix(SftpBaseProyecto(proyecto), rel);
        var bytes = _sftp.DescargarBytes(remote);

        var contentType = ObtenerMimeTypePorExtension(Path.GetExtension(rel));
        var downloadName = Path.GetFileName(rel);
        return File(bytes, contentType, fileDownloadName: downloadName);
    }

    // ==========================
    // BORRADOR / CARGAR
    // ==========================
    [HttpGet]
    [AutorizarAccion("Crear Proyectos", "Crear")]
    public IActionResult Cargar()
    {
        string username = HttpContext.Session.GetString("Username");
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        if (empresaId == null || string.IsNullOrEmpty(username))
            return RedirectToAction("Login", "Login");

        var modelo = new Proyecto
        {
            EmpresaID = empresaId.Value,
            Estado = EstadoProyecto.Completado,
            Prioridad = PrioridadProyecto.Media,
            EsActivo = true,
            Progreso = 100,
            CreadoPor = username
        };
        return View(modelo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AutorizarAccion("Crear Proyectos", "Crear")]
    public async Task<IActionResult> GuardarBorrador(Proyecto modelo)
    {
        int? usuarioId = HttpContext.Session.GetInt32("UsuarioID");
        int? empresaId = HttpContext.Session.GetInt32("EmpresaID");
        string username = HttpContext.Session.GetString("Username");

        if (empresaId is null || usuarioId is null || string.IsNullOrEmpty(username))
        {
            Response.StatusCode = 401;
            return Json(new { ok = false, message = "Sesión inválida." });
        }

        if (string.IsNullOrWhiteSpace(modelo.NombreProyecto))
            return Json(new { ok = false, message = "El nombre del proyecto es obligatorio." });

        var esNuevo = modelo.ProyectoID <= 0;
        modelo.Estado = EstadoProyecto.Completado;
        modelo.Progreso = 100;

        if (esNuevo)
        {
            modelo.FechaCreacion = DateTime.UtcNow;
            modelo.EmpresaID = empresaId.Value;
            modelo.EsActivo = true;
            modelo.CreadoPor = username;
        }
        else
        {
            modelo.EmpresaID = empresaId.Value;
        }

        try
        {
            var id = await _proyectosBD.GuardarBorradorAsync(modelo, empresaId.Value, usuarioId.Value);
            return Json(new { ok = id > 0, id, message = id > 0 ? null : "No se pudo guardar el proyecto." });
        }
        catch (Exception ex)
        {
            Response.StatusCode = 500;
            return Json(new
            {
                ok = false,
                message = "Error al guardar el proyecto.",
                detail = ex.Message,
                inner = ex.InnerException?.Message,
                stack = ex.StackTrace
            });
        }
    }

    // ==========================
    // Helpers
    // ==========================
    private static string JoinPosix(params string[] parts)
        => string.Join('/', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim('/')));

    private string SftpBaseProyecto(Proyecto p)
        => JoinPosix("Proyectos", _rutaNas.ObtenerNombreCarpetaProyecto(p.ProyectoID, p.NombreProyecto));

    private string NormalizarRutaRelativa(string? ruta)
    {
        ruta ??= "";
        ruta = ruta.Replace('\\', '/').Trim('/');
        if (ruta.Contains("..")) throw new InvalidOperationException("Ruta inválida.");
        return ruta;
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

    private static string IconoPorExt(string nombreOExtension)
    {
        var ext = Path.GetExtension(nombreOExtension ?? string.Empty).ToLowerInvariant();
        var mime = ObtenerMimeTypePorExtension(ext);

        if (mime == "application/pdf") return "fas fa-file-pdf text-danger";
        if (mime == "application/msword" ||
            mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return "fas fa-file-word text-primary";
        if (mime == "application/vnd.ms-excel" ||
            mime == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            return "fas fa-file-excel text-success";
        if (mime == "image/jpeg" || mime == "image/png" ||
            mime == "image/gif" || mime == "image/webp") return "fas fa-file-image";

        return ext switch
        {
            ".ppt" or ".pptx" => "fas fa-file-powerpoint text-warning",
            ".zip" or ".rar" => "fas fa-file-archive",
            ".txt" or ".md" => "fas fa-file-lines",
            _ => "fas fa-file"
        };
    }

    private async Task<string> ResolverRutaRelativaCarpetaAsync(int proyectoId, int? carpetaId)
    {
        if (!carpetaId.HasValue) return "";
        var ruta = await _proyectosBD.ObtenerRutaRelativaCarpetaAsync(proyectoId, carpetaId.Value);
        return string.IsNullOrWhiteSpace(ruta) ? "" : ruta;
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

    public record EliminarReq(int ProyectoId, string RutaRelativa);
}
