using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProyectoMatrix.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data;
using System.Text.Json;
using ProyectoMatrix.Servicios;

namespace ProyectoMatrix.Controllers
{
    public class CitasController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<CitasController> _logger;
        private readonly ServicioNotificaciones _notif;

        public CitasController(
     IConfiguration configuration,
     ILogger<CitasController> logger,
     ServicioNotificaciones notif)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

            _logger = logger;
            _notif = notif;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var vm = new CitasIndexVm
            {
                ModuloVisible = true,
                EsEditor = false,
                EsUsuarioFinal = false
            };

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var rolesUsuario = await ObtenerRolesUsuarioAsync(conn, usuarioId.Value);

                var esUsuarioFinal = rolesUsuario.Contains(5);
                var esEditorContenido = rolesUsuario.Contains(3);

                var esAdministrador =
                    rolesUsuario.Contains(1) ||
                    rolesUsuario.Contains(2);

                vm.RolID = rolesUsuario.FirstOrDefault();

                // Rol 5: Usuario Final
                // No puede ver panel administrador.
                vm.EsUsuarioFinal = esUsuarioFinal;

                // Rol 3: Autor/Editor de Contenido
                // Puede ver panel editor, pero NO vista usuario final.
                // También se permite a roles admin 1 y 2.
                vm.EsEditor = esEditorContenido || esAdministrador;

                if (esEditorContenido && !esUsuarioFinal)
                {
                    vm.EsUsuarioFinal = false;
                }

                if (esUsuarioFinal && !esEditorContenido && !esAdministrador)
                {
                    vm.EsEditor = false;
                }

                if (vm.EsEditor)
                {
                    vm.Editor = await CargarIndexEditorAsync(conn);
                }

                if (vm.EsUsuarioFinal)
                {
                    var empresaId = ObtenerEmpresaId();

                    if (empresaId.HasValue)
                    {
                        var empresaPermitida = await UsuarioTieneEmpresaAsync(
                            conn,
                            usuarioId.Value,
                            empresaId.Value
                        );

                        if (!empresaPermitida)
                            empresaId = null;
                    }

                    if (!empresaId.HasValue)
                        empresaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId.Value);

                    vm.EmpresaID = empresaId;

