using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ProyectoMatrix.ViewModels.Formularios;
using System.Data;
using System.Text.Json;

namespace ProyectoMatrix.Servicios
{
    public class FormulariosSqlService
    {
        private readonly string _connectionString;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public FormulariosSqlService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
        }


        public async Task GarantizarPlantillaBaseGuiasAsync()
        {
            var plantilla = CrearPlantillaBaseGuias();
            NormalizarCampos(plantilla.Campos);

            var estructuraJson = JsonSerializer.Serialize(plantilla.Campos, _jsonOptions);
            var datosFijosPdfJson = SerializarDatosOficiales(plantilla);

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                IF EXISTS (
                    SELECT 1
                    FROM dbo.FormularioPlantillas
                    WHERE Modulo = @Modulo
                      AND Nombre = @Nombre
                      AND EsPlantillaBase = 1
                )
                BEGIN
                    UPDATE dbo.FormularioPlantillas
                    SET
                        Descripcion = @Descripcion,
                        Categoria = @Categoria,
                        EstructuraJson = @EstructuraJson,
                        DatosFijosPdfJson = @DatosFijosPdfJson,
                        Activo = 1,
                        FechaActualizacion = SYSDATETIME()
                    WHERE Modulo = @Modulo
                      AND Nombre = @Nombre
                      AND EsPlantillaBase = 1;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.FormularioPlantillas
                    (
                        Nombre,
                        Descripcion,
                        Categoria,
                        Modulo,
                        EstructuraJson,
                        DatosFijosPdfJson,
                        Activo,
                        EsPlantillaBase
                    )
                    VALUES
                    (
                        @Nombre,
                        @Descripcion,
                        @Categoria,
                        @Modulo,
                        @EstructuraJson,
                        @DatosFijosPdfJson,
                        1,
                        1
                    );
                END;
            ", connection);

            command.Parameters.AddWithValue("@Nombre", plantilla.Nombre);
            command.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(plantilla.Descripcion) ? (object)DBNull.Value : plantilla.Descripcion);
            command.Parameters.AddWithValue("@Categoria", plantilla.Categoria ?? "Guias");
            command.Parameters.AddWithValue("@Modulo", plantilla.Modulo);
            command.Parameters.AddWithValue("@EstructuraJson", estructuraJson);
            command.Parameters.AddWithValue("@DatosFijosPdfJson", string.IsNullOrWhiteSpace(datosFijosPdfJson) ? (object)DBNull.Value : datosFijosPdfJson);

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }

        private FormularioPlantillaViewModel CrearPlantillaBaseGuias()
        {
            return new FormularioPlantillaViewModel
            {
                Nombre = "Solicitud de Guía",
                Descripcion = "Plantilla base para capturar solicitudes de guías y envíos con los mismos datos del modal original de Guías.",
                Categoria = "Guias",
                Modulo = "Logistica",
                Activo = true,
                EsPlantillaBase = true,
                DatosOficiales = new FormularioDatosOficialesViewModel
                {
                    Area = "Logística",
                    Pagina = "1 de 1",
                    Codigo = "GUIA-BASE"
                },
                Campos = new List<FormularioCampoViewModel>
                {
                    new() { Clave = "empresa_solicitante", Etiqueta = "Empresa solicitante", Tipo = "texto", Obligatorio = true, Copiable = true, Placeholder = "Razón social o empresa solicitante" },
                    new() { Clave = "cliente_proyecto", Etiqueta = "Nombre del cliente / proyecto", Tipo = "texto", Obligatorio = true, Copiable = true, Placeholder = "Cliente o proyecto relacionado" },
                    new() { Clave = "quien_gestiona", Etiqueta = "¿Quién gestiona la recolección / entrega?", Tipo = "select", Obligatorio = true, Copiable = true, Opciones = new List<string> { "Logística", "Usuario" } },
                    new() { Clave = "tipo_requerimiento", Etiqueta = "Tipo de requerimiento", Tipo = "select", Obligatorio = true, Copiable = true, Opciones = new List<string> { "Solicitud de guía", "Recolección", "Entrega", "Cambio de guía" } },
                    new() { Clave = "fecha_envio", Etiqueta = "Fecha de envío", Tipo = "fecha", Obligatorio = true, Copiable = true },

                    new() { Clave = "tipo_entrega", Etiqueta = "Tipo de entrega", Tipo = "select", Obligatorio = true, Copiable = true, Opciones = new List<string> { "Ocurre", "Domicilio", "Recolección" } },
                    new() { Clave = "remitente_nombre", Etiqueta = "Nombre del remitente", Tipo = "texto", Obligatorio = true, Copiable = true },
                    new() { Clave = "remitente_telefono", Etiqueta = "Teléfono del remitente", Tipo = "texto", Obligatorio = false, Copiable = true },
                    new() { Clave = "direccion_remitente_tipo", Etiqueta = "Tipo de dirección del remitente", Tipo = "select", Obligatorio = false, Copiable = true, Opciones = new List<string> { "Empresa", "Sucursal", "Domicilio", "Otra" } },
                    new() { Clave = "origen", Etiqueta = "Dirección de origen", Tipo = "texto", Obligatorio = false, Copiable = true },
                    new() { Clave = "codigo_postal_origen", Etiqueta = "Código postal origen", Tipo = "texto", Obligatorio = false, Copiable = true },

                    new() { Clave = "destinatario_nombre", Etiqueta = "Nombre destinatario", Tipo = "texto", Obligatorio = true, Copiable = true },
                    new() { Clave = "destinatario_telefono", Etiqueta = "Contacto del destinatario", Tipo = "texto", Obligatorio = true, Copiable = true },
                    new() { Clave = "destinatario_correo", Etiqueta = "Correo electrónico del destinatario", Tipo = "texto", Obligatorio = false, Copiable = true },
                    new() { Clave = "codigo_postal_destino", Etiqueta = "Código postal destino", Tipo = "texto", Obligatorio = true, Copiable = true },
                    new() { Clave = "destino", Etiqueta = "Dirección del destino", Tipo = "texto", Obligatorio = true, Copiable = true },

                    new() { Clave = "tipo_envio", Etiqueta = "Tipo de envío", Tipo = "select", Obligatorio = true, Copiable = true, Opciones = new List<string> { "Paquete", "Sobre", "Tarima", "Caja", "Otro" } },
                    new() { Clave = "contenido_declarado", Etiqueta = "Contenido declarado", Tipo = "texto", Obligatorio = true, Copiable = true },
                    new() { Clave = "informacion_dimensiones_peso", Etiqueta = "¿Cuenta con dimensiones y peso?", Tipo = "select", Obligatorio = false, Copiable = true, Opciones = new List<string> { "Sí", "No", "Pendiente" } },
                    new() { Clave = "peso_kg", Etiqueta = "Peso kg", Tipo = "numero", Obligatorio = false, Copiable = true },
                    new() { Clave = "largo_cm", Etiqueta = "Largo cm", Tipo = "numero", Obligatorio = false, Copiable = true },
                    new() { Clave = "alto_cm", Etiqueta = "Alto cm", Tipo = "numero", Obligatorio = false, Copiable = true },
                    new() { Clave = "ancho_cm", Etiqueta = "Ancho cm", Tipo = "numero", Obligatorio = false, Copiable = true },
                    new() { Clave = "requiere_cadena_frio", Etiqueta = "Requiere cadena de frío", Tipo = "select", Obligatorio = false, Copiable = true, Opciones = new List<string> { "No", "Sí" } },
                    new() { Clave = "costo", Etiqueta = "Costo", Tipo = "numero", Obligatorio = false, Copiable = false },
                    new() { Clave = "observaciones", Etiqueta = "Observaciones", Tipo = "textarea", Obligatorio = false, Copiable = true }
                }
            };
        }

        public async Task<List<FormularioPlantillaViewModel>> ObtenerPlantillasAsync(
            string modulo = "Logistica",
            string? origenModulo = null,
            bool incluirInactivos = false)
        {
            var lista = new List<FormularioPlantillaViewModel>();
            var categoriaFiltro = NormalizarCategoria(origenModulo);

            var sql = @"
                SELECT
                    IdFormulario,
                    Nombre,
                    Descripcion,
                    Categoria,
                    Modulo,
                    EstructuraJson,
                    DatosFijosPdfJson,
                    Activo,
                    EsPlantillaBase,
                    FechaCreacion,
                    FechaActualizacion
                FROM dbo.FormularioPlantillas
                WHERE Modulo = @Modulo
                  AND (@IncluirInactivos = 1 OR Activo = 1)
                  AND (
                        @CategoriaFiltro IS NULL
                        OR Categoria = @CategoriaFiltro
                        OR Categoria = 'Ambos'
                      )
                ORDER BY Activo DESC, EsPlantillaBase DESC, FechaCreacion DESC;";

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@Modulo", string.IsNullOrWhiteSpace(modulo) ? "Logistica" : modulo.Trim());
            command.Parameters.AddWithValue("@IncluirInactivos", incluirInactivos ? 1 : 0);
            command.Parameters.AddWithValue("@CategoriaFiltro", (object?)categoriaFiltro ?? DBNull.Value);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(MapearPlantilla(reader));
            }

            return lista;
        }

        public async Task<FormularioPlantillaViewModel?> ObtenerPlantillaPorIdAsync(
            int idFormulario,
            bool incluirInactiva = false)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                SELECT
                    IdFormulario,
                    Nombre,
                    Descripcion,
                    Categoria,
                    Modulo,
                    EstructuraJson,
                    DatosFijosPdfJson,
                    Activo,
                    EsPlantillaBase,
                    FechaCreacion,
                    FechaActualizacion
                FROM dbo.FormularioPlantillas
                WHERE IdFormulario = @IdFormulario
                  AND (@IncluirInactiva = 1 OR Activo = 1);
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", idFormulario);
            command.Parameters.AddWithValue("@IncluirInactiva", incluirInactiva ? 1 : 0);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return MapearPlantilla(reader);
        }

        public async Task<int> CrearPlantillaAsync(FormularioPlantillaViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Nombre))
                throw new ArgumentException("El nombre del formulario es obligatorio.");

            if (model.Campos == null || model.Campos.Count == 0)
                throw new ArgumentException("El formulario debe tener al menos un campo.");

            model.Categoria = NormalizarCategoria(model.Categoria) ?? "Ambos";
            model.Modulo = "Logistica";
            model.EsPlantillaBase = false;

            NormalizarCampos(model.Campos);

            var estructuraJson = JsonSerializer.Serialize(model.Campos, _jsonOptions);
            var datosFijosPdfJson = SerializarDatosOficiales(model);

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                INSERT INTO dbo.FormularioPlantillas
                (
                    Nombre,
                    Descripcion,
                    Categoria,
                    Modulo,
                    EstructuraJson,
                    DatosFijosPdfJson,
                    Activo,
                    EsPlantillaBase
                )
                OUTPUT INSERTED.IdFormulario
                VALUES
                (
                    @Nombre,
                    @Descripcion,
                    @Categoria,
                    @Modulo,
                    @EstructuraJson,
                    @DatosFijosPdfJson,
                    1,
                    @EsPlantillaBase
                );
            ", connection);

            command.Parameters.AddWithValue("@Nombre", model.Nombre.Trim());
            command.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(model.Descripcion) ? (object)DBNull.Value : model.Descripcion.Trim());
            command.Parameters.AddWithValue("@Categoria", model.Categoria);
            command.Parameters.AddWithValue("@Modulo", model.Modulo);
            command.Parameters.AddWithValue("@EstructuraJson", estructuraJson);
            command.Parameters.AddWithValue("@DatosFijosPdfJson", string.IsNullOrWhiteSpace(datosFijosPdfJson) ? (object)DBNull.Value : datosFijosPdfJson);
            command.Parameters.AddWithValue("@EsPlantillaBase", model.EsPlantillaBase);

            await connection.OpenAsync();

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task ActualizarPlantillaAsync(FormularioPlantillaViewModel model)
        {
            if (model.IdFormulario <= 0)
                throw new ArgumentException("IdFormulario inválido.");

            if (string.IsNullOrWhiteSpace(model.Nombre))
                throw new ArgumentException("El nombre del formulario es obligatorio.");

            if (model.Campos == null || model.Campos.Count == 0)
                throw new ArgumentException("El formulario debe tener al menos un campo.");

            model.Categoria = NormalizarCategoria(model.Categoria) ?? "Ambos";
            model.Modulo = "Logistica";
            model.EsPlantillaBase = false;

            NormalizarCampos(model.Campos);

            var estructuraJson = JsonSerializer.Serialize(model.Campos, _jsonOptions);
            var datosFijosPdfJson = SerializarDatosOficiales(model);

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                UPDATE dbo.FormularioPlantillas
                SET
                    Nombre = @Nombre,
                    Descripcion = @Descripcion,
                    Categoria = @Categoria,
                    Modulo = @Modulo,
                    EstructuraJson = @EstructuraJson,
                    DatosFijosPdfJson = @DatosFijosPdfJson,
                    Activo = @Activo,
                    EsPlantillaBase = @EsPlantillaBase,
                    FechaActualizacion = SYSDATETIME()
                WHERE IdFormulario = @IdFormulario;
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", model.IdFormulario);
            command.Parameters.AddWithValue("@Nombre", model.Nombre.Trim());
            command.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(model.Descripcion) ? (object)DBNull.Value : model.Descripcion.Trim());
            command.Parameters.AddWithValue("@Categoria", model.Categoria);
            command.Parameters.AddWithValue("@Modulo", model.Modulo);
            command.Parameters.AddWithValue("@EstructuraJson", estructuraJson);
            command.Parameters.AddWithValue("@DatosFijosPdfJson", string.IsNullOrWhiteSpace(datosFijosPdfJson) ? (object)DBNull.Value : datosFijosPdfJson);
            command.Parameters.AddWithValue("@Activo", model.Activo);
            command.Parameters.AddWithValue("@EsPlantillaBase", model.EsPlantillaBase);

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }

        public async Task DesactivarPlantillaAsync(int idFormulario)
        {
            if (idFormulario <= 0)
                throw new ArgumentException("IdFormulario inválido.");

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                UPDATE dbo.FormularioPlantillas
                SET
                    Activo = 0,
                    FechaActualizacion = SYSDATETIME()
                WHERE IdFormulario = @IdFormulario;
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", idFormulario);

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }


        public async Task ReactivarPlantillaAsync(int idFormulario)
        {
            if (idFormulario <= 0)
                throw new ArgumentException("IdFormulario inválido.");

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                UPDATE dbo.FormularioPlantillas
                SET
                    Activo = 1,
                    FechaActualizacion = SYSDATETIME()
                WHERE IdFormulario = @IdFormulario;
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", idFormulario);

            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> GuardarRespuestaAsync(FormularioRespuestaViewModel model)
        {
            if (model.IdFormulario <= 0)
                throw new ArgumentException("IdFormulario inválido.");

            if (model.UsuarioID <= 0)
                throw new ArgumentException("UsuarioID inválido.");

            var datosJson = SerializarValoresFormulario(
                model.Valores ?? new Dictionary<string, string?>()
            );

            var datosPdfJson = datosJson;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                INSERT INTO dbo.FormularioRespuestas
                (
                    IdFormulario,
                    UsuarioID,
                    Estado,
                    DatosJson,
                    DatosPdfJson,
                    RespuestaOrigenID,
                    OrigenTipo,
                    OrigenID
                )
                OUTPUT INSERTED.IdRespuesta
                VALUES
                (
                    @IdFormulario,
                    @UsuarioID,
                    @Estado,
                    @DatosJson,
                    @DatosPdfJson,
                    @RespuestaOrigenID,
                    @OrigenTipo,
                    @OrigenID
                );
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", model.IdFormulario);
            command.Parameters.AddWithValue("@UsuarioID", model.UsuarioID);
            command.Parameters.AddWithValue("@Estado", string.IsNullOrWhiteSpace(model.Estado) ? "Registrado" : model.Estado);
            command.Parameters.AddWithValue("@DatosJson", datosJson);
            command.Parameters.AddWithValue("@DatosPdfJson", datosPdfJson);
            command.Parameters.AddWithValue("@RespuestaOrigenID", (object?)model.RespuestaOrigenID ?? DBNull.Value);
            command.Parameters.AddWithValue("@OrigenTipo", string.IsNullOrWhiteSpace(model.OrigenTipo) ? (object)DBNull.Value : model.OrigenTipo);
            command.Parameters.AddWithValue("@OrigenID", (object?)model.OrigenID ?? DBNull.Value);

            await connection.OpenAsync();

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<Dictionary<string, string?>> ObtenerValoresRespuestaAsync(int idRespuesta)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                SELECT DatosJson
                FROM dbo.FormularioRespuestas
                WHERE IdRespuesta = @IdRespuesta
                  AND EstaBorrado = 0;
            ", connection);

            command.Parameters.AddWithValue("@IdRespuesta", idRespuesta);

            await connection.OpenAsync();

            var datosJson = await command.ExecuteScalarAsync();

            if (datosJson == null || datosJson == DBNull.Value)
                return new Dictionary<string, string?>();

            return DeserializarValoresFormulario(datosJson.ToString() ?? "{}");
        }

        public async Task<FormularioLlenadoViewModel?> PrepararLlenadoAsync(int idFormulario)
        {
            var plantilla = await ObtenerPlantillaPorIdAsync(idFormulario);

            if (plantilla == null)
                return null;

            var valores = new Dictionary<string, string?>();

            foreach (var campo in plantilla.Campos)
            {
                valores[campo.Clave] = null;
            }

            return new FormularioLlenadoViewModel
            {
                IdFormulario = plantilla.IdFormulario,
                NombreFormulario = plantilla.Nombre,
                Descripcion = plantilla.Descripcion,
                Categoria = plantilla.Categoria,
                DatosFijosPdfJson = plantilla.DatosFijosPdfJson,
                DatosOficiales = plantilla.DatosOficiales,
                Campos = plantilla.Campos,
                Valores = valores
            };
        }

        public async Task<FormularioLlenadoViewModel?> PrepararLlenadoDesdeRespuestaAsync(
            int idFormularioDestino,
            int idRespuestaOrigen)
        {
            var plantillaDestino = await ObtenerPlantillaPorIdAsync(idFormularioDestino);

            if (plantillaDestino == null)
                return null;

            var valoresOrigen = await ObtenerValoresRespuestaAsync(idRespuestaOrigen);

            var valoresIniciales = new Dictionary<string, string?>();

            foreach (var campo in plantillaDestino.Campos)
            {
                if (campo.Copiable && valoresOrigen.ContainsKey(campo.Clave))
                {
                    valoresIniciales[campo.Clave] = valoresOrigen[campo.Clave];
                }
                else
                {
                    valoresIniciales[campo.Clave] = null;
                }
            }

            return new FormularioLlenadoViewModel
            {
                IdFormulario = plantillaDestino.IdFormulario,
                NombreFormulario = plantillaDestino.Nombre,
                Descripcion = plantillaDestino.Descripcion,
                Categoria = plantillaDestino.Categoria,
                DatosFijosPdfJson = plantillaDestino.DatosFijosPdfJson,
                DatosOficiales = plantillaDestino.DatosOficiales,
                Campos = plantillaDestino.Campos,
                Valores = valoresIniciales,
                RespuestaOrigenID = idRespuestaOrigen
            };
        }

        public async Task<List<FormularioRespuestaResumenViewModel>> ObtenerRespuestasResumenAsync()
        {
            var lista = new List<FormularioRespuestaResumenViewModel>();

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                SELECT
                    IdRespuesta,
                    IdFormulario,
                    NombreFormulario,
                    Categoria,
                    Modulo,
                    UsuarioID,
                    Folio,
                    Factura,
                    Cliente,
                    Proyecto,
                    Solicitante,
                    CompaniaSolicitante,
                    Departamento,
                    CentroCosto,
                    FechaCarga,
                    HorarioCarga,
                    DireccionRecoleccion,
                    DestinoPrincipal,
                    Fletero,
                    CostoFlete,
                    Estado,
                    OrigenTipo,
                    OrigenID,
                    RespuestaOrigenID,
                    FechaRegistro,
                    FechaActualizacion
                FROM dbo.vw_FormularioRespuestasResumen
                ORDER BY FechaRegistro DESC;
            ", connection);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new FormularioRespuestaResumenViewModel
                {
                    IdRespuesta = Convert.ToInt32(reader["IdRespuesta"]),
                    IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                    NombreFormulario = reader["NombreFormulario"]?.ToString() ?? string.Empty,
                    Categoria = reader["Categoria"] == DBNull.Value ? null : reader["Categoria"]?.ToString(),
                    Modulo = reader["Modulo"]?.ToString() ?? "Logistica",
                    UsuarioID = Convert.ToInt32(reader["UsuarioID"]),

                    Folio = reader["Folio"] == DBNull.Value ? null : reader["Folio"]?.ToString(),
                    Factura = reader["Factura"] == DBNull.Value ? null : reader["Factura"]?.ToString(),
                    Cliente = reader["Cliente"] == DBNull.Value ? null : reader["Cliente"]?.ToString(),
                    Proyecto = reader["Proyecto"] == DBNull.Value ? null : reader["Proyecto"]?.ToString(),
                    Solicitante = reader["Solicitante"] == DBNull.Value ? null : reader["Solicitante"]?.ToString(),
                    CompaniaSolicitante = reader["CompaniaSolicitante"] == DBNull.Value ? null : reader["CompaniaSolicitante"]?.ToString(),
                    Departamento = reader["Departamento"] == DBNull.Value ? null : reader["Departamento"]?.ToString(),
                    CentroCosto = reader["CentroCosto"] == DBNull.Value ? null : reader["CentroCosto"]?.ToString(),
                    FechaCarga = reader["FechaCarga"] == DBNull.Value ? null : reader["FechaCarga"]?.ToString(),
                    HorarioCarga = reader["HorarioCarga"] == DBNull.Value ? null : reader["HorarioCarga"]?.ToString(),
                    DireccionRecoleccion = reader["DireccionRecoleccion"] == DBNull.Value ? null : reader["DireccionRecoleccion"]?.ToString(),
                    DestinoPrincipal = reader["DestinoPrincipal"] == DBNull.Value ? null : reader["DestinoPrincipal"]?.ToString(),
                    Fletero = reader["Fletero"] == DBNull.Value ? null : reader["Fletero"]?.ToString(),
                    CostoFlete = reader["CostoFlete"] == DBNull.Value ? null : reader["CostoFlete"]?.ToString(),

                    Estado = reader["Estado"]?.ToString() ?? "Registrado",
                    OrigenTipo = reader["OrigenTipo"] == DBNull.Value ? null : reader["OrigenTipo"]?.ToString(),
                    OrigenID = reader["OrigenID"] == DBNull.Value ? null : Convert.ToInt32(reader["OrigenID"]),
                    RespuestaOrigenID = reader["RespuestaOrigenID"] == DBNull.Value ? null : Convert.ToInt32(reader["RespuestaOrigenID"]),
                    FechaRegistro = Convert.ToDateTime(reader["FechaRegistro"]),
                    FechaActualizacion = reader["FechaActualizacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["FechaActualizacion"])
                });
            }

            return lista;
        }

        public async Task<bool> UsuarioPerteneceALogisticaAsync(int usuarioId)
        {
            if (usuarioId <= 0)
                return false;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var tablasBaseExisten = (await ExisteTablaAsync(connection, "Usuarios"))
                && (await ExisteTablaAsync(connection, "Roles"));

            if (tablasBaseExisten)
            {
                await using var commandRol = new SqlCommand(@"
                    SELECT CAST(CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.Usuarios u
                        INNER JOIN dbo.Roles r ON r.RolID = u.RolID
                        WHERE u.UsuarioID = @UsuarioID
                          AND (
                                UPPER(LTRIM(RTRIM(r.NombreRol))) LIKE '%ADMIN%'
                                OR UPPER(LTRIM(RTRIM(r.NombreRol))) LIKE '%LOGISTICA%'
                                OR UPPER(LTRIM(RTRIM(r.NombreRol))) LIKE '%LOGÍSTICA%'
                              )
                    ) THEN 1 ELSE 0 END AS bit);
                ", connection);

                commandRol.Parameters.AddWithValue("@UsuarioID", usuarioId);

                var rolResult = await commandRol.ExecuteScalarAsync();
                if (rolResult is bool rolOk && rolOk)
                    return true;
            }

            var tablasDepartamentoExisten = (await ExisteTablaAsync(connection, "EmpleadoDepartamentos"))
                && (await ExisteTablaAsync(connection, "Departamentos"));

            if (tablasDepartamentoExisten)
            {
                await using var commandDepartamento = new SqlCommand(@"
                    SELECT CAST(CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.EmpleadoDepartamentos ed
                        INNER JOIN dbo.Departamentos d ON d.DepartamentoID = ed.DepartamentoID
                        WHERE ed.UsuarioID = @UsuarioID
                          AND (
                                UPPER(LTRIM(RTRIM(d.NombreDepartamento))) LIKE '%LOGISTICA%'
                                OR UPPER(LTRIM(RTRIM(d.NombreDepartamento))) LIKE '%LOGÍSTICA%'
                              )
                          AND (d.Activo = 1 OR d.Activo IS NULL)
                    ) THEN 1 ELSE 0 END AS bit);
                ", connection);

                commandDepartamento.Parameters.AddWithValue("@UsuarioID", usuarioId);

                var depResult = await commandDepartamento.ExecuteScalarAsync();
                if (depResult is bool depOk && depOk)
                    return true;
            }

            return false;
        }

        private async Task<bool> ExisteTablaAsync(SqlConnection connection, string tableName)
        {
            await using var command = new SqlCommand(@"
                SELECT CAST(CASE WHEN OBJECT_ID(@Tabla, 'U') IS NOT NULL THEN 1 ELSE 0 END AS bit);
            ", connection);

            command.Parameters.AddWithValue("@Tabla", $"dbo.{tableName}");
            var result = await command.ExecuteScalarAsync();

            return result is bool existe && existe;
        }

        private FormularioPlantillaViewModel MapearPlantilla(SqlDataReader reader)
        {
            var estructuraJson = reader["EstructuraJson"]?.ToString() ?? "[]";
            var datosFijosPdfJson = reader["DatosFijosPdfJson"] == DBNull.Value
                ? null
                : reader["DatosFijosPdfJson"]?.ToString();

            var campos = DeserializarCampos(estructuraJson);
            foreach (var campo in campos)
            {
                campo.OpcionesTexto = campo.Opciones == null || campo.Opciones.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, campo.Opciones);
            }

            return new FormularioPlantillaViewModel
            {
                IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                Nombre = reader["Nombre"]?.ToString() ?? "",
                Descripcion = reader["Descripcion"] == DBNull.Value ? null : reader["Descripcion"]?.ToString(),
                Categoria = reader["Categoria"] == DBNull.Value ? null : reader["Categoria"]?.ToString(),
                Modulo = reader["Modulo"]?.ToString() ?? "Logistica",
                DatosFijosPdfJson = datosFijosPdfJson,
                DatosOficiales = FormularioDatosOficialesViewModel.FromJson(datosFijosPdfJson),
                Activo = Convert.ToBoolean(reader["Activo"]),
                EsPlantillaBase = Convert.ToBoolean(reader["EsPlantillaBase"]),
                FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                FechaActualizacion = reader["FechaActualizacion"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(reader["FechaActualizacion"]),
                Campos = campos
            };
        }

        private string? SerializarDatosOficiales(FormularioPlantillaViewModel model)
        {
            if (model.DatosOficiales == null)
            {
                if (string.IsNullOrWhiteSpace(model.DatosFijosPdfJson))
                    return null;

                return model.DatosFijosPdfJson;
            }

            if (!model.DatosOficiales.TieneDatos())
                return null;

            return JsonSerializer.Serialize(model.DatosOficiales, _jsonOptions);
        }

        private List<FormularioCampoViewModel> DeserializarCampos(string estructuraJson)
        {
            try
            {
                return JsonSerializer.Deserialize<List<FormularioCampoViewModel>>(
                    estructuraJson,
                    _jsonOptions
                ) ?? new List<FormularioCampoViewModel>();
            }
            catch
            {
                return new List<FormularioCampoViewModel>();
            }
        }

        private void NormalizarCampos(List<FormularioCampoViewModel> campos)
        {
            foreach (var campo in campos)
            {
                campo.Clave = NormalizarClave(campo.Clave);

                if (string.IsNullOrWhiteSpace(campo.Clave))
                {
                    campo.Clave = NormalizarClave(campo.Etiqueta);
                }

                campo.Etiqueta = string.IsNullOrWhiteSpace(campo.Etiqueta)
                    ? campo.Clave
                    : campo.Etiqueta.Trim();

                campo.Tipo = NormalizarTipo(campo.Tipo);

                if (!string.IsNullOrWhiteSpace(campo.OpcionesTexto))
                {
                    campo.Opciones = campo.OpcionesTexto
                        .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                campo.Opciones ??= new List<string>();
            }
        }

        private string NormalizarTipo(string? tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo))
                return "texto";

            var t = tipo.Trim().ToLowerInvariant();

            return t switch
            {
                "text" => "texto",
                "texto corto" => "texto",
                "texto_largo" => "textarea",
                "texto largo" => "textarea",
                "lista" => "select",
                "lista desplegable" => "select",
                "number" => "numero",
                "date" => "fecha",
                _ => t
            };
        }

        private string NormalizarClave(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return string.Empty;

            var limpio = valor.Trim().ToLowerInvariant();

            limpio = limpio
                .Replace("á", "a")
                .Replace("é", "e")
                .Replace("í", "i")
                .Replace("ó", "o")
                .Replace("ú", "u")
                .Replace("ü", "u")
                .Replace("ñ", "n");

            var chars = limpio.Select(c =>
                char.IsLetterOrDigit(c) ? c : '_'
            ).ToArray();

            limpio = new string(chars);

            while (limpio.Contains("__"))
                limpio = limpio.Replace("__", "_");

            return limpio.Trim('_');
        }

        private string? NormalizarCategoria(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var limpio = valor.Trim();

            if (limpio.Equals("Transportes", StringComparison.OrdinalIgnoreCase))
                return "Transporte";

            if (limpio.Equals("Transporte", StringComparison.OrdinalIgnoreCase))
                return "Transporte";

            if (limpio.Equals("Guia", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guía", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guias", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Guías", StringComparison.OrdinalIgnoreCase))
                return "Guias";

            if (limpio.Equals("Ambos", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("General", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Logistica", StringComparison.OrdinalIgnoreCase)
                || limpio.Equals("Logística", StringComparison.OrdinalIgnoreCase))
                return "Ambos";

            return limpio;
        }

        private string SerializarValoresFormulario(Dictionary<string, string?> valores)
        {
            var resultado = new Dictionary<string, object?>();

            foreach (var item in valores)
            {
                var clave = item.Key;
                var valor = item.Value;

                if (string.IsNullOrWhiteSpace(valor))
                {
                    resultado[clave] = null;
                    continue;
                }

                var valorLimpio = valor.Trim();

                if (valorLimpio.StartsWith("[") || valorLimpio.StartsWith("{"))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(valorLimpio);
                        resultado[clave] = document.RootElement.Clone();
                        continue;
                    }
                    catch
                    {
                        resultado[clave] = valor;
                    }
                }
                else
                {
                    resultado[clave] = valor;
                }
            }

            return JsonSerializer.Serialize(resultado, _jsonOptions);
        }

        private Dictionary<string, string?> DeserializarValoresFormulario(string datosJson)
        {
            var resultado = new Dictionary<string, string?>();

            if (string.IsNullOrWhiteSpace(datosJson))
                return resultado;

            try
            {
                using var document = JsonDocument.Parse(datosJson);

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    resultado[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Array => property.Value.GetRawText(),
                        JsonValueKind.Object => property.Value.GetRawText(),
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch
            {
                return new Dictionary<string, string?>();
            }

            return resultado;
        }
    }
}
