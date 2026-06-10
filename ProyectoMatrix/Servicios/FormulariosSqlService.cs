using Microsoft.Data.SqlClient;
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

        public async Task<List<FormularioPlantillaViewModel>> ObtenerPlantillasAsync(string modulo = "Logistica")
        {
            var lista = new List<FormularioPlantillaViewModel>();

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
                WHERE Activo = 1
                  AND Modulo = @Modulo
                ORDER BY EsPlantillaBase DESC, FechaCreacion DESC;
            ", connection);

            command.Parameters.AddWithValue("@Modulo", modulo);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var estructuraJson = reader["EstructuraJson"]?.ToString() ?? "[]";

                lista.Add(new FormularioPlantillaViewModel
                {
                    IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                    Nombre = reader["Nombre"]?.ToString() ?? "",
                    Descripcion = reader["Descripcion"]?.ToString(),
                    Categoria = reader["Categoria"]?.ToString(),
                    Modulo = reader["Modulo"]?.ToString() ?? "Logistica",
                    DatosFijosPdfJson = reader["DatosFijosPdfJson"]?.ToString(),
                    Activo = Convert.ToBoolean(reader["Activo"]),
                    EsPlantillaBase = Convert.ToBoolean(reader["EsPlantillaBase"]),
                    FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                    FechaActualizacion = reader["FechaActualizacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["FechaActualizacion"]),
                    Campos = DeserializarCampos(estructuraJson)
                });
            }

            return lista;
        }

        public async Task<FormularioPlantillaViewModel?> ObtenerPlantillaPorIdAsync(int idFormulario)
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
                  AND Activo = 1;
            ", connection);

            command.Parameters.AddWithValue("@IdFormulario", idFormulario);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            var estructuraJson = reader["EstructuraJson"]?.ToString() ?? "[]";

            return new FormularioPlantillaViewModel
            {
                IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                Nombre = reader["Nombre"]?.ToString() ?? "",
                Descripcion = reader["Descripcion"]?.ToString(),
                Categoria = reader["Categoria"]?.ToString(),
                Modulo = reader["Modulo"]?.ToString() ?? "Logistica",
                DatosFijosPdfJson = reader["DatosFijosPdfJson"]?.ToString(),
                Activo = Convert.ToBoolean(reader["Activo"]),
                EsPlantillaBase = Convert.ToBoolean(reader["EsPlantillaBase"]),
                FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                FechaActualizacion = reader["FechaActualizacion"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(reader["FechaActualizacion"]),
                Campos = DeserializarCampos(estructuraJson)
            };
        }

        public async Task<int> CrearPlantillaAsync(FormularioPlantillaViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Nombre))
                throw new ArgumentException("El nombre del formulario es obligatorio.");

            if (model.Campos == null || model.Campos.Count == 0)
                throw new ArgumentException("El formulario debe tener al menos un campo.");

            NormalizarCampos(model.Campos);

            var estructuraJson = JsonSerializer.Serialize(model.Campos, _jsonOptions);

            object datosFijosPdfJson = string.IsNullOrWhiteSpace(model.DatosFijosPdfJson)
                ? DBNull.Value
                : model.DatosFijosPdfJson;

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
            command.Parameters.AddWithValue("@Descripcion", (object?)model.Descripcion ?? DBNull.Value);
            command.Parameters.AddWithValue("@Categoria", (object?)model.Categoria ?? DBNull.Value);
            command.Parameters.AddWithValue("@Modulo", string.IsNullOrWhiteSpace(model.Modulo) ? "Logistica" : model.Modulo.Trim());
            command.Parameters.AddWithValue("@EstructuraJson", estructuraJson);
            command.Parameters.AddWithValue("@DatosFijosPdfJson", datosFijosPdfJson);
            command.Parameters.AddWithValue("@EsPlantillaBase", model.EsPlantillaBase);

            await connection.OpenAsync();

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<int> GuardarRespuestaAsync(FormularioRespuestaViewModel model)
        {
            if (model.IdFormulario <= 0)
                throw new ArgumentException("IdFormulario inválido.");

            if (model.UsuarioID <= 0)
                throw new ArgumentException("UsuarioID inválido.");

            var datosJson = JsonSerializer.Serialize(model.Valores ?? new(), _jsonOptions);

            /*
                Por ahora guardamos los mismos valores como DatosPdfJson.
                Esto asegura que no se pierda información para el PDF.
                Después podemos depurar para que solo se impriman claves oficiales.
            */
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
            command.Parameters.AddWithValue("@Estado", string.IsNullOrWhiteSpace(model.Estado) ? "Borrador" : model.Estado);
            command.Parameters.AddWithValue("@DatosJson", datosJson);
            command.Parameters.AddWithValue("@DatosPdfJson", datosPdfJson);
            command.Parameters.AddWithValue("@RespuestaOrigenID", (object?)model.RespuestaOrigenID ?? DBNull.Value);
            command.Parameters.AddWithValue("@OrigenTipo", (object?)model.OrigenTipo ?? DBNull.Value);
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

            return JsonSerializer.Deserialize<Dictionary<string, string?>>(
                datosJson.ToString() ?? "{}",
                _jsonOptions
            ) ?? new Dictionary<string, string?>();
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
                    NombreFormulario = reader["NombreFormulario"]?.ToString() ?? "",
                    Categoria = reader["Categoria"]?.ToString(),
                    Modulo = reader["Modulo"]?.ToString() ?? "Logistica",
                    UsuarioID = Convert.ToInt32(reader["UsuarioID"]),

                    Folio = reader["Folio"]?.ToString(),
                    Factura = reader["Factura"]?.ToString(),
                    Cliente = reader["Cliente"]?.ToString(),
                    Proyecto = reader["Proyecto"]?.ToString(),
                    Solicitante = reader["Solicitante"]?.ToString(),
                    Departamento = reader["Departamento"]?.ToString(),
                    CentroCosto = reader["CentroCosto"]?.ToString(),
                    FechaCarga = reader["FechaCarga"]?.ToString(),
                    HorarioCarga = reader["HorarioCarga"]?.ToString(),
                    DireccionRecoleccion = reader["DireccionRecoleccion"]?.ToString(),
                    DestinoPrincipal = reader["DestinoPrincipal"]?.ToString(),
                    Fletero = reader["Fletero"]?.ToString(),
                    CostoFlete = reader["CostoFlete"]?.ToString(),

                    Estado = reader["Estado"]?.ToString() ?? "Borrador",
                    OrigenTipo = reader["OrigenTipo"]?.ToString(),
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



        public async Task<List<FormularioRespuestaDetalleViewModel>> ObtenerRespuestasPorOrigenAsync(
            string origenTipo,
            int origenId)
        {
            var lista = new List<FormularioRespuestaDetalleViewModel>();

            if (string.IsNullOrWhiteSpace(origenTipo) || origenId <= 0)
                return lista;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(@"
                SELECT
                    r.IdRespuesta,
                    r.IdFormulario,
                    p.Nombre AS NombreFormulario,
                    p.Categoria,
                    p.EstructuraJson,
                    r.DatosJson,
                    r.FechaRegistro
                FROM dbo.FormularioRespuestas r
                INNER JOIN dbo.FormularioPlantillas p
                    ON p.IdFormulario = r.IdFormulario
                WHERE r.EstaBorrado = 0
                  AND r.OrigenTipo = @OrigenTipo
                  AND r.OrigenID = @OrigenID
                ORDER BY r.FechaRegistro DESC;
            ", connection);

            command.Parameters.AddWithValue("@OrigenTipo", origenTipo.Trim());
            command.Parameters.AddWithValue("@OrigenID", origenId);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var estructuraJson = reader["EstructuraJson"]?.ToString() ?? "[]";
                var datosJson = reader["DatosJson"]?.ToString() ?? "{}";

                var camposPlantilla = DeserializarCampos(estructuraJson);
                var valores = DeserializarValoresDetalle(datosJson);

                var detalle = new FormularioRespuestaDetalleViewModel
                {
                    IdRespuesta = Convert.ToInt32(reader["IdRespuesta"]),
                    IdFormulario = Convert.ToInt32(reader["IdFormulario"]),
                    NombreFormulario = reader["NombreFormulario"]?.ToString() ?? string.Empty,
                    Categoria = reader["Categoria"]?.ToString(),
                    FechaRegistro = Convert.ToDateTime(reader["FechaRegistro"]),
                    Campos = camposPlantilla.Select(c => new FormularioRespuestaCampoValorViewModel
                    {
                        Clave = c.Clave,
                        Etiqueta = string.IsNullOrWhiteSpace(c.Etiqueta) ? c.Clave : c.Etiqueta,
                        Tipo = string.IsNullOrWhiteSpace(c.Tipo) ? "texto" : c.Tipo,
                        Valor = valores.TryGetValue(c.Clave, out var valor) ? valor : null
                    }).ToList()
                };

                lista.Add(detalle);
            }

            return lista;
        }

        private Dictionary<string, string?> DeserializarValoresDetalle(string datosJson)
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
                        JsonValueKind.True => "Sí",
                        JsonValueKind.False => "No",
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

                campo.Tipo = string.IsNullOrWhiteSpace(campo.Tipo)
                    ? "texto"
                    : campo.Tipo.Trim().ToLower();
            }
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
                .Replace("ñ", "n");

            var chars = limpio.Select(c =>
                char.IsLetterOrDigit(c) ? c : '_'
            ).ToArray();

            limpio = new string(chars);

            while (limpio.Contains("__"))
                limpio = limpio.Replace("__", "_");

            return limpio.Trim('_');
        }
    }
}