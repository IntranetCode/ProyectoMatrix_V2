using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    public class OrganigramasController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<OrganigramasController> _logger;
        private readonly IWebHostEnvironment _env;


        private const long MaxFileBytes = 20 * 1024 * 1024; // 20 MB

        private static readonly string[] ExtensionesPermitidas =
        {
            ".pdf", ".png", ".jpg", ".jpeg"
        };

        private static readonly Dictionary<string, string> MimePermitidos = new()
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg"
        };

        public OrganigramasController(
            IConfiguration configuration,
            ILogger<OrganigramasController> logger,
            IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

            _logger = logger;
            _env = env;
        }

        // ==========================================================
        // INDEX
        // Usuario normal: ve organigramas asignados a su empresa.
        // Usuario con override: ve todos y puede administrar.
        // ==========================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var vm = new OrganigramasIndexVm();

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

                vm.EsEditor = esEditor;
                vm.PuedeCrear = esEditor;
                vm.PuedeEditar = esEditor;
                vm.PuedeEliminar = esEditor;

                if (esEditor)
                {
                    vm.Organigramas = await CargarOrganigramasEditorAsync(conn);
                }
                else
                {
                    var empresasUsuario = await ObtenerEmpresasActivasUsuarioAsync(conn, usuarioId.Value);

                    if (!empresasUsuario.Any())
                    {
                        vm.AlertaSistema = "No se encontró una empresa activa para el usuario actual.";
                        return View(vm);
                    }

                    vm.Organigramas = await CargarOrganigramasUsuarioAsync(conn, empresasUsuario);
                }

                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando Index de Organigramas.");

                vm.AlertaSistema =
                    "No se pudieron cargar los organigramas. Revisa que existan las tablas Organigramas y OrganigramaEmpresas.";

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando Index de Organigramas.");

                vm.AlertaSistema = "Ocurrió un error al cargar el módulo de organigramas.";
                return View(vm);
            }
        }

        // ==========================================================
        // CREAR - GET
        // ==========================================================

        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

            if (!esEditor)
                return Forbid();

            var vm = new OrganigramaEditorVm
            {
                Empresas = await CargarEmpresasSelectAsync(conn)
            };

            return View(vm);
        }

        // ==========================================================
        // CREAR - POST
        // Guarda archivo y asigna empresas seleccionadas.
        // ==========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(OrganigramaEditorVm vm)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

            if (!esEditor)
                return Forbid();

            vm.Empresas = await CargarEmpresasSelectAsync(conn);

            if (vm.Archivo == null || vm.Archivo.Length == 0)
                ModelState.AddModelError(nameof(vm.Archivo), "Debe seleccionar un archivo.");

            if (vm.Archivo != null && vm.Archivo.Length > MaxFileBytes)
                ModelState.AddModelError(nameof(vm.Archivo), "El archivo no debe superar 20 MB.");

            if (vm.EmpresasSeleccionadas == null || !vm.EmpresasSeleccionadas.Any())
                ModelState.AddModelError(nameof(vm.EmpresasSeleccionadas), "Debe seleccionar al menos una empresa.");

            var extension = vm.Archivo == null
                ? string.Empty
                : Path.GetExtension(vm.Archivo.FileName).ToLowerInvariant();

            if (vm.Archivo != null && !ExtensionesPermitidas.Contains(extension))
                ModelState.AddModelError(nameof(vm.Archivo), "Solo se permiten archivos PDF, PNG, JPG o JPEG.");

            if (!ModelState.IsValid)
                return View(vm);

            var empresasSeleccionadas = vm.EmpresasSeleccionadas
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!empresasSeleccionadas.Any())
            {
                ModelState.AddModelError(nameof(vm.EmpresasSeleccionadas), "Debe seleccionar al menos una empresa válida.");
                return View(vm);
            }

            var mimeType = MimePermitidos[extension];
            var archivoGuardado = $"{Guid.NewGuid():N}{extension}";

            var carpetaRelativa = Path.Combine("App_Data", "organigramas");
            var carpetaFisica = Path.Combine(_env.ContentRootPath, carpetaRelativa);

            Directory.CreateDirectory(carpetaFisica);

            var rutaFisica = Path.Combine(carpetaFisica, archivoGuardado);
            var rutaRelativa = Path.Combine(carpetaRelativa, archivoGuardado).Replace("\\", "/");

            await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew))
            {
                await vm.Archivo!.CopyToAsync(stream);
            }

            using var tx = conn.BeginTransaction();

            try
            {
                int organigramaId;

                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Organigramas
                    (
                        Titulo,
                        Descripcion,
                        ArchivoOriginal,
                        ArchivoGuardado,
                        RutaRelativa,
                        Extension,
                        MimeType,
                        TamanioBytes,
                        CreadoPorUsuarioID
                    )
                    OUTPUT INSERTED.OrganigramaID
                    VALUES
                    (
                        @Titulo,
                        @Descripcion,
                        @ArchivoOriginal,
                        @ArchivoGuardado,
                        @RutaRelativa,
                        @Extension,
                        @MimeType,
                        @TamanioBytes,
                        @CreadoPorUsuarioID
                    );
                ", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@Titulo", vm.Titulo.Trim());
                    cmd.Parameters.AddWithValue("@Descripcion", (object?)vm.Descripcion?.Trim() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ArchivoOriginal", vm.Archivo!.FileName);
                    cmd.Parameters.AddWithValue("@ArchivoGuardado", archivoGuardado);
                    cmd.Parameters.AddWithValue("@RutaRelativa", rutaRelativa);
                    cmd.Parameters.AddWithValue("@Extension", extension.Replace(".", ""));
                    cmd.Parameters.AddWithValue("@MimeType", mimeType);
                    cmd.Parameters.AddWithValue("@TamanioBytes", vm.Archivo.Length);
                    cmd.Parameters.AddWithValue("@CreadoPorUsuarioID", usuarioId.Value);

                    organigramaId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                foreach (var empresaId in empresasSeleccionadas)
                {
                    using var cmd = new SqlCommand(@"
                        INSERT INTO dbo.OrganigramaEmpresas
                        (
                            OrganigramaID,
                            EmpresaID,
                            Activo,
                            AsignadoPorUsuarioID
                        )
                        VALUES
                        (
                            @OrganigramaID,
                            @EmpresaID,
                            1,
                            @AsignadoPorUsuarioID
                        );
                    ", conn, tx);

                    cmd.Parameters.AddWithValue("@OrganigramaID", organigramaId);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                    cmd.Parameters.AddWithValue("@AsignadoPorUsuarioID", usuarioId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();

                TempData["Ok"] = "Organigrama creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                tx.Rollback();

                if (System.IO.File.Exists(rutaFisica))
                    System.IO.File.Delete(rutaFisica);

                _logger.LogError(ex, "Error creando organigrama.");

                ModelState.AddModelError(string.Empty, "No se pudo crear el organigrama.");
                return View(vm);
            }
        }

        // ==========================================================
        // ARCHIVO
        // Sirve PDF o imagen solo si el usuario tiene acceso.
        // ==========================================================

        [HttpGet]
        public async Task<IActionResult> Archivo(int id)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);
            var empresasUsuario = await ObtenerEmpresasActivasUsuarioAsync(conn, usuarioId.Value);

            var puedeVer = await UsuarioPuedeVerOrganigramaAsync(
                conn,
                id,
                empresasUsuario,
                esEditor
            );

            if (!puedeVer)
                return Forbid();

            string? rutaRelativa = null;
            string? mimeType = null;
            string? archivoOriginal = null;

            using (var cmd = new SqlCommand(@"
                SELECT
                    RutaRelativa,
                    MimeType,
                    ArchivoOriginal
                FROM dbo.Organigramas
                WHERE OrganigramaID = @OrganigramaID
                  AND Activo = 1;
            ", conn))
            {
                cmd.Parameters.AddWithValue("@OrganigramaID", id);

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return NotFound();

                rutaRelativa = ReadString(rd, "RutaRelativa");
                mimeType = ReadString(rd, "MimeType");
                archivoOriginal = ReadString(rd, "ArchivoOriginal");
            }

            var rutaFisica = Path.Combine(
                _env.ContentRootPath,
                rutaRelativa.Replace("/", Path.DirectorySeparatorChar.ToString())
            );

            if (!System.IO.File.Exists(rutaFisica))
                return NotFound();

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{archivoOriginal}\"";

            return PhysicalFile(rutaFisica, mimeType);
        }

        // ==========================================================
        // EDITAR - GET
        // ==========================================================

        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

            if (!esEditor)
                return Forbid();

            var vm = await CargarOrganigramaParaEditarAsync(conn, id);

            if (vm == null)
                return NotFound();

            vm.Empresas = await CargarEmpresasSelectAsync(conn);

            return View(vm);
        }

        // ==========================================================
        // EDITAR - POST
        // ==========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(OrganigramaEditorVm vm)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

            if (!esEditor)
                return Forbid();

            vm.Empresas = await CargarEmpresasSelectAsync(conn);

            if (vm.OrganigramaID <= 0)
                ModelState.AddModelError(string.Empty, "El organigrama no es válido.");

            if (vm.EmpresasSeleccionadas == null || !vm.EmpresasSeleccionadas.Any())
                ModelState.AddModelError(nameof(vm.EmpresasSeleccionadas), "Debe seleccionar al menos una empresa.");

            var extension = string.Empty;

            if (vm.Archivo != null && vm.Archivo.Length > 0)
            {
                if (vm.Archivo.Length > MaxFileBytes)
                    ModelState.AddModelError(nameof(vm.Archivo), "El archivo no debe superar 20 MB.");

                extension = Path.GetExtension(vm.Archivo.FileName).ToLowerInvariant();

                if (!ExtensionesPermitidas.Contains(extension))
                    ModelState.AddModelError(nameof(vm.Archivo), "Solo se permiten archivos PDF, PNG, JPG o JPEG.");
            }

            if (!ModelState.IsValid)
                return View(vm);

            var existe = await ExisteOrganigramaActivoAsync(conn, vm.OrganigramaID);

            if (!existe)
                return NotFound();

            var empresasSeleccionadas = vm.EmpresasSeleccionadas
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!empresasSeleccionadas.Any())
            {
                ModelState.AddModelError(nameof(vm.EmpresasSeleccionadas), "Debe seleccionar al menos una empresa válida.");
                return View(vm);
            }

            string? rutaFisicaNueva = null;
            string? rutaRelativaNueva = null;
            string? archivoGuardadoNuevo = null;
            string? mimeTypeNuevo = null;
            string? extensionSinPunto = null;

            var tieneArchivoNuevo = vm.Archivo != null && vm.Archivo.Length > 0;

            if (tieneArchivoNuevo)
            {
                mimeTypeNuevo = MimePermitidos[extension];
                extensionSinPunto = extension.Replace(".", "");
                archivoGuardadoNuevo = $"{Guid.NewGuid():N}{extension}";

                var carpetaRelativa = Path.Combine("App_Data", "organigramas");
                var carpetaFisica = Path.Combine(_env.ContentRootPath, carpetaRelativa);

                Directory.CreateDirectory(carpetaFisica);

                rutaFisicaNueva = Path.Combine(carpetaFisica, archivoGuardadoNuevo);
                rutaRelativaNueva = Path.Combine(carpetaRelativa, archivoGuardadoNuevo).Replace("\\", "/");

                await using (var stream = new FileStream(rutaFisicaNueva, FileMode.CreateNew))
                {
                    await vm.Archivo!.CopyToAsync(stream);
                }
            }

            using var tx = conn.BeginTransaction();

            try
            {
                if (tieneArchivoNuevo)
                {
                    using var cmd = new SqlCommand(@"
                UPDATE dbo.Organigramas
                SET
                    Titulo = @Titulo,
                    Descripcion = @Descripcion,
                    ArchivoOriginal = @ArchivoOriginal,
                    ArchivoGuardado = @ArchivoGuardado,
                    RutaRelativa = @RutaRelativa,
                    Extension = @Extension,
                    MimeType = @MimeType,
                    TamanioBytes = @TamanioBytes,
                    FechaModificacion = SYSDATETIME(),
                    ModificadoPorUsuarioID = @UsuarioID
                WHERE OrganigramaID = @OrganigramaID
                  AND Activo = 1;
            ", conn, tx);

                    cmd.Parameters.AddWithValue("@OrganigramaID", vm.OrganigramaID);
                    cmd.Parameters.AddWithValue("@Titulo", vm.Titulo.Trim());
                    cmd.Parameters.AddWithValue("@Descripcion", (object?)vm.Descripcion?.Trim() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ArchivoOriginal", vm.Archivo!.FileName);
                    cmd.Parameters.AddWithValue("@ArchivoGuardado", archivoGuardadoNuevo!);
                    cmd.Parameters.AddWithValue("@RutaRelativa", rutaRelativaNueva!);
                    cmd.Parameters.AddWithValue("@Extension", extensionSinPunto!);
                    cmd.Parameters.AddWithValue("@MimeType", mimeTypeNuevo!);
                    cmd.Parameters.AddWithValue("@TamanioBytes", vm.Archivo.Length);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    using var cmd = new SqlCommand(@"
                UPDATE dbo.Organigramas
                SET
                    Titulo = @Titulo,
                    Descripcion = @Descripcion,
                    FechaModificacion = SYSDATETIME(),
                    ModificadoPorUsuarioID = @UsuarioID
                WHERE OrganigramaID = @OrganigramaID
                  AND Activo = 1;
            ", conn, tx);

                    cmd.Parameters.AddWithValue("@OrganigramaID", vm.OrganigramaID);
                    cmd.Parameters.AddWithValue("@Titulo", vm.Titulo.Trim());
                    cmd.Parameters.AddWithValue("@Descripcion", (object?)vm.Descripcion?.Trim() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = new SqlCommand(@"
            UPDATE dbo.OrganigramaEmpresas
            SET Activo = 0
            WHERE OrganigramaID = @OrganigramaID
              AND Activo = 1;
        ", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@OrganigramaID", vm.OrganigramaID);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var empresaId in empresasSeleccionadas)
                {
                    using var cmd = new SqlCommand(@"
                IF EXISTS (
                    SELECT 1
                    FROM dbo.OrganigramaEmpresas
                    WHERE OrganigramaID = @OrganigramaID
                      AND EmpresaID = @EmpresaID
                )
                BEGIN
                    UPDATE dbo.OrganigramaEmpresas
                    SET
                        Activo = 1,
                        FechaAsignacion = SYSDATETIME(),
                        AsignadoPorUsuarioID = @UsuarioID
                    WHERE OrganigramaID = @OrganigramaID
                      AND EmpresaID = @EmpresaID;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.OrganigramaEmpresas
                    (
                        OrganigramaID,
                        EmpresaID,
                        Activo,
                        AsignadoPorUsuarioID
                    )
                    VALUES
                    (
                        @OrganigramaID,
                        @EmpresaID,
                        1,
                        @UsuarioID
                    );
                END
            ", conn, tx);

                    cmd.Parameters.AddWithValue("@OrganigramaID", vm.OrganigramaID);
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaId);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();

                TempData["Ok"] = "Organigrama actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                tx.Rollback();

                if (!string.IsNullOrWhiteSpace(rutaFisicaNueva) && System.IO.File.Exists(rutaFisicaNueva))
                    System.IO.File.Delete(rutaFisicaNueva);

                _logger.LogError(ex, "Error editando organigrama.");

                ModelState.AddModelError(string.Empty, "No se pudo actualizar el organigrama.");
                return View(vm);
            }
        }

        // ==========================================================
        // ELIMINAR
        // Baja lógica. No borra el archivo físico por seguridad.
        // ==========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);

            if (!esEditor)
                return Forbid();

            using var tx = conn.BeginTransaction();

            try
            {
                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.Organigramas
                    SET
                        Activo = 0,
                        FechaEliminacion = SYSDATETIME(),
                        EliminadoPorUsuarioID = @UsuarioID
                    WHERE OrganigramaID = @OrganigramaID
                      AND Activo = 1;
                ", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@OrganigramaID", id);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.OrganigramaEmpresas
                    SET Activo = 0
                    WHERE OrganigramaID = @OrganigramaID
                      AND Activo = 1;
                ", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@OrganigramaID", id);

                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();

                TempData["Ok"] = "Organigrama eliminado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                tx.Rollback();

                _logger.LogError(ex, "Error eliminando organigrama.");

                TempData["Error"] = "No se pudo eliminar el organigrama.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Visor(int id)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var esEditor = await UsuarioTieneOverrideOrganigramasAsync(conn, usuarioId.Value);
            var empresasUsuario = await ObtenerEmpresasActivasUsuarioAsync(conn, usuarioId.Value);

            var puedeVer = await UsuarioPuedeVerOrganigramaAsync(
                conn,
                id,
                empresasUsuario,
                esEditor
            );

            if (!puedeVer)
                return Forbid();

            OrganigramaViewerVm? vm = null;

            using (var cmd = new SqlCommand(@"
        SELECT
            OrganigramaID,
            Titulo,
            Extension,
            MimeType
        FROM dbo.Organigramas
        WHERE OrganigramaID = @OrganigramaID
          AND Activo = 1;
    ", conn))
            {
                cmd.Parameters.AddWithValue("@OrganigramaID", id);

                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm = new OrganigramaViewerVm
                    {
                        OrganigramaID = ReadInt(rd, "OrganigramaID"),
                        Titulo = ReadString(rd, "Titulo"),
                        Extension = ReadString(rd, "Extension"),
                        MimeType = ReadString(rd, "MimeType"),

                        // IMPORTANTE:
                        // URL relativa para evitar problemas con localhost, https, Dev Tunnels o proxy.
                        ArchivoUrl = Url.Action(
                            nameof(Archivo),
                            "Organigramas",
                            new { id }
                        ) ?? string.Empty
                    };
                }
            }

            if (vm == null)
                return NotFound();

            return View("Visor", vm);
        }

        // ==========================================================
        // CONSULTAS PRIVADAS
        // ==========================================================

        private async Task<List<OrganigramaListaVm>> CargarOrganigramasEditorAsync(
            SqlConnection conn)
        {
            var organigramas = new List<OrganigramaListaVm>();

            using var cmd = new SqlCommand(@"
                SELECT
                    O.OrganigramaID,
                    O.Titulo,
                    O.Descripcion,
                    O.Extension,
                    O.MimeType,
                    O.FechaCreacion,
                    CAST(O.CreadoPorUsuarioID AS NVARCHAR(20)) AS CreadoPor,
                    STRING_AGG(E.Nombre, ', ') AS EmpresasAsignadas
                FROM dbo.Organigramas O
                LEFT JOIN dbo.OrganigramaEmpresas OE
                    ON OE.OrganigramaID = O.OrganigramaID
                   AND OE.Activo = 1
                LEFT JOIN dbo.Empresas E
                    ON E.EmpresaID = OE.EmpresaID
                   AND E.Activa = 1
                WHERE O.Activo = 1
                GROUP BY
                    O.OrganigramaID,
                    O.Titulo,
                    O.Descripcion,
                    O.Extension,
                    O.MimeType,
                    O.FechaCreacion,
                    O.CreadoPorUsuarioID
                ORDER BY O.FechaCreacion DESC, O.OrganigramaID DESC;
            ", conn);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                organigramas.Add(new OrganigramaListaVm
                {
                    OrganigramaID = ReadInt(rd, "OrganigramaID"),
                    Titulo = ReadString(rd, "Titulo"),
                    Descripcion = ReadNullableString(rd, "Descripcion"),
                    Extension = ReadString(rd, "Extension"),
                    MimeType = ReadString(rd, "MimeType"),
                    FechaCreacion = ReadDateTime(rd, "FechaCreacion") ?? DateTime.MinValue,
                    CreadoPor = ReadString(rd, "CreadoPor"),
                    EmpresasAsignadas = ReadString(rd, "EmpresasAsignadas")
                });
            }

            return organigramas;
        }

        private async Task<List<OrganigramaListaVm>> CargarOrganigramasUsuarioAsync(
            SqlConnection conn,
            List<int> empresasUsuario)
        {
            var organigramas = new List<OrganigramaListaVm>();

            var parametros = string.Join(",", empresasUsuario.Select((_, i) => $"@EmpresaID{i}"));

            using var cmd = new SqlCommand($@"
                SELECT
                    O.OrganigramaID,
                    O.Titulo,
                    O.Descripcion,
                    O.Extension,
                    O.MimeType,
                    O.FechaCreacion,
                    CAST(O.CreadoPorUsuarioID AS NVARCHAR(20)) AS CreadoPor,
                    STRING_AGG(E.Nombre, ', ') AS EmpresasAsignadas
                FROM dbo.Organigramas O
                INNER JOIN dbo.OrganigramaEmpresas OE
                    ON OE.OrganigramaID = O.OrganigramaID
                   AND OE.Activo = 1
                INNER JOIN dbo.Empresas E
                    ON E.EmpresaID = OE.EmpresaID
                   AND E.Activa = 1
                WHERE O.Activo = 1
                  AND EXISTS (
                        SELECT 1
                        FROM dbo.OrganigramaEmpresas OE2
                        WHERE OE2.OrganigramaID = O.OrganigramaID
                          AND OE2.Activo = 1
                          AND OE2.EmpresaID IN ({parametros})
                  )
                GROUP BY
                    O.OrganigramaID,
                    O.Titulo,
                    O.Descripcion,
                    O.Extension,
                    O.MimeType,
                    O.FechaCreacion,
                    O.CreadoPorUsuarioID
                ORDER BY O.FechaCreacion DESC, O.OrganigramaID DESC;
            ", conn);

            for (var i = 0; i < empresasUsuario.Count; i++)
                cmd.Parameters.AddWithValue($"@EmpresaID{i}", empresasUsuario[i]);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                organigramas.Add(new OrganigramaListaVm
                {
                    OrganigramaID = ReadInt(rd, "OrganigramaID"),
                    Titulo = ReadString(rd, "Titulo"),
                    Descripcion = ReadNullableString(rd, "Descripcion"),
                    Extension = ReadString(rd, "Extension"),
                    MimeType = ReadString(rd, "MimeType"),
                    FechaCreacion = ReadDateTime(rd, "FechaCreacion") ?? DateTime.MinValue,
                    CreadoPor = ReadString(rd, "CreadoPor"),
                    EmpresasAsignadas = ReadString(rd, "EmpresasAsignadas")
                });
            }

            return organigramas;
        }

        private async Task<List<SelectListItem>> CargarEmpresasSelectAsync(SqlConnection conn)
        {
            var empresas = new List<SelectListItem>();

            using var cmd = new SqlCommand(@"
                SELECT EmpresaID, Nombre
                FROM dbo.Empresas
                WHERE Activa = 1
                ORDER BY Nombre;
            ", conn);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                empresas.Add(new SelectListItem
                {
                    Value = Convert.ToString(rd["EmpresaID"]) ?? string.Empty,
                    Text = Convert.ToString(rd["Nombre"]) ?? string.Empty
                });
            }

            return empresas;
        }

        private async Task<bool> UsuarioTieneOverrideOrganigramasAsync(
            SqlConnection conn,
            int usuarioId)
        {
            using var cmd = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.PermisosUsuarioOverride PUO
                INNER JOIN dbo.SubMenus SM
                    ON SM.SubMenuID = PUO.SubMenuID
                WHERE PUO.UsuarioID = @UsuarioID
                    AND PUO.Decision = 1
                    AND SM.Activo = 1
                    AND (
                        SM.UrlEnlace = '/Organigramas/Index'
                        OR SM.UrlEnlace = '/Organigrama/Index'
                        OR SM.Nombre LIKE '%Organigrama%'
                    );
            ", conn);

            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

            return total > 0;
        }

        private async Task<bool> UsuarioPuedeVerOrganigramaAsync(
            SqlConnection conn,
            int organigramaId,
            List<int> empresasUsuario,
            bool esEditor)
        {
            if (esEditor)
            {
                using var cmdEditor = new SqlCommand(@"
                    SELECT COUNT(1)
                    FROM dbo.Organigramas
                    WHERE OrganigramaID = @OrganigramaID
                      AND Activo = 1;
                ", conn);

                cmdEditor.Parameters.AddWithValue("@OrganigramaID", organigramaId);

                var totalEditor = Convert.ToInt32(await cmdEditor.ExecuteScalarAsync() ?? 0);
                return totalEditor > 0;
            }

            if (!empresasUsuario.Any())
                return false;

            var parametros = string.Join(",", empresasUsuario.Select((_, i) => $"@EmpresaID{i}"));

            using var cmd = new SqlCommand($@"
                SELECT COUNT(1)
                FROM dbo.Organigramas O
                INNER JOIN dbo.OrganigramaEmpresas OE
                    ON OE.OrganigramaID = O.OrganigramaID
                   AND OE.Activo = 1
                WHERE O.OrganigramaID = @OrganigramaID
                  AND O.Activo = 1
                  AND OE.EmpresaID IN ({parametros});
            ", conn);

            cmd.Parameters.AddWithValue("@OrganigramaID", organigramaId);

            for (var i = 0; i < empresasUsuario.Count; i++)
                cmd.Parameters.AddWithValue($"@EmpresaID{i}", empresasUsuario[i]);

            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

            return total > 0;
        }

        private async Task<List<int>> ObtenerEmpresasActivasUsuarioAsync(
            SqlConnection conn,
            int usuarioId)
        {
            var empresas = new List<int>();

            using var cmd = new SqlCommand(@"
                SELECT UE.EmpresaID
                FROM dbo.UsuariosEmpresas UE
                INNER JOIN dbo.Empresas E
                    ON E.EmpresaID = UE.EmpresaID
                WHERE UE.UsuarioID = @UsuarioID
                  AND UE.Activo = 1
                  AND E.Activa = 1
                ORDER BY E.Nombre;
            ", conn);

            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                empresas.Add(Convert.ToInt32(rd["EmpresaID"]));

            return empresas;
        }

        private int? ObtenerUsuarioId()
        {
            if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid))
                return uid;

            if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid))
                return uid;

            return HttpContext.Session.GetInt32("UsuarioID");
        }

        private static int ReadInt(SqlDataReader rd, string column)
        {
            var value = rd[column];
            return value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static string ReadString(SqlDataReader rd, string column)
        {
            var value = rd[column];
            return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
        }

        private static string? ReadNullableString(SqlDataReader rd, string column)
        {
            var value = rd[column];
            return value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static DateTime? ReadDateTime(SqlDataReader rd, string column)
        {
            var value = rd[column];
            return value == DBNull.Value ? null : Convert.ToDateTime(value);
        }

        private async Task<OrganigramaEditorVm?> CargarOrganigramaParaEditarAsync(
    SqlConnection conn,
    int organigramaId)
        {
            OrganigramaEditorVm? vm = null;

            using (var cmd = new SqlCommand(@"
        SELECT
            OrganigramaID,
            Titulo,
            Descripcion,
            ArchivoOriginal,
            Extension
        FROM dbo.Organigramas
        WHERE OrganigramaID = @OrganigramaID
          AND Activo = 1;
    ", conn))
            {
                cmd.Parameters.AddWithValue("@OrganigramaID", organigramaId);

                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm = new OrganigramaEditorVm
                    {
                        OrganigramaID = ReadInt(rd, "OrganigramaID"),
                        Titulo = ReadString(rd, "Titulo"),
                        Descripcion = ReadNullableString(rd, "Descripcion"),
                        ArchivoActual = ReadString(rd, "ArchivoOriginal"),
                        ExtensionActual = ReadString(rd, "Extension")
                    };
                }
            }

            if (vm == null)
                return null;

            using (var cmd = new SqlCommand(@"
        SELECT EmpresaID
        FROM dbo.OrganigramaEmpresas
        WHERE OrganigramaID = @OrganigramaID
          AND Activo = 1;
    ", conn))
            {
                cmd.Parameters.AddWithValue("@OrganigramaID", organigramaId);

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                    vm.EmpresasSeleccionadas.Add(Convert.ToInt32(rd["EmpresaID"]));
            }

            return vm;
        }

        private async Task<bool> ExisteOrganigramaActivoAsync(
            SqlConnection conn,
            int organigramaId)
        {
            using var cmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.Organigramas
        WHERE OrganigramaID = @OrganigramaID
          AND Activo = 1;
    ", conn);

            cmd.Parameters.AddWithValue("@OrganigramaID", organigramaId);

            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

            return total > 0;
        }
    }
}