                    if (empresaId.HasValue)
                    {
                        vm.Usuario = await CargarIndexUsuarioAsync(
                            conn,
                            usuarioId.Value,
                            empresaId.Value
                        );
                    }
                    else
                    {
                        vm.Usuario = new CitasUsuarioIndexVm
                        {
                            NombreUsuario = User.Identity?.Name ?? "Usuario",
                            ModuloVisible = true,
                            EstadoPantalla = "sin_asignacion",
                            PuedeAgendar = false,
                            MensajeUsuario = "No se encontró una empresa activa para el usuario actual."
                        };
                    }
                }

                if (!vm.EsEditor && !vm.EsUsuarioFinal)
                {
                    vm.AlertaSistema = "Tu rol no tiene permisos configurados para visualizar este módulo.";
                }

                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando Index de Citas.");

                vm.ModuloVisible = true;
                vm.AlertaSistema = "Error SQL cargando citas: " + ex.Message;

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando Index de Citas.");

                vm.ModuloVisible = true;
                vm.AlertaSistema = "Ocurrió un error al cargar el módulo de citas: " + ex.Message;

                return View(vm);
            }
        }

        private async Task<CitasUsuarioIndexVm> CargarIndexUsuarioAsync(
            SqlConnection conn,
            int usuarioId,
            int empresaId)
        {
            var nombreUsuario = await ObtenerNombreUsuarioAsync(conn, usuarioId);
            var personaId = await ObtenerPersonaIdAsync(conn, usuarioId);

            var vm = new CitasUsuarioIndexVm
            {
                NombreUsuario = nombreUsuario,
                EstadoPantalla = "sin_asignacion",
                ModuloVisible = true,
                PuedeAgendar = false,
                TieneEventoDisponible = false,
                TieneAsignacionEmpresa = false,
                MensajeUsuario = "No hay una jornada de mapeo activa asignada para tu empresa."
            };

            var eventos = await CargarEventosUsuarioAsync(conn, empresaId, personaId);

            vm.Eventos = eventos;

            if (!eventos.Any())
                return vm;

            vm.TieneAsignacionEmpresa = true;
            vm.TieneEventoDisponible = true;

            var eventoPrincipal =
                eventos
                    .Where(x => x.TieneCita && !string.Equals(x.EstadoCita, "finalizada", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.FechaCita ?? DateTime.MinValue)
                    .FirstOrDefault()
                ?? eventos
                    .Where(x => x.PuedeAgendar)
                    .OrderBy(x => x.FechaInicioEvento ?? DateTime.MaxValue)
                    .FirstOrDefault()
                ?? eventos
                    .Where(x => (x.FechaInicioEvento ?? DateTime.MaxValue) >= DateTime.Today)
                    .OrderBy(x => x.FechaInicioEvento ?? DateTime.MaxValue)
                    .FirstOrDefault()
                ?? eventos
                    .OrderByDescending(x => x.FechaInicioEvento ?? DateTime.MinValue)
                    .FirstOrDefault();

            if (eventoPrincipal == null)
                return vm;

            vm.AgendaID = eventoPrincipal.AgendaID;
            vm.AgendaEmpresaID = eventoPrincipal.AgendaEmpresaID;
            vm.NombreEvento = eventoPrincipal.NombreEvento;
            vm.NombreEmpresa = eventoPrincipal.NombreEmpresa;

            vm.FechaInicioEvento = eventoPrincipal.FechaInicioEvento;
            vm.FechaFinEvento = eventoPrincipal.FechaFinEvento;
            vm.FechaInicioSolicitud = eventoPrincipal.FechaInicioSolicitud;
            vm.FechaFinSolicitud = eventoPrincipal.FechaCierreAgendamiento ?? eventoPrincipal.FechaFinSolicitud;

            vm.EstadoPantalla = eventoPrincipal.EstadoPantalla;
            vm.PuedeAgendar = eventoPrincipal.PuedeAgendar;
            vm.MensajeUsuario = eventoPrincipal.MensajeUsuario;

            vm.TieneCita = eventoPrincipal.TieneCita;
            vm.CitaID = eventoPrincipal.CitaID;
            vm.FechaCita = eventoPrincipal.FechaCita;
            vm.HoraInicio = eventoPrincipal.HoraInicio;
            vm.HoraFin = eventoPrincipal.HoraFin;
            vm.EstadoCita = eventoPrincipal.EstadoCita;
            vm.EstadoFormulario = eventoPrincipal.EstadoFormulario;

            vm.EntrevistaFinalizada =
                string.Equals(eventoPrincipal.EstadoCita, "finalizada", StringComparison.OrdinalIgnoreCase);

            if (vm.EntrevistaFinalizada)
            {
                vm.ResultadoResumen =
                    "Tu entrevista ya fue finalizada. El resultado básico se mostrará cuando el reporte esté disponible.";
            }

            return vm;
        }

        #region Crear Cuestionario

        [HttpGet]
        public async Task<IActionResult> CrearCuestionario(int? cuestionarioId, int? agendaID)
        {
            ViewBag.AgendaID = agendaID;

            if (!cuestionarioId.HasValue || cuestionarioId.Value <= 0)
            {
                var nuevoVm = new CrearCuestionarioVm
                {
                    Nombre = string.Empty,
                    Descripcion = string.Empty,
                    Version = 1,
                    Estatus = "borrador",
                    Preguntas = new List<PreguntaEditorVm>
                    {
                        new PreguntaEditorVm
                        {
                            Orden = 1,
                            TipoPregunta = "texto",
                            Obligatoria = true,
                            Activa = true,
                            Opciones = new List<OpcionPreguntaEditorVm>()
                        }
                    }
                };

                return View(nuevoVm);
            }

            var vm = await ObtenerCuestionarioAsync(cuestionarioId.Value);

            if (vm == null)
            {
                TempData["CitasError"] = "No se encontró el cuestionario solicitado.";
                return RedirectToAction(nameof(Index));
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCuestionario(
            CrearCuestionarioVm vm,
            string AccionFormulario,
            int? agendaId)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            if (vm == null)
            {
                TempData["CitasError"] = "No se recibió información del cuestionario.";
                return RedirectToAction(nameof(CrearCuestionario));
            }

            vm.Preguntas ??= new List<PreguntaEditorVm>();

            vm.Estatus = AccionFormulario == "activo"
                ? "activo"
                : "borrador";

            var errores = ValidarCuestionario(vm);

            if (errores.Any())
            {
                foreach (var error in errores)
                    ModelState.AddModelError(string.Empty, error);

                return View(vm);
            }

            try
            {
                var cuestionarioId = await GuardarCuestionarioAsync(vm, usuarioId.Value);

                if (agendaId.HasValue && agendaId.Value > 0)
                {
                    await AsociarCuestionarioAAgendaAsync(agendaId.Value, cuestionarioId);

                    TempData["CitasOk"] = "Cuestionario creado y asociado correctamente a la agenda.";
                    return RedirectToAction(nameof(CrearAgenda), new { agendaId = agendaId.Value });
                }

                TempData["CitasOk"] = vm.Estatus == "activo"
                    ? "Cuestionario guardado y activado correctamente."
                    : "Cuestionario guardado como borrador correctamente.";

                return RedirectToAction(nameof(CrearCuestionario), new { cuestionarioId });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL guardando cuestionario.");

                ModelState.AddModelError(string.Empty, "No se pudo guardar el cuestionario en la base de datos. Revisa que existan las tablas Cuestionarios, Preguntas y OpcionesPregunta.");

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general guardando cuestionario.");

                ModelState.AddModelError(string.Empty, "Ocurrió un error al guardar el cuestionario.");

                return View(vm);
            }
        }


        #region Listado Cuestionarios

        [HttpGet]
        public async Task<IActionResult> Cuestionarios(string estatus = "todos", string busqueda = "")
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            try
            {
                var vm = await ObtenerCuestionariosIndexAsync(estatus, busqueda);
                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando listado de cuestionarios.");

                TempData["CitasError"] = "No se pudo cargar el listado de cuestionarios.";

                var vm = new CuestionariosIndexVm
                {
                    FiltroEstatus = estatus,
                    Busqueda = busqueda,
                    Cuestionarios = new List<CuestionarioListaVm>()
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando listado de cuestionarios.");

                TempData["CitasError"] = "Ocurrió un error al cargar los cuestionarios.";

                var vm = new CuestionariosIndexVm
                {
                    FiltroEstatus = estatus,
                    Busqueda = busqueda,
                    Cuestionarios = new List<CuestionarioListaVm>()
                };

                return View(vm);
            }
        }

        private async Task<CuestionariosIndexVm> ObtenerCuestionariosIndexAsync(string estatus, string busqueda)
        {
            estatus = string.IsNullOrWhiteSpace(estatus)
                ? "todos"
                : estatus.Trim().ToLowerInvariant();

            busqueda = string.IsNullOrWhiteSpace(busqueda)
                ? string.Empty
                : busqueda.Trim();

            var vm = new CuestionariosIndexVm
            {
                FiltroEstatus = estatus,
                Busqueda = busqueda,
                Cuestionarios = new List<CuestionarioListaVm>()
            };

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using (var resumenCmd = new SqlCommand(@"
                SELECT
                    COUNT(1) AS TotalCuestionarios,
                    SUM(CASE WHEN Estatus = 'borrador' THEN 1 ELSE 0 END) AS TotalBorradores,
                    SUM(CASE WHEN Estatus = 'activo' THEN 1 ELSE 0 END) AS TotalActivos,
                    SUM(CASE WHEN Estatus = 'cerrado' THEN 1 ELSE 0 END) AS TotalCerrados
                FROM dbo.Cuestionarios
                WHERE Activo = 1;
            ", conn))
            {
                using var rd = await resumenCmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.TotalCuestionarios = ReadInt(rd, "TotalCuestionarios");
                    vm.TotalBorradores = ReadInt(rd, "TotalBorradores");
                    vm.TotalActivos = ReadInt(rd, "TotalActivos");
                    vm.TotalCerrados = ReadInt(rd, "TotalCerrados");
                }
            }

            using (var cmd = new SqlCommand(@"
                SELECT
                    c.CuestionarioID,
                    c.Nombre,
                    c.Descripcion,
                    c.Version,
                    c.Estatus,
                    c.Activo,
                    c.FechaCreacion,
                    COUNT(p.PreguntaID) AS TotalPreguntas
                FROM dbo.Cuestionarios c
                LEFT JOIN dbo.Preguntas p
                    ON p.CuestionarioID = c.CuestionarioID
                AND p.Activa = 1
                WHERE
                    c.Activo = 1
                    AND (@Estatus = 'todos' OR c.Estatus = @Estatus)
                    AND (
                        @Busqueda = ''
                        OR c.Nombre LIKE '%' + @Busqueda + '%'
                        OR ISNULL(c.Descripcion, '') LIKE '%' + @Busqueda + '%'
                    )
                GROUP BY
                    c.CuestionarioID,
                    c.Nombre,
                    c.Descripcion,
                    c.Version,
                    c.Estatus,
                    c.Activo,
                    c.FechaCreacion
                ORDER BY
                    c.FechaCreacion DESC,
                    c.CuestionarioID DESC;
            ", conn))
            {
                cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = estatus;
                cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 150).Value = busqueda;

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    vm.Cuestionarios.Add(new CuestionarioListaVm
                    {
                        CuestionarioID = ReadInt(rd, "CuestionarioID"),
                        Nombre = ReadString(rd, "Nombre"),
                        Descripcion = ReadString(rd, "Descripcion"),
                        Version = ReadInt(rd, "Version"),
                        Estatus = ReadString(rd, "Estatus"),
                        Activo = ReadBool(rd, "Activo"),
                        FechaCreacion = ReadDateTime(rd, "FechaCreacion") ?? DateTime.MinValue,
                        CreadoPor = string.Empty,
                        TotalPreguntas = ReadInt(rd, "TotalPreguntas")
                    });
                }
            }

            return vm;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstatusCuestionario(int cuestionarioId, string estatus)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            estatus = string.IsNullOrWhiteSpace(estatus)
                ? string.Empty
                : estatus.Trim().ToLowerInvariant();

            var estatusValidos = new[] { "borrador", "activo", "cerrado" };

            if (!estatusValidos.Contains(estatus))
            {
                TempData["CitasError"] = "El estatus seleccionado no es válido.";
                return RedirectToAction(nameof(Cuestionarios));
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    UPDATE dbo.Cuestionarios
                    SET Estatus = @Estatus
                    WHERE CuestionarioID = @CuestionarioID
                    AND Activo = 1;
                ", conn);

                cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = estatus;
                cmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;

                var filas = await cmd.ExecuteNonQueryAsync();

                TempData["CitasOk"] = filas > 0
                    ? "Estatus del cuestionario actualizado correctamente."
                    : "No se encontró el cuestionario seleccionado.";

                return RedirectToAction(nameof(Cuestionarios));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL actualizando estatus del cuestionario.");

                TempData["CitasError"] = "No se pudo actualizar el estatus del cuestionario.";
                return RedirectToAction(nameof(Cuestionarios));
            }
        }

        [HttpGet]
        public async Task<IActionResult> DetalleCuestionario(int cuestionarioId)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return Unauthorized(new { ok = false, mensaje = "Sesión no válida." });

            if (cuestionarioId <= 0)
                return BadRequest(new { ok = false, mensaje = "Cuestionario inválido." });

            var vm = await ObtenerCuestionarioAsync(cuestionarioId);

            if (vm == null)
                return NotFound(new { ok = false, mensaje = "No se encontró el cuestionario solicitado." });

            DateTime? fechaCreacion = null;
            bool activo = true;
            int creadoPorUsuarioId = 0;
            string creadoPor = string.Empty;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    SELECT
                        c.FechaCreacion,
                        c.Activo,
                        c.CreadoPorUsuarioID,
                        ISNULL(u.Username, '') AS CreadoPor
                    FROM dbo.Cuestionarios c
                    LEFT JOIN dbo.Usuarios u
                        ON u.UsuarioID = c.CreadoPorUsuarioID
                    WHERE c.CuestionarioID = @CuestionarioID;
                ", conn);

                cmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;

                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    fechaCreacion = ReadDateTime(rd, "FechaCreacion");
                    activo = ReadBool(rd, "Activo");
                    creadoPorUsuarioId = ReadInt(rd, "CreadoPorUsuarioID");
                    creadoPor = ReadString(rd, "CreadoPor");
                }
            }

            return Json(new
            {
                ok = true,
                cuestionario = new
                {
                    cuestionarioId = vm.CuestionarioID ?? 0,
                    nombre = vm.Nombre,
                    descripcion = vm.Descripcion,
                    version = vm.Version,
                    estatus = vm.Estatus,
                    activo,
                    fechaCreacion = fechaCreacion?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    creadoPorUsuarioId,
                    creadoPor,
                    totalPreguntas = vm.Preguntas?.Count ?? 0,
                    preguntas = (vm.Preguntas ?? new List<PreguntaEditorVm>())
                        .OrderBy(p => p.Orden)
                        .Select(p => new
                        {
                            preguntaId = p.PreguntaID ?? 0,
                            textoPregunta = p.TextoPregunta,
                            tipoPregunta = p.TipoPregunta,
                            dimension = p.Dimension,
                            orden = p.Orden,
                            obligatoria = p.Obligatoria,
                            configuracionJson = p.ConfiguracionJson,
                            activa = p.Activa,
                            opciones = (p.Opciones ?? new List<OpcionPreguntaEditorVm>())
                                .OrderBy(o => o.Orden)
                                .Select(o => new
                                {
                                    opcionId = o.OpcionID ?? 0,
                                    textoOpcion = o.TextoOpcion,
                                    valorPuntaje = o.ValorPuntaje,
                                    orden = o.Orden,
                                    activa = o.Activa
                                })
                        })
                }
            });
        }

        #endregion

        private static List<string> ValidarCuestionario(CrearCuestionarioVm vm)
        {
            var errores = new List<string>();

            if (string.IsNullOrWhiteSpace(vm.Nombre))
                errores.Add("El nombre del cuestionario es obligatorio.");

            if (vm.Version < 1)
                errores.Add("La versión debe ser mayor o igual a 1.");

            if (string.IsNullOrWhiteSpace(vm.Estatus))
                errores.Add("El estatus del cuestionario es obligatorio.");

            var estatusValidos = new[] { "borrador", "activo", "cerrado" };

            if (!estatusValidos.Contains((vm.Estatus ?? "").ToLowerInvariant()))
                errores.Add("El estatus del cuestionario no es válido.");

            if (vm.Preguntas == null || !vm.Preguntas.Any())
            {
                errores.Add("Debes agregar al menos una pregunta.");
                return errores;
            }

            var tiposValidos = new[] { "texto", "opcion_multiple", "checkbox" };

            for (var i = 0; i < vm.Preguntas.Count; i++)
            {
                var pregunta = vm.Preguntas[i];
                var numeroPregunta = i + 1;

                pregunta.TipoPregunta = string.IsNullOrWhiteSpace(pregunta.TipoPregunta)
                    ? "texto"
                    : pregunta.TipoPregunta.Trim().ToLowerInvariant();

                pregunta.Dimension = string.IsNullOrWhiteSpace(pregunta.Dimension)
                    ? string.Empty
                    : pregunta.Dimension.Trim();

                pregunta.ConfiguracionJson = string.Empty;

                if (!tiposValidos.Contains(pregunta.TipoPregunta))
                    errores.Add($"La pregunta {numeroPregunta} tiene un tipo inválido.");

                if (string.IsNullOrWhiteSpace(pregunta.TextoPregunta))
                    errores.Add($"La pregunta {numeroPregunta} no tiene texto.");

                if (pregunta.TipoPregunta == "texto")
                {
                    pregunta.Opciones = new List<OpcionPreguntaEditorVm>();
                    continue;
                }

                if (pregunta.TipoPregunta == "opcion_multiple" || pregunta.TipoPregunta == "checkbox")
                {
                    pregunta.Opciones ??= new List<OpcionPreguntaEditorVm>();

                    if (pregunta.Opciones.Count < 2)
                        errores.Add($"La pregunta {numeroPregunta} debe tener al menos 2 opciones.");

                    var yaTieneCorrecta = false;

                    for (var j = 0; j < pregunta.Opciones.Count; j++)
                    {
                        var opcion = pregunta.Opciones[j];

                        if (string.IsNullOrWhiteSpace(opcion.TextoOpcion))
                        {
                            errores.Add($"La opción {j + 1} de la pregunta {numeroPregunta} no tiene texto.");
                        }

                        var marcadaCorrecta = opcion.ValorPuntaje > 0;

                        if (pregunta.TipoPregunta == "opcion_multiple")
                        {
                            if (marcadaCorrecta && !yaTieneCorrecta)
                            {
                                opcion.ValorPuntaje = 1;
                                yaTieneCorrecta = true;
                            }
                            else
                            {
                                opcion.ValorPuntaje = 0;
                            }
                        }
                        else
                        {
                            opcion.ValorPuntaje = marcadaCorrecta ? 1 : 0;
                        }
                    }
                }
            }

            return errores;
        }

        private async Task<int> GuardarCuestionarioAsync(CrearCuestionarioVm vm, int usuarioId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                int cuestionarioId = vm.CuestionarioID ?? 0;

                if (cuestionarioId > 0)
                {
                    await EliminarPreguntasYOpcionesAsync(conn, tx, cuestionarioId);

                    using var updateCmd = new SqlCommand(@"
                        UPDATE dbo.Cuestionarios
                        SET
                            Nombre = @Nombre,
                            Descripcion = @Descripcion,
                            Version = @Version,
                            Estatus = @Estatus,
                            Activo = @Activo
                        WHERE CuestionarioID = @CuestionarioID;
                    ", conn, tx);

                    updateCmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = vm.Nombre.Trim();
                    updateCmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = DbValue(vm.Descripcion);
                    updateCmd.Parameters.Add("@Version", SqlDbType.Int).Value = vm.Version;
                    updateCmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = vm.Estatus;
                    updateCmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = true;
                    updateCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;

                    await updateCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    using var insertCmd = new SqlCommand(@"
                        INSERT INTO dbo.Cuestionarios
                        (
                            Nombre,
                            Descripcion,
                            Version,
                            Estatus,
                            Activo,
                            FechaCreacion,
                            CreadoPorUsuarioID
                        )
                        OUTPUT INSERTED.CuestionarioID
                        VALUES
                        (
                            @Nombre,
                            @Descripcion,
                            @Version,
                            @Estatus,
                            @Activo,
                            SYSDATETIME(),
                            @CreadoPorUsuarioID
                        );
                    ", conn, tx);

                    insertCmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = vm.Nombre.Trim();
                    insertCmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = DbValue(vm.Descripcion);
                    insertCmd.Parameters.Add("@Version", SqlDbType.Int).Value = vm.Version;
                    insertCmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = vm.Estatus;
                    insertCmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = true;
                    insertCmd.Parameters.Add("@CreadoPorUsuarioID", SqlDbType.Int).Value = usuarioId;

                    cuestionarioId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
                }

                var preguntas = vm.Preguntas ?? new List<PreguntaEditorVm>();
                var ordenPregunta = 1;

                foreach (var pregunta in preguntas)
                {
                    if (string.IsNullOrWhiteSpace(pregunta.TextoPregunta))
                        continue;

                    pregunta.TipoPregunta = string.IsNullOrWhiteSpace(pregunta.TipoPregunta)
                        ? "texto"
                        : pregunta.TipoPregunta.Trim().ToLowerInvariant();

                    if (pregunta.TipoPregunta != "texto" &&
                        pregunta.TipoPregunta != "opcion_multiple" &&
                        pregunta.TipoPregunta != "checkbox")
                    {
                        pregunta.TipoPregunta = "texto";
                    }

                    pregunta.Dimension = string.IsNullOrWhiteSpace(pregunta.Dimension)
                        ? string.Empty
                        : pregunta.Dimension.Trim();
                    pregunta.ConfiguracionJson = string.Empty;

                    if (pregunta.TipoPregunta == "texto")
                    {
                        pregunta.Opciones = new List<OpcionPreguntaEditorVm>();
                    }

                    int preguntaId;

                    using (var preguntaCmd = new SqlCommand(@"
                        INSERT INTO dbo.Preguntas
                        (
                            CuestionarioID,
                            TextoPregunta,
                            TipoPregunta,
                            Dimension,
                            Orden,
                            Obligatoria,
                            ConfiguracionJson,
                            Activa
                        )
                        OUTPUT INSERTED.PreguntaID
                        VALUES
                        (
                            @CuestionarioID,
                            @TextoPregunta,
                            @TipoPregunta,
                            @Dimension,
                            @Orden,
                            @Obligatoria,
                            @ConfiguracionJson,
                            @Activa
                        );
                    ", conn, tx))
                    {
                        preguntaCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;
                        preguntaCmd.Parameters.Add("@TextoPregunta", SqlDbType.NVarChar, 500).Value = pregunta.TextoPregunta.Trim();
                        preguntaCmd.Parameters.Add("@TipoPregunta", SqlDbType.NVarChar, 30).Value = pregunta.TipoPregunta;
                        preguntaCmd.Parameters.Add("@Dimension", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(pregunta.Dimension) ? DBNull.Value : pregunta.Dimension.Trim();
                        preguntaCmd.Parameters.Add("@Orden", SqlDbType.Int).Value = ordenPregunta;
                        preguntaCmd.Parameters.Add("@Obligatoria", SqlDbType.Bit).Value = pregunta.Obligatoria;
                        preguntaCmd.Parameters.Add("@ConfiguracionJson", SqlDbType.NVarChar, -1).Value = DBNull.Value;
                        preguntaCmd.Parameters.Add("@Activa", SqlDbType.Bit).Value = true;

                        preguntaId = Convert.ToInt32(await preguntaCmd.ExecuteScalarAsync());
                    }

                    if (pregunta.TipoPregunta == "opcion_multiple" || pregunta.TipoPregunta == "checkbox")
                    {
                        var opciones = pregunta.Opciones ?? new List<OpcionPreguntaEditorVm>();
                        var ordenOpcion = 1;

                        foreach (var opcion in opciones)
                        {
                            if (string.IsNullOrWhiteSpace(opcion.TextoOpcion))
                                continue;

                            var puntaje = opcion.ValorPuntaje > 0 ? 1m : 0;

                            if (puntaje < 0)
                                puntaje = 0;

                            if (puntaje > 10)
                                puntaje = 10;

                            puntaje = Math.Round(puntaje, 0);

                            using var opcionCmd = new SqlCommand(@"
                                INSERT INTO dbo.OpcionesPregunta
                                (
                                    PreguntaID,
                                    TextoOpcion,
                                    ValorPuntaje,
                                    Orden,
                                    Activa
                                )
                                VALUES
                                (
                                    @PreguntaID,
                                    @TextoOpcion,
                                    @ValorPuntaje,
                                    @Orden,
                                    @Activa
                                );
                            ", conn, tx);

                            opcionCmd.Parameters.Add("@PreguntaID", SqlDbType.Int).Value = preguntaId;
                            opcionCmd.Parameters.Add("@TextoOpcion", SqlDbType.NVarChar, 300).Value = opcion.TextoOpcion.Trim();

                            var puntajeParam = opcionCmd.Parameters.Add("@ValorPuntaje", SqlDbType.Decimal);
                            puntajeParam.Precision = 10;
                            puntajeParam.Scale = 2;
                            puntajeParam.Value = puntaje;

                            opcionCmd.Parameters.Add("@Orden", SqlDbType.Int).Value = ordenOpcion;
                            opcionCmd.Parameters.Add("@Activa", SqlDbType.Bit).Value = true;

                            await opcionCmd.ExecuteNonQueryAsync();

                            ordenOpcion++;
                        }
                    }

                    ordenPregunta++;
                }

                tx.Commit();

                return cuestionarioId;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static async Task EliminarPreguntasYOpcionesAsync(
            SqlConnection conn,
            SqlTransaction tx,
            int cuestionarioId)
        {
            using (var deleteOpcionesCmd = new SqlCommand(@"
                DELETE op
                FROM dbo.OpcionesPregunta op
                INNER JOIN dbo.Preguntas p
                    ON op.PreguntaID = p.PreguntaID
                WHERE p.CuestionarioID = @CuestionarioID;
            ", conn, tx))
            {
                deleteOpcionesCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;
                await deleteOpcionesCmd.ExecuteNonQueryAsync();
            }

            using (var deletePreguntasCmd = new SqlCommand(@"
                DELETE FROM dbo.Preguntas
                WHERE CuestionarioID = @CuestionarioID;
            ", conn, tx))
            {
                deletePreguntasCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;
                await deletePreguntasCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<CrearCuestionarioVm?> ObtenerCuestionarioAsync(int cuestionarioId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            CrearCuestionarioVm? vm = null;

            using (var cmd = new SqlCommand(@"
                SELECT
                    CuestionarioID,
                    Nombre,
                    Descripcion,
                    Version,
                    Estatus
                FROM dbo.Cuestionarios
                WHERE CuestionarioID = @CuestionarioID;
            ", conn))
            {
                cmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;

                using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    vm = new CrearCuestionarioVm
                    {
                        CuestionarioID = Convert.ToInt32(reader["CuestionarioID"]),
                        Nombre = reader["Nombre"]?.ToString() ?? string.Empty,
                        Descripcion = reader["Descripcion"]?.ToString() ?? string.Empty,
                        Version = Convert.ToInt32(reader["Version"]),
                        Estatus = reader["Estatus"]?.ToString() ?? "borrador",
                        Preguntas = new List<PreguntaEditorVm>()
                    };
                }
            }

            if (vm == null)
                return null;

            using (var preguntasCmd = new SqlCommand(@"
                SELECT
                    PreguntaID,
                    CuestionarioID,
                    TextoPregunta,
                    TipoPregunta,
                    Dimension,
                    Orden,
                    Obligatoria,
                    ConfiguracionJson,
                    Activa
                FROM dbo.Preguntas
                WHERE CuestionarioID = @CuestionarioID
                ORDER BY Orden;
            ", conn))
            {
                preguntasCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;

                using var reader = await preguntasCmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    vm.Preguntas.Add(new PreguntaEditorVm
                    {
                        PreguntaID = Convert.ToInt32(reader["PreguntaID"]),
                        CuestionarioID = Convert.ToInt32(reader["CuestionarioID"]),
                        TextoPregunta = reader["TextoPregunta"]?.ToString() ?? string.Empty,
                        TipoPregunta = reader["TipoPregunta"]?.ToString() ?? "texto",
                        Dimension = reader["Dimension"]?.ToString() ?? string.Empty,
                        Orden = Convert.ToInt32(reader["Orden"]),
                        Obligatoria = Convert.ToBoolean(reader["Obligatoria"]),
                        ConfiguracionJson = reader["ConfiguracionJson"]?.ToString() ?? string.Empty,
                        Activa = Convert.ToBoolean(reader["Activa"]),
                        Opciones = new List<OpcionPreguntaEditorVm>()
                    });
                }
            }

            foreach (var pregunta in vm.Preguntas)
            {
                using var opcionesCmd = new SqlCommand(@"
                    SELECT
                        OpcionID,
                        PreguntaID,
                        TextoOpcion,
                        ValorPuntaje,
                        Orden,
                        Activa
                    FROM dbo.OpcionesPregunta
                    WHERE PreguntaID = @PreguntaID
                    ORDER BY Orden;
                ", conn);

                opcionesCmd.Parameters.Add("@PreguntaID", SqlDbType.Int).Value = pregunta.PreguntaID ?? 0;

                using var reader = await opcionesCmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    pregunta.Opciones.Add(new OpcionPreguntaEditorVm
                    {
                        OpcionID = Convert.ToInt32(reader["OpcionID"]),
                        PreguntaID = Convert.ToInt32(reader["PreguntaID"]),
                        TextoOpcion = reader["TextoOpcion"]?.ToString() ?? string.Empty,
                        ValorPuntaje = Convert.ToDecimal(reader["ValorPuntaje"]),
                        Orden = Convert.ToInt32(reader["Orden"]),
                        Activa = Convert.ToBoolean(reader["Activa"])
                    });
                }
            }

            if (!vm.Preguntas.Any())
            {
                vm.Preguntas.Add(new PreguntaEditorVm
                {
                    Orden = 1,
                    TipoPregunta = "texto",
                    Obligatoria = true,
                    Activa = true,
                    Opciones = new List<OpcionPreguntaEditorVm>()
                });
            }

            return vm;
        }

        private static object DbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value.Trim();
        }

        private async Task AsociarCuestionarioAAgendaAsync(int agendaId, int cuestionarioId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
                UPDATE dbo.Agendas
                SET CuestionarioID = @CuestionarioID
                WHERE AgendaID = @AgendaID;
            ", conn);

            cmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = cuestionarioId;
            cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

            await cmd.ExecuteNonQueryAsync();
        }

        #endregion


        private async Task<List<CitasUsuarioEventoVm>> CargarEventosUsuarioAsync(
            SqlConnection conn,
            int empresaId,
            int? personaId)
        {
            var eventos = new List<CitasUsuarioEventoVm>();

            using (var cmd = new SqlCommand(@"
                SELECT
                    ae.AgendaEmpresaID,
                    ae.AgendaID,
                    ae.EmpresaID,
                    ISNULL(e.Nombre, CONCAT('Empresa #', ae.EmpresaID)) AS NombreEmpresa,

                    ISNULL(a.Nombre, CONCAT('Agenda #', ae.AgendaID)) AS NombreEvento,
                    a.FechaInicio AS FechaInicioEvento,
                    a.FechaFin AS FechaFinEvento,
                    ISNULL(a.Estatus, '') AS EstatusEvento,
                    ISNULL(a.Activa, 0) AS AgendaActiva,

                    ae.FechaInicioSolicitud,
                    ae.FechaFinSolicitud,
                    ISNULL(ae.EstadoAsignacion, '') AS EstadoAsignacion,
                    ISNULL(ae.Activa, 0) AS AgendaEmpresaActiva,

                    ISNULL(q.Activo, 0) AS CuestionarioActivo,
                    ISNULL(q.Estatus, '') AS EstatusCuestionario,

                    c.CitaID,
                    c.FechaCita,
                    c.HoraInicio,
                    c.HoraFin,
                    c.Estado AS EstadoCita,
                    c.EstadoFormulario
                FROM dbo.AgendaEmpresas ae
                INNER JOIN dbo.Agendas a
                    ON a.AgendaID = ae.AgendaID
                LEFT JOIN dbo.Empresas e
                    ON e.EmpresaID = ae.EmpresaID
                LEFT JOIN dbo.Cuestionarios q
                    ON q.CuestionarioID = a.CuestionarioID
                OUTER APPLY
                (
                    SELECT TOP 1
                        c.CitaID,
                        c.FechaCita,
                        c.HoraInicio,
                        c.HoraFin,
                        c.Estado,
                        c.EstadoFormulario
                    FROM dbo.Citas c
                    WHERE c.AgendaID = ae.AgendaID
                    AND c.EmpresaID = ae.EmpresaID
                    AND @PersonaID IS NOT NULL
                    AND c.PersonaID = @PersonaID
                    AND c.Estado <> 'cancelada'
                    ORDER BY
                        c.FechaRegistro DESC,
                        c.CitaID DESC
                ) c
                WHERE ae.EmpresaID = @EmpresaID
                AND ISNULL(a.Estatus, '') <> 'cancelado'
                ORDER BY
                    a.FechaInicio DESC,
                    ae.AgendaEmpresaID DESC;
            ", conn))
            {
                cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value =
                    personaId.HasValue ? personaId.Value : DBNull.Value;

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var citaId = ReadInt(rd, "CitaID");

                    eventos.Add(new CitasUsuarioEventoVm
                    {
                        AgendaEmpresaID = ReadInt(rd, "AgendaEmpresaID"),
                        AgendaID = ReadInt(rd, "AgendaID"),
                        EmpresaID = ReadInt(rd, "EmpresaID"),
                        NombreEmpresa = ReadString(rd, "NombreEmpresa"),

                        NombreEvento = ReadString(rd, "NombreEvento"),
                        FechaInicioEvento = ReadDateTime(rd, "FechaInicioEvento"),
                        FechaFinEvento = ReadDateTime(rd, "FechaFinEvento"),
                        EstatusEvento = ReadString(rd, "EstatusEvento"),
                        AgendaActiva = ReadBool(rd, "AgendaActiva"),

                        FechaInicioSolicitud = ReadDateTime(rd, "FechaInicioSolicitud"),
                        FechaFinSolicitud = ReadDateTime(rd, "FechaFinSolicitud"),
                        EstadoAsignacion = ReadString(rd, "EstadoAsignacion"),
                        AgendaEmpresaActiva = ReadBool(rd, "AgendaEmpresaActiva"),

                        CuestionarioActivo = ReadBool(rd, "CuestionarioActivo"),
                        EstatusCuestionario = ReadString(rd, "EstatusCuestionario"),

                        CitaID = citaId > 0 ? citaId : null,
                        FechaCita = ReadDateTime(rd, "FechaCita"),
                        HoraInicio = ReadTimeSpan(rd, "HoraInicio"),
                        HoraFin = ReadTimeSpan(rd, "HoraFin"),
                        EstadoCita = ReadString(rd, "EstadoCita"),
                        EstadoFormulario = ReadString(rd, "EstadoFormulario")
                    });
                }
            }

            var ahora = DateTime.Now;

            foreach (var evento in eventos)
            {
                evento.TieneCita = evento.CitaID.HasValue;

                evento.FechaCierreAgendamiento =
                    evento.FechaInicioEvento.HasValue && evento.FechaFinSolicitud.HasValue
                        ? CalcularCierreAgendamiento(evento.FechaInicioEvento.Value, evento.FechaFinSolicitud.Value)
                        : evento.FechaFinSolicitud;

                var estadoAsignacion = evento.EstadoAsignacion.ToLowerInvariant();
                var estatusEvento = evento.EstatusEvento.ToLowerInvariant();
                var estatusCuestionario = evento.EstatusCuestionario.ToLowerInvariant();

                var eventoNoTieneCuestionario =
     string.IsNullOrWhiteSpace(evento.EstatusCuestionario) &&
     !evento.CuestionarioActivo;

                var cuestionarioDisponible =
                    eventoNoTieneCuestionario ||
                    (
                        evento.CuestionarioActivo &&
                        estatusCuestionario == "activo"
                    );
                var asignacionPuedeAbrir =
                    estadoAsignacion == "programada" ||
                    estadoAsignacion == "abierta";

                var dentroPeriodoSolicitud =
                    evento.FechaInicioSolicitud.HasValue &&
                    evento.FechaCierreAgendamiento.HasValue &&
                    ahora >= evento.FechaInicioSolicitud.Value &&
                    ahora <= evento.FechaCierreAgendamiento.Value;

                if (evento.TieneCita)
                {
                    evento.PuedeAgendar = false;

                    if (string.Equals(evento.EstadoCita, "finalizada", StringComparison.OrdinalIgnoreCase))
                    {
                        evento.EstadoPantalla = "finalizado";
                        evento.MensajeUsuario = "Tu entrevista de este evento ya fue finalizada.";
                    }
                    else
                    {
                        evento.EstadoPantalla = "con_cita";
                        evento.MensajeUsuario = "Ya tienes una cita registrada para este evento.";
                    }

                    continue;
                }

                if (!evento.AgendaActiva || !evento.AgendaEmpresaActiva || estatusEvento != "activo")
                {
                    evento.EstadoPantalla = "finalizado";
                    evento.PuedeAgendar = false;
                    evento.MensajeUsuario = "Este evento ya no está activo.";
                    continue;
                }

                if (!cuestionarioDisponible)
                {
                    evento.EstadoPantalla = "en_proceso";
                    evento.PuedeAgendar = false;
                    evento.MensajeUsuario = "El evento está asignado, pero el cuestionario aún no está activo.";
                    continue;
                }

                if (!asignacionPuedeAbrir)
                {
                    evento.EstadoPantalla = "finalizado";
                    evento.PuedeAgendar = false;
                    evento.MensajeUsuario = "El periodo de solicitud de este evento no está abierto.";
                    continue;
                }

                if (evento.FechaInicioSolicitud.HasValue && ahora < evento.FechaInicioSolicitud.Value)
                {
                    evento.EstadoPantalla = "en_proceso";
                    evento.PuedeAgendar = false;
                    evento.MensajeUsuario = "Este evento abrirá próximamente.";
                    continue;
                }

                if (evento.FechaCierreAgendamiento.HasValue && ahora > evento.FechaCierreAgendamiento.Value)
                {
                    var eventoYaTermino =
                        evento.FechaFinEvento.HasValue &&
                        ahora.Date > evento.FechaFinEvento.Value.Date;

                    evento.PuedeAgendar = false;

                    if (eventoYaTermino)
                    {
                        evento.EstadoPantalla = "finalizado";
                        evento.MensajeUsuario = "Este evento ya finalizó.";
                    }
                    else
                    {
                        evento.EstadoPantalla = "agendamiento_cerrado";
                        evento.MensajeUsuario = "El evento sigue activo, pero el periodo para solicitar cita ya cerró.";
                    }

                    continue;
                }

                if (dentroPeriodoSolicitud &&
                    evento.FechaInicioEvento.HasValue &&
                    evento.FechaFinEvento.HasValue)
                {
                    evento.TieneSlotsDisponibles = await HaySlotsDisponiblesAsync(
                        conn,
                        evento.AgendaID,
                        evento.FechaInicioEvento.Value,
                        evento.FechaFinEvento.Value
                    );

                    evento.PuedeAgendar = evento.TieneSlotsDisponibles;
                    evento.EstadoPantalla = evento.TieneSlotsDisponibles ? "abierto" : "finalizado";
                    evento.MensajeUsuario = evento.TieneSlotsDisponibles
                        ? "Ya puedes solicitar tu cita para este evento."
                        : "Ya no hay espacios disponibles para este evento.";

                    continue;
                }

                evento.EstadoPantalla = "finalizado";
                evento.PuedeAgendar = false;
                evento.MensajeUsuario = "Este evento no está disponible para agendar.";
            }

            return eventos;
        }

        private async Task<CitasEditorIndexVm> CargarIndexEditorAsync(SqlConnection conn)
        {
            var vm = new CitasEditorIndexVm();

            using (var cmd = new SqlCommand(@"
                SELECT
                    (SELECT COUNT(*)
                     FROM dbo.Agendas
                     WHERE Activa = 1) AS TotalEventosActivos,

                    (SELECT COUNT(DISTINCT EmpresaID)
                     FROM dbo.AgendaEmpresas
                     WHERE Activa = 1) AS TotalEmpresasHabilitadas,

                    (SELECT COUNT(*)
                     FROM dbo.Citas
                     WHERE Estado <> 'cancelada') AS TotalCitasAgendadas,

                    (SELECT COUNT(*)
                     FROM dbo.Citas
                     WHERE Estado = 'finalizada') AS TotalEntrevistasFinalizadas,

                    (SELECT COUNT(*)
                     FROM dbo.Citas
                     WHERE Estado = 'no_asistio') AS TotalNoAsistieron,

                    (SELECT COUNT(*)
                     FROM dbo.Citas
                     WHERE Estado IN ('pendiente', 'asistio')) AS TotalPendientes;
            ", conn))
            {
                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.TotalEventosActivos = ReadInt(rd, "TotalEventosActivos");
                    vm.TotalEmpresasHabilitadas = ReadInt(rd, "TotalEmpresasHabilitadas");
                    vm.TotalCitasAgendadas = ReadInt(rd, "TotalCitasAgendadas");
                    vm.TotalEntrevistasFinalizadas = ReadInt(rd, "TotalEntrevistasFinalizadas");
                    vm.TotalNoAsistieron = ReadInt(rd, "TotalNoAsistieron");
                    vm.TotalPendientes = ReadInt(rd, "TotalPendientes");
                }
            }

            using (var cmd = new SqlCommand(@"
                SELECT TOP 5
                    a.AgendaID,
                    a.Nombre AS NombreEvento,
                    a.FechaInicio,
                    a.FechaFin,
                    COUNT(DISTINCT ae.EmpresaID) AS EmpresasHabilitadas,
                    COUNT(DISTINCT CASE
                        WHEN c.Estado <> 'cancelada' THEN c.CitaID
                    END) AS CitasAgendadas
                FROM dbo.Agendas a
                LEFT JOIN dbo.AgendaEmpresas ae
                    ON ae.AgendaID = a.AgendaID
                   AND ae.Activa = 1
                LEFT JOIN dbo.Citas c
                    ON c.AgendaID = a.AgendaID
                WHERE a.Activa = 1
                GROUP BY
                    a.AgendaID,
                    a.Nombre,
                    a.FechaInicio,
                    a.FechaFin,
                    a.FechaCreacion
                ORDER BY a.FechaCreacion DESC, a.AgendaID DESC;
            ", conn))
            {
                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    vm.Eventos.Add(new CitasEventoResumenVm
                    {
                        AgendaID = ReadInt(rd, "AgendaID"),
                        NombreEvento = ReadString(rd, "NombreEvento"),
                        FechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.MinValue,
                        FechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.MinValue,
                        EmpresasHabilitadas = ReadInt(rd, "EmpresasHabilitadas"),
                        CitasAgendadas = ReadInt(rd, "CitasAgendadas")
                    });
                }
            }

            using (var cmd = new SqlCommand(@"
                SELECT TOP 30
                    a.AgendaID,
                    a.Nombre AS NombreEvento,
                    e.EmpresaID,
                    e.Nombre AS NombreEmpresa,

                    COUNT(DISTINCT u.UsuarioID) AS TotalUsuarios,

                    COUNT(DISTINCT CASE
                        WHEN c.CitaID IS NOT NULL
                         AND c.Estado <> 'cancelada'
                        THEN c.PersonaID
                    END) AS Agendaron,

                    COUNT(DISTINCT CASE
                        WHEN c.Estado = 'finalizada'
                        THEN c.PersonaID
                    END) AS TomaronEntrevista,

                    COUNT(DISTINCT CASE
                        WHEN c.Estado = 'no_asistio'
                        THEN c.PersonaID
                    END) AS NoAsistieron

                FROM dbo.AgendaEmpresas ae
                INNER JOIN dbo.Agendas a
                    ON a.AgendaID = ae.AgendaID
                INNER JOIN dbo.Empresas e
                    ON e.EmpresaID = ae.EmpresaID
                LEFT JOIN dbo.UsuariosEmpresas ue
                    ON ue.EmpresaID = e.EmpresaID
                   AND ue.Activo = 1
                LEFT JOIN dbo.Usuarios u
                    ON u.UsuarioID = ue.UsuarioID
                   AND ISNULL(u.Activo, 1) = 1
                LEFT JOIN dbo.Persona p
                    ON p.PersonaID = u.PersonaID
                LEFT JOIN dbo.Citas c
                    ON c.AgendaID = a.AgendaID
                   AND c.EmpresaID = e.EmpresaID
                   AND c.PersonaID = u.PersonaID
                   AND c.Estado <> 'cancelada'
                WHERE ae.Activa = 1
                  AND a.Activa = 1
                  AND e.Activa = 1
                GROUP BY
                    a.AgendaID,
                    a.Nombre,
                    a.FechaInicio,
                    e.EmpresaID,
                    e.Nombre
                ORDER BY
                    a.FechaInicio DESC,
                    e.Nombre ASC;
            ", conn))
            {
                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var totalUsuarios = ReadInt(rd, "TotalUsuarios");
                    var agendaron = ReadInt(rd, "Agendaron");
                    var tomaron = ReadInt(rd, "TomaronEntrevista");
                    var noAsistieron = ReadInt(rd, "NoAsistieron");

                    var porcentaje = totalUsuarios == 0
                        ? 0
                        : Math.Round((tomaron * 100m) / totalUsuarios, 2);

                    vm.Empresas.Add(new CitasEmpresaAvanceVm
                    {
                        AgendaID = ReadInt(rd, "AgendaID"),
                        NombreEvento = ReadString(rd, "NombreEvento"),
                        EmpresaID = ReadInt(rd, "EmpresaID"),
                        NombreEmpresa = ReadString(rd, "NombreEmpresa"),
                        TotalUsuarios = totalUsuarios,
                        Agendaron = agendaron,
                        TomaronEntrevista = tomaron,
                        NoAsistieron = noAsistieron,
                        FaltanPorAgendar = Math.Max(totalUsuarios - agendaron, 0),
                        FaltanPorEntrevistar = Math.Max(totalUsuarios - tomaron - noAsistieron, 0),
                        PorcentajeAvance = porcentaje
                    });
                }
            }

            return vm;
        }

        // METODO CREAR / EDITAR AGENDA

        [HttpGet]
        public async Task<IActionResult> CrearAgenda(int agendaId)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var vm = await ConstruirCrearAgendaVmAsync(conn, agendaId);

                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando CrearAgenda.");

                TempData["CitasError"] = "No se pudo cargar la pantalla para crear agenda.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando CrearAgenda.");

                TempData["CitasError"] = "Ocurrió un error al cargar la pantalla para crear agenda.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Agendas()
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var agendas = new List<CitasEventoResumenVm>();

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    SELECT
                        a.AgendaID,
                        a.Nombre AS NombreEvento,
                        a.FechaInicio,
                        a.FechaFin,
                        COUNT(DISTINCT ae.EmpresaID) AS EmpresasHabilitadas,
                        COUNT(DISTINCT c.CitaID) AS CitasAgendadas
                    FROM dbo.Agendas a
                    LEFT JOIN dbo.AgendaEmpresas ae
                        ON ae.AgendaID = a.AgendaID
                    AND ae.Activa = 1
                    LEFT JOIN dbo.Citas c
                        ON c.AgendaID = a.AgendaID
                    AND c.Estado <> 'cancelada'
                    WHERE a.Activa = 1
                    GROUP BY
                        a.AgendaID,
                        a.Nombre,
                        a.FechaInicio,
                        a.FechaFin,
                        a.FechaCreacion
                    ORDER BY
                        a.FechaCreacion DESC,
                        a.AgendaID DESC;
                ", conn);

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    agendas.Add(new CitasEventoResumenVm
                    {
                        AgendaID = ReadInt(rd, "AgendaID"),
                        NombreEvento = ReadString(rd, "NombreEvento"),
                        FechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.MinValue,
                        FechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.MinValue,
                        EmpresasHabilitadas = ReadInt(rd, "EmpresasHabilitadas"),
                        CitasAgendadas = ReadInt(rd, "CitasAgendadas")
                    });
                }

                return View(agendas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando administración de agendas.");

                TempData["CitasError"] = "No se pudieron cargar las agendas creadas.";
                return RedirectToAction(nameof(CrearAgenda));
            }
        }

       [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CrearAgenda(
    CrearAgendaVm vm,
    string AccionFormulario,
    bool OmitirCuestionario = false,
    bool CrearCuestionarioDespues = false)
{
    var usuarioId = ObtenerUsuarioId();

    if (!usuarioId.HasValue)
        return RedirectToAction("Login", "Login");

    var noTieneCuestionario =
        !vm.CuestionarioID.HasValue || vm.CuestionarioID.Value <= 0;

    if (noTieneCuestionario && CrearCuestionarioDespues)
    {
        vm.CuestionarioID = null;
        vm.Estatus = "activo";
    }
    else if (noTieneCuestionario && OmitirCuestionario)
    {
        vm.CuestionarioID = null;
        vm.Estatus = AccionFormulario == "activo"
            ? "activo"
            : "borrador";
    }
    else
    {
        vm.Estatus = AccionFormulario == "activo"
            ? "activo"
            : "borrador";
    }

    var errores = ValidarAgenda(
        vm,
        crearCuestionarioDespues: CrearCuestionarioDespues,
        omitirCuestionario: OmitirCuestionario
    );

    if (errores.Any())
    {
        foreach (var error in errores)
            ModelState.AddModelError(string.Empty, error);

        using var connErrores = new SqlConnection(_connectionString);
        await connErrores.OpenAsync();

        await RecargarListasCrearAgendaAsync(connErrores, vm);

        return View(vm);
    }

    try
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var agendaId = await GuardarAgendaAsync(conn, vm, usuarioId.Value);

        if (CrearCuestionarioDespues)
        {
            TempData["CitasOk"] = "El evento se guardó como activo. Ahora crea el cuestionario para asociarlo automáticamente.";

            return RedirectToAction(nameof(CrearCuestionario), new
            {
                agendaId
            });
        }

        if (OmitirCuestionario && noTieneCuestionario)
        {
            TempData["CitasOk"] = "El evento se guardó sin cuestionario.";

            return RedirectToAction(nameof(Index));
        }

        TempData["CitasOk"] = vm.Estatus == "activo"
            ? "Evento guardado y activado correctamente."
            : "Evento guardado correctamente.";

        return RedirectToAction(nameof(CrearAgenda), new
        {
            agendaId
        });
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Error SQL guardando agenda.");

        ModelState.AddModelError(string.Empty, "No se pudo guardar el evento en la base de datos.");

        using var connErrores = new SqlConnection(_connectionString);
        await connErrores.OpenAsync();

        await RecargarListasCrearAgendaAsync(connErrores, vm);

        return View(vm);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general guardando agenda.");

        ModelState.AddModelError(string.Empty, "Ocurrió un error al guardar el evento.");

        using var connErrores = new SqlConnection(_connectionString);
        await connErrores.OpenAsync();

        await RecargarListasCrearAgendaAsync(connErrores, vm);

        return View(vm);
    }
}

        private async Task<CrearAgendaVm> ConstruirCrearAgendaVmAsync(
            SqlConnection conn,
            int? agendaId)
        {
            CrearAgendaVm vm;

            if (agendaId.HasValue && agendaId.Value > 0)
            {
                vm = await ObtenerAgendaAsync(conn, agendaId.Value)
                    ?? new CrearAgendaVm();
            }
            else
            {
                vm = new CrearAgendaVm
                {
                    FechaInicio = DateTime.Today,
                    FechaFin = DateTime.Today.AddDays(7),
                    Estatus = "borrador"
                };
            }

            await RecargarListasCrearAgendaAsync(conn, vm);

            return vm;
        }

        private async Task RecargarListasCrearAgendaAsync(
            SqlConnection conn,
            CrearAgendaVm vm)
        {
            vm.Cuestionarios = await CargarCuestionariosSelectAsync(
                conn,
                vm.CuestionarioID ?? 0
            );

            vm.Empresas = await CargarEmpresasAgendaAsync(
                conn,
                vm.AgendaID,
                vm.Empresas
            );

            vm.Dias = await CargarDiasAgendaAsync(
                conn,
                vm.AgendaID,
                vm.Dias
            );
        }

        private async Task<List<SelectListItem>> CargarCuestionariosSelectAsync(
            SqlConnection conn,
            int cuestionarioIdSeleccionado)
        {
            var cuestionarios = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Selecciona un cuestionario",
                    Selected = cuestionarioIdSeleccionado <= 0
                }
            };

            using var cmd = new SqlCommand(@"
                SELECT
                    CuestionarioID,
                    Nombre,
                    Version,
                    Estatus
                FROM dbo.Cuestionarios
                WHERE Activo = 1
                AND Estatus IN ('borrador', 'activo')
                ORDER BY
                    CASE WHEN Estatus = 'activo' THEN 0 ELSE 1 END,
                    Nombre;
            ", conn);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var cuestionarioId = ReadInt(rd, "CuestionarioID");
                var nombre = ReadString(rd, "Nombre");
                var version = ReadInt(rd, "Version");
                var estatus = ReadString(rd, "Estatus");

                cuestionarios.Add(new SelectListItem
                {
                    Value = cuestionarioId.ToString(),
                    Text = $"{nombre} - v{version} ({estatus})",
                    Selected = cuestionarioIdSeleccionado == cuestionarioId
                });
            }

            return cuestionarios;
        }

        private async Task<List<AgendaEmpresaEditorVm>> CargarEmpresasAgendaAsync(
            SqlConnection conn,
            int agendaId,
            List<AgendaEmpresaEditorVm>? empresasPost)
        {
            var empresas = new List<AgendaEmpresaEditorVm>();

            using var cmd = new SqlCommand(@"
                SELECT
                    e.EmpresaID,
                    e.Nombre AS NombreEmpresa,
                    ae.AgendaEmpresaID,
                    ae.FechaInicioSolicitud,
                    ae.FechaFinSolicitud,
                    ae.EstadoAsignacion,
                    ae.MostrarAntesDeInicio,
                    ae.MostrarDespuesDeFin,
                    ae.MensajeAntesDeInicio,
                    ae.MensajeDespuesDeFin
                FROM dbo.Empresas e
                LEFT JOIN dbo.AgendaEmpresas ae
                    ON ae.EmpresaID = e.EmpresaID
                AND ae.AgendaID = @AgendaID
                AND ae.Activa = 1
                WHERE e.Activa = 1
                ORDER BY e.Nombre;
            ", conn);

            cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var empresaId = ReadInt(rd, "EmpresaID");

                var empresaPost = empresasPost?
                    .FirstOrDefault(x => x.EmpresaID == empresaId);

                var tieneAsignacion = rd["AgendaEmpresaID"] != DBNull.Value;

                empresas.Add(new AgendaEmpresaEditorVm
                {
                    AgendaEmpresaID = empresaPost?.AgendaEmpresaID ?? ReadInt(rd, "AgendaEmpresaID"),
                    EmpresaID = empresaId,
                    NombreEmpresa = ReadString(rd, "NombreEmpresa"),

                    Seleccionada = empresaPost?.Seleccionada ?? tieneAsignacion,

                    FechaInicioSolicitud =
                        empresaPost?.FechaInicioSolicitud ??
                        ReadDateTime(rd, "FechaInicioSolicitud") ??
                        DateTime.Today,

                    FechaFinSolicitud =
                        empresaPost?.FechaFinSolicitud ??
                        ReadDateTime(rd, "FechaFinSolicitud") ??
                        DateTime.Today.AddDays(7),

                    EstadoAsignacion =
                        !string.IsNullOrWhiteSpace(empresaPost?.EstadoAsignacion)
                            ? empresaPost.EstadoAsignacion
                            : !string.IsNullOrWhiteSpace(ReadString(rd, "EstadoAsignacion"))
                                ? ReadString(rd, "EstadoAsignacion")
                                : "programada",

                    MostrarAntesDeInicio =
                        empresaPost?.MostrarAntesDeInicio ??
                        (rd["MostrarAntesDeInicio"] == DBNull.Value || ReadBool(rd, "MostrarAntesDeInicio")),

                    MostrarDespuesDeFin =
                        empresaPost?.MostrarDespuesDeFin ??
                        (rd["MostrarDespuesDeFin"] == DBNull.Value || ReadBool(rd, "MostrarDespuesDeFin")),

                    MensajeAntesDeInicio =
                        empresaPost?.MensajeAntesDeInicio ??
                        ReadString(rd, "MensajeAntesDeInicio"),

                    MensajeDespuesDeFin =
                        empresaPost?.MensajeDespuesDeFin ??
                        ReadString(rd, "MensajeDespuesDeFin")
                });
            }

            return empresas;
        }

        private async Task<List<AgendaDiaEditorVm>> CargarDiasAgendaAsync(
            SqlConnection conn,
            int agendaId,
            List<AgendaDiaEditorVm>? diasPost)
        {
            var dias = new List<AgendaDiaEditorVm>();

            if (agendaId > 0)
            {
                using var cmd = new SqlCommand(@"
                    SELECT
                        AgendaDiaID,
                        AgendaID,
                        DiaSemana,
                        HoraInicio,
                        HoraFin,
                        DuracionCitaMinutos,
                        DescansoMinutos,
                        CapacidadCitas,
                        Activo
                    FROM dbo.AgendaDias
                    WHERE AgendaID = @AgendaID
                    ORDER BY DiaSemana;
                ", conn);

                cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var diaSemana = ReadInt(rd, "DiaSemana");

                    var diaPost = diasPost?
                        .FirstOrDefault(x => x.DiaSemana == diaSemana);

                    dias.Add(new AgendaDiaEditorVm
                    {
                        AgendaDiaID = diaPost?.AgendaDiaID ?? ReadInt(rd, "AgendaDiaID"),
                        AgendaID = agendaId,
                        DiaSemana = diaSemana,
                        NombreDia = NombreDiaSemana(diaSemana),

                        Activo = diaPost?.Activo ?? ReadBool(rd, "Activo"),

                        HoraInicio =
                            diaPost?.HoraInicio ??
                            ReadTimeSpan(rd, "HoraInicio") ??
                            new TimeSpan(9, 0, 0),

                        HoraFin =
                            diaPost?.HoraFin ??
                            ReadTimeSpan(rd, "HoraFin") ??
                            new TimeSpan(17, 0, 0),

                        DuracionCitaMinutos =
                            diaPost?.DuracionCitaMinutos ?? ReadInt(rd, "DuracionCitaMinutos"),

                        DescansoMinutos =
                            diaPost?.DescansoMinutos ?? ReadInt(rd, "DescansoMinutos"),

                        CapacidadCitas =
                            diaPost?.CapacidadCitas ?? ReadInt(rd, "CapacidadCitas")
                    });
                }
            }

            if (!dias.Any())
            {
                dias = CrearDiasDefault();
            }

            return dias;
        }

        private static List<AgendaDiaEditorVm> CrearDiasDefault()
        {
            var dias = new List<AgendaDiaEditorVm>();

            for (var dia = 1; dia <= 7; dia++)
            {
                dias.Add(new AgendaDiaEditorVm
                {
                    DiaSemana = dia,
                    NombreDia = NombreDiaSemana(dia),
                    Activo = dia >= 1 && dia <= 5,
                    HoraInicio = new TimeSpan(9, 0, 0),
                    HoraFin = new TimeSpan(17, 0, 0),
                    DuracionCitaMinutos = 40,
                    DescansoMinutos = 20,
                    CapacidadCitas = 1
                });
            }

            return dias;
        }

        private static string NombreDiaSemana(int diaSemana)
        {
            return diaSemana switch
            {
                1 => "Lunes",
                2 => "Martes",
                3 => "Miércoles",
                4 => "Jueves",
                5 => "Viernes",
                6 => "Sábado",
                7 => "Domingo",
                _ => "Día"
            };
        }

        private async Task<CrearAgendaVm?> ObtenerAgendaAsync(
            SqlConnection conn,
            int agendaId)
        {
            using var cmd = new SqlCommand(@"
                SELECT
                    AgendaID,
                    CuestionarioID,
                    Nombre,
                    Descripcion,
                    FechaInicio,
                    FechaFin,
                    Estatus
                FROM dbo.Agendas
                WHERE AgendaID = @AgendaID;
            ", conn);

            cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

            using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new CrearAgendaVm
            {
                AgendaID = ReadInt(rd, "AgendaID"),
                CuestionarioID = rd["CuestionarioID"] == DBNull.Value
    ? null
    : ReadInt(rd, "CuestionarioID"),
                Nombre = ReadString(rd, "Nombre"),
                Descripcion = ReadString(rd, "Descripcion"),
                FechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.Today,
                FechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.Today,
                Estatus = ReadString(rd, "Estatus")
            };
        }

      private static List<string> ValidarAgenda(
    CrearAgendaVm vm,
    bool crearCuestionarioDespues = false,
    bool omitirCuestionario = false)
{
    var errores = new List<string>();

    if ((!vm.CuestionarioID.HasValue || vm.CuestionarioID.Value <= 0)
        && !crearCuestionarioDespues
        && !omitirCuestionario)
    {
        errores.Add("Debes indicar si deseas agregar un cuestionario al evento.");
    }

    if (string.IsNullOrWhiteSpace(vm.Nombre))
        errores.Add("El nombre de la agenda es obligatorio.");

    if (vm.FechaInicio.Date > vm.FechaFin.Date)
        errores.Add("La fecha de inicio no puede ser mayor que la fecha de fin.");

    var empresasSeleccionadas = vm.Empresas?
        .Where(x => x.Seleccionada)
        .ToList() ?? new List<AgendaEmpresaEditorVm>();

    if (!empresasSeleccionadas.Any())
        errores.Add("Debes seleccionar al menos una empresa.");

    foreach (var empresa in empresasSeleccionadas)
    {
        if (!empresa.FechaInicioSolicitud.HasValue)
            errores.Add($"La empresa {empresa.NombreEmpresa} no tiene fecha de inicio de solicitud.");

        if (!empresa.FechaFinSolicitud.HasValue)
            errores.Add($"La empresa {empresa.NombreEmpresa} no tiene fecha de fin de solicitud.");

        if (empresa.FechaInicioSolicitud.HasValue &&
            empresa.FechaFinSolicitud.HasValue &&
            empresa.FechaInicioSolicitud.Value >= empresa.FechaFinSolicitud.Value)
        {
            errores.Add($"La fecha de inicio de solicitud debe ser menor que la fecha fin para {empresa.NombreEmpresa}.");
        }
    }

    var diasActivos = vm.Dias?
        .Where(x => x.Activo)
        .ToList() ?? new List<AgendaDiaEditorVm>();

    if (!diasActivos.Any())
        errores.Add("Debes activar al menos un día de atención.");

    foreach (var dia in diasActivos)
    {
        if (dia.HoraInicio >= dia.HoraFin)
            errores.Add($"El horario de {dia.NombreDia} no es válido.");

        if (dia.DuracionCitaMinutos <= 0)
            errores.Add($"La duración de cita de {dia.NombreDia} debe ser mayor a cero.");

        if (dia.DescansoMinutos < 0)
            errores.Add($"El descanso de {dia.NombreDia} no puede ser negativo.");

        if (dia.CapacidadCitas <= 0)
            errores.Add($"La capacidad de {dia.NombreDia} debe ser mayor a cero.");
    }

    return errores;
}

        private async Task<int> GuardarAgendaAsync(           SqlConnection conn,           CrearAgendaVm vm,            int usuarioId)
        {
            using var tx = conn.BeginTransaction();

            var transaccionCompletada = false;

            try
            {
                var agendaId = vm.AgendaID;

                var eraAgendaNueva = agendaId <= 0;
                var estatusAnterior = string.Empty;
                var empresasAntes = new HashSet<int>();

                var empresasSeleccionadas = vm.Empresas?
                    .Where(x => x.Seleccionada)
                    .Select(x => x.EmpresaID)
                    .Distinct()
                    .ToList() ?? new List<int>();

                if (!eraAgendaNueva)
                {
                    using (var cmdInfoAnterior = new SqlCommand(@"
                        SELECT Estatus
                        FROM dbo.Agendas
                        WHERE AgendaID = @AgendaID;
                    ", conn, tx))
                    {
                        cmdInfoAnterior.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

                        var result = await cmdInfoAnterior.ExecuteScalarAsync();

                        estatusAnterior = result == null || result == DBNull.Value
                            ? string.Empty
                            : Convert.ToString(result) ?? string.Empty;
                    }

                    using (var cmdEmpresasAntes = new SqlCommand(@"
                        SELECT EmpresaID
                        FROM dbo.AgendaEmpresas
                        WHERE AgendaID = @AgendaID
                        AND Activa = 1;
                    ", conn, tx))
                    {
                        cmdEmpresasAntes.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

                        using var rd = await cmdEmpresasAntes.ExecuteReaderAsync();

                        while (await rd.ReadAsync())
                        {
                            empresasAntes.Add(ReadInt(rd, "EmpresaID"));
                        }
                    }
                }

                if (agendaId > 0)
                {
                    using var updateCmd = new SqlCommand(@"
                        UPDATE dbo.Agendas
                        SET
                            CuestionarioID = @CuestionarioID,
                            Nombre = @Nombre,
                            Descripcion = @Descripcion,
                            FechaInicio = @FechaInicio,
                            FechaFin = @FechaFin,
                            Estatus = @Estatus,
                            Activa = 1,
                            FechaPublicacion = CASE
                                WHEN @Estatus = 'activo' THEN ISNULL(FechaPublicacion, SYSDATETIME())
                                ELSE FechaPublicacion
                            END,
                            PublicadoPorUsuarioID = CASE
                                WHEN @Estatus = 'activo' THEN ISNULL(PublicadoPorUsuarioID, @UsuarioID)
                                ELSE PublicadoPorUsuarioID
                            END
                        WHERE AgendaID = @AgendaID;
                    ", conn, tx);

                    updateCmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

                    updateCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value =
                        vm.CuestionarioID.HasValue && vm.CuestionarioID.Value > 0
                            ? (object)vm.CuestionarioID.Value
                            : DBNull.Value;

                    updateCmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = vm.Nombre.Trim();
                    updateCmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = DbValue(vm.Descripcion);
                    updateCmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = vm.FechaInicio.Date;
                    updateCmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = vm.FechaFin.Date;
                    updateCmd.Parameters.Add("@Estatus", SqlDbType.VarChar, 20).Value = vm.Estatus;
                    updateCmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                    await updateCmd.ExecuteNonQueryAsync();

                    await EliminarConfiguracionAgendaAsync(conn, tx, agendaId);
                }
                else
                {
                    using var insertCmd = new SqlCommand(@"
                        INSERT INTO dbo.Agendas
                        (
                            CuestionarioID,
                            Nombre,
                            Descripcion,
                            FechaInicio,
                            FechaFin,
                            Estatus,
                            Activa,
                            CreadoPorUsuarioID,
                            FechaCreacion,
                            FechaPublicacion,
                            PublicadoPorUsuarioID
                        )
                        OUTPUT INSERTED.AgendaID
                        VALUES
                        (
                            @CuestionarioID,
                            @Nombre,
                            @Descripcion,
                            @FechaInicio,
                            @FechaFin,
                            @Estatus,
                            1,
                            @UsuarioID,
                            SYSDATETIME(),
                            CASE WHEN @Estatus = 'activo' THEN SYSDATETIME() ELSE NULL END,
                            CASE WHEN @Estatus = 'activo' THEN @UsuarioID ELSE NULL END
                        );
                    ", conn, tx);

                    insertCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value =
                        vm.CuestionarioID.HasValue && vm.CuestionarioID.Value > 0
                            ? (object)vm.CuestionarioID.Value
                            : DBNull.Value;

                    insertCmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = vm.Nombre.Trim();
                    insertCmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = DbValue(vm.Descripcion);
                    insertCmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = vm.FechaInicio.Date;
                    insertCmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = vm.FechaFin.Date;
                    insertCmd.Parameters.Add("@Estatus", SqlDbType.VarChar, 20).Value = vm.Estatus;
                    insertCmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                    agendaId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
                }

                await InsertarAgendaEmpresasAsync(conn, tx, agendaId, vm, usuarioId);
                await InsertarAgendaDiasAsync(conn, tx, agendaId, vm);

                var empresasANotificar = new List<int>();

                var tieneCuestionario =
                    vm.CuestionarioID.HasValue &&
                    vm.CuestionarioID.Value > 0;

                if (string.Equals(vm.Estatus, "activo", StringComparison.OrdinalIgnoreCase))
                {
                    if (eraAgendaNueva)
                    {
                        empresasANotificar = empresasSeleccionadas;
                    }
                    else if (!string.Equals(estatusAnterior, "activo", StringComparison.OrdinalIgnoreCase))
                    {
                        empresasANotificar = empresasSeleccionadas;
                    }
                    else
                    {
                        empresasANotificar = empresasSeleccionadas
                            .Where(id => !empresasAntes.Contains(id))
                            .ToList();
                    }
                }

                tx.Commit();
                transaccionCompletada = true;

                if (empresasANotificar.Any())
                {
                    await NotificarUsuariosEventoHabilitadoAsync(agendaId, empresasANotificar);
                }

                return agendaId;
            }
            catch
            {
                if (!transaccionCompletada)
                {
                    tx.Rollback();
                }

                throw;
            }
        }

        private static async Task EliminarConfiguracionAgendaAsync(
            SqlConnection conn,
            SqlTransaction tx,
            int agendaId)
        {
            using (var deleteDiasCmd = new SqlCommand(@"
                DELETE FROM dbo.AgendaDias
                WHERE AgendaID = @AgendaID;
            ", conn, tx))
            {
                deleteDiasCmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                await deleteDiasCmd.ExecuteNonQueryAsync();
            }

            using (var deleteEmpresasCmd = new SqlCommand(@"
                DELETE FROM dbo.AgendaEmpresas
                WHERE AgendaID = @AgendaID;
            ", conn, tx))
            {
                deleteEmpresasCmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                await deleteEmpresasCmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertarAgendaEmpresasAsync(
            SqlConnection conn,
            SqlTransaction tx,
            int agendaId,
            CrearAgendaVm vm,
            int usuarioId)
        {
            var empresas = vm.Empresas?
                .Where(x => x.Seleccionada)
                .ToList() ?? new List<AgendaEmpresaEditorVm>();

            foreach (var empresa in empresas)
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.AgendaEmpresas
                    (
                        AgendaID,
                        EmpresaID,
                        FechaInicioSolicitud,
                        FechaFinSolicitud,
                        EstadoAsignacion,
                        MostrarAntesDeInicio,
                        MostrarDespuesDeFin,
                        MensajeAntesDeInicio,
                        MensajeDespuesDeFin,
                        Activa,
                        FechaAsignacion,
                        AsignadoPorUsuarioID
                    )
                    VALUES
                    (
                        @AgendaID,
                        @EmpresaID,
                        @FechaInicioSolicitud,
                        @FechaFinSolicitud,
                        @EstadoAsignacion,
                        @MostrarAntesDeInicio,
                        @MostrarDespuesDeFin,
                        @MensajeAntesDeInicio,
                        @MensajeDespuesDeFin,
                        1,
                        SYSDATETIME(),
                        @UsuarioID
                    );
                ", conn, tx);

                cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresa.EmpresaID;
                cmd.Parameters.Add("@FechaInicioSolicitud", SqlDbType.DateTime2).Value = empresa.FechaInicioSolicitud!.Value;
                cmd.Parameters.Add("@FechaFinSolicitud", SqlDbType.DateTime2).Value = empresa.FechaFinSolicitud!.Value;
                cmd.Parameters.Add("@EstadoAsignacion", SqlDbType.VarChar, 30).Value =
                    string.IsNullOrWhiteSpace(empresa.EstadoAsignacion)
                        ? "programada"
                        : empresa.EstadoAsignacion;

                cmd.Parameters.Add("@MostrarAntesDeInicio", SqlDbType.Bit).Value = empresa.MostrarAntesDeInicio;
                cmd.Parameters.Add("@MostrarDespuesDeFin", SqlDbType.Bit).Value = empresa.MostrarDespuesDeFin;
                cmd.Parameters.Add("@MensajeAntesDeInicio", SqlDbType.NVarChar, 300).Value = DbValue(empresa.MensajeAntesDeInicio);
                cmd.Parameters.Add("@MensajeDespuesDeFin", SqlDbType.NVarChar, 300).Value = DbValue(empresa.MensajeDespuesDeFin);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertarAgendaDiasAsync(
            SqlConnection conn,
            SqlTransaction tx,
            int agendaId,
            CrearAgendaVm vm)
        {
            var dias = vm.Dias ?? new List<AgendaDiaEditorVm>();

            foreach (var dia in dias)
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.AgendaDias
                    (
                        AgendaID,
                        DiaSemana,
                        HoraInicio,
                        HoraFin,
                        DuracionCitaMinutos,
                        DescansoMinutos,
                        CapacidadCitas,
                        Activo
                    )
                    VALUES
                    (
                        @AgendaID,
                        @DiaSemana,
                        @HoraInicio,
                        @HoraFin,
                        @DuracionCitaMinutos,
                        @DescansoMinutos,
                        @CapacidadCitas,
                        @Activo
                    );
                ", conn, tx);

                cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                cmd.Parameters.Add("@DiaSemana", SqlDbType.Int).Value = dia.DiaSemana;
                cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value = dia.HoraInicio;
                cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value = dia.HoraFin;
                cmd.Parameters.Add("@DuracionCitaMinutos", SqlDbType.Int).Value = dia.DuracionCitaMinutos;
                cmd.Parameters.Add("@DescansoMinutos", SqlDbType.Int).Value = dia.DescansoMinutos;
                cmd.Parameters.Add("@CapacidadCitas", SqlDbType.Int).Value = dia.CapacidadCitas;
                cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = dia.Activo;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarAgenda(int agendaId)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            if (agendaId <= 0)
            {
                TempData["CitasError"] = "Agenda inválida.";
                return RedirectToAction(nameof(Agendas));
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction();

                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.Agendas
                    SET
                        Estatus = 'cancelado',
                        Activa = 0
                    WHERE AgendaID = @AgendaID;
                ", conn, tx))
                {
                    cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

                    var filas = await cmd.ExecuteNonQueryAsync();

                    if (filas == 0)
                    {
                        tx.Rollback();

                        TempData["CitasError"] = "No se encontró la agenda seleccionada.";
                        return RedirectToAction(nameof(Agendas));
                    }
                }

                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.AgendaEmpresas
                    SET
                        Activa = 0,
                        EstadoAsignacion = 'cerrada'
                    WHERE AgendaID = @AgendaID;
                ", conn, tx))
                {
                    cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();

                TempData["CitasOk"] = "La agenda fue eliminada correctamente.";
                return RedirectToAction(nameof(Agendas));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL eliminando agenda.");

                TempData["CitasError"] = "No se pudo eliminar la agenda.";
                return RedirectToAction(nameof(Agendas));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general eliminando agenda.");

                TempData["CitasError"] = "Ocurrió un error al eliminar la agenda.";
                return RedirectToAction(nameof(Agendas));
            }
        }

        //METODO AGENDAR

        [HttpGet]
        public async Task<IActionResult> Agendar(int? agendaId, int? agendaEmpresaId)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var personaId = await ObtenerPersonaIdAsync(conn, usuarioId.Value);

                if (!personaId.HasValue)
                {
                    TempData["CitasError"] = "No se encontró una persona asociada al usuario actual.";
                    return RedirectToAction(nameof(Index));
                }

                var empresaId = ObtenerEmpresaId();

                if (empresaId.HasValue)
                {
                    var empresaPermitida = await UsuarioTieneEmpresaAsync(
                        conn,
                        usuarioId.Value,
                        empresaId.Value
                    );

                    if (!empresaPermitida)
                        empresaId = null;
                }

                if (!empresaId.HasValue)
                    empresaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId.Value);

                if (!empresaId.HasValue)
                {
                    TempData["CitasError"] = "No se encontró una empresa activa para el usuario actual.";
                    return RedirectToAction(nameof(Index));
                }

                var contexto = await ObtenerContextoAgendamientoAsync(
                    conn,
                    empresaId.Value,
                    agendaId,
                    agendaEmpresaId
                );

                if (contexto == null)
                {
                    TempData["CitasError"] = "No hay una agenda disponible para tu empresa.";
                    return RedirectToAction(nameof(Index));
                }

                var ahora = DateTime.Now;

                var fechaFinPermitida = CalcularCierreAgendamiento(
                    contexto.FechaInicioAgenda,
                    contexto.FechaFinSolicitud
                );

                if (ahora < contexto.FechaInicioSolicitud || ahora > fechaFinPermitida)
                {
                    TempData["CitasError"] = "El periodo para solicitar cita ya cerró. Las citas se cierran 24 horas antes del inicio del evento.";
                    return RedirectToAction(nameof(Index));
                }

                var yaTieneCita = await PersonaTieneCitaActivaAsync(
                    conn,
                    personaId.Value,
                    contexto.AgendaID
                );

                if (yaTieneCita)
                {
                    TempData["CitasError"] = "Ya tienes una cita activa para esta agenda.";
                    return RedirectToAction(nameof(Index));
                }

                var slots = await GenerarSlotsDisponiblesAsync(
                    conn,
                    contexto.AgendaID,
                    contexto.FechaInicioAgenda,
                    contexto.FechaFinAgenda
                );
                if (!slots.Any())
                {
                    TempData["CitasError"] = "No hay horarios configurados para esta jornada de mapeo.";
                    return RedirectToAction(nameof(Index));
                }

                var vm = new AgendarCitaVm
                {
                    AgendaID = contexto.AgendaID,
                    AgendaEmpresaID = contexto.AgendaEmpresaID,
                    NombreEvento = contexto.NombreEvento,
                    FechaInicioSolicitud = contexto.FechaInicioSolicitud,
                    FechaFinSolicitud = fechaFinPermitida,
                    Slots = slots
                };

                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando pantalla Agendar.");

                TempData["CitasError"] = "No se pudo cargar la pantalla para agendar cita.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando pantalla Agendar.");

                TempData["CitasError"] = "Ocurrió un error al cargar la pantalla para agendar cita.";
                return RedirectToAction(nameof(Index));
            }
        }

        //METODO REPORTES
        [HttpGet]
        public async Task<IActionResult> Reportes(int? empresaId, string periodo = "30", string? q = null)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var vm = new CitasIndexVm
            {
                EsEditor = true,
                ModuloVisible = true
            };

            if (string.IsNullOrWhiteSpace(periodo))
                periodo = "30";

            periodo = periodo.ToLowerInvariant();

            if (periodo != "30" && periodo != "90" && periodo != "365" && periodo != "all")
                periodo = "30";

            DateTime? fechaInicio = null;
            DateTime? fechaFin = null;

            if (periodo == "30")
            {
                fechaInicio = DateTime.Today.AddDays(-30);
                fechaFin = DateTime.Today;
            }
            else if (periodo == "90")
            {
                fechaInicio = DateTime.Today.AddDays(-90);
                fechaFin = DateTime.Today;
            }
            else if (periodo == "365")
            {
                fechaInicio = DateTime.Today.AddDays(-365);
                fechaFin = DateTime.Today;
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                ViewBag.Periodo = periodo;
                ViewBag.EmpresaID = empresaId;
                ViewBag.Busqueda = q ?? string.Empty;
                ViewBag.Empresas = await CargarEmpresasSelectAsync(conn, empresaId);

                vm.Editor = await CargarReportesEditorAsync(
                    conn,
                    empresaId,
                    fechaInicio,
                    fechaFin
                );

                return View(vm);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL cargando Reportes de Citas.");

                ViewBag.Periodo = periodo;
                ViewBag.EmpresaID = empresaId;
                ViewBag.Busqueda = q ?? string.Empty;
                ViewBag.Empresas = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = "",
                        Text = "Todas las empresas",
                        Selected = true
                    }
                };

                vm.AlertaSistema =
                    "No se pudieron cargar los reportes. Revisa que existan las tablas del módulo de citas.";

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general cargando Reportes de Citas.");

                ViewBag.Periodo = periodo;
                ViewBag.EmpresaID = empresaId;
                ViewBag.Busqueda = q ?? string.Empty;
                ViewBag.Empresas = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = "",
                        Text = "Todas las empresas",
                        Selected = true
                    }
                };

                vm.AlertaSistema = "Ocurrió un error al cargar los reportes de mapeo.";

                return View(vm);
            }
        }


        private async Task<List<SelectListItem>> CargarEmpresasSelectAsync(
            SqlConnection conn,
            int? empresaIdSeleccionada)
        {
            var empresas = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Todas las empresas",
                    Selected = !empresaIdSeleccionada.HasValue
                }
            };

            using var cmd = new SqlCommand(@"
                SELECT
                    EmpresaID,
                    Nombre
                FROM dbo.Empresas
                WHERE Activa = 1
                ORDER BY Nombre;
            ", conn);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var empresaId = ReadInt(rd, "EmpresaID");
                var nombre = ReadString(rd, "Nombre");

                empresas.Add(new SelectListItem
                {
                    Value = empresaId.ToString(),
                    Text = nombre,
                    Selected = empresaIdSeleccionada.HasValue && empresaIdSeleccionada.Value == empresaId
                });
            }

            return empresas;
        }

        private async Task<CitasEditorIndexVm> CargarReportesEditorAsync(
            SqlConnection conn,
            int? empresaId,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var vm = new CitasEditorIndexVm();

            using (var cmd = new SqlCommand(@"
                SELECT
                    (
                        SELECT COUNT(DISTINCT a.AgendaID)
                        FROM dbo.Agendas a
                        LEFT JOIN dbo.AgendaEmpresas ae
                            ON ae.AgendaID = a.AgendaID
                           AND ae.Activa = 1
                        WHERE a.Activa = 1
                          AND (@EmpresaID IS NULL OR ae.EmpresaID = @EmpresaID)
                    ) AS TotalEventosActivos,

                    (
                        SELECT COUNT(DISTINCT ae.EmpresaID)
                        FROM dbo.AgendaEmpresas ae
                        INNER JOIN dbo.Empresas e
                            ON e.EmpresaID = ae.EmpresaID
                        WHERE ae.Activa = 1
                          AND e.Activa = 1
                          AND (@EmpresaID IS NULL OR ae.EmpresaID = @EmpresaID)
                    ) AS TotalEmpresasHabilitadas,

                    (
                        SELECT COUNT(*)
                        FROM dbo.Citas c
                        WHERE c.Estado <> 'cancelada'
                          AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
                          AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                          AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                    ) AS TotalCitasAgendadas,

                    (
                        SELECT COUNT(*)
                        FROM dbo.Citas c
                        WHERE c.Estado = 'finalizada'
                          AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
                          AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                          AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                    ) AS TotalEntrevistasFinalizadas,

                    (
                        SELECT COUNT(*)
                        FROM dbo.Citas c
                        WHERE c.Estado = 'no_asistio'
                          AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
                          AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                          AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                    ) AS TotalNoAsistieron,

                    (
                        SELECT COUNT(*)
                        FROM dbo.Citas c
                        WHERE c.Estado IN ('pendiente', 'asistio')
                          AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
                          AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                          AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                    ) AS TotalPendientes;
            ", conn))
            {
                AgregarParametrosReporte(cmd, empresaId, fechaInicio, fechaFin);

                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.TotalEventosActivos = ReadInt(rd, "TotalEventosActivos");
                    vm.TotalEmpresasHabilitadas = ReadInt(rd, "TotalEmpresasHabilitadas");
                    vm.TotalCitasAgendadas = ReadInt(rd, "TotalCitasAgendadas");
                    vm.TotalEntrevistasFinalizadas = ReadInt(rd, "TotalEntrevistasFinalizadas");
                    vm.TotalNoAsistieron = ReadInt(rd, "TotalNoAsistieron");
                    vm.TotalPendientes = ReadInt(rd, "TotalPendientes");
                }
            }

            using (var cmd = new SqlCommand(@"
                SELECT TOP 20
                    a.AgendaID,
                    a.Nombre AS NombreEvento,
                    a.FechaInicio,
                    a.FechaFin,
                    COUNT(DISTINCT ae.EmpresaID) AS EmpresasHabilitadas,
                    COUNT(DISTINCT c.CitaID) AS CitasAgendadas
                FROM dbo.Agendas a
                LEFT JOIN dbo.AgendaEmpresas ae
                    ON ae.AgendaID = a.AgendaID
                   AND ae.Activa = 1
                LEFT JOIN dbo.Citas c
                    ON c.AgendaID = a.AgendaID
                   AND c.Estado <> 'cancelada'
                   AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
                   AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                   AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                WHERE a.Activa = 1
                  AND (@EmpresaID IS NULL OR ae.EmpresaID = @EmpresaID)
                GROUP BY
                    a.AgendaID,
                    a.Nombre,
                    a.FechaInicio,
                    a.FechaFin,
                    a.FechaCreacion
                ORDER BY
                    a.FechaCreacion DESC,
                    a.AgendaID DESC;
            ", conn))
            {
                AgregarParametrosReporte(cmd, empresaId, fechaInicio, fechaFin);

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    vm.Eventos.Add(new CitasEventoResumenVm
                    {
                        AgendaID = ReadInt(rd, "AgendaID"),
                        NombreEvento = ReadString(rd, "NombreEvento"),
                        FechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.MinValue,
                        FechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.MinValue,
                        EmpresasHabilitadas = ReadInt(rd, "EmpresasHabilitadas"),
                        CitasAgendadas = ReadInt(rd, "CitasAgendadas")
                    });
                }
            }

            using (var cmd = new SqlCommand(@"
                SELECT TOP 100
                    a.AgendaID,
                    a.Nombre AS NombreEvento,
                    e.EmpresaID,
                    e.Nombre AS NombreEmpresa,

                    COUNT(DISTINCT u.UsuarioID) AS TotalUsuarios,

                    COUNT(DISTINCT CASE
                        WHEN c.CitaID IS NOT NULL
                         AND c.Estado <> 'cancelada'
                        THEN c.PersonaID
                    END) AS Agendaron,

                    COUNT(DISTINCT CASE
                        WHEN c.Estado = 'finalizada'
                        THEN c.PersonaID
                    END) AS TomaronEntrevista,

                    COUNT(DISTINCT CASE
                        WHEN c.Estado = 'no_asistio'
                        THEN c.PersonaID
                    END) AS NoAsistieron

                FROM dbo.AgendaEmpresas ae
                INNER JOIN dbo.Agendas a
                    ON a.AgendaID = ae.AgendaID
                INNER JOIN dbo.Empresas e
                    ON e.EmpresaID = ae.EmpresaID
                LEFT JOIN dbo.UsuariosEmpresas ue
                    ON ue.EmpresaID = e.EmpresaID
                   AND ue.Activo = 1
                LEFT JOIN dbo.Usuarios u
                    ON u.UsuarioID = ue.UsuarioID
                   AND ISNULL(u.Activo, 1) = 1
                LEFT JOIN dbo.Citas c
                    ON c.AgendaID = a.AgendaID
                   AND c.EmpresaID = e.EmpresaID
                   AND c.PersonaID = u.PersonaID
                   AND c.Estado <> 'cancelada'
                   AND (@FechaInicio IS NULL OR c.FechaCita >= @FechaInicio)
                   AND (@FechaFin IS NULL OR c.FechaCita <= @FechaFin)
                WHERE ae.Activa = 1
                  AND a.Activa = 1
                  AND e.Activa = 1
                  AND (@EmpresaID IS NULL OR e.EmpresaID = @EmpresaID)
                GROUP BY
                    a.AgendaID,
                    a.Nombre,
                    a.FechaInicio,
                    e.EmpresaID,
                    e.Nombre
                ORDER BY
                    a.FechaInicio DESC,
                    e.Nombre ASC;
            ", conn))
            {
                AgregarParametrosReporte(cmd, empresaId, fechaInicio, fechaFin);

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var totalUsuarios = ReadInt(rd, "TotalUsuarios");
                    var agendaron = ReadInt(rd, "Agendaron");
                    var tomaron = ReadInt(rd, "TomaronEntrevista");
                    var noAsistieron = ReadInt(rd, "NoAsistieron");

                    var porcentaje = totalUsuarios == 0
                        ? 0
                        : Math.Round((tomaron * 100m) / totalUsuarios, 2);

                    vm.Empresas.Add(new CitasEmpresaAvanceVm
                    {
                        AgendaID = ReadInt(rd, "AgendaID"),
                        NombreEvento = ReadString(rd, "NombreEvento"),
                        EmpresaID = ReadInt(rd, "EmpresaID"),
                        NombreEmpresa = ReadString(rd, "NombreEmpresa"),
                        TotalUsuarios = totalUsuarios,
                        Agendaron = agendaron,
                        TomaronEntrevista = tomaron,
                        NoAsistieron = noAsistieron,
                        FaltanPorAgendar = Math.Max(totalUsuarios - agendaron, 0),
                        FaltanPorEntrevistar = Math.Max(totalUsuarios - tomaron - noAsistieron, 0),
                        PorcentajeAvance = porcentaje
                    });
                }
            }

            return vm;
        }

        private static void AgregarParametrosReporte(
            SqlCommand cmd,
            int? empresaId,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value =
                empresaId.HasValue ? (object)empresaId.Value : DBNull.Value;

            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value =
                fechaInicio.HasValue ? (object)fechaInicio.Value.Date : DBNull.Value;

            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value =
                fechaFin.HasValue ? (object)fechaFin.Value.Date : DBNull.Value;
        }

     [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Agendar(AgendarCitaPostVm vm)
    {
        var usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
            return RedirectToAction("Login", "Login");

        if (!ModelState.IsValid)
        {
            TempData["CitasError"] = "Selecciona una fecha y horario válidos.";
            return RedirectToAction(nameof(Agendar), new
            {
                agendaId = vm.AgendaID,
                agendaEmpresaId = vm.AgendaEmpresaID
            });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var personaId = await ObtenerPersonaIdAsync(conn, usuarioId.Value);

            if (!personaId.HasValue)
            {
                TempData["CitasError"] = "No se encontró una persona asociada al usuario actual.";
                return RedirectToAction(nameof(Index));
            }

            var empresaId = ObtenerEmpresaId();

            if (empresaId.HasValue)
            {
                var empresaPermitida = await UsuarioTieneEmpresaAsync(
                    conn,
                    usuarioId.Value,
                    empresaId.Value
                );

                if (!empresaPermitida)
                    empresaId = null;
            }

            if (!empresaId.HasValue)
                empresaId = await ObtenerEmpresaActivaUsuarioAsync(conn, usuarioId.Value);

            if (!empresaId.HasValue)
            {
                TempData["CitasError"] = "No se encontró una empresa activa para el usuario actual.";
                return RedirectToAction(nameof(Index));
            }

            using var tx = conn.BeginTransaction();

            var contexto = await ObtenerContextoAgendamientoAsync(
                conn,
                empresaId.Value,
                vm.AgendaID,
                vm.AgendaEmpresaID,
                tx
            );

            if (contexto == null)
            {
                tx.Rollback();
                TempData["CitasError"] = "La agenda seleccionada no está disponible.";
                return RedirectToAction(nameof(Index));
            }

           var ahora = DateTime.Now;

            var fechaFinPermitida = CalcularCierreAgendamiento(
                contexto.FechaInicioAgenda,
                contexto.FechaFinSolicitud
            );

            if (ahora < contexto.FechaInicioSolicitud || ahora > fechaFinPermitida)
            {
                tx.Rollback();
                TempData["CitasError"] = "El periodo para solicitar cita ya cerró. Las citas se cierran 24 horas antes del inicio del evento.";
                return RedirectToAction(nameof(Index));
            }

            if (vm.FechaCita.Date < contexto.FechaInicioAgenda.Date ||
                vm.FechaCita.Date > contexto.FechaFinAgenda.Date)
            {
                tx.Rollback();
                TempData["CitasError"] = "La fecha seleccionada está fuera del periodo de la agenda.";
                return RedirectToAction(nameof(Agendar), new
                {
                    agendaId = vm.AgendaID,
                    agendaEmpresaId = vm.AgendaEmpresaID
                });
            }

            if (vm.HoraInicio >= vm.HoraFin)
            {
                tx.Rollback();
                TempData["CitasError"] = "El horario seleccionado no es válido.";
                return RedirectToAction(nameof(Agendar), new
                {
                    agendaId = vm.AgendaID,
                    agendaEmpresaId = vm.AgendaEmpresaID
                });
            }

            var diaValido = await SlotPerteneceAConfiguracionAsync(
                conn,
                contexto.AgendaID,
                vm.FechaCita,
                vm.HoraInicio,
                vm.HoraFin,
                tx
            );

            if (!diaValido)
            {
                tx.Rollback();
                TempData["CitasError"] = "El horario seleccionado no pertenece a la configuración de la agenda.";
                return RedirectToAction(nameof(Agendar), new
                {
                    agendaId = vm.AgendaID,
                    agendaEmpresaId = vm.AgendaEmpresaID
                });
            }

            var yaTieneCita = await PersonaTieneCitaActivaAsync(
                conn,
                personaId.Value,
                contexto.AgendaID,
                tx
            );

            if (yaTieneCita)
            {
                tx.Rollback();
                TempData["CitasError"] = "Ya tienes una cita activa para esta agenda.";
                return RedirectToAction(nameof(Index));
            }

            var horarioOcupado = await HorarioOcupadoAsync(
                conn,
                contexto.AgendaID,
                vm.FechaCita,
                vm.HoraInicio,
                vm.HoraFin,
                tx
            );

            if (horarioOcupado)
            {
                tx.Rollback();
                TempData["CitasError"] = "El horario seleccionado ya fue ocupado. Elige otro horario.";
                return RedirectToAction(nameof(Agendar), new
                {
                    agendaId = vm.AgendaID,
                    agendaEmpresaId = vm.AgendaEmpresaID
                });
            }
            
           var slotsDisponibles = await GenerarSlotsDisponiblesAsync(
    conn,
    contexto.AgendaID,
    contexto.FechaInicioAgenda,
    contexto.FechaFinAgenda,
    tx
);

var slotSeleccionadoDisponible = slotsDisponibles.Any(x =>
    x.Disponible &&
    x.Fecha.Date == vm.FechaCita.Date &&
    x.HoraInicio == vm.HoraInicio &&
    x.HoraFin == vm.HoraFin
);

if (!slotSeleccionadoDisponible)
{
    tx.Rollback();

    TempData["CitasError"] = "El horario seleccionado ya no está disponible. Elige otro horario.";

    return RedirectToAction(nameof(Agendar), new
    {
        agendaId = vm.AgendaID,
        agendaEmpresaId = vm.AgendaEmpresaID
    });
}

                int nuevaCitaId = 0;

                using (var cmd = new SqlCommand(@"
    INSERT INTO dbo.Citas
    (
        AgendaEmpresaID,
        AgendaID,
        CuestionarioID,
        EmpresaID,
        PersonaID,
        FechaCita,
        HoraInicio,
        HoraFin,
        Estado,
        EstadoFormulario,
        FechaRegistro,
        RegistradoPorUsuarioID,
        FechaCambioEstado,
        CambiadoPorUsuarioID
    )
    OUTPUT INSERTED.CitaID
    VALUES
    (
        @AgendaEmpresaID,
        @AgendaID,
        @CuestionarioID,
        @EmpresaID,
        @PersonaID,
        @FechaCita,
        @HoraInicio,
        @HoraFin,
        'pendiente',
        'pendiente',
        SYSDATETIME(),
        @UsuarioID,
        SYSDATETIME(),
        @UsuarioID
    );
", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@AgendaEmpresaID", contexto.AgendaEmpresaID);
                    cmd.Parameters.AddWithValue("@AgendaID", contexto.AgendaID);
                    cmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value =
     contexto.CuestionarioID.HasValue
         ? contexto.CuestionarioID.Value
         : DBNull.Value;
                    cmd.Parameters.AddWithValue("@EmpresaID", empresaId.Value);
                    cmd.Parameters.AddWithValue("@PersonaID", personaId.Value);
                    cmd.Parameters.AddWithValue("@FechaCita", vm.FechaCita.Date);
                    cmd.Parameters.AddWithValue("@HoraInicio", vm.HoraInicio);
                    cmd.Parameters.AddWithValue("@HoraFin", vm.HoraFin);
                    cmd.Parameters.AddWithValue("@UsuarioID", usuarioId.Value);

                    nuevaCitaId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                tx.Commit();

                await NotificarUsuarioCitaRegistradaAsync(nuevaCitaId);
                await NotificarEditoresNuevaCitaAsync(nuevaCitaId);

                TempData["CitasOk"] = "Tu cita fue registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            _logger.LogWarning(ex, "Intento de cita duplicada.");

            TempData["CitasError"] = "Ese horario ya fue ocupado o ya tienes una cita activa.";
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL registrando cita.");

            TempData["CitasError"] = "No se pudo registrar la cita.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error general registrando cita.");

            TempData["CitasError"] = "Ocurrió un error al registrar la cita.";
            return RedirectToAction(nameof(Index));
        }
    }


//JMETODOS PARA LA BANDEJA DE CITAS

[HttpGet]
public async Task<IActionResult> Bandeja(int? agendaId, int? empresaId, DateTime? fecha)
{
    var usuarioId = ObtenerUsuarioId();

    if (!usuarioId.HasValue)
        return RedirectToAction("Login", "Login");

    var vm = new CitasBandejaVm
    {
        AgendaID = agendaId,
        EmpresaID = empresaId,
        Fecha = fecha
    };

    try
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await CargarBandejaEventosAsync(conn, vm);

        if (!vm.Eventos.Any())
            return View(vm);

        if (!vm.AgendaID.HasValue || !vm.Eventos.Any(x => x.AgendaID == vm.AgendaID.Value))
        {
            vm.AgendaID = vm.Eventos
                .OrderByDescending(x => string.Equals(x.Estatus, "activo", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.FechaInicio)
                .Select(x => x.AgendaID)
                .FirstOrDefault();
        }

        await CargarBandejaEmpresasAsync(conn, vm);

        if (vm.EmpresaID.HasValue && !vm.Empresas.Any(x => x.EmpresaID == vm.EmpresaID.Value))
            vm.EmpresaID = null;

        await CargarBandejaCitasAsync(conn, vm);

        return View(vm);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Error SQL cargando Bandeja de Citas.");

        vm.AlertaSistema = "No se pudo cargar la bandeja de citas. Revisa la configuración de agendas, empresas y citas.";
        return View(vm);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general cargando Bandeja de Citas.");

        vm.AlertaSistema = "Ocurrió un error al cargar la bandeja de citas.";
        return View(vm);
    }
}

private async Task CargarBandejaEventosAsync(SqlConnection conn, CitasBandejaVm vm)
{
    using var cmd = new SqlCommand(@"
        SELECT
            a.AgendaID,
            a.Nombre AS NombreEvento,
            a.Descripcion,
            a.FechaInicio,
            a.FechaFin,
            a.Estatus,

            COUNT(DISTINCT ae.EmpresaID) AS EmpresasParticipantes,

            COUNT(DISTINCT CASE
                WHEN c.Estado <> 'cancelada' THEN c.CitaID
            END) AS TotalCitas,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'pendiente' THEN c.CitaID
            END) AS Pendientes,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'finalizada' THEN c.CitaID
            END) AS Finalizadas,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'no_asistio' THEN c.CitaID
            END) AS NoAsistieron

        FROM dbo.Agendas a
        LEFT JOIN dbo.AgendaEmpresas ae
            ON ae.AgendaID = a.AgendaID
           AND ae.Activa = 1
        LEFT JOIN dbo.Citas c
            ON c.AgendaID = a.AgendaID
           AND c.Estado <> 'cancelada'
        WHERE a.Activa = 1
        GROUP BY
            a.AgendaID,
            a.Nombre,
            a.Descripcion,
            a.FechaInicio,
            a.FechaFin,
            a.Estatus,
            a.FechaCreacion
        ORDER BY
            CASE WHEN a.Estatus = 'activo' THEN 0 ELSE 1 END,
            a.FechaInicio DESC,
            a.FechaCreacion DESC,
            a.AgendaID DESC;
    ", conn);

    using var rd = await cmd.ExecuteReaderAsync();

    while (await rd.ReadAsync())
    {
        vm.Eventos.Add(new CitasBandejaEventoVm
        {
            AgendaID = ReadInt(rd, "AgendaID"),
            NombreEvento = ReadString(rd, "NombreEvento"),
            Descripcion = ReadString(rd, "Descripcion"),
            FechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.MinValue,
            FechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.MinValue,
            Estatus = ReadString(rd, "Estatus"),
            EmpresasParticipantes = ReadInt(rd, "EmpresasParticipantes"),
            TotalCitas = ReadInt(rd, "TotalCitas"),
            Pendientes = ReadInt(rd, "Pendientes"),
            Finalizadas = ReadInt(rd, "Finalizadas"),
            NoAsistieron = ReadInt(rd, "NoAsistieron")
        });
    }
}

private async Task CargarBandejaEmpresasAsync(SqlConnection conn, CitasBandejaVm vm)
{
    vm.Empresas.Clear();

    if (!vm.AgendaID.HasValue || vm.AgendaID.Value <= 0)
        return;

    using var cmd = new SqlCommand(@"
        SELECT
            ae.AgendaEmpresaID,
            ae.AgendaID,
            ae.EmpresaID,
            e.Nombre AS NombreEmpresa,
            ae.EstadoAsignacion,
            ae.FechaInicioSolicitud,
            ae.FechaFinSolicitud,

            COUNT(DISTINCT ue.UsuarioID) AS TotalUsuarios,

            COUNT(DISTINCT CASE
                WHEN c.Estado <> 'cancelada' THEN c.CitaID
            END) AS TotalCitas,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'pendiente' THEN c.CitaID
            END) AS Pendientes,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'asistio' THEN c.CitaID
            END) AS Asistieron,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'finalizada' THEN c.CitaID
            END) AS Finalizadas,

            COUNT(DISTINCT CASE
                WHEN c.Estado = 'no_asistio' THEN c.CitaID
            END) AS NoAsistieron

        FROM dbo.AgendaEmpresas ae
        INNER JOIN dbo.Empresas e
            ON e.EmpresaID = ae.EmpresaID
        LEFT JOIN dbo.UsuariosEmpresas ue
            ON ue.EmpresaID = e.EmpresaID
           AND ue.Activo = 1
        LEFT JOIN dbo.Usuarios u
            ON u.UsuarioID = ue.UsuarioID
           AND ISNULL(u.Activo, 1) = 1
        LEFT JOIN dbo.Citas c
            ON c.AgendaID = ae.AgendaID
           AND c.EmpresaID = ae.EmpresaID
           AND c.Estado <> 'cancelada'
        WHERE ae.AgendaID = @AgendaID
          AND ae.Activa = 1
          AND e.Activa = 1
        GROUP BY
            ae.AgendaEmpresaID,
            ae.AgendaID,
            ae.EmpresaID,
            e.Nombre,
            ae.EstadoAsignacion,
            ae.FechaInicioSolicitud,
            ae.FechaFinSolicitud
        ORDER BY e.Nombre ASC;
    ", conn);

    cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = vm.AgendaID.Value;

    using var rd = await cmd.ExecuteReaderAsync();

    while (await rd.ReadAsync())
    {
        vm.Empresas.Add(new CitasBandejaEmpresaVm
        {
            AgendaEmpresaID = ReadInt(rd, "AgendaEmpresaID"),
            AgendaID = ReadInt(rd, "AgendaID"),
            EmpresaID = ReadInt(rd, "EmpresaID"),
            NombreEmpresa = ReadString(rd, "NombreEmpresa"),
            EstadoAsignacion = ReadString(rd, "EstadoAsignacion"),
            FechaInicioSolicitud = ReadDateTime(rd, "FechaInicioSolicitud") ?? DateTime.MinValue,
            FechaFinSolicitud = ReadDateTime(rd, "FechaFinSolicitud") ?? DateTime.MinValue,
            TotalUsuarios = ReadInt(rd, "TotalUsuarios"),
            TotalCitas = ReadInt(rd, "TotalCitas"),
            Pendientes = ReadInt(rd, "Pendientes"),
            Asistieron = ReadInt(rd, "Asistieron"),
            Finalizadas = ReadInt(rd, "Finalizadas"),
            NoAsistieron = ReadInt(rd, "NoAsistieron")
        });
    }
}

private async Task CargarBandejaCitasAsync(SqlConnection conn, CitasBandejaVm vm)
{
    vm.Citas.Clear();

    if (!vm.AgendaID.HasValue || vm.AgendaID.Value <= 0)
        return;

    using var cmd = new SqlCommand(@"
    SELECT
        c.CitaID,
        c.AgendaEmpresaID,
        c.AgendaID,
        c.EmpresaID,
        c.PersonaID,

        COALESCE(
            NULLIF(LTRIM(RTRIM(CONCAT(
                ISNULL(p.Nombre, ''),
                ' ',
                ISNULL(p.ApellidoPaterno, ''),
                ' ',
                ISNULL(p.ApellidoMaterno, '')
            ))), ''),
            CONCAT('Persona #', c.PersonaID)
        ) AS NombrePersona,

        ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa,
        ISNULL(a.Nombre, CONCAT('Agenda #', c.AgendaID)) AS NombreEvento,

        c.FechaCita,
        c.HoraInicio,
        c.HoraFin,
        c.Estado,
        c.EstadoFormulario

    FROM dbo.Citas c
    INNER JOIN dbo.Agendas a
        ON a.AgendaID = c.AgendaID
    INNER JOIN dbo.Empresas e
        ON e.EmpresaID = c.EmpresaID
    LEFT JOIN dbo.Persona p
        ON p.PersonaID = c.PersonaID
    WHERE c.AgendaID = @AgendaID
      AND c.Estado <> 'cancelada'
      AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
      AND (@Fecha IS NULL OR c.FechaCita = @Fecha)
    ORDER BY
        c.FechaCita ASC,
        c.HoraInicio ASC,
        e.Nombre ASC,
        c.CitaID ASC;
", conn);

    cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = vm.AgendaID.Value;

    cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value =
        vm.EmpresaID.HasValue && vm.EmpresaID.Value > 0
            ? (object)vm.EmpresaID.Value
            : DBNull.Value;

    cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value =
        vm.Fecha.HasValue
            ? (object)vm.Fecha.Value.Date
            : DBNull.Value;

    using var rd = await cmd.ExecuteReaderAsync();

    while (await rd.ReadAsync())
    {
        vm.Citas.Add(new CitaBandejaVm
        {
            CitaID = ReadInt(rd, "CitaID"),
            AgendaEmpresaID = ReadInt(rd, "AgendaEmpresaID"),
            AgendaID = ReadInt(rd, "AgendaID"),
            EmpresaID = ReadInt(rd, "EmpresaID"),
            PersonaID = ReadInt(rd, "PersonaID"),
            NombrePersona = ReadString(rd, "NombrePersona"),
            NombreEmpresa = ReadString(rd, "NombreEmpresa"),
            NombreEvento = ReadString(rd, "NombreEvento"),
            FechaCita = ReadDateTime(rd, "FechaCita") ?? DateTime.MinValue,
            HoraInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero,
            HoraFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero,
            Estado = ReadString(rd, "Estado"),
            EstadoFormulario = ReadString(rd, "EstadoFormulario")
        });
    }
}


//METODOS PARA CAPTURAR UNA CITA Y EMPEZARLA

[HttpGet]
public async Task<IActionResult> Capturar(int citaId)
{
    var vm = await CargarCapturaEntrevistaAsync(citaId);

    if (vm == null)
    {
        TempData["CitasError"] = "No se encontró la cita seleccionada.";
        return RedirectToAction(nameof(Bandeja));
    }

    if (string.Equals(vm.Estado, "no_asistio", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(vm.Estado, "cancelada", StringComparison.OrdinalIgnoreCase))
    {
        TempData["CitasError"] = "Esta cita no puede capturarse porque está marcada como no asistió o cancelada.";
        return RedirectToAction(nameof(Bandeja));
    }

    return View(vm);
}


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Capturar(CapturarEntrevistaVm vm, string accion)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            var finalizar = string.Equals(accion, "finalizar", StringComparison.OrdinalIgnoreCase);

            vm.Respuestas ??= new List<RespuestaEntrevistaVm>();
            vm.DimensionesEvaluadas ??= new List<DimensionEvaluadaVm>();

            var vmBase = await CargarCapturaEntrevistaAsync(vm.CitaID);

            if (vmBase == null)
            {
                TempData["CitasError"] = "No se encontró la cita seleccionada.";
                return RedirectToAction(nameof(Bandeja));
            }

            // Conserva las dimensiones capturadas si hay error de validación.
            vmBase.Respuestas = vm.Respuestas;
            vmBase.DimensionesEvaluadas = vm.DimensionesEvaluadas;

            foreach (var respuesta in vm.Respuestas.Where(x => x.ValorNumerico.HasValue))
            {
                if (respuesta.ValorNumerico.Value < 1 || respuesta.ValorNumerico.Value > 10)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Las calificaciones capturadas en preguntas deben estar entre 1 y 10."
                    );
                }
            }

            foreach (var dimension in vm.DimensionesEvaluadas.Where(x => x.Calificacion.HasValue))
            {
                if (dimension.Calificacion.Value < 1 || dimension.Calificacion.Value > 10)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"La calificación de la dimensión \"{dimension.DimensionNombre}\" debe estar entre 1 y 10."
                    );
                }
            }

            var dimensionesConNombre = vm.DimensionesEvaluadas
                .Where(x => !string.IsNullOrWhiteSpace(x.DimensionNombre))
                .ToList();

            foreach (var dimension in dimensionesConNombre)
            {
                if (dimension.CalificacionMaxima <= 0)
                    dimension.CalificacionMaxima = 10m;
            }

            if (finalizar)
            {
                foreach (var pregunta in vmBase.Preguntas.Where(x => x.Obligatoria))
                {
                    var respuesta = vm.Respuestas.FirstOrDefault(x => x.PreguntaID == pregunta.PreguntaID);
                    var tipo = (pregunta.TipoPregunta ?? "texto").Trim().ToLowerInvariant();

                    var tieneRespuesta = false;

                    if (tipo == "texto")
                    {
                        tieneRespuesta =
                            respuesta != null &&
                            !string.IsNullOrWhiteSpace(respuesta.ValorTexto);
                    }
                    else if (tipo == "opcion_multiple")
                    {
                        tieneRespuesta =
                            respuesta != null &&
                            respuesta.OpcionID.HasValue;
                    }
                    else if (tipo == "checkbox")
                    {
                        tieneRespuesta =
                            respuesta != null &&
                            !string.IsNullOrWhiteSpace(respuesta.ValorTexto);
                    }
                    else
                    {
                        tieneRespuesta =
                            respuesta != null &&
                            (
                                respuesta.OpcionID.HasValue ||
                                respuesta.ValorNumerico.HasValue ||
                                !string.IsNullOrWhiteSpace(respuesta.ValorTexto)
                            );
                    }

                    if (!tieneRespuesta)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            $"La pregunta obligatoria \"{pregunta.TextoPregunta}\" no tiene respuesta."
                        );
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                return View(vmBase);
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estado = finalizar ? "finalizada" : "asistio";
                var estadoFormulario = finalizar ? "completado" : "en_captura";

                const string sqlActualizarCita = @"
UPDATE Citas
SET
    Estado = @Estado,
    EstadoFormulario = @EstadoFormulario,
    EntrevistadorUsuarioID = COALESCE(EntrevistadorUsuarioID, @UsuarioID),
    FechaConfirmacionAsistencia = COALESCE(FechaConfirmacionAsistencia, GETDATE()),
    ConfirmadoPorUsuarioID = COALESCE(ConfirmadoPorUsuarioID, @UsuarioID),
    FechaCambioEstado = GETDATE(),
    CambiadoPorUsuarioID = @UsuarioID
WHERE
    CitaID = @CitaID
    AND Estado NOT IN ('no_asistio', 'cancelada');";

                using (var cmd = new SqlCommand(sqlActualizarCita, connection, transaction))
                {
                    cmd.Parameters.Add("@Estado", SqlDbType.VarChar, 30).Value = estado;
                    cmd.Parameters.Add("@EstadoFormulario", SqlDbType.VarChar, 30).Value = estadoFormulario;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;

                    var rows = await cmd.ExecuteNonQueryAsync();

                    if (rows == 0)
                    {
                        transaction.Rollback();
                        TempData["CitasError"] = "La cita no existe o no puede capturarse.";
                        return RedirectToAction(nameof(Bandeja));
                    }
                }

                const string sqlEliminarRespuestas = @"
DELETE FROM Respuestas
WHERE CitaID = @CitaID;";

                using (var cmd = new SqlCommand(sqlEliminarRespuestas, connection, transaction))
                {
                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlInsertarRespuesta = @"
INSERT INTO Respuestas
(
    CitaID,
    PreguntaID,
    OpcionID,
    ValorTexto,
    ValorNumerico,
    FechaRespuesta,
    CapturadoPorUsuarioID
)
VALUES
(
    @CitaID,
    @PreguntaID,
    @OpcionID,
    @ValorTexto,
    @ValorNumerico,
    GETDATE(),
    @UsuarioID
);";

                foreach (var respuesta in vm.Respuestas)
                {
                    var tieneRespuesta =
                        respuesta.OpcionID.HasValue ||
                        respuesta.ValorNumerico.HasValue ||
                        !string.IsNullOrWhiteSpace(respuesta.ValorTexto);

                    if (!tieneRespuesta)
                        continue;

                    using var cmd = new SqlCommand(sqlInsertarRespuesta, connection, transaction);

                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;
                    cmd.Parameters.Add("@PreguntaID", SqlDbType.Int).Value = respuesta.PreguntaID;

                    cmd.Parameters.Add("@OpcionID", SqlDbType.Int).Value =
                        respuesta.OpcionID.HasValue
                            ? respuesta.OpcionID.Value
                            : DBNull.Value;

                    cmd.Parameters.Add("@ValorTexto", SqlDbType.NVarChar, -1).Value =
                        string.IsNullOrWhiteSpace(respuesta.ValorTexto)
                            ? DBNull.Value
                            : respuesta.ValorTexto.Trim();

                    var valorNumericoParam = cmd.Parameters.Add("@ValorNumerico", SqlDbType.Decimal);
                    valorNumericoParam.Precision = 10;
                    valorNumericoParam.Scale = 2;
                    valorNumericoParam.Value =
                        respuesta.ValorNumerico.HasValue
                            ? respuesta.ValorNumerico.Value
                            : DBNull.Value;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlEliminarDimensiones = @"
DELETE FROM dbo.CitaDimensionesEvaluadas
WHERE CitaID = @CitaID;";

                using (var cmd = new SqlCommand(sqlEliminarDimensiones, connection, transaction))
                {
                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlInsertarDimension = @"
INSERT INTO dbo.CitaDimensionesEvaluadas
(
    CitaID,
    AgendaID,
    EmpresaID,
    PersonaID,
    DimensionNombre,
    Calificacion,
    CalificacionMaxima,
    Comentario,
    Orden,
    Activa,
    FechaRegistro,
    RegistradoPorUsuarioID
)
VALUES
(
    @CitaID,
    @AgendaID,
    @EmpresaID,
    @PersonaID,
    @DimensionNombre,
    @Calificacion,
    @CalificacionMaxima,
    @Comentario,
    @Orden,
    1,
    SYSDATETIME(),
    @UsuarioID
);";

                var ordenDimension = 1;

                foreach (var dimension in dimensionesConNombre)
                {
                    using var cmd = new SqlCommand(sqlInsertarDimension, connection, transaction);

                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;
                    cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = vmBase.AgendaID;
                    cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = vmBase.EmpresaID;
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = vmBase.PersonaID;

                    cmd.Parameters.Add("@DimensionNombre", SqlDbType.NVarChar, 150).Value =
                        dimension.DimensionNombre.Trim();

                    var calificacionParam = cmd.Parameters.Add("@Calificacion", SqlDbType.Decimal);
                    calificacionParam.Precision = 10;
                    calificacionParam.Scale = 2;
                    calificacionParam.Value =
                        dimension.Calificacion.HasValue
                            ? dimension.Calificacion.Value
                            : DBNull.Value;

                    var maxParam = cmd.Parameters.Add("@CalificacionMaxima", SqlDbType.Decimal);
                    maxParam.Precision = 10;
                    maxParam.Scale = 2;
                    maxParam.Value =
                        dimension.CalificacionMaxima <= 0
                            ? 10m
                            : dimension.CalificacionMaxima;

                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(dimension.Comentario)
                            ? DBNull.Value
                            : dimension.Comentario.Trim();

                    cmd.Parameters.Add("@Orden", SqlDbType.Int).Value = ordenDimension;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;

                    await cmd.ExecuteNonQueryAsync();

                    ordenDimension++;
                }

                if (finalizar)
                {
                    var respuestasConPuntaje = vm.Respuestas
                        .Where(x => x.ValorNumerico.HasValue)
                        .ToList();

                    var dimensionesConCalificacion = dimensionesConNombre
                        .Where(x => x.Calificacion.HasValue)
                        .ToList();

                    var promedioDimensiones = dimensionesConCalificacion.Any()
                        ? Math.Round(dimensionesConCalificacion.Average(x => x.Calificacion!.Value), 2)
                        : (decimal?)null;

                    var resultado = new
                    {
                        FechaCalculo = DateTime.Now,
                        CitaID = vm.CitaID,
                        TotalRespuestasConPuntaje = respuestasConPuntaje.Count,
                        TotalDimensiones = dimensionesConNombre.Count,
                        TotalDimensionesConCalificacion = dimensionesConCalificacion.Count,
                        PromedioDimensiones = promedioDimensiones,
                        Resumen = "Resultado calculado con respuestas y dimensiones disponibles. Las dimensiones sin calificación se conservan como observación."
                    };

                    var resultadoJson = JsonSerializer.Serialize(resultado);

                    const string sqlResultado = @"
UPDATE Citas
SET
    ResultadoJson = @ResultadoJson,
    FechaResultado = GETDATE()
WHERE CitaID = @CitaID;";

                    using var cmd = new SqlCommand(sqlResultado, connection, transaction);
                    cmd.Parameters.Add("@ResultadoJson", SqlDbType.NVarChar, -1).Value = resultadoJson;
                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = vm.CitaID;

                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();

                if (finalizar)
                {
                    TempData["CitasOk"] = "La entrevista se finalizó correctamente.";

                    return RedirectToAction(nameof(Resultados), new
                    {
                        citaId = vm.CitaID,
                        origen = "captura"
                    });
                }

                TempData["CitasOk"] = "El avance de la entrevista se guardó correctamente.";
                return RedirectToAction(nameof(Bandeja));
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                TempData["CitasError"] = "Ocurrió un error al guardar la entrevista: " + ex.Message;
                return RedirectToAction(nameof(Capturar), new { citaId = vm.CitaID });
            }
        }

        //NUEVA CORRECCION: Metodo que marca el boton de no asistió en editor

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarNoAsistio(
    int citaId,
    int? agendaId,
    int? empresaId,
    DateTime? fecha)
        {
            var usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return RedirectToAction("Login", "Login");

            if (citaId <= 0)
            {
                TempData["CitasError"] = "No se recibió una cita válida.";
                return RedirectToAction(nameof(Bandeja), new
                {
                    agendaId,
                    empresaId,
                    fecha
                });
            }

            int? agendaIdReal = agendaId;
            int? empresaIdReal = empresaId;
            DateTime? fechaReal = fecha;

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction();

                string estadoActual = string.Empty;

                using (var cmdInfo = new SqlCommand(@"
            SELECT TOP 1
                AgendaID,
                EmpresaID,
                FechaCita,
                Estado
            FROM dbo.Citas
            WHERE CitaID = @CitaID;
        ", conn, tx))
                {
                    cmdInfo.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                    using var rd = await cmdInfo.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        tx.Rollback();

                        TempData["CitasError"] = "No se encontró la cita seleccionada.";
                        return RedirectToAction(nameof(Bandeja), new
                        {
                            agendaId,
                            empresaId,
                            fecha
                        });
                    }

                    agendaIdReal = ReadInt(rd, "AgendaID");
                    empresaIdReal = ReadInt(rd, "EmpresaID");
                    fechaReal = ReadDateTime(rd, "FechaCita");
                    estadoActual = ReadString(rd, "Estado");
                }

                if (string.Equals(estadoActual, "finalizada", StringComparison.OrdinalIgnoreCase))
                {
                    tx.Rollback();

                    TempData["CitasError"] = "No puedes marcar como no asistió una cita finalizada.";
                    return RedirectToAction(nameof(Bandeja), new
                    {
                        agendaId = agendaIdReal,
                        empresaId = empresaIdReal,
                        fecha = fechaReal
                    });
                }

                if (string.Equals(estadoActual, "cancelada", StringComparison.OrdinalIgnoreCase))
                {
                    tx.Rollback();

                    TempData["CitasError"] = "No puedes modificar una cita cancelada.";
                    return RedirectToAction(nameof(Bandeja), new
                    {
                        agendaId = agendaIdReal,
                        empresaId = empresaIdReal,
                        fecha = fechaReal
                    });
                }

                if (string.Equals(estadoActual, "no_asistio", StringComparison.OrdinalIgnoreCase))
                {
                    tx.Rollback();

                    TempData["CitasError"] = "Esta cita ya está marcada como no asistió.";
                    return RedirectToAction(nameof(Bandeja), new
                    {
                        agendaId = agendaIdReal,
                        empresaId = empresaIdReal,
                        fecha = fechaReal
                    });
                }

                using (var cmd = new SqlCommand(@"
            UPDATE dbo.Citas
            SET
                Estado = 'no_asistio',
                EstadoFormulario = 'pendiente',
                ResultadoJson = NULL,
                FechaResultado = NULL,
                FechaCambioEstado = SYSDATETIME(),
                CambiadoPorUsuarioID = @UsuarioID
            WHERE CitaID = @CitaID
              AND Estado NOT IN ('finalizada', 'cancelada', 'no_asistio');
        ", conn, tx))
                {
                    cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;

                    var rows = await cmd.ExecuteNonQueryAsync();

                    if (rows <= 0)
                    {
                        tx.Rollback();

                        TempData["CitasError"] = "No se pudo actualizar la cita.";
                        return RedirectToAction(nameof(Bandeja), new
                        {
                            agendaId = agendaIdReal,
                            empresaId = empresaIdReal,
                            fecha = fechaReal
                        });
                    }
                }

                tx.Commit();

                var notificacion = await NotificarUsuarioCitaNoAsistioAsync(citaId);

                if (notificacion)
                {
                    TempData["CitasOk"] = "La cita fue marcada como no asistió correctamente y se notificó al usuario por correo.";
                }
                else
                {
                    TempData["CitasOk"] = "La cita fue marcada como no asistió correctamente.";
                    TempData["CitasError"] = "No se pudo confirmar el envío del correo de inasistencia. Revisa el log del servicio de notificaciones.";
                }

                return RedirectToAction(nameof(Bandeja), new
                {
                    agendaId = agendaIdReal,
                    empresaId = empresaIdReal,
                    fecha = fechaReal
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL marcando cita como no asistió. CitaID={CitaID}", citaId);

                TempData["CitasError"] = "No se pudo marcar la cita como no asistió.";
                return RedirectToAction(nameof(Bandeja), new
                {
                    agendaId = agendaIdReal,
                    empresaId = empresaIdReal,
                    fecha = fechaReal
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general marcando cita como no asistió. CitaID={CitaID}", citaId);

                TempData["CitasError"] = "Ocurrió un error al marcar la cita como no asistió.";
                return RedirectToAction(nameof(Bandeja), new
                {
                    agendaId = agendaIdReal,
                    empresaId = empresaIdReal,
                    fecha = fechaReal
                });
            }
        }



        //metodo para los resultaos de la entrevista

        [HttpGet]
public async Task<IActionResult> Resultados(
    int? agendaId,
    int? empresaId,
    string? busqueda,
    int? citaId,
    string? origen)
{
    var usuarioId = ObtenerUsuarioId();

    if (!usuarioId.HasValue)
        return RedirectToAction("Login", "Login");

    var rolId = ObtenerRolId();

    var vm = new ResultadosIndexVm
    {
        RolID = rolId
    };

    ViewBag.Origen = origen ?? string.Empty;

    try
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var esEditor =
            rolId == 1 ||
            rolId == 2 ||
            rolId == 3;

        var esUsuarioFinal = rolId == 5;

        // 1. Si viene citaId, SIEMPRE mostrar resultado individual.
        // Esto aplica para editor, admin y usuario final.
        if (citaId.HasValue && citaId.Value > 0)
        {
            int? personaIdFiltro = null;

            // El usuario final solo puede ver su propia cita.
            if (esUsuarioFinal && !esEditor)
            {
                personaIdFiltro = await ObtenerPersonaIdAsync(conn, usuarioId.Value);

                if (!personaIdFiltro.HasValue)
                {
                    vm.Mensaje = "No se encontró una persona asociada al usuario actual.";
                    return View(vm);
                }
            }

            vm.Individual = await CargarResultadoCitaAsync(
                conn,
                citaId.Value,
                personaIdFiltro
            );

            if (vm.Individual == null)
            {
                vm.Mensaje = "No se encontró el resultado solicitado.";
            }

            return View(vm);
        }

        // 2. Si NO viene citaId y es editor/admin, mostrar listado general.
        if (esEditor)
        {
            vm.Admin = await CargarResultadosAdminAsync(
                conn,
                agendaId,
                empresaId,
                busqueda
            );

            return View(vm);
        }

        // 3. Usuario final sin citaId: buscar su último resultado.
        if (esUsuarioFinal)
        {
            var personaId = await ObtenerPersonaIdAsync(conn, usuarioId.Value);

            if (!personaId.HasValue)
            {
                vm.Mensaje = "No se encontró una persona asociada al usuario actual.";
                return View(vm);
            }

            int? citaResultadoId = null;

            using var cmd = new SqlCommand(@"
                SELECT TOP 1 CitaID
                FROM dbo.Citas
                WHERE PersonaID = @PersonaID
                  AND Estado = 'finalizada'
                  AND EstadoFormulario = 'completado'
                  AND Estado <> 'cancelada'
                ORDER BY FechaResultado DESC, FechaRegistro DESC, CitaID DESC;
            ", conn);

            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
                citaResultadoId = Convert.ToInt32(result);

            if (!citaResultadoId.HasValue)
            {
                vm.Mensaje = "Todavía no tienes resultados disponibles.";
                return View(vm);
            }

            vm.Individual = await CargarResultadoCitaAsync(
                conn,
                citaResultadoId.Value,
                personaId.Value
            );

            if (vm.Individual == null)
                vm.Mensaje = "No se encontró tu resultado.";

            return View(vm);
        }

        vm.Mensaje = "No tienes acceso a esta sección.";
        return View(vm);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Error SQL cargando resultados.");

        TempData["CitasError"] = "No se pudieron cargar los resultados.";
        return RedirectToAction(nameof(Index));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general cargando resultados.");

        TempData["CitasError"] = "Ocurrió un error al cargar los resultados.";
        return RedirectToAction(nameof(Index));
    }
}

private async Task<ResultadosAdminVm> CargarResultadosAdminAsync(
    SqlConnection conn,
    int? agendaId,
    int? empresaId,
    string? busqueda)
{
    var vm = new ResultadosAdminVm
    {
        AgendaID = agendaId,
        EmpresaID = empresaId,
        Busqueda = busqueda
    };

    var citaIds = new List<int>();

    using (var cmd = new SqlCommand(@"
        SELECT
            c.CitaID
        FROM dbo.Citas c
        INNER JOIN dbo.Agendas a
            ON a.AgendaID = c.AgendaID
        LEFT JOIN dbo.Empresas e
            ON e.EmpresaID = c.EmpresaID
        WHERE c.Estado IN ('asistio', 'finalizada')
          AND c.Estado <> 'cancelada'
          AND (@AgendaID IS NULL OR c.AgendaID = @AgendaID)
          AND (@EmpresaID IS NULL OR c.EmpresaID = @EmpresaID)
          AND (
                @Busqueda IS NULL
                OR @Busqueda = ''
                OR a.Nombre LIKE '%' + @Busqueda + '%'
                OR e.Nombre LIKE '%' + @Busqueda + '%'
                OR CONCAT('Persona #', c.PersonaID) LIKE '%' + @Busqueda + '%'
                OR CONCAT('CIT-', RIGHT('00000' + CAST(c.CitaID AS VARCHAR(10)), 5)) LIKE '%' + @Busqueda + '%'
              )
        ORDER BY c.FechaCita DESC, c.HoraInicio DESC, c.CitaID DESC;
    ", conn))
    {
        cmd.Parameters.Add("@AgendaID", System.Data.SqlDbType.Int).Value =
            agendaId.HasValue ? (object)agendaId.Value : DBNull.Value;

        cmd.Parameters.Add("@EmpresaID", System.Data.SqlDbType.Int).Value =
            empresaId.HasValue ? (object)empresaId.Value : DBNull.Value;

        cmd.Parameters.Add("@Busqueda", System.Data.SqlDbType.NVarChar, 150).Value =
            string.IsNullOrWhiteSpace(busqueda) ? DBNull.Value : busqueda.Trim();

        using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
            citaIds.Add(ReadInt(rd, "CitaID"));
    }

    var resultadosIndividuales = new List<ResultadoCitaVm>();

    foreach (var id in citaIds)
    {
        var resultado = await CargarResultadoCitaAsync(conn, id, personaIdFiltro: null);

        if (resultado == null)
            continue;

        resultadosIndividuales.Add(resultado);

        vm.Personas.Add(new ResultadoPersonaResumenVm
        {
            CitaID = resultado.CitaID,
            PersonaID = resultado.PersonaID,
            AgendaID = resultado.AgendaID,
            EmpresaID = resultado.EmpresaID,
            NombrePersona = resultado.NombrePersona,
            NombreEmpresa = resultado.NombreEmpresa,
            NombreEvento = resultado.NombreEvento,
            FechaCita = resultado.FechaCita ?? DateTime.Today,
            HoraInicio = resultado.HoraInicio ?? TimeSpan.Zero,
            HoraFin = resultado.HoraFin ?? TimeSpan.Zero,
            Estado = resultado.Estado,
            EstadoFormulario = resultado.EstadoFormulario,
            PuntajeGlobal = resultado.PuntajeGlobal,
            PorcentajeGlobal = resultado.PorcentajeGlobal,
            TotalDimensiones = resultado.TotalDimensiones,
            TotalPreguntasEvaluables = resultado.TotalPreguntasEvaluables,
            NivelGeneral = resultado.NivelGeneral,
            CssNivelGeneral = resultado.CssNivelGeneral
        });
    }

    vm.TotalPersonas = vm.Personas.Count;
    vm.TotalFinalizadas = vm.Personas.Count(x =>
        string.Equals(x.Estado, "finalizada", StringComparison.OrdinalIgnoreCase));

    vm.TotalAsistieron = vm.Personas.Count(x =>
        string.Equals(x.Estado, "asistio", StringComparison.OrdinalIgnoreCase));

    vm.TotalConResultado = vm.Personas.Count(x => x.TieneResultado);

    var personasConResultado = vm.Personas
        .Where(x => x.TieneResultado)
        .ToList();

    if (personasConResultado.Any())
    {
        vm.PorcentajeGlobal = Math.Round(personasConResultado.Average(x => x.PorcentajeGlobal), 1);
        vm.NivelGeneral = NivelPorPorcentaje(vm.PorcentajeGlobal);
        vm.CssNivelGeneral = CssPorPorcentaje(vm.PorcentajeGlobal);
    }

    vm.DimensionesGlobales = resultadosIndividuales
        .SelectMany(x => x.Dimensiones)
        .GroupBy(x => x.Dimension)
        .Select(g =>
        {
            var porcentaje = Math.Round(g.Average(x => x.Porcentaje), 1);

            return new ResultadoDimensionGlobalVm
            {
                Dimension = g.Key,
                Porcentaje = porcentaje,
                Promedio = Math.Round(porcentaje / 20m, 2),
                TotalPersonas = g.Count(),
                Nivel = NivelPorPorcentaje(porcentaje),
                CssNivel = CssPorPorcentaje(porcentaje)
            };
        })
        .OrderByDescending(x => x.Porcentaje)
        .ToList();

    return vm;
}

private int ObtenerRolId()
{
    var rolId = HttpContext.Session.GetInt32("RolID");

    if (rolId.HasValue)
        return rolId.Value;

    if (int.TryParse(User.FindFirst("RolID")?.Value, out var rolClaim))
        return rolClaim;

    if (int.TryParse(User.FindFirst("RolId")?.Value, out var rolClaimAlt))
        return rolClaimAlt;

    return 0;
}

[HttpGet]
public async Task<IActionResult> ResultadoCita(int citaId)
{
    var usuarioId = ObtenerUsuarioId();

    if (!usuarioId.HasValue)
        return RedirectToAction("Login", "Login");

    try
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var vm = await CargarResultadoCitaAsync(conn, citaId, personaIdFiltro: null);

        if (vm == null)
        {
            TempData["CitasError"] = "No se encontró el resultado solicitado.";
            return RedirectToAction(nameof(Resultados));
        }

        return View(vm);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Error SQL cargando resultado individual.");

        TempData["CitasError"] = "No se pudo cargar el resultado.";
        return RedirectToAction(nameof(Resultados));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general cargando resultado individual.");

        TempData["CitasError"] = "Ocurrió un error al cargar el resultado.";
        return RedirectToAction(nameof(Resultados));
    }
}


[HttpGet]
public async Task<IActionResult> MisResultados()
{
    var usuarioId = ObtenerUsuarioId();

    if (!usuarioId.HasValue)
        return RedirectToAction("Login", "Login");

    try
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var personaId = await ObtenerPersonaIdAsync(conn, usuarioId.Value);

        if (!personaId.HasValue)
        {
            TempData["CitasError"] = "No se encontró una persona asociada al usuario actual.";
            return RedirectToAction(nameof(Index));
        }

        int? citaId = null;

        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 CitaID
            FROM dbo.Citas
            WHERE PersonaID = @PersonaID
              AND Estado = 'finalizada'
              AND EstadoFormulario = 'completado'
              AND Estado <> 'cancelada'
            ORDER BY FechaResultado DESC, FechaRegistro DESC, CitaID DESC;
        ", conn))
        {
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
                citaId = Convert.ToInt32(result);
        }

        if (!citaId.HasValue)
        {
            TempData["CitasError"] = "Todavía no tienes resultados disponibles.";
            return RedirectToAction(nameof(Index));
        }

        var vm = await CargarResultadoCitaAsync(conn, citaId.Value, personaId.Value);

        if (vm == null)
        {
            TempData["CitasError"] = "No se encontró tu resultado.";
            return RedirectToAction(nameof(Index));
        }

        return View("ResultadoCita", vm);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Error SQL cargando MisResultados.");

        TempData["CitasError"] = "No se pudieron cargar tus resultados.";
        return RedirectToAction(nameof(Index));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general cargando MisResultados.");

        TempData["CitasError"] = "Ocurrió un error al cargar tus resultados.";
        return RedirectToAction(nameof(Index));
    }
}


        //helper para los resultados
        private async Task<ResultadoCitaVm?> CargarResultadoCitaAsync(
            SqlConnection conn,
            int citaId,
            int? personaIdFiltro)
        {
            ResultadoCitaVm? vm = null;

            using (var cmd = new SqlCommand(@"
        SELECT TOP 1
            c.CitaID,
            c.AgendaID,
            c.CuestionarioID,
            c.EmpresaID,
            c.PersonaID,
            c.FechaCita,
            c.HoraInicio,
            c.HoraFin,
            c.Estado,
            c.EstadoFormulario,
            c.ResultadoJson,
            c.FechaResultado,
            ISNULL(a.Nombre, CONCAT('Agenda #', c.AgendaID)) AS NombreEvento,
            ISNULL(q.Nombre, 'Sin cuestionario') AS NombreCuestionario,
            ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa,
            CONCAT('Persona #', c.PersonaID) AS NombrePersona
        FROM dbo.Citas c
        INNER JOIN dbo.Agendas a
            ON a.AgendaID = c.AgendaID
        LEFT JOIN dbo.Cuestionarios q
            ON q.CuestionarioID = c.CuestionarioID
        LEFT JOIN dbo.Empresas e
            ON e.EmpresaID = c.EmpresaID
        WHERE c.CitaID = @CitaID
          AND c.Estado IN ('asistio', 'finalizada')
          AND c.Estado <> 'cancelada'
          AND (@PersonaID IS NULL OR c.PersonaID = @PersonaID);
    ", conn))
            {
                cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value =
                    personaIdFiltro.HasValue
                        ? personaIdFiltro.Value
                        : DBNull.Value;

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                vm = new ResultadoCitaVm
                {
                    CitaID = ReadInt(rd, "CitaID"),
                    AgendaID = ReadInt(rd, "AgendaID"),

                    CuestionarioID = rd["CuestionarioID"] == DBNull.Value
                        ? null
                        : ReadInt(rd, "CuestionarioID"),

                    EmpresaID = ReadInt(rd, "EmpresaID"),
                    PersonaID = ReadInt(rd, "PersonaID"),
                    FechaCita = ReadDateTime(rd, "FechaCita"),
                    HoraInicio = ReadTimeSpan(rd, "HoraInicio"),
                    HoraFin = ReadTimeSpan(rd, "HoraFin"),
                    Estado = ReadString(rd, "Estado"),
                    EstadoFormulario = ReadString(rd, "EstadoFormulario"),
                    ResultadoJson = ReadString(rd, "ResultadoJson"),
                    FechaResultado = ReadDateTime(rd, "FechaResultado"),
                    NombreEvento = ReadString(rd, "NombreEvento"),
                    NombreCuestionario = ReadString(rd, "NombreCuestionario"),
                    NombreEmpresa = ReadString(rd, "NombreEmpresa"),
                    NombrePersona = ReadString(rd, "NombrePersona"),
                    Preguntas = new List<ResultadoPreguntaVm>(),
                    Dimensiones = new List<ResultadoDimensionVm>()
                };
            }

            if (vm == null)
                return null;

            var respuestasPorPregunta = new Dictionary<int, RespuestaResultadoTemp>();

            if (vm.CuestionarioID.HasValue && vm.CuestionarioID.Value > 0)
            {
                using (var respuestasCmd = new SqlCommand(@"
            SELECT
                PreguntaID,
                OpcionID,
                ValorTexto,
                ValorNumerico
            FROM dbo.Respuestas
            WHERE CitaID = @CitaID;
        ", conn))
                {
                    respuestasCmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                    using var rd = await respuestasCmd.ExecuteReaderAsync();

                    while (await rd.ReadAsync())
                    {
                        var preguntaId = ReadInt(rd, "PreguntaID");

                        if (!respuestasPorPregunta.ContainsKey(preguntaId))
                        {
                            respuestasPorPregunta[preguntaId] = new RespuestaResultadoTemp
                            {
                                OpcionID = rd["OpcionID"] == DBNull.Value
                                    ? null
                                    : Convert.ToInt32(rd["OpcionID"]),
                                ValorTexto = ReadString(rd, "ValorTexto"),
                                ValorNumerico = rd["ValorNumerico"] == DBNull.Value
                                    ? null
                                    : Convert.ToDecimal(rd["ValorNumerico"])
                            };
                        }
                    }
                }

                using (var preguntasCmd = new SqlCommand(@"
            SELECT
                PreguntaID,
                TextoPregunta,
                TipoPregunta,
                Dimension,
                Orden,
                Obligatoria
            FROM dbo.Preguntas
            WHERE CuestionarioID = @CuestionarioID
              AND Activa = 1
            ORDER BY Orden, PreguntaID;
        ", conn))
                {
                    preguntasCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = vm.CuestionarioID.Value;

                    using var rd = await preguntasCmd.ExecuteReaderAsync();

                    while (await rd.ReadAsync())
                    {
                        var preguntaId = ReadInt(rd, "PreguntaID");

                        respuestasPorPregunta.TryGetValue(preguntaId, out var respuesta);

                        var valorNumerico = respuesta?.ValorNumerico;
                        var respuestaTexto = string.IsNullOrWhiteSpace(respuesta?.ValorTexto)
                            ? "Sin respuesta"
                            : respuesta!.ValorTexto;

                        var dimension = ReadString(rd, "Dimension");

                        var preguntaResultado = new ResultadoPreguntaVm
                        {
                            PreguntaID = preguntaId,
                            TextoPregunta = ReadString(rd, "TextoPregunta"),
                            TipoPregunta = ReadString(rd, "TipoPregunta"),
                            Dimension = string.IsNullOrWhiteSpace(dimension)
                                ? "Sin dimensión"
                                : dimension,
                            RespuestaTexto = respuestaTexto,
                            Maximo = 10m,
                            Puntaje = valorNumerico,
                            Porcentaje = 0,
                            Nivel = "Sin calificación",
                            CssNivel = "score-muted"
                        };

                        if (valorNumerico.HasValue)
                        {
                            preguntaResultado.Porcentaje = CalcularPorcentaje(valorNumerico.Value, 10m);
                            preguntaResultado.Nivel = NivelPorPorcentaje(preguntaResultado.Porcentaje);
                            preguntaResultado.CssNivel = CssPorPorcentaje(preguntaResultado.Porcentaje);
                        }

                        vm.Preguntas.Add(preguntaResultado);
                    }
                }
            }

            vm.TotalPreguntas = vm.Preguntas.Count;

            vm.TotalRespondidas = vm.Preguntas.Count(x =>
                !string.Equals(x.RespuestaTexto, "Sin respuesta", StringComparison.OrdinalIgnoreCase));

            var preguntasConPuntaje = vm.Preguntas
                .Where(x => x.Puntaje.HasValue && x.Maximo > 0)
                .ToList();

            vm.TotalPreguntasEvaluables = preguntasConPuntaje.Count;

            if (preguntasConPuntaje.Any())
            {
                vm.PorcentajeGlobal = Math.Round(preguntasConPuntaje.Average(x => x.Porcentaje), 1);
                vm.PuntajeGlobal = Math.Round(vm.PorcentajeGlobal / 10m, 2);
                vm.NivelGeneral = NivelPorPorcentaje(vm.PorcentajeGlobal);
                vm.CssNivelGeneral = CssPorPorcentaje(vm.PorcentajeGlobal);

                var mejor = preguntasConPuntaje
                    .OrderByDescending(x => x.Puntaje)
                    .FirstOrDefault();

                var menor = preguntasConPuntaje
                    .OrderBy(x => x.Puntaje)
                    .FirstOrDefault();

                if (mejor != null && menor != null && mejor.PreguntaID != menor.PreguntaID)
                {
                    vm.ResumenResultado =
                        $"El promedio general fue de {vm.PuntajeGlobal:0.##}/10. La respuesta mejor evaluada obtuvo {mejor.Puntaje:0.##}/10 y la principal área de seguimiento obtuvo {menor.Puntaje:0.##}/10.";
                }
                else
                {
                    vm.ResumenResultado =
                        $"El promedio general fue de {vm.PuntajeGlobal:0.##}/10 con base en las preguntas abiertas calificadas.";
                }

                vm.Dimensiones = preguntasConPuntaje
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Dimension) ? "Sin dimensión" : x.Dimension)
                    .Select(g =>
                    {
                        var porcentaje = Math.Round(g.Average(x => x.Porcentaje), 1);

                        return new ResultadoDimensionVm
                        {
                            Dimension = g.Key ?? "Sin dimensión",
                            Promedio = Math.Round(porcentaje / 10m, 2),
                            Porcentaje = porcentaje,
                            TotalPreguntas = g.Count(),
                            Nivel = NivelPorPorcentaje(porcentaje),
                            CssNivel = CssPorPorcentaje(porcentaje)
                        };
                    })
                    .OrderByDescending(x => x.Porcentaje)
                    .ToList();
            }
            else
            {
                vm.PorcentajeGlobal = 0;
                vm.PuntajeGlobal = 0;
                vm.NivelGeneral = "Sin calificación";
                vm.CssNivelGeneral = "score-muted";
                vm.ResumenResultado = vm.CuestionarioID.HasValue && vm.CuestionarioID.Value > 0
                    ? "Este cuestionario todavía no tiene preguntas calificadas."
                    : "Esta entrevista no tiene cuestionario asociado.";
            }

            using (var dimensionesCmd = new SqlCommand(@"
        SELECT
            DimensionNombre,
            Calificacion,
            CalificacionMaxima,
            Comentario
        FROM dbo.CitaDimensionesEvaluadas
        WHERE CitaID = @CitaID
          AND Activa = 1
        ORDER BY Orden, CitaDimensionID;
    ", conn))
            {
                dimensionesCmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var rd = await dimensionesCmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var calificacion = rd["Calificacion"] == DBNull.Value
                        ? (decimal?)null
                        : Convert.ToDecimal(rd["Calificacion"]);

                    var maximo = rd["CalificacionMaxima"] == DBNull.Value
                        ? 10m
                        : Convert.ToDecimal(rd["CalificacionMaxima"]);

                    var porcentaje = calificacion.HasValue && maximo > 0
                        ? CalcularPorcentaje(calificacion.Value, maximo)
                        : 0;

                    vm.Dimensiones.Add(new ResultadoDimensionVm
                    {
                        Dimension = ReadString(rd, "DimensionNombre"),
                        Promedio = calificacion ?? 0,
                        Porcentaje = porcentaje,
                        TotalPreguntas = 0,
                        Nivel = calificacion.HasValue
                            ? NivelPorPorcentaje(porcentaje)
                            : "Sin calificación",
                        CssNivel = calificacion.HasValue
                            ? CssPorPorcentaje(porcentaje)
                            : "score-muted"
                    });
                }
            }

            vm.TotalDimensiones = vm.Dimensiones.Count;

            var dimensionesConCalificacion = vm.Dimensiones
                .Where(x => x.Porcentaje > 0)
                .ToList();

            if (!preguntasConPuntaje.Any() && dimensionesConCalificacion.Any())
            {
                vm.PorcentajeGlobal = Math.Round(dimensionesConCalificacion.Average(x => x.Porcentaje), 1);
                vm.PuntajeGlobal = Math.Round(vm.PorcentajeGlobal / 10m, 2);
                vm.NivelGeneral = NivelPorPorcentaje(vm.PorcentajeGlobal);
                vm.CssNivelGeneral = CssPorPorcentaje(vm.PorcentajeGlobal);

                vm.ResumenResultado =
                    $"El promedio general fue de {vm.PuntajeGlobal:0.##}/10 con base en las dimensiones evaluadas.";
            }
            else if (!preguntasConPuntaje.Any() && vm.TotalDimensiones > 0)
            {
                vm.ResumenResultado =
                    "La entrevista tiene dimensiones registradas, pero ninguna tiene calificación numérica.";
            }

            return vm;
        }
        private sealed class OpcionResultadoTemp
{
    public int OpcionID { get; set; }

    public string Texto { get; set; } = string.Empty;

    public decimal Puntaje { get; set; }
}

private sealed class RespuestaResultadoTemp
{
    public int? OpcionID { get; set; }

    public string ValorTexto { get; set; } = string.Empty;

    public decimal? ValorNumerico { get; set; }
}

        private async Task<string> ObtenerNombreUsuarioAsync(SqlConnection conn, int usuarioId)
        {
            using var cmd = new SqlCommand(@"
        SELECT TOP 1
            COALESCE(
                NULLIF(LTRIM(RTRIM(CONCAT(
                    ISNULL(p.Nombre, ''),
                    ' ',
                    ISNULL(p.ApellidoPaterno, ''),
                    ' ',
                    ISNULL(p.ApellidoMaterno, '')
                ))), ''),
                NULLIF(LTRIM(RTRIM(u.Username)), ''),
                'Usuario'
            ) AS NombreUsuario
        FROM dbo.Usuarios u
        LEFT JOIN dbo.Persona p
            ON p.PersonaID = u.PersonaID
        WHERE u.UsuarioID = @UsuarioID;
    ", conn);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? "Usuario"
                : Convert.ToString(result) ?? "Usuario";
        }

        private static decimal ObtenerMaximoEscala(string json)
{
    if (string.IsNullOrWhiteSpace(json))
        return 5m;

    try
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("max", out var maxElement))
        {
            var max = maxElement.GetDecimal();
            return max <= 0 ? 5m : max;
        }
    }
    catch
    {
        return 5m;
    }

    return 5m;
}

private static decimal CalcularPorcentaje(decimal puntaje, decimal maximo)
{
    if (maximo <= 0)
        return 0;

    var porcentaje = Math.Round((puntaje * 100m) / maximo, 1);

    if (porcentaje < 0)
        return 0;

    if (porcentaje > 100)
        return 100;

    return porcentaje;
}

private static string NivelPorPorcentaje(decimal porcentaje)
{
    if (porcentaje >= 85)
        return "Sobresaliente";

    if (porcentaje >= 70)
        return "Fuerte";

    if (porcentaje >= 50)
        return "En desarrollo";

    if (porcentaje > 0)
        return "Requiere atención";

    return "Sin calificación";
}

private static string CssPorPorcentaje(decimal porcentaje)
{
    if (porcentaje >= 85)
        return "score-high";

    if (porcentaje >= 70)
        return "score-good";

    if (porcentaje >= 50)
        return "score-mid";

    if (porcentaje > 0)
        return "score-low";

    return "score-muted";
}


        private async Task<CapturarEntrevistaVm?> CargarCapturaEntrevistaAsync(int citaId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sqlCita = @"
SELECT TOP 1
    c.CitaID,
    c.AgendaID,
    c.PersonaID,
    c.EmpresaID,
    c.FechaCita,
    c.HoraInicio,
    c.HoraFin,
    c.Estado,
    c.EstadoFormulario,
    c.CuestionarioID,
    a.Nombre AS NombreEvento,
    ISNULL(q.Nombre, 'Sin cuestionario') AS NombreCuestionario,
    CONCAT('Persona #', c.PersonaID) AS NombrePersona,
    ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa
FROM dbo.Citas c
INNER JOIN dbo.Agendas a
    ON a.AgendaID = c.AgendaID
LEFT JOIN dbo.Cuestionarios q
    ON q.CuestionarioID = c.CuestionarioID
LEFT JOIN dbo.Empresas e
    ON e.EmpresaID = c.EmpresaID
WHERE c.CitaID = @CitaID;";

            using var cmd = new SqlCommand(sqlCita, conn);
            cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

            using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var vm = new CapturarEntrevistaVm
            {
                CitaID = ReadInt(rd, "CitaID"),
                AgendaID = ReadInt(rd, "AgendaID"),
                PersonaID = ReadInt(rd, "PersonaID"),
                EmpresaID = ReadInt(rd, "EmpresaID"),

                CuestionarioID = rd["CuestionarioID"] == DBNull.Value
                    ? null
                    : ReadInt(rd, "CuestionarioID"),

                NombreEvento = ReadString(rd, "NombreEvento"),
                NombreCuestionario = ReadString(rd, "NombreCuestionario"),
                NombrePersona = ReadString(rd, "NombrePersona"),
                NombreEmpresa = ReadString(rd, "NombreEmpresa"),
                FechaCita = ReadDateTime(rd, "FechaCita") ?? DateTime.Today,
                HoraInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero,
                HoraFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero,
                Estado = ReadString(rd, "Estado"),
                EstadoFormulario = ReadString(rd, "EstadoFormulario"),
                Preguntas = new List<PreguntaEntrevistaVm>(),
                Respuestas = new List<RespuestaEntrevistaVm>(),
                DimensionesEvaluadas = new List<DimensionEvaluadaVm>()
            };

            rd.Close();

            if (vm.CuestionarioID.HasValue && vm.CuestionarioID.Value > 0)
            {
                using var preguntasCmd = new SqlCommand(@"
SELECT
    PreguntaID,
    TextoPregunta,
    TipoPregunta,
    Dimension,
    Obligatoria
FROM dbo.Preguntas
WHERE CuestionarioID = @CuestionarioID
  AND Activa = 1
ORDER BY Orden, PreguntaID;", conn);

                preguntasCmd.Parameters.Add("@CuestionarioID", SqlDbType.Int).Value = vm.CuestionarioID.Value;

                using var preguntasRd = await preguntasCmd.ExecuteReaderAsync();

                while (await preguntasRd.ReadAsync())
                {
                    vm.Preguntas.Add(new PreguntaEntrevistaVm
                    {
                        PreguntaID = ReadInt(preguntasRd, "PreguntaID"),
                        TextoPregunta = ReadString(preguntasRd, "TextoPregunta"),
                        TipoPregunta = ReadString(preguntasRd, "TipoPregunta"),
                        Dimension = ReadString(preguntasRd, "Dimension"),
                        Obligatoria = ReadBool(preguntasRd, "Obligatoria"),
                        Opciones = new List<OpcionEntrevistaVm>()
                    });
                }

                preguntasRd.Close();

                foreach (var pregunta in vm.Preguntas)
                {
                    using var opcionesCmd = new SqlCommand(@"
SELECT
    OpcionID,
    TextoOpcion,
    ValorPuntaje
FROM dbo.OpcionesPregunta
WHERE PreguntaID = @PreguntaID
ORDER BY Orden, OpcionID;", conn);

                    opcionesCmd.Parameters.Add("@PreguntaID", SqlDbType.Int).Value =
                        pregunta.PreguntaID.HasValue
                            ? pregunta.PreguntaID.Value
                            : 0;

                    using var opcionesRd = await opcionesCmd.ExecuteReaderAsync();

                    while (await opcionesRd.ReadAsync())
                    {
                        pregunta.Opciones.Add(new OpcionEntrevistaVm
                        {
                            OpcionID = ReadInt(opcionesRd, "OpcionID"),
                            TextoOpcion = ReadString(opcionesRd, "TextoOpcion"),
                            ValorPuntaje = opcionesRd["ValorPuntaje"] == DBNull.Value
                                ? 0m
                                : Convert.ToDecimal(opcionesRd["ValorPuntaje"])
                        });
                    }
                }

                using var respuestasCmd = new SqlCommand(@"
SELECT
    PreguntaID,
    OpcionID,
    ValorTexto,
    ValorNumerico
FROM dbo.Respuestas
WHERE CitaID = @CitaID;", conn);

                respuestasCmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var respuestasRd = await respuestasCmd.ExecuteReaderAsync();

                while (await respuestasRd.ReadAsync())
                {
                    vm.Respuestas.Add(new RespuestaEntrevistaVm
                    {
                        PreguntaID = ReadInt(respuestasRd, "PreguntaID"),
                        OpcionID = respuestasRd["OpcionID"] == DBNull.Value
                            ? null
                            : ReadInt(respuestasRd, "OpcionID"),
                        ValorTexto = ReadString(respuestasRd, "ValorTexto"),
                        ValorNumerico = respuestasRd["ValorNumerico"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(respuestasRd["ValorNumerico"])
                    });
                }
            }

            using (var dimensionesCmd = new SqlCommand(@"
SELECT
    CitaDimensionID,
    DimensionNombre,
    Calificacion,
    CalificacionMaxima,
    Comentario,
    Orden
FROM dbo.CitaDimensionesEvaluadas
WHERE CitaID = @CitaID
  AND Activa = 1
ORDER BY Orden, CitaDimensionID;", conn))
            {
                dimensionesCmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var dimensionesRd = await dimensionesCmd.ExecuteReaderAsync();

                while (await dimensionesRd.ReadAsync())
                {
                    vm.DimensionesEvaluadas.Add(new DimensionEvaluadaVm
                    {
                        CitaDimensionID = ReadInt(dimensionesRd, "CitaDimensionID"),
                        DimensionNombre = ReadString(dimensionesRd, "DimensionNombre"),
                        Calificacion = dimensionesRd["Calificacion"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(dimensionesRd["Calificacion"]),
                        CalificacionMaxima = dimensionesRd["CalificacionMaxima"] == DBNull.Value
                            ? 10m
                            : Convert.ToDecimal(dimensionesRd["CalificacionMaxima"]),
                        Comentario = ReadString(dimensionesRd, "Comentario"),
                        Orden = ReadInt(dimensionesRd, "Orden")
                    });
                }
            }

            return vm;
        }

        private sealed class ContextoAgendamiento
        {
            public int AgendaEmpresaID { get; set; }
            public int AgendaID { get; set; }
            public int? CuestionarioID { get; set; }
            public int EmpresaID { get; set; }
            public string NombreEvento { get; set; } = string.Empty;
            public DateTime FechaInicioAgenda { get; set; }
            public DateTime FechaFinAgenda { get; set; }
            public DateTime FechaInicioSolicitud { get; set; }
            public DateTime FechaFinSolicitud { get; set; }
        }

        private async Task<ContextoAgendamiento?> ObtenerContextoAgendamientoAsync(         SqlConnection conn,         int empresaId,          int? agendaId,          int? agendaEmpresaId,          SqlTransaction? tx = null)
        {
            const string sql = @"
        SELECT TOP 1
            ae.AgendaEmpresaID,
            ae.AgendaID,
            ae.EmpresaID,
            ae.FechaInicioSolicitud,
            ae.FechaFinSolicitud,
            a.CuestionarioID,
            a.Nombre AS NombreEvento,
            a.FechaInicio,
            a.FechaFin
        FROM dbo.AgendaEmpresas ae
        INNER JOIN dbo.Agendas a
            ON a.AgendaID = ae.AgendaID
        LEFT JOIN dbo.Cuestionarios q
            ON q.CuestionarioID = a.CuestionarioID
        WHERE ae.EmpresaID = @EmpresaID
          AND ae.Activa = 1
          AND ae.EstadoAsignacion IN ('programada', 'abierta')
          AND a.Activa = 1
          AND a.Estatus = 'activo'
          AND (
                a.CuestionarioID IS NULL
                OR (
                    q.Activo = 1
                    AND q.Estatus = 'activo'
                )
          )
          AND (@AgendaID IS NULL OR ae.AgendaID = @AgendaID)
          AND (@AgendaEmpresaID IS NULL OR ae.AgendaEmpresaID = @AgendaEmpresaID)
        ORDER BY
            CASE
                WHEN SYSDATETIME() BETWEEN ae.FechaInicioSolicitud AND ae.FechaFinSolicitud THEN 0
                WHEN SYSDATETIME() < ae.FechaInicioSolicitud THEN 1
                ELSE 2
            END,
            ae.FechaInicioSolicitud ASC,
            ae.AgendaEmpresaID DESC;";

            using var cmd = new SqlCommand(sql, conn, tx);

            cmd.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;
            cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value =
                agendaId.HasValue ? agendaId.Value : DBNull.Value;
            cmd.Parameters.Add("@AgendaEmpresaID", SqlDbType.Int).Value =
                agendaEmpresaId.HasValue ? agendaEmpresaId.Value : DBNull.Value;

            using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ContextoAgendamiento
            {
                AgendaEmpresaID = ReadInt(rd, "AgendaEmpresaID"),
                AgendaID = ReadInt(rd, "AgendaID"),
                EmpresaID = ReadInt(rd, "EmpresaID"),
                CuestionarioID = rd["CuestionarioID"] == DBNull.Value
                    ? null
                    : ReadInt(rd, "CuestionarioID"),
                NombreEvento = ReadString(rd, "NombreEvento"),
                FechaInicioAgenda = ReadDateTime(rd, "FechaInicio") ?? DateTime.Today,
                FechaFinAgenda = ReadDateTime(rd, "FechaFin") ?? DateTime.Today,
                FechaInicioSolicitud = ReadDateTime(rd, "FechaInicioSolicitud") ?? DateTime.MinValue,
                FechaFinSolicitud = ReadDateTime(rd, "FechaFinSolicitud") ?? DateTime.MinValue
            };
        }

        private async Task<List<SlotDisponibleVm>> GenerarSlotsDisponiblesAsync(
    SqlConnection conn,
    int agendaId,
    DateTime fechaInicioAgenda,
    DateTime fechaFinAgenda,
    SqlTransaction? tx = null)
{
    var reglas = new List<AgendaDiaEditorVm>();

    using (var cmd = new SqlCommand(@"
        SELECT
            AgendaDiaID,
            AgendaID,
            DiaSemana,
            HoraInicio,
            HoraFin,
            DuracionCitaMinutos,
            DescansoMinutos,
            CapacidadCitas,
            Activo
        FROM dbo.AgendaDias
        WHERE AgendaID = @AgendaID
          AND Activo = 1
        ORDER BY DiaSemana, HoraInicio;
    ", conn, tx))
    {
        cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

        using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            reglas.Add(new AgendaDiaEditorVm
            {
                AgendaDiaID = ReadInt(rd, "AgendaDiaID"),
                AgendaID = ReadInt(rd, "AgendaID"),
                DiaSemana = ReadInt(rd, "DiaSemana"),
                HoraInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero,
                HoraFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero,
                DuracionCitaMinutos = ReadInt(rd, "DuracionCitaMinutos"),
                DescansoMinutos = ReadInt(rd, "DescansoMinutos"),
                CapacidadCitas = ReadInt(rd, "CapacidadCitas"),
                Activo = ReadBool(rd, "Activo")
            });
        }
    }

    var ocupacionPorSlot = new Dictionary<string, int>();

    using (var cmd = new SqlCommand(@"
        SELECT
            FechaCita,
            HoraInicio,
            HoraFin,
            COUNT(1) AS TotalOcupadas
        FROM dbo.Citas
        WHERE AgendaID = @AgendaID
          AND Estado IN ('pendiente', 'asistio', 'finalizada')
        GROUP BY
            FechaCita,
            HoraInicio,
            HoraFin;
    ", conn, tx))
    {
        cmd.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

        using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var fecha = Convert.ToDateTime(rd["FechaCita"]).Date;

            var inicio = rd["HoraInicio"] is TimeSpan hi
                ? hi
                : TimeSpan.Parse(Convert.ToString(rd["HoraInicio"]) ?? "00:00");

            var fin = rd["HoraFin"] is TimeSpan hf
                ? hf
                : TimeSpan.Parse(Convert.ToString(rd["HoraFin"]) ?? "00:00");

            var totalOcupadas = ReadInt(rd, "TotalOcupadas");
            var key = SlotKey(fecha, inicio, fin);

            ocupacionPorSlot[key] = totalOcupadas;
        }
    }

    var slots = new List<SlotDisponibleVm>();
    var ahora = DateTime.Now;

    for (var fecha = fechaInicioAgenda.Date; fecha <= fechaFinAgenda.Date; fecha = fecha.AddDays(1))
    {
        var diaSemana = ConvertirDiaSemanaSql(fecha.DayOfWeek);

        var reglasDelDia = reglas
            .Where(x => x.DiaSemana == diaSemana && x.Activo)
            .ToList();

        foreach (var regla in reglasDelDia)
        {
            var inicio = regla.HoraInicio;
            var finJornada = regla.HoraFin;
            var duracion = TimeSpan.FromMinutes(regla.DuracionCitaMinutos);
            var descanso = TimeSpan.FromMinutes(regla.DescansoMinutos);

            var capacidad = regla.CapacidadCitas <= 0
                ? 1
                : regla.CapacidadCitas;

            if (duracion <= TimeSpan.Zero)
                continue;

            if (finJornada <= inicio)
                continue;

            while (inicio.Add(duracion) <= finJornada)
            {
                var fin = inicio.Add(duracion);
                var fechaHoraInicio = fecha.Date.Add(inicio);
                var key = SlotKey(fecha, inicio, fin);

                var ocupadas = ocupacionPorSlot.TryGetValue(key, out var totalOcupadas)
                    ? totalOcupadas
                    : 0;

                var horarioVencido = fechaHoraInicio <= ahora;
                var horarioLleno = ocupadas >= capacidad;

                slots.Add(new SlotDisponibleVm
                {
                    Fecha = fecha,
                    HoraInicio = inicio,
                    HoraFin = fin,
                    Disponible = !horarioVencido && !horarioLleno
                });

                inicio = fin.Add(descanso);
            }
        }
    }

    return slots
        .OrderBy(x => x.Fecha)
        .ThenBy(x => x.HoraInicio)
        .ToList();
}

private async Task<bool> PersonaTieneCitaActivaAsync(
    SqlConnection conn,
    int personaId,
    int agendaId,
    SqlTransaction? tx = null)
{
    using var cmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.Citas
        WHERE AgendaID = @AgendaID
          AND PersonaID = @PersonaID
          AND Estado IN ('pendiente', 'asistio', 'finalizada');", conn, tx);

    cmd.Parameters.AddWithValue("@AgendaID", agendaId);
    cmd.Parameters.AddWithValue("@PersonaID", personaId);

    var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

    return total > 0;
}

private async Task<bool> HorarioOcupadoAsync(
    SqlConnection conn,
    int agendaId,
    DateTime fechaCita,
    TimeSpan horaInicio,
    TimeSpan horaFin,
    SqlTransaction? tx = null)
{
    using var cmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.Citas
        WHERE AgendaID = @AgendaID
          AND FechaCita = @FechaCita
          AND Estado IN ('pendiente', 'asistio', 'finalizada')
          AND HoraInicio < @HoraFin
          AND HoraFin > @HoraInicio;", conn, tx);

    cmd.Parameters.AddWithValue("@AgendaID", agendaId);
    cmd.Parameters.AddWithValue("@FechaCita", fechaCita.Date);
    cmd.Parameters.AddWithValue("@HoraInicio", horaInicio);
    cmd.Parameters.AddWithValue("@HoraFin", horaFin);

    var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

    return total > 0;
}

private async Task<bool> SlotPerteneceAConfiguracionAsync(
    SqlConnection conn,
    int agendaId,
    DateTime fechaCita,
    TimeSpan horaInicio,
    TimeSpan horaFin,
    SqlTransaction? tx = null)
{
    var diaSemana = ConvertirDiaSemanaSql(fechaCita.DayOfWeek);
    var duracionMinutos = Convert.ToInt32((horaFin - horaInicio).TotalMinutes);

    using var cmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.AgendaDias
        WHERE AgendaID = @AgendaID
          AND Activo = 1
          AND DiaSemana = @DiaSemana
          AND HoraInicio <= @HoraInicio
          AND HoraFin >= @HoraFin
          AND DuracionCitaMinutos = @DuracionMinutos;", conn, tx);

    cmd.Parameters.AddWithValue("@AgendaID", agendaId);
    cmd.Parameters.AddWithValue("@DiaSemana", diaSemana);
    cmd.Parameters.AddWithValue("@HoraInicio", horaInicio);
    cmd.Parameters.AddWithValue("@HoraFin", horaFin);
    cmd.Parameters.AddWithValue("@DuracionMinutos", duracionMinutos);

    var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

    return total > 0;
}


        private async Task NotificarEditoresNuevaCitaAsync(int citaId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var editorPersonaIds = new List<int>();

                using (var cmdEditores = new SqlCommand(@"
    SELECT DISTINCT PersonaID
    FROM dbo.Usuarios
    WHERE RolID = 3
      AND PersonaID IS NOT NULL
      AND ISNULL(Activo, 1) = 1;
", conn))
                {
                    using var rdEditores = await cmdEditores.ExecuteReaderAsync();

                    while (await rdEditores.ReadAsync())
                    {
                        editorPersonaIds.Add(ReadInt(rdEditores, "PersonaID"));
                    }
                }
                
                if (!editorPersonaIds.Any())
                    return;

                using var cmd = new SqlCommand(@"
            SELECT TOP 1
                c.CitaID,
                c.PersonaID,
                c.FechaCita,
                c.HoraInicio,
                c.HoraFin,
                ISNULL(a.Nombre, CONCAT('Agenda #', c.AgendaID)) AS NombreEvento,
                ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(CONCAT(
                        ISNULL(p.Nombre, ''),
                        ' ',
                        ISNULL(p.ApellidoPaterno, ''),
                        ' ',
                        ISNULL(p.ApellidoMaterno, '')
                    ))), ''),
                    'Usuario'
                ) AS NombrePersona
            FROM dbo.Citas c
            INNER JOIN dbo.Agendas a
                ON a.AgendaID = c.AgendaID
            INNER JOIN dbo.Empresas e
                ON e.EmpresaID = c.EmpresaID
            INNER JOIN dbo.Persona p
                ON p.PersonaID = c.PersonaID
            WHERE c.CitaID = @CitaID;
        ", conn);

                cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                var nombrePersona = ReadString(rd, "NombrePersona");
                var nombreEvento = ReadString(rd, "NombreEvento");
                var nombreEmpresa = ReadString(rd, "NombreEmpresa");
                var fechaCita = ReadDateTime(rd, "FechaCita") ?? DateTime.Today;
                var horaInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero;
                var horaFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero;

                var asunto = $"Nueva cita registrada - {nombreEvento}";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#198754; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Nueva cita registrada</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Se ha registrado una nueva cita en el módulo de jornadas de mapeo.</p>

      <div style='background:#f8f9fa; border-left:4px solid #198754; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio de cita:</strong> {citaId}</p>
        <p style='margin:0 0 6px;'><strong>Colaborador:</strong> {System.Net.WebUtility.HtmlEncode(nombrePersona)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(nombreEmpresa)}</p>
        <p style='margin:0 0 6px;'><strong>Evento:</strong> {System.Net.WebUtility.HtmlEncode(nombreEvento)}</p>
        <p style='margin:0 0 6px;'><strong>Fecha:</strong> {fechaCita:dd/MM/yyyy}</p>
        <p style='margin:0;'><strong>Horario:</strong> {horaInicio:hh\\:mm} - {horaFin:hh\\:mm}</p>
      </div>

      <p>Ingresa a la Intranet, módulo <strong>Citas</strong>, para revisar la bandeja.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                var resultado = await _notif.EnviarABccPersonasAsync(
                    editorPersonaIds,
                    asunto,
                    html
                );

                _logger.LogInformation(
                    "Correo nueva cita a editores: Enc={Enc}, Env={Env}, Filt={Filt}, Err={Err}",
                    resultado.Encontrados,
                    resultado.Enviados,
                    resultado.FiltradosPorCandados,
                    resultado.Errores
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo a editores por nueva cita (CitaID={CitaID})", citaId);
            }
        }



        private static int ConvertirDiaSemanaSql(DayOfWeek dia)
{
    return dia switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        DayOfWeek.Saturday => 6,
        DayOfWeek.Sunday => 7,
        _ => 0
    };
}

        private async Task NotificarUsuarioCitaRegistradaAsync(int citaId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
            SELECT TOP 1
                c.CitaID,
                c.PersonaID,
                c.FechaCita,
                c.HoraInicio,
                c.HoraFin,
                ISNULL(a.Nombre, CONCAT('Agenda #', c.AgendaID)) AS NombreEvento,
                ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(CONCAT(
                        ISNULL(p.Nombre, ''),
                        ' ',
                        ISNULL(p.ApellidoPaterno, ''),
                        ' ',
                        ISNULL(p.ApellidoMaterno, '')
                    ))), ''),
                    'Usuario'
                ) AS NombrePersona
            FROM dbo.Citas c
            INNER JOIN dbo.Agendas a
                ON a.AgendaID = c.AgendaID
            INNER JOIN dbo.Empresas e
                ON e.EmpresaID = c.EmpresaID
            INNER JOIN dbo.Persona p
                ON p.PersonaID = c.PersonaID
            WHERE c.CitaID = @CitaID;
        ", conn);

                cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return;

                var personaId = ReadInt(rd, "PersonaID");
                var nombrePersona = ReadString(rd, "NombrePersona");
                var nombreEvento = ReadString(rd, "NombreEvento");
                var nombreEmpresa = ReadString(rd, "NombreEmpresa");
                var fechaCita = ReadDateTime(rd, "FechaCita") ?? DateTime.Today;
                var horaInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero;
                var horaFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero;

                var asunto = $"Confirmación de cita - {nombreEvento}";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Cita registrada correctamente</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola <strong>{System.Net.WebUtility.HtmlEncode(nombrePersona)}</strong>,</p>

      <p>Tu cita para la jornada de mapeo fue registrada correctamente.</p>

      <div style='background:#f8f9fa; border-left:4px solid #0d6efd; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Folio de cita:</strong> {citaId}</p>
        <p style='margin:0 0 6px;'><strong>Evento:</strong> {System.Net.WebUtility.HtmlEncode(nombreEvento)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(nombreEmpresa)}</p>
        <p style='margin:0 0 6px;'><strong>Fecha:</strong> {fechaCita:dd/MM/yyyy}</p>
        <p style='margin:0;'><strong>Horario:</strong> {horaInicio:hh\\:mm} - {horaFin:hh\\:mm}</p>
      </div>

      <p>Por favor ingresa puntualmente a tu entrevista.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo de confirmación de cita (CitaID={CitaID})", citaId);
            }
        }

        //NUEVO: Funcion que notifica al usuario cuando no asistió a su cita

        private async Task<bool> NotificarUsuarioCitaNoAsistioAsync(int citaId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
            SELECT TOP 1
                c.CitaID,
                c.PersonaID,
                c.FechaCita,
                c.HoraInicio,
                c.HoraFin,
                ISNULL(a.Nombre, CONCAT('Agenda #', c.AgendaID)) AS NombreEvento,
                ISNULL(e.Nombre, CONCAT('Empresa #', c.EmpresaID)) AS NombreEmpresa,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(CONCAT(
                        ISNULL(p.Nombre, ''),
                        ' ',
                        ISNULL(p.ApellidoPaterno, ''),
                        ' ',
                        ISNULL(p.ApellidoMaterno, '')
                    ))), ''),
                    'Usuario'
                ) AS NombrePersona
            FROM dbo.Citas c
            INNER JOIN dbo.Agendas a
                ON a.AgendaID = c.AgendaID
            INNER JOIN dbo.Empresas e
                ON e.EmpresaID = c.EmpresaID
            LEFT JOIN dbo.Persona p
                ON p.PersonaID = c.PersonaID
            WHERE c.CitaID = @CitaID;
        ", conn);

                cmd.Parameters.Add("@CitaID", SqlDbType.Int).Value = citaId;

                using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    _logger.LogWarning(
                        "No se encontró información para notificar inasistencia. CitaID={CitaID}",
                        citaId
                    );

                    return false;
                }

                var personaId = ReadInt(rd, "PersonaID");
                var nombrePersona = ReadString(rd, "NombrePersona");
                var nombreEvento = ReadString(rd, "NombreEvento");
                var nombreEmpresa = ReadString(rd, "NombreEmpresa");
                var fechaCita = ReadDateTime(rd, "FechaCita") ?? DateTime.Today;
                var horaInicio = ReadTimeSpan(rd, "HoraInicio") ?? TimeSpan.Zero;
                var horaFin = ReadTimeSpan(rd, "HoraFin") ?? TimeSpan.Zero;

                if (personaId <= 0)
                {
                    _logger.LogWarning(
                        "La cita no tiene PersonaID válido para notificar inasistencia. CitaID={CitaID}",
                        citaId
                    );

                    return false;
                }

                var asunto = $"Notificación de inasistencia - {nombreEvento}";

                var nombrePersonaHtml = System.Net.WebUtility.HtmlEncode(nombrePersona);
                var nombreEventoHtml = System.Net.WebUtility.HtmlEncode(nombreEvento);
                var nombreEmpresaHtml = System.Net.WebUtility.HtmlEncode(nombreEmpresa);

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>

    <div style='padding:20px; background:#991b1b; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Notificación de inasistencia</h2>
    </div>

    <div style='padding:22px; color:#333;'>
      <p>Hola <strong>{nombrePersonaHtml}</strong>,</p>

      <p>
        Te notificamos que tu cita fue marcada como <strong>no asistida</strong>
        en la jornada de mapeo de talento.
      </p>

      <div style='background:#fef2f2; border-left:4px solid #dc2626; padding:14px 16px; border-radius:6px; margin:16px 0;'>
        <p style='margin:0 0 7px;'><strong>Folio de cita:</strong> CIT-{citaId:D5}</p>
        <p style='margin:0 0 7px;'><strong>Evento:</strong> {nombreEventoHtml}</p>
        <p style='margin:0 0 7px;'><strong>Empresa:</strong> {nombreEmpresaHtml}</p>
        <p style='margin:0 0 7px;'><strong>Fecha:</strong> {fechaCita:dd/MM/yyyy}</p>
        <p style='margin:0;'><strong>Horario:</strong> {horaInicio:hh\:mm} - {horaFin:hh\:mm}</p>
      </div>

      <p>
        Si consideras que esta información no es correcta, por favor comunícate directamente
        con el área de Recursos Humanos.
      </p>

      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

                var resultado = await _notif.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html
                );

                _logger.LogInformation(
                    "Correo inasistencia CitaID={CitaID}, PersonaID={PersonaID}: Enc={Enc}, Env={Env}, Filt={Filt}, Err={Err}",
                    citaId,
                    personaId,
                    resultado.Encontrados,
                    resultado.Enviados,
                    resultado.FiltradosPorCandados,
                    resultado.Errores
                );

                return resultado.Enviados > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando correo de inasistencia de cita. CitaID={CitaID}",
                    citaId
                );

                return false;
            }
        }

        private static string SlotKey(DateTime fecha, TimeSpan inicio, TimeSpan fin)
{
    return $"{fecha:yyyyMMdd}|{inicio:hh\\:mm}|{fin:hh\\:mm}";
}

        private async Task<int?> ObtenerPersonaIdAsync(SqlConnection conn, int usuarioId)
        {
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 PersonaID
                FROM dbo.Usuarios
                WHERE UsuarioID = @UsuarioID;
            ", conn);

            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return null;

            return Convert.ToInt32(result);
        }

        private async Task<int?> ObtenerEmpresaActivaUsuarioAsync(SqlConnection conn, int usuarioId)
        {
            const string sql = @"
                SELECT TOP 1 UE.EmpresaID
                FROM dbo.UsuariosEmpresas UE
                INNER JOIN dbo.Empresas E
                    ON E.EmpresaID = UE.EmpresaID
                WHERE UE.UsuarioID = @UsuarioID
                  AND UE.Activo = 1
                  AND E.Activa = 1
                ORDER BY UE.EmpresaID;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return null;

            return Convert.ToInt32(result);
        }

        private async Task<bool> UsuarioTieneEmpresaAsync(
            SqlConnection conn,
            int usuarioId,
            int empresaId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.UsuariosEmpresas UE
                INNER JOIN dbo.Empresas E
                    ON E.EmpresaID = UE.EmpresaID
                WHERE UE.UsuarioID = @UsuarioID
                  AND UE.EmpresaID = @EmpresaID
                  AND UE.Activo = 1
                  AND E.Activa = 1;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@EmpresaID", empresaId);

            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

            return total > 0;
        }

        private async Task<List<int>> ObtenerEmpresasActivasUsuarioAsync(
            SqlConnection conn,
            int usuarioId)
        {
            var empresas = new List<int>();

            const string sql = @"
                SELECT UE.EmpresaID
                FROM dbo.UsuariosEmpresas UE
                INNER JOIN dbo.Empresas E
                    ON E.EmpresaID = UE.EmpresaID
                WHERE UE.UsuarioID = @UsuarioID
                  AND UE.Activo = 1
                  AND E.Activa = 1
                ORDER BY E.Nombre;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                empresas.Add(Convert.ToInt32(rd["EmpresaID"]));

            return empresas;
        }

        private async Task<List<int>> ObtenerRolesUsuarioAsync(SqlConnection conn, int usuarioId)
{
    var roles = new HashSet<int>();

    AgregarRolDesdeValor(HttpContext.Session.GetInt32("RolID")?.ToString(), roles);
    AgregarRolDesdeValor(HttpContext.Session.GetInt32("RoleID")?.ToString(), roles);
    AgregarRolDesdeValor(HttpContext.Session.GetInt32("IdRol")?.ToString(), roles);

    AgregarRolDesdeValor(User.FindFirst("RolID")?.Value, roles);
    AgregarRolDesdeValor(User.FindFirst("RoleID")?.Value, roles);
    AgregarRolDesdeValor(User.FindFirst("IdRol")?.Value, roles);
    AgregarRolDesdeValor(User.FindFirst(ClaimTypes.Role)?.Value, roles);

    const string sql = @"
        CREATE TABLE #RolesUsuarioTmp
        (
            RolID INT NULL
        );

        IF COL_LENGTH('dbo.Usuarios', 'RolID') IS NOT NULL
        BEGIN
            EXEC sp_executesql
                N'INSERT INTO #RolesUsuarioTmp (RolID)
                  SELECT TRY_CONVERT(INT, RolID)
                  FROM dbo.Usuarios
                  WHERE UsuarioID = @UsuarioID',
                N'@UsuarioID INT',
                @UsuarioID = @UsuarioID;
        END;

        IF COL_LENGTH('dbo.Usuarios', 'RoleID') IS NOT NULL
        BEGIN
            EXEC sp_executesql
                N'INSERT INTO #RolesUsuarioTmp (RolID)
                  SELECT TRY_CONVERT(INT, RoleID)
                  FROM dbo.Usuarios
                  WHERE UsuarioID = @UsuarioID',
                N'@UsuarioID INT',
                @UsuarioID = @UsuarioID;
        END;

        IF COL_LENGTH('dbo.Usuarios', 'IdRol') IS NOT NULL
        BEGIN
            EXEC sp_executesql
                N'INSERT INTO #RolesUsuarioTmp (RolID)
                  SELECT TRY_CONVERT(INT, IdRol)
                  FROM dbo.Usuarios
                  WHERE UsuarioID = @UsuarioID',
                N'@UsuarioID INT',
                @UsuarioID = @UsuarioID;
        END;

        IF OBJECT_ID('dbo.UsuariosRoles', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.UsuariosRoles', 'UsuarioID') IS NOT NULL
           AND COL_LENGTH('dbo.UsuariosRoles', 'RolID') IS NOT NULL
        BEGIN
            EXEC sp_executesql
                N'INSERT INTO #RolesUsuarioTmp (RolID)
                  SELECT TRY_CONVERT(INT, RolID)
                  FROM dbo.UsuariosRoles
                  WHERE UsuarioID = @UsuarioID',
                N'@UsuarioID INT',
                @UsuarioID = @UsuarioID;
        END;

        IF OBJECT_ID('dbo.UsuarioRoles', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.UsuarioRoles', 'UsuarioID') IS NOT NULL
           AND COL_LENGTH('dbo.UsuarioRoles', 'RolID') IS NOT NULL
        BEGIN
            EXEC sp_executesql
                N'INSERT INTO #RolesUsuarioTmp (RolID)
                  SELECT TRY_CONVERT(INT, RolID)
                  FROM dbo.UsuarioRoles
                  WHERE UsuarioID = @UsuarioID',
                N'@UsuarioID INT',
                @UsuarioID = @UsuarioID;
        END;

        SELECT DISTINCT RolID
        FROM #RolesUsuarioTmp
        WHERE RolID IS NOT NULL;
    ";

    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

    using var rd = await cmd.ExecuteReaderAsync();

    while (await rd.ReadAsync())
    {
        var rolId = ReadInt(rd, "RolID");

        if (rolId > 0)
            roles.Add(rolId);
    }

    // Si no encuentra rol, lo tratamos como Usuario Final para NO exponer el panel admin.
    if (!roles.Any())
        roles.Add(5);

    return roles.ToList();
}

private static void AgregarRolDesdeValor(string? valor, HashSet<int> roles)
{
    if (string.IsNullOrWhiteSpace(valor))
        return;

    if (int.TryParse(valor, out var rolId))
    {
        if (rolId > 0)
            roles.Add(rolId);

        return;
    }

    var normalizado = valor.Trim().ToLowerInvariant();

    if (normalizado.Contains("usuario final"))
        roles.Add(5);

    if (normalizado.Contains("autor") || normalizado.Contains("editor"))
        roles.Add(3);

    if (normalizado.Contains("administrador de intranet"))
        roles.Add(1);

    if (normalizado.Contains("administrador de ti"))
        roles.Add(2);
}

        private int? ObtenerUsuarioId()
        {
            if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid))
                return uid;

            if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid))
                return uid;

            return HttpContext.Session.GetInt32("UsuarioID");
        }

        private int? ObtenerEmpresaId()
        {
            var empresaId = HttpContext.Session.GetInt32("EmpresaID");

            if (!empresaId.HasValue &&
                int.TryParse(User.FindFirst("EmpresaID")?.Value, out var eid))
            {
                empresaId = eid;
            }

            if (!empresaId.HasValue &&
                int.TryParse(User.FindFirst("IDEmpresa")?.Value, out var eidAlt))
            {
                empresaId = eidAlt;
            }

            return empresaId;
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

        private static DateTime? ReadDateTime(SqlDataReader rd, string column)
        {
            var value = rd[column];
            return value == DBNull.Value ? null : Convert.ToDateTime(value);
        }

        private static TimeSpan? ReadTimeSpan(SqlDataReader rd, string column)
        {
            var value = rd[column];

            if (value == DBNull.Value)
                return null;

            if (value is TimeSpan ts)
                return ts;

            return TimeSpan.Parse(Convert.ToString(value) ?? "00:00:00");
        }

        private static bool ReadBool(SqlDataReader rd, string column)
        {
            var value = rd[column];

            if (value == DBNull.Value)
                return false;

            return Convert.ToBoolean(value);
        }

        private static DateTime CalcularCierreAgendamiento(
            DateTime fechaInicioEvento,
            DateTime fechaFinSolicitud)
        {
            // Ahora el cierre real lo controla RH desde CrearAgenda:
            // AgendaEmpresas.FechaFinSolicitud.
            return fechaFinSolicitud;
        }

        private async Task<bool> HaySlotsDisponiblesAsync(
            SqlConnection conn,
            int agendaId,
            DateTime fechaInicioAgenda,
            DateTime fechaFinAgenda)
        {
            var slots = await GenerarSlotsDisponiblesAsync(
                conn,
                agendaId,
                fechaInicioAgenda,
                fechaFinAgenda
            );

            return slots.Any(x => x.Disponible);
        }

        


        private async Task NotificarUsuariosEventoHabilitadoAsync(int agendaId, List<int> empresaIds)
{
    try
    {
        if (empresaIds == null || !empresaIds.Any())
            return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmdAgenda = new SqlCommand(@"
            SELECT TOP 1
                AgendaID,
                Nombre,
                Descripcion,
                FechaInicio,
                FechaFin
            FROM dbo.Agendas
            WHERE AgendaID = @AgendaID;
        ", conn);

        cmdAgenda.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;

        string nombreEvento = "Jornada de mapeo";
        string descripcion = string.Empty;
        DateTime fechaInicio = DateTime.Today;
        DateTime fechaFin = DateTime.Today;

        using (var rd = await cmdAgenda.ExecuteReaderAsync())
        {
            if (!await rd.ReadAsync())
                return;

            nombreEvento = ReadString(rd, "Nombre");
            descripcion = ReadString(rd, "Descripcion");
            fechaInicio = ReadDateTime(rd, "FechaInicio") ?? DateTime.Today;
            fechaFin = ReadDateTime(rd, "FechaFin") ?? DateTime.Today;
        }

        foreach (var empresaId in empresaIds.Distinct())
        {
            var personaIds = new List<int>();
            string nombreEmpresa = string.Empty;
            DateTime? fechaInicioSolicitud = null;
            DateTime? fechaFinSolicitud = null;

            using (var cmdEmpresa = new SqlCommand(@"
                SELECT TOP 1
                    e.Nombre AS NombreEmpresa,
                    ae.FechaInicioSolicitud,
                    ae.FechaFinSolicitud
                FROM dbo.AgendaEmpresas ae
                INNER JOIN dbo.Empresas e
                    ON e.EmpresaID = ae.EmpresaID
                WHERE ae.AgendaID = @AgendaID
                  AND ae.EmpresaID = @EmpresaID
                  AND ae.Activa = 1;
            ", conn))
            {
                cmdEmpresa.Parameters.Add("@AgendaID", SqlDbType.Int).Value = agendaId;
                cmdEmpresa.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

                using var rdEmpresa = await cmdEmpresa.ExecuteReaderAsync();

                if (await rdEmpresa.ReadAsync())
                {
                    nombreEmpresa = ReadString(rdEmpresa, "NombreEmpresa");
                    fechaInicioSolicitud = ReadDateTime(rdEmpresa, "FechaInicioSolicitud");
                    fechaFinSolicitud = ReadDateTime(rdEmpresa, "FechaFinSolicitud");
                }
            }

            using (var cmdUsuarios = new SqlCommand(@"
                SELECT DISTINCT u.PersonaID
                FROM dbo.Usuarios u
                INNER JOIN dbo.UsuariosEmpresas ue
                    ON ue.UsuarioID = u.UsuarioID
                   AND ue.Activo = 1
                WHERE ue.EmpresaID = @EmpresaID
                  AND u.PersonaID IS NOT NULL
                  AND ISNULL(u.Activo, 1) = 1
                  AND ISNULL(u.RolID, 0) = 5;
            ", conn))
            {
                cmdUsuarios.Parameters.Add("@EmpresaID", SqlDbType.Int).Value = empresaId;

                using var rdUsuarios = await cmdUsuarios.ExecuteReaderAsync();

                while (await rdUsuarios.ReadAsync())
                {
                    personaIds.Add(ReadInt(rdUsuarios, "PersonaID"));
                }
            }

            if (!personaIds.Any())
                continue;

            var asunto = $"Nueva jornada de mapeo disponible - {nombreEvento}";

            var descripcionHtml = string.IsNullOrWhiteSpace(descripcion)
                ? ""
                : $"<p>{System.Net.WebUtility.HtmlEncode(descripcion)}</p>";

            var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:20px; background:#0d6efd; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Jornada de mapeo disponible</h2>
    </div>

    <div style='padding:20px; color:#333;'>
      <p>Hola,</p>

      <p>Se ha habilitado una nueva jornada de mapeo para tu empresa.</p>

      <div style='background:#f8f9fa; border-left:4px solid #0d6efd; padding:12px 14px; border-radius:6px; margin:14px 0;'>
        <p style='margin:0 0 6px;'><strong>Evento:</strong> {System.Net.WebUtility.HtmlEncode(nombreEvento)}</p>
        <p style='margin:0 0 6px;'><strong>Empresa:</strong> {System.Net.WebUtility.HtmlEncode(nombreEmpresa)}</p>
        <p style='margin:0 0 6px;'><strong>Periodo del evento:</strong> {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}</p>
        <p style='margin:0 0 6px;'><strong>Inicio para solicitar cita:</strong> {(fechaInicioSolicitud.HasValue ? fechaInicioSolicitud.Value.ToString("dd/MM/yyyy HH:mm") : "Por definir")}</p>
        <p style='margin:0;'><strong>Fin para solicitar cita:</strong> {(fechaFinSolicitud.HasValue ? fechaFinSolicitud.Value.ToString("dd/MM/yyyy HH:mm") : "Por definir")}</p>
      </div>

      {descripcionHtml}

      <p>Ingresa a la Intranet, módulo <strong>Citas</strong>, para solicitar tu horario.</p>
      <p>https://intranet.nsgroup.com.mx/</p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por la Intranet NS Group. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

            var resultado = await _notif.EnviarABccPersonasAsync(
                personaIds,
                asunto,
                html
            );

            _logger.LogInformation(
                "Correo evento habilitado AgendaID={AgendaID}, EmpresaID={EmpresaID}: Enc={Enc}, Env={Env}, Filt={Filt}, Err={Err}",
                agendaId,
                empresaId,
                resultado.Encontrados,
                resultado.Enviados,
                resultado.FiltradosPorCandados,
                resultado.Errores
            );
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error enviando notificación de evento habilitado AgendaID={AgendaID}", agendaId);
    }
}

    }
}

