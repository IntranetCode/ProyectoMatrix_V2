using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProyectoMatrix.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;
using System.Linq;
using System.Security.Claims;

namespace ProyectoMatrix.Controllers
{
    [Route("[controller]")]
    public class OperacionesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OperacionesController> _logger;
        private readonly IConfiguration _configuration; 
        private List<int>? _misPermisosCache;
        private const int PermisoHistorial = 33;
        private const int PermisoConfigurar = 31;
        private const int PermisoGestor = 32;
        private const int PermisoCapturar = 34;

        public OperacionesController(ILogger<OperacionesController> logger, ApplicationDbContext context, IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
        }

        #region 1. TABLERO Y GRÁFICAS (DASHBOARD)
        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return RedirectToAction("Login", "Login");

            CargarPermisosViewBag();

            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            var departamentosQuery = _context.Departamentos
                .Where(d => d.Activo && _context.RegistroKpis.Any(r => r.Metrica.DepartamentoID == d.DepartamentoID && r.Activo));

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    departamentosQuery = departamentosQuery.Where(d => datosAcceso.Departamentos.Contains(d.DepartamentoID));
                else
                    departamentosQuery = departamentosQuery.Where(d => false);
            }

            var departamentosConDatos = departamentosQuery
                .OrderBy(d => d.NombreDepartamento)
                .ToList();

            ViewBag.PuedeGestionarTodosDepartamentos = puedeGestionarTodos;
            ViewBag.EsUsuarioLimitado = !puedeGestionarTodos;

            return View(departamentosConDatos);
        }

        // --- GRÁFICA GENERAL (TASA DE CUMPLIMIENTO BI) ---
        [HttpGet("ObtenerDatosGrafica")]
        public JsonResult ObtenerDatosGrafica(string filtroTiempo = "anio", int? mes = null, int? semana = null, string? dia = null)
        {
            var query = _context.RegistroKpis
                .Include(r => r.DetallesValores).ThenInclude(v => v.Variable)
                .Include(r => r.Metrica).ThenInclude(m => m.Departamento)
                .Where(r => r.Activo == true && r.Metrica.MetaEsperada != null && r.Metrica.SentidoMeta != null);

            int usuarioId = ObtenerUsuarioIdActual();
            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    query = query.Where(r => datosAcceso.Departamentos.Contains(r.Metrica.DepartamentoID));
                else
                    query = query.Where(r => false);
            }

            int anioActual = DateTime.Now.Year;
            int mesActual = mes ?? DateTime.Now.Month;
            int semanaActual = semana ?? ObtenerSemanaDelAno(DateTime.Now);

            if (filtroTiempo != "historico")
                query = query.Where(r => r.Anio == anioActual);

            var registrosEnPeriodo = query.ToList();

            if (filtroTiempo == "mes")
            {
                var inicioMes = new DateTime(anioActual, mesActual, 1).DayOfYear;
                var finMes = new DateTime(anioActual, mesActual, DateTime.DaysInMonth(anioActual, mesActual)).DayOfYear;

                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo == mesActual) ||
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo >= ((mesActual - 1) * 4) && r.NumeroPeriodo <= (mesActual * 4)) ||
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo >= inicioMes && r.NumeroPeriodo <= finMes)
                ).ToList();
            }
            else if (filtroTiempo == "semana")
            {
                var rangoDias = ObtenerRangoDiasDelAnoPorSemana(semanaActual);
                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo == semanaActual) ||
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo == ((semanaActual - 1) / 4) + 1) ||
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo >= rangoDias.inicio && r.NumeroPeriodo <= rangoDias.fin)
                ).ToList();
            }

            var datos = registrosEnPeriodo
                .GroupBy(r => r.Metrica.Departamento.NombreDepartamento)
                .Select(gDepto =>
                {
                    var kpisEvaluados = gDepto.GroupBy(r => r.Metrica).Select(gKpi =>
                    {
                        var metrica = gKpi.Key;
                        decimal promedioActual = 0;

                        var primeraVariableId = gKpi.SelectMany(r => r.DetallesValores)
                            .Where(v => v.Variable.EsLinea == false)
                            .Select(v => v.VariableID)
                            .FirstOrDefault();

                        var valoresValidos = gKpi.SelectMany(r => r.DetallesValores)
                            .Where(v => v.VariableID == primeraVariableId)
                            .Select(v => v.Valor)
                            .ToList();

                        if (valoresValidos.Any())
                            promedioActual = valoresValidos.Average();

                        decimal meta = metrica.MetaEsperada ?? 0;
                        bool cumple = false;

                        if (metrica.SentidoMeta == "MayorMejor") cumple = promedioActual >= meta;
                        else if (metrica.SentidoMeta == "MenorMejor") cumple = promedioActual <= meta;
                        else if (metrica.SentidoMeta == "Exacto") cumple = Math.Round(promedioActual, 2) == Math.Round(meta, 2);

                        return new
                        {
                            nombreKpi = metrica.NombreMetrica,
                            promedioActual = Math.Round(promedioActual, 2),
                            metaEsperada = meta,
                            cumple = cumple
                        };
                    }).ToList();

                    int totalKpis = kpisEvaluados.Count;
                    int kpisCumplidos = kpisEvaluados.Count(k => k.cumple);
                    decimal porcentajeCumplimiento = totalKpis > 0 ? Math.Round((decimal)kpisCumplidos / totalKpis * 100, 2) : 0;

                    return new
                    {
                        departamento = gDepto.Key,
                        valor = porcentajeCumplimiento,
                        totalKpis = totalKpis,
                        cumplidos = kpisCumplidos,
                        detalle = kpisEvaluados
                    };
                }).ToList();

            return Json(datos);
        }

        // --- TABLERO POR ÁREA (FILTRO CORREGIDO POR PERIODO REAL) ---
        [HttpGet("ObtenerTableroDepartamento")]
        public JsonResult ObtenerTableroDepartamento(int idDepartamento, string filtroTiempo = "anio", int? mes = null, int? semana = null, string? dia = null)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0 || !UsuarioPuedeAccederDepartamento(usuarioId, idDepartamento))
                return Json(new List<object>());

            var query = _context.RegistroKpis
                .Include(r => r.DetallesValores).ThenInclude(v => v.Variable)
                .Include(r => r.Metrica)
                .Where(r => r.Metrica.DepartamentoID == idDepartamento && r.Activo == true);

            int anioActual = DateTime.Now.Year;
            int mesActual = mes ?? DateTime.Now.Month;
            int semanaActual = semana ?? ObtenerSemanaDelAno(DateTime.Now);
            DateTime fechaSeleccionada;
            int diaDelAno = 0;

            if (!string.IsNullOrEmpty(dia) && DateTime.TryParse(dia, out fechaSeleccionada))
                diaDelAno = fechaSeleccionada.DayOfYear;

            if (filtroTiempo != "historico")
                query = query.Where(r => r.Anio == anioActual);

            var registrosEnPeriodo = query
                .OrderBy(r => r.Anio)
                .ThenBy(r => r.NumeroPeriodo)
                .ToList();

            if (filtroTiempo == "mes")
            {
                var inicioMes = new DateTime(anioActual, mesActual, 1).DayOfYear;
                var finMes = new DateTime(anioActual, mesActual, DateTime.DaysInMonth(anioActual, mesActual)).DayOfYear;

                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo == mesActual) ||
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo >= ((mesActual - 1) * 4) && r.NumeroPeriodo <= (mesActual * 4)) ||
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo >= inicioMes && r.NumeroPeriodo <= finMes)
                ).ToList();
            }
            else if (filtroTiempo == "trimestre")
            {
                int trimestre = (int)Math.Ceiling(mesActual / 3.0);
                int mesInicio = (trimestre - 1) * 3 + 1;
                int mesFin = trimestre * 3;
                var inicioTrimestre = new DateTime(anioActual, mesInicio, 1).DayOfYear;
                var finTrimestre = new DateTime(anioActual, mesFin, DateTime.DaysInMonth(anioActual, mesFin)).DayOfYear;

                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo >= mesInicio && r.NumeroPeriodo <= mesFin) ||
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo >= ((mesInicio - 1) * 4) && r.NumeroPeriodo <= (mesFin * 4)) ||
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo >= inicioTrimestre && r.NumeroPeriodo <= finTrimestre)
                ).ToList();
            }
            else if (filtroTiempo == "semana")
            {
                var rangoDias = ObtenerRangoDiasDelAnoPorSemana(semanaActual);

                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo == semanaActual) ||
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo == ((semanaActual - 1) / 4) + 1) ||
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo >= rangoDias.inicio && r.NumeroPeriodo <= rangoDias.fin)
                ).ToList();
            }
            else if (filtroTiempo == "dia")
            {
                int diaPeriodo = diaDelAno > 0 ? diaDelAno : DateTime.Now.DayOfYear;

                registrosEnPeriodo = registrosEnPeriodo.Where(r =>
                    (r.Metrica.Frecuencia == "Diario" && r.NumeroPeriodo == diaPeriodo) ||
                    (r.Metrica.Frecuencia == "Semanal" && r.NumeroPeriodo == ObtenerSemanaDelAno(new DateTime(anioActual, 1, 1).AddDays(diaPeriodo - 1))) ||
                    (r.Metrica.Frecuencia == "Mensual" && r.NumeroPeriodo == new DateTime(anioActual, 1, 1).AddDays(diaPeriodo - 1).Month)
                ).ToList();
            }

            var tablero = registrosEnPeriodo
                .GroupBy(r => r.Metrica)
                .Select(g => new
                {
                    MetricaID = g.Key.MetricaID,
                    NombreMetrica = g.Key.NombreMetrica,
                    TipoValor = g.Key.TipoValor,
                    UnidadMedida = g.Key.UnidadMedida,
                    TipoGrafica = g.Key.TipoGraficaDefecto,

                    // NUEVO: configuración de tarjeta
                    VariableTarjetaID = g.Key.VariableTarjetaID,
                    ModoTarjeta = g.Key.ModoTarjeta,

                    Historial = g.Select(r => new
                    {
                        periodo = FormatearEtiquetaPeriodo(r.Metrica.Frecuencia, r.NumeroPeriodo, r.Anio),

                        variables = r.DetallesValores.Select(v => new
                        {
                            variableID = v.VariableID,
                            nombre = v.Variable.NombreVariable,
                            valor = v.Valor,
                            esLinea = v.Variable.EsLinea,
                            tipoCaptura = string.IsNullOrWhiteSpace(v.Variable.TipoCaptura) ? "Manual" : v.Variable.TipoCaptura,
                            meta = r.Metrica.MetaEsperada,
                            sentido = r.Metrica.SentidoMeta,
                            tipoValor = string.IsNullOrWhiteSpace(v.Variable.TipoValor) ? r.Metrica.TipoValor : v.Variable.TipoValor,
                            unidadMedida = string.IsNullOrWhiteSpace(v.Variable.UnidadMedida) ? r.Metrica.UnidadMedida : v.Variable.UnidadMedida
                        }).ToList()
                    }).ToList()
                })
                .ToList();

            return Json(tablero);
        }

        // --- GRÁFICA POR ÁREA: Comparativa ---
        [HttpGet("ObtenerDatosPorDepartamento")]
        public JsonResult ObtenerDatosPorDepartamento(int id)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0 || !UsuarioPuedeAccederDepartamento(usuarioId, id))
                return Json(new List<object>());

            var ultimosRegistros = _context.RegistroKpis
                .Include(r => r.DetallesValores).ThenInclude(v => v.Variable)
                .Include(r => r.Metrica)
                .Where(r => r.Metrica.DepartamentoID == id && r.Activo == true && r.Metrica.TipoValor.ToLower().Contains("porcentaje"))
                .ToList()
                .GroupBy(r => r.MetricaID)
                .Select(g => g.OrderByDescending(r => r.FechaCaptura).First())
                .ToList();

            var datos = ultimosRegistros
                .Select(r => new
                {
                    metrica = r.Metrica.NombreMetrica,
                    valor = Math.Round(r.DetallesValores.FirstOrDefault(v => v.Variable.EsLinea == true)?.Valor ?? 0, 2)
                })
                .ToList();

            return Json(datos);
        }

        [HttpGet("ObtenerListaMetricas")]
        public JsonResult ObtenerListaMetricas()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            var query = _context.CatMetricas
                .Include(m => m.Departamento)
                .Where(m => m.Activo)
                .AsQueryable();

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    query = query.Where(m => datosAcceso.Departamentos.Contains(m.DepartamentoID));
                else
                    query = query.Where(m => false);
            }

            var metricas = query
                .OrderBy(m => m.Departamento.NombreDepartamento)
                .ThenBy(m => m.NombreMetrica)
                .Select(m => new
                {
                    m.MetricaID,
                    m.NombreMetrica,
                    nombreDepartamento = m.Departamento.NombreDepartamento
                })
                .ToList();
            
            return Json(metricas);
        }

        [HttpGet("ObtenerHistorialMetrica")]
        public JsonResult ObtenerHistorialMetrica(int idMetrica, int anio)
        {
            int usuarioId = ObtenerUsuarioIdActual();
            var departamentoMetrica = _context.CatMetricas
                .Where(m => m.MetricaID == idMetrica)
                .Select(m => (int?)m.DepartamentoID)
                .FirstOrDefault();

            if (usuarioId == 0 || !departamentoMetrica.HasValue || !UsuarioPuedeAccederDepartamento(usuarioId, departamentoMetrica.Value))
                return Json(new List<object>());

            var historial = _context.RegistroKpis
                .Include(r => r.DetallesValores)
                    .ThenInclude(v => v.Variable)
                .Include(r => r.Metrica)
                .Where(r => r.MetricaID == idMetrica && r.Anio == anio && r.Activo == true)
                .OrderBy(r => r.NumeroPeriodo)
                .ToList()
                .Select(r => new
                {
                    periodo = FormatearEtiquetaPeriodo(r.Metrica.Frecuencia, r.NumeroPeriodo, r.Anio),
                    tipoGrafica = r.Metrica.TipoGraficaDefecto,
                    tipoValor = r.Metrica.TipoValor,
                    unidadMedida = r.Metrica.UnidadMedida,
                    variables = r.DetallesValores.Select(v => new
                    {
                        variableID = v.VariableID,
                        nombre = v.Variable.NombreVariable,
                        valor = v.Valor,
                        esLinea = v.Variable.EsLinea,
                        meta = r.Metrica.MetaEsperada,
                        sentido = r.Metrica.SentidoMeta,
                        tipoValor = string.IsNullOrWhiteSpace(v.Variable.TipoValor) ? r.Metrica.TipoValor : v.Variable.TipoValor,
                        unidadMedida = string.IsNullOrWhiteSpace(v.Variable.UnidadMedida) ? r.Metrica.UnidadMedida : v.Variable.UnidadMedida
                    }).ToList()
                })
                .ToList();

            return Json(historial);
        }
        #endregion

        #region 2. CAPTURA DE DATOS (OPERACIÓN)

        [HttpGet("Captura")]
        public IActionResult Captura()
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
                return RedirectToAction("Login", "Login");

            var permisos = CargarPermisosSubMenuUsuario();
            ViewBag.MisPermisos = permisos;

            if (!TieneAccesoCaptura())
                return Forbid();

            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            var departamentosQuery = _context.Departamentos
                .Where(d => d.Activo)
                .AsQueryable();

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    departamentosQuery = departamentosQuery.Where(d => datosAcceso.Departamentos.Contains(d.DepartamentoID));
                else
                    departamentosQuery = departamentosQuery.Where(d => false);
            }

            var departamentosPermitidos = departamentosQuery
                .OrderBy(d => d.NombreDepartamento)
                .ToList();

            ViewBag.DepartamentoBloqueado = !puedeGestionarTodos && departamentosPermitidos.Count == 1;
            ViewBag.DepartamentoUsuarioID = (!puedeGestionarTodos && departamentosPermitidos.Count == 1)
                ? (int?)departamentosPermitidos.First().DepartamentoID
                : null;
            ViewBag.PuedeGestionarTodosDepartamentos = puedeGestionarTodos;
            ViewBag.EsUsuarioLimitado = !puedeGestionarTodos;
            ViewBag.Departamentos = departamentosPermitidos;

            var queryMetricas = _context.CatMetricas
                .Include(m => m.Departamento)
                .Include(m => m.VariablesConfiguradas)
                .Where(m => m.Activo)
                .AsQueryable();

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    queryMetricas = queryMetricas.Where(m => datosAcceso.Departamentos.Contains(m.DepartamentoID));
                else
                    queryMetricas = queryMetricas.Where(m => false);
            }

            var metricas = queryMetricas
                .OrderBy(m => m.Departamento.NombreDepartamento)
                .ThenBy(m => m.NombreMetrica)
                .ToList();

            return View(metricas);
        }

        [HttpGet("ObtenerKpisImportablesPorDepartamento")]
        public IActionResult ObtenerKpisImportablesPorDepartamento(int? departamentoId)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
            {
                return Json(new
                {
                    success = false,
                    mensaje = "Sesión expirada."
                });
            }

            if (!departamentoId.HasValue || departamentoId.Value <= 0)
            {
                return Json(new
                {
                    success = false,
                    mensaje = "Selecciona un departamento válido."
                });
            }

            if (!UsuarioPuedeAccederDepartamento(usuarioId, departamentoId.Value))
            {
                return Json(new
                {
                    success = false,
                    mensaje = "No tienes permiso para importar KPIs de este departamento."
                });
            }

            var metricas = _context.CatMetricas
                .Include(m => m.VariablesConfiguradas)
                .Where(m => m.Activo)
                .Where(m => m.DepartamentoID == departamentoId.Value)
                .Where(m => m.VariablesConfiguradas.Any(v =>
                    v.TipoCaptura == null ||
                    v.TipoCaptura == "" ||
                    v.TipoCaptura == "Manual"))
                .OrderBy(m => m.NombreMetrica)
                .Select(m => new
                {
                    metricaId = m.MetricaID,
                    nombreMetrica = m.NombreMetrica,
                    frecuencia = m.Frecuencia,
                    totalVariablesManuales = m.VariablesConfiguradas.Count(v =>
                        v.TipoCaptura == null ||
                        v.TipoCaptura == "" ||
                        v.TipoCaptura == "Manual")
                })
                .ToList();

            return Json(new
            {
                success = true,
                metricas
            });
        }

        [HttpGet("ObtenerVariablesPorMetrica")]
        public JsonResult ObtenerVariablesPorMetrica(int idMetrica)
        {
            int usuarioId = ObtenerUsuarioIdActual();

            var metrica = _context.CatMetricas
                .Include(m => m.VariablesConfiguradas)
                .FirstOrDefault(m => m.MetricaID == idMetrica && m.Activo);

            if (metrica == null || metrica.VariablesConfiguradas == null)
                return Json(new List<object>());

            if (usuarioId == 0 || !UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID))
                return Json(new List<object>());

            var variables = metrica.VariablesConfiguradas.Select(v => new
            {
                id = v.VariableID,
                nombre = v.NombreVariable,
                esLinea = v.EsLinea,
                tipoCaptura = string.IsNullOrEmpty(v.TipoCaptura) ? "Manual" : v.TipoCaptura,
                valorFijo = v.ValorFijo,
                formula = v.Formula,
                tipoValor = string.IsNullOrWhiteSpace(v.TipoValor) ? metrica.TipoValor : v.TipoValor,
                unidadMedida = string.IsNullOrWhiteSpace(v.UnidadMedida) ? metrica.UnidadMedida : v.UnidadMedida
            }).ToList();

            return Json(variables);
        }

        [HttpPost("GuardarKpi")]
        public IActionResult GuardarKpi(int metricaId, int? anio, int? numeroPeriodo, DateTime? fechaDiaria, Dictionary<int, decimal> valores)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Captura");
                }

                var metrica = _context.CatMetricas.FirstOrDefault(m => m.MetricaID == metricaId && m.Activo);
                if (metrica == null)
                {
                    TempData["Mensaje"] = "No se encontró el KPI seleccionado.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Captura");
                }

                if (!UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID))
                {
                    TempData["Mensaje"] = "No tienes permiso para capturar KPIs de este departamento.";
                    TempData["Tipo"] = "danger";
                    return RedirectToAction("Captura");
                }

                int periodoFinal = numeroPeriodo ?? 0;
                int anioFinal = anio ?? DateTime.Now.Year;

                if (fechaDiaria.HasValue)
                {
                    periodoFinal = fechaDiaria.Value.DayOfYear;
                    anioFinal = fechaDiaria.Value.Year;        
                }

                var registro = new RegistroKpi
                {
                    MetricaID = metricaId,
                    Anio = anioFinal,
                    NumeroPeriodo = periodoFinal,
                    FechaCaptura = DateTime.Now,
                    UsuarioID = usuarioId,
                    Activo = true
                };

                if (valores != null)
                {
                    foreach(var item in valores)
                    {
                        registro.DetallesValores.Add(new RegistroKpis_Valores
                        {
                            VariableID = item.Key,
                            Valor = item.Value    
                        });
                    }
                }

                _context.RegistroKpis.Add(registro);
                _context.SaveChanges();

                TempData["Mensaje"] = "¡Datos guardados correctamente!";
                TempData["Tipo"] = "success";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                TempData["Mensaje"] = "Error al guardar: " + ex.Message;
                TempData["Tipo"] = "danger";
            }
            return RedirectToAction("Captura");
        }

        [HttpGet("DescargarGuiaExcel")]
        public IActionResult DescargarGuiaExcel()
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                    return RedirectToAction("Login", "Login");

                var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
                bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

                var queryMetricas = _context.CatMetricas
                    .Include(m => m.Departamento)
                    .Include(m => m.VariablesConfiguradas)
                    .Where(m => m.Activo)
                    .AsQueryable();

                if (!puedeGestionarTodos)
                {
                    if (datosAcceso.Departamentos.Any())
                        queryMetricas = queryMetricas.Where(m => datosAcceso.Departamentos.Contains(m.DepartamentoID));
                    else
                        queryMetricas = queryMetricas.Where(m => false);
                }

                var metricas = queryMetricas
                    .OrderBy(m => m.Departamento.NombreDepartamento)
                    .ThenBy(m => m.NombreMetrica)
                    .ToList();

                using var workbook = new XLWorkbook();

                CrearHojaTutorialExcel(workbook);
                CrearHojaCargaExcel(workbook, metricas);
                CrearHojaEjemplosExcel(workbook);
                CrearHojaCatalogoExcel(workbook, metricas);

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                var nombreArchivo = $"Plantilla_Carga_KPIs_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    nombreArchivo
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar la plantilla de Excel para KPIs.");

                TempData["Mensaje"] = "No se pudo generar la plantilla de Excel: " + ex.Message;
                TempData["Tipo"] = "danger";

                return RedirectToAction("Captura");
            }
        }

        [HttpGet("DescargarPlantillaExcelKpi")]
        public IActionResult DescargarPlantillaExcelKpi(int metricaId, int anio, int? periodoInicio, int? periodoFin)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();

                if (usuarioId == 0)
                    return RedirectToAction("Login", "Login");

                var metrica = _context.CatMetricas
                    .Include(m => m.Departamento)
                    .Include(m => m.VariablesConfiguradas)
                    .FirstOrDefault(m => m.MetricaID == metricaId && m.Activo);

                if (metrica == null)
                {
                    TempData["Mensaje"] = "No se encontró el KPI seleccionado.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Captura");
                }

                if (!UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID))
                {
                    TempData["Mensaje"] = "No tienes permiso para descargar plantillas de este KPI.";
                    TempData["Tipo"] = "danger";
                    return RedirectToAction("Captura");
                }

                var variablesManuales = metrica.VariablesConfiguradas
                    .Where(v => string.IsNullOrWhiteSpace(v.TipoCaptura) ||
                                v.TipoCaptura.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => v.VariableID)
                    .ToList();

                if (!variablesManuales.Any())
                {
                    TempData["Mensaje"] = "Este KPI no tiene variables manuales para importar.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Captura");
                }

                int inicio = periodoInicio ?? 1;
                int fin = periodoFin ?? inicio;

                if (metrica.Frecuencia.Equals("Mensual", StringComparison.OrdinalIgnoreCase))
                {
                    inicio = Math.Clamp(inicio, 1, 12);
                    fin = Math.Clamp(fin, 1, 12);
                }
                else if (metrica.Frecuencia.Equals("Semanal", StringComparison.OrdinalIgnoreCase))
                {
                    inicio = Math.Clamp(inicio, 1, 53);
                    fin = Math.Clamp(fin, 1, 53);
                }
                else if (metrica.Frecuencia.Equals("Diario", StringComparison.OrdinalIgnoreCase))
                {
                    int diasMaximos = DateTime.IsLeapYear(anio) ? 366 : 365;
                    inicio = Math.Clamp(inicio, 1, diasMaximos);
                    fin = Math.Clamp(fin, 1, diasMaximos);
                }

                if (inicio > fin)
                {
                    TempData["Mensaje"] = "El periodo inicial no puede ser mayor al periodo final.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Captura");
                }

                using var workbook = new XLWorkbook();

                CrearHojaTutorialExcelKpi(workbook, metrica);
                CrearHojaCargaExcelKpi(workbook, metrica, variablesManuales, anio, inicio, fin);
                CrearHojaCatalogoExcelKpi(workbook, metrica);

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                string nombreSeguro = string.Concat(metrica.NombreMetrica
                    .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-'))
                    .Replace(" ", "_");

                var nombreArchivo = $"Plantilla_{nombreSeguro}_{anio}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    nombreArchivo
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar plantilla específica de KPI.");

                TempData["Mensaje"] = "No se pudo generar la plantilla del KPI: " + ex.Message;
                TempData["Tipo"] = "danger";

                return RedirectToAction("Captura");
            }
        }

        private void CrearHojaCargaExcelKpi(
            XLWorkbook workbook,
            CatMetricas metrica,
            List<CatMetricas_Variables> variablesManuales,
            int anio,
            int periodoInicio,
            int periodoFin)
        {
            var hoja = workbook.Worksheets.Add("Carga");

            hoja.Cell("A1").Value = $"CARGA DE HISTÓRICOS KPI - {metrica.NombreMetrica}";
            hoja.Cell("A1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 16;
            hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
            hoja.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#154360");
            hoja.Range("A1:K1").Merge();

            hoja.Cell("A2").Value = $"Departamento: {metrica.Departamento?.NombreDepartamento ?? "Sin departamento"} | Frecuencia: {metrica.Frecuencia}";
            hoja.Cell("A2").Style.Font.Bold = true;
            hoja.Cell("A2").Style.Fill.BackgroundColor = XLColor.FromHtml("#D1E7DD");
            hoja.Range("A2:K2").Merge();

            hoja.Cell("A3").Value = "Llena únicamente Año, Periodo, Fecha y Valor. El sistema agregará automáticamente variables fijas y calculadas.";
            hoja.Cell("A3").Style.Font.Italic = true;
            hoja.Cell("A3").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
            hoja.Range("A3:K3").Merge();

            string[] encabezados =
            {
                "Año",
                "Periodo",
                "Fecha",
                "Variable",
                "Valor",
                "Notas",
                "MetricaID",
                "VariableID",
                "DepartamentoID",
                "Frecuencia",
                "TipoCaptura"
            };

            int filaEncabezado = 5;

            for (int i = 0; i < encabezados.Length; i++)
            {
                hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
            }

            AplicarEstiloEncabezadoExcel(hoja.Range(filaEncabezado, 1, filaEncabezado, encabezados.Length));

            int fila = filaEncabezado + 1;

            for (int periodo = periodoInicio; periodo <= periodoFin; periodo++)
            {
                foreach (var variable in variablesManuales)
                {
                    hoja.Cell(fila, 1).Value = anio;

                    if (metrica.Frecuencia.Equals("Diario", StringComparison.OrdinalIgnoreCase))
                    {
                        var fecha = new DateTime(anio, 1, 1).AddDays(periodo - 1);

                        hoja.Cell(fila, 2).Value = "";
                        hoja.Cell(fila, 3).Value = fecha;
                        hoja.Cell(fila, 3).Style.DateFormat.Format = "yyyy-mm-dd";
                        hoja.Cell(fila, 6).Value = "Captura el valor histórico para esta fecha.";
                    }
                    else
                    {
                        hoja.Cell(fila, 2).Value = periodo;
                        hoja.Cell(fila, 3).Value = "";
                        hoja.Cell(fila, 6).Value = "Captura el valor histórico para este periodo.";
                    }

                    hoja.Cell(fila, 4).Value = variable.NombreVariable;
                    hoja.Cell(fila, 5).Value = "";

                    hoja.Cell(fila, 7).Value = metrica.MetricaID;
                    hoja.Cell(fila, 8).Value = variable.VariableID;
                    hoja.Cell(fila, 9).Value = metrica.DepartamentoID;
                    hoja.Cell(fila, 10).Value = metrica.Frecuencia;
                    hoja.Cell(fila, 11).Value = "Manual";

                    fila++;
                }
            }

            var rangoDatos = hoja.Range(filaEncabezado, 1, Math.Max(filaEncabezado, fila - 1), encabezados.Length);
            rangoDatos.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.SetAutoFilter();

            hoja.Column(1).Style.Fill.BackgroundColor = XLColor.White;
            hoja.Column(2).Style.Fill.BackgroundColor = XLColor.White;
            hoja.Column(3).Style.Fill.BackgroundColor = XLColor.White;
            hoja.Column(4).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
            hoja.Column(5).Style.Fill.BackgroundColor = XLColor.FromHtml("#82E0AA");
            hoja.Column(6).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F3F4");

            hoja.Column(1).Style.NumberFormat.Format = "0";
            hoja.Column(2).Style.NumberFormat.Format = "0";
            hoja.Column(3).Style.DateFormat.Format = "yyyy-mm-dd";
            hoja.Column(5).Style.NumberFormat.Format = "0.00";

            hoja.Column(1).Width = 10;
            hoja.Column(2).Width = 12;
            hoja.Column(3).Width = 14;
            hoja.Column(4).Width = 36;
            hoja.Column(5).Width = 18;
            hoja.Column(6).Width = 55;

            for (int col = 7; col <= 11; col++)
            {
                hoja.Column(col).Hide();
            }

            hoja.SheetView.FreezeRows(filaEncabezado);
            hoja.SetTabActive();
        }

        private void CrearHojaTutorialExcelKpi(XLWorkbook workbook, CatMetricas metrica)
        {
            var hoja = workbook.Worksheets.Add("Tutorial");

            hoja.Cell("A1").Value = "GUÍA RÁPIDA DE CARGA";
            hoja.Cell("A1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 18;
            hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
            hoja.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#198754");
            hoja.Range("A1:D1").Merge();

            hoja.Cell("A3").Value = "KPI";
            hoja.Cell("B3").Value = metrica.NombreMetrica;
            hoja.Range("B3:D3").Merge();

            hoja.Cell("A4").Value = "Departamento";
            hoja.Cell("B4").Value = metrica.Departamento?.NombreDepartamento ?? "";
            hoja.Range("B4:D4").Merge();

            hoja.Cell("A5").Value = "Frecuencia";
            hoja.Cell("B5").Value = metrica.Frecuencia;
            hoja.Range("B5:D5").Merge();

            hoja.Cell("A7").Value = "Instrucciones";
            hoja.Cell("A7").Style.Font.Bold = true;
            hoja.Cell("A7").Style.Fill.BackgroundColor = XLColor.FromHtml("#CFE2FF");

            var pasos = new[]
            {
                "1. Ve a la hoja Carga.",
                "2. Llena solo la columna Valor en las filas que quieras importar.",
                "3. Para KPI mensual o semanal, usa Año y Periodo.",
                "4. Para KPI diario, usa Fecha.",
                "5. No modifiques las columnas ocultas.",
                "6. Guarda el archivo y súbelo al sistema para previsualizar.",
                "7. Revisa la previsualización antes de confirmar."
            };

            int fila = 8;

            foreach (var paso in pasos)
            {
                hoja.Cell(fila, 1).Value = paso;
                hoja.Range(fila, 1, fila, 4).Merge();
                fila++;
            }

            hoja.Columns().AdjustToContents();
            hoja.Column(1).Width = 28;
            hoja.Column(2).Width = 55;
        }

        private void CrearHojaCatalogoExcelKpi(XLWorkbook workbook, CatMetricas metrica)
        {
            var hoja = workbook.Worksheets.Add("Catalogo");

            string[] encabezados =
            {
                "DepartamentoID",
                "Departamento",
                "MetricaID",
                "Metrica",
                "Frecuencia",
                "VariableID",
                "Variable",
                "TipoCaptura"
            };

            for (int i = 0; i < encabezados.Length; i++)
            {
                hoja.Cell(1, i + 1).Value = encabezados[i];
            }

            AplicarEstiloEncabezadoExcel(hoja.Range(1, 1, 1, encabezados.Length));

            int fila = 2;

            foreach (var variable in metrica.VariablesConfiguradas.OrderBy(v => v.VariableID))
            {
                hoja.Cell(fila, 1).Value = metrica.DepartamentoID;
                hoja.Cell(fila, 2).Value = metrica.Departamento?.NombreDepartamento ?? "";
                hoja.Cell(fila, 3).Value = metrica.MetricaID;
                hoja.Cell(fila, 4).Value = metrica.NombreMetrica;
                hoja.Cell(fila, 5).Value = metrica.Frecuencia;
                hoja.Cell(fila, 6).Value = variable.VariableID;
                hoja.Cell(fila, 7).Value = variable.NombreVariable;
                hoja.Cell(fila, 8).Value = string.IsNullOrWhiteSpace(variable.TipoCaptura)
                    ? "Manual"
                    : variable.TipoCaptura;

                fila++;
            }

            hoja.Columns().AdjustToContents();
            hoja.RangeUsed().SetAutoFilter();
            hoja.Hide();
        }

        [HttpGet("PrevisualizarImportacion/{id}")]
        public IActionResult PrevisualizarImportacion(int id)
        {
            var importacion = _context.ImportacionesKpi
                .Include(i => i.Detalles)
                .FirstOrDefault(i => i.ImportacionID == id);

            if (importacion == null)
            {
                TempData["Mensaje"] = "No se encontró la importación solicitada.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            return View(importacion);
        }

        [HttpPost("PrevisualizarImportacionExcel")]
        public async Task<IActionResult> PrevisualizarImportacionExcel(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Mensaje"] = "Selecciona un archivo Excel válido.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            var extension = Path.GetExtension(excelFile.FileName).ToLowerInvariant();

            if (extension != ".xlsx" && extension != ".xls")
            {
                TempData["Mensaje"] = "Solo se admiten archivos .xlsx o .xls.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            int usuarioId = ObtenerUsuarioIdActual();

            if (usuarioId == 0)
            {
                TempData["Mensaje"] = "Sesión expirada.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            int totalFilas = 0;
            int filasValidas = 0;
            int filasConError = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var importacion = new ImportacionKpi
                {
                    NombreArchivo = excelFile.FileName,
                    UsuarioID = usuarioId,
                    FechaImportacion = DateTime.Now,
                    Estado = "Pendiente",
                    TotalFilas = 0,
                    FilasValidas = 0,
                    FilasConError = 0,
                    Observaciones = "Archivo cargado para previsualización."
                };

                _context.ImportacionesKpi.Add(importacion);
                await _context.SaveChangesAsync();

                using (var stream = excelFile.OpenReadStream())
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration
                        {
                            UseHeaderRow = true,
                            ReadHeaderRow = rowReader =>
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    rowReader.Read();
                                }
                            }
                        }
                    });

                    if (dataSet.Tables.Count == 0)
                    {
                        importacion.Estado = "ConErrores";
                        importacion.Observaciones = "El archivo no contiene hojas válidas.";
                        await _context.SaveChangesAsync();

                        TempData["Mensaje"] = "El archivo Excel no contiene hojas válidas.";
                        TempData["Tipo"] = "warning";
                        return RedirectToAction("Captura");
                    }

                    var tabla = dataSet.Tables
                        .Cast<DataTable>()
                        .FirstOrDefault(t => t.TableName.Equals("Carga", StringComparison.OrdinalIgnoreCase))
                        ?? dataSet.Tables[0];

                    if (tabla.Rows.Count == 0)
                    {
                        importacion.Estado = "ConErrores";
                        importacion.Observaciones = "La hoja de carga está vacía.";
                        await _context.SaveChangesAsync();

                        TempData["Mensaje"] = "La hoja de carga está vacía.";
                        TempData["Tipo"] = "warning";
                        return RedirectToAction("Captura");
                    }

                    int idxDepartamentoID = ObtenerIndiceColumna(tabla, "DepartamentoID", "DeptoID", "Depto ID", "IdDepartamento");
                    int idxDepartamento = ObtenerIndiceColumna(tabla, "Departamento", "Area", "Área");
                    int idxMetricaID = ObtenerIndiceColumna(tabla, "MetricaID", "MétricaID", "IdMetrica", "Id Métrica", "KPIID", "KPI ID", "KPI ID");
                    int idxMetrica = ObtenerIndiceColumna(tabla, "Metrica", "Métrica", "KPI", "NombreMetrica");
                    int idxFrecuencia = ObtenerIndiceColumna(tabla, "Frecuencia");
                    int idxAnio = ObtenerIndiceColumna(tabla, "Anio", "Año", "Year");
                    int idxPeriodo = ObtenerIndiceColumna(tabla, "Periodo", "NúmeroPeriodo", "NumeroPeriodo");
                    int idxFecha = ObtenerIndiceColumna(tabla, "Fecha", "FechaMedicion", "Fecha Medicion");
                    int idxVariableID = ObtenerIndiceColumna(tabla, "VariableID", "IdVariable", "Id Variable", "Variable ID");
                    int idxVariable = ObtenerIndiceColumna(tabla, "Variable", "NombreVariable");
                    int idxTipoCaptura = ObtenerIndiceColumna(tabla, "TipoCaptura", "Tipo Captura", "Tipo");
                    int idxImportar = ObtenerIndiceColumna(tabla, "Importar", "Importar?", "¿Importar?", "Cargar", "Cargar?");
                    int idxValor = ObtenerIndiceColumna(tabla, "Valor", "Valor a importar", "Resultado", "Cantidad");

                    if (idxMetricaID < 0 || idxVariableID < 0 || idxValor < 0)
                    {
                        importacion.Estado = "ConErrores";
                        importacion.Observaciones = "La plantilla no contiene columnas obligatorias: KPI ID, Variable ID y Valor.";
                        await _context.SaveChangesAsync();

                        TempData["Mensaje"] = "La plantilla no contiene columnas obligatorias: KPI ID, Variable ID y Valor.";
                        TempData["Tipo"] = "danger";
                        return RedirectToAction("Captura");
                    }

                    var metricas = _context.CatMetricas
                        .Include(m => m.Departamento)
                        .Include(m => m.VariablesConfiguradas)
                        .Where(m => m.Activo)
                        .ToList();

                    var datosAccesoImportacion = ObtenerDatosAccesoUsuario(usuarioId);
                    bool puedeGestionarTodosImportacion = UsuarioPuedeGestionarTodosLosDepartamentos(datosAccesoImportacion);

                    if (!puedeGestionarTodosImportacion)
                    {
                        metricas = metricas
                            .Where(m => datosAccesoImportacion.Departamentos.Contains(m.DepartamentoID))
                            .ToList();
                    }

                    var metricasPorId = metricas.ToDictionary(m => m.MetricaID);

                    var variablesPorId = metricas
                        .SelectMany(m => m.VariablesConfiguradas)
                        .GroupBy(v => v.VariableID)
                        .ToDictionary(g => g.Key, g => g.First());

                    var detalles = new List<ImportacionKpiDetalle>();

                    for (int rowIndex = 0; rowIndex < tabla.Rows.Count; rowIndex++)
                    {
                        var fila = tabla.Rows[rowIndex];
                        int numeroFilaExcel = rowIndex + 6;

                        string? importarTexto = idxImportar >= 0
                            ? fila[idxImportar]?.ToString()?.Trim()
                            : null;

                        string? valorTexto = idxValor >= 0
                            ? fila[idxValor]?.ToString()?.Trim()
                            : null;

                        int? metricaId = ConvertirEntero(fila[idxMetricaID]);
                        int? variableId = ConvertirEntero(fila[idxVariableID]);

                        bool filaVacia = !metricaId.HasValue &&
                                         !variableId.HasValue &&
                                         string.IsNullOrWhiteSpace(importarTexto) &&
                                         string.IsNullOrWhiteSpace(valorTexto);

                        if (filaVacia)
                            continue;

                        totalFilas++;

                        int? departamentoIdExcel = idxDepartamentoID >= 0 ? ConvertirEntero(fila[idxDepartamentoID]) : null;
                        string? departamentoExcel = idxDepartamento >= 0 ? fila[idxDepartamento]?.ToString()?.Trim() : null;

                        string? nombreMetricaExcel = idxMetrica >= 0 ? fila[idxMetrica]?.ToString()?.Trim() : null;
                        string? nombreVariableExcel = idxVariable >= 0 ? fila[idxVariable]?.ToString()?.Trim() : null;

                        string estado = "Valida";
                        string observacion = "Lista para importar.";

                        CatMetricas? metrica = null;
                        CatMetricas_Variables? variable = null;

                        if (!metricaId.HasValue)
                        {
                            estado = "Error";
                            observacion = "Falta KPI ID.";
                        }
                        else if (!metricasPorId.TryGetValue(metricaId.Value, out metrica))
                        {
                            estado = "Error";
                            observacion = $"La métrica {metricaId.Value} no existe, está inactiva o no pertenece a tu departamento.";
                        }

                        if (estado != "Error")
                        {
                            if (!variableId.HasValue)
                            {
                                estado = "Error";
                                observacion = "Falta Variable ID.";
                            }
                            else if (!variablesPorId.TryGetValue(variableId.Value, out variable))
                            {
                                estado = "Error";
                                observacion = $"La variable {variableId.Value} no existe o no pertenece a una métrica permitida.";
                            }
                            else if (metrica != null && variable.MetricaID != metrica.MetricaID)
                            {
                                estado = "Error";
                                observacion = $"La variable {variableId.Value} no pertenece a la métrica {metrica.MetricaID}.";
                            }
                        }

                        string frecuencia = metrica?.Frecuencia
                            ?? (idxFrecuencia >= 0 ? fila[idxFrecuencia]?.ToString()?.Trim() ?? "" : "");

                        DateTime? fecha = idxFecha >= 0 ? ConvertirFecha(fila[idxFecha]) : null;

                        int? anio = idxAnio >= 0 ? ConvertirEntero(fila[idxAnio]) : null;
                        int? periodoExcel = idxPeriodo >= 0 ? ConvertirEntero(fila[idxPeriodo]) : null;

                        int? numeroPeriodo = null;

                        if (estado != "Error")
                        {
                            int anioBase = anio ?? DateTime.Now.Year;

                            int periodoCalculado = ObtenerPeriodoImportacion(
                                frecuencia,
                                anioBase,
                                periodoExcel,
                                fecha
                            );

                            if (periodoCalculado <= 0)
                            {
                                estado = "Error";
                                observacion = "No se pudo determinar el periodo.";
                            }
                            else
                            {
                                numeroPeriodo = periodoCalculado;

                                if (frecuencia.Equals("Diario", StringComparison.OrdinalIgnoreCase) && fecha.HasValue)
                                {
                                    anio = fecha.Value.Year;
                                }
                                else if (!anio.HasValue)
                                {
                                    anio = anioBase;
                                }
                            }
                        }

                        decimal? valor = null;
                        decimal? valorCalculado = null;

                        string? tipoCaptura = variable?.TipoCaptura;

                        if (string.IsNullOrWhiteSpace(tipoCaptura))
                        {
                            tipoCaptura = idxTipoCaptura >= 0
                                ? fila[idxTipoCaptura]?.ToString()?.Trim()
                                : "Manual";
                        }

                        if (string.IsNullOrWhiteSpace(tipoCaptura))
                        {
                            tipoCaptura = "Manual";
                        }

                        if (estado != "Error")
                        {
                            bool importarFila = idxImportar >= 0
                                ? EsFilaMarcadaParaImportar(importarTexto)
                                : !string.IsNullOrWhiteSpace(valorTexto);

                            if (tipoCaptura.Equals("Calculado", StringComparison.OrdinalIgnoreCase))
                            {
                                estado = "Ignorada";
                                observacion = "Variable calculada. El sistema la generará automáticamente al confirmar.";
                                valorCalculado = null;
                            }
                            else if (tipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                            {
                                valor = variable?.ValorFijo;

                                estado = "Ignorada";
                                observacion = "Variable fija. El sistema la agregará automáticamente al confirmar.";
                            }
                            else
                            {
                                if (!importarFila)
                                {
                                    estado = "Ignorada";
                                    observacion = idxImportar >= 0
                                        ? "Fila no marcada para importar. Escribe SI en la columna Importar para tomar este valor."
                                        : "Fila manual vacía. No se importará.";
                                }
                                else
                                {
                                    valor = ConvertirDecimal(fila[idxValor]);

                                    if (!valor.HasValue)
                                    {
                                        estado = "Error";
                                        observacion = "La fila manual no tiene un Valor numérico válido.";
                                    }
                                    else
                                    {
                                        estado = "Valida";
                                        observacion = "Lista para importar.";
                                    }
                                }
                            }
                        }

                        if (estado == "Valida" || estado == "Advertencia")
                        {
                            filasValidas++;
                        }
                        else if (estado == "Error")
                        {
                            filasConError++;
                        }

                        detalles.Add(new ImportacionKpiDetalle
                        {
                            ImportacionID = importacion.ImportacionID,
                            NumeroFilaExcel = numeroFilaExcel,

                            DepartamentoID = metrica?.DepartamentoID ?? departamentoIdExcel,
                            Departamento = metrica?.Departamento?.NombreDepartamento ?? departamentoExcel,

                            MetricaID = metrica?.MetricaID ?? metricaId,
                            NombreMetrica = metrica?.NombreMetrica ?? nombreMetricaExcel,

                            TipoValor = variable != null && !string.IsNullOrWhiteSpace(variable.TipoValor) ? variable.TipoValor : metrica?.TipoValor,
                            UnidadMedida = variable != null && !string.IsNullOrWhiteSpace(variable.UnidadMedida) ? variable.UnidadMedida : metrica?.UnidadMedida,

                            Frecuencia = frecuencia,
                            Anio = anio,
                            NumeroPeriodo = numeroPeriodo,
                            FechaMedicion = fecha,

                            VariableID = variable?.VariableID ?? variableId,
                            NombreVariable = variable?.NombreVariable ?? nombreVariableExcel,
                            TipoCaptura = tipoCaptura,

                            Valor = valor,
                            ValorCalculado = valorCalculado,

                            Estado = estado,
                            Observacion = observacion
                        });
                    }

                    RecalcularEstadosPorGrupoImportacion(detalles);

                    filasValidas = detalles.Count(d => d.Estado == "Valida" || d.Estado == "Advertencia");
                    filasConError = detalles.Count(d => d.Estado == "Error");

                    _context.ImportacionesKpi_Detalle.AddRange(detalles);

                    importacion.TotalFilas = totalFilas;
                    importacion.FilasValidas = filasValidas;
                    importacion.FilasConError = filasConError;

                    importacion.Estado = filasConError > 0
                        ? "ConErrores"
                        : "Pendiente";

                    importacion.Observaciones = $"Archivo leído correctamente. {filasValidas} filas válidas, {filasConError} con error.";

                    await _context.SaveChangesAsync();
                }

                return RedirectToAction("PrevisualizarImportacion", new { id = importacion.ImportacionID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al previsualizar importación de Excel.");

                TempData["Mensaje"] = "Error al leer el Excel: " + ex.Message;
                TempData["Tipo"] = "danger";

                return RedirectToAction("Captura");
            }
        }

        [HttpPost("CancelarImportacionExcel")]
        public async Task<IActionResult> CancelarImportacionExcel(int importacionId)
        {
            var importacion = _context.ImportacionesKpi
                .FirstOrDefault(i => i.ImportacionID == importacionId);

            if (importacion == null)
            {
                TempData["Mensaje"] = "No se encontró la importación.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            if (importacion.Estado == "Confirmada")
            {
                TempData["Mensaje"] = "Esta importación ya fue confirmada y no puede cancelarse.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("PrevisualizarImportacion", new { id = importacionId });
            }

            importacion.Estado = "Cancelada";
            importacion.Observaciones = "Importación cancelada por el usuario.";

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "La importación fue cancelada. No se guardó nada en el historial.";
            TempData["Tipo"] = "info";

            return RedirectToAction("Captura");
        }

        [HttpPost("ConfirmarImportacionExcel")]
        public async Task<IActionResult> ConfirmarImportacionExcel(int importacionId)
        {
            var importacion = _context.ImportacionesKpi
                .Include(i => i.Detalles)
                .FirstOrDefault(i => i.ImportacionID == importacionId);

            if (importacion == null)
            {
                TempData["Mensaje"] = "No se encontró la importación solicitada.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            if (importacion.Estado == "Confirmada")
            {
                TempData["Mensaje"] = "Esta importación ya fue confirmada anteriormente.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("PrevisualizarImportacion", new { id = importacionId });
            }

            if (importacion.Estado == "Cancelada")
            {
                TempData["Mensaje"] = "Esta importación fue cancelada y no puede confirmarse.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("Captura");
            }

            var detalles = importacion.Detalles.ToList();

            if (detalles.Any(d => d.Estado == "Error"))
            {
                TempData["Mensaje"] = "No se puede confirmar la importación porque contiene errores.";
                TempData["Tipo"] = "danger";
                return RedirectToAction("PrevisualizarImportacion", new { id = importacionId });
            }

            var detallesValidos = detalles
                .Where(d =>
                    d.Estado == "Valida" &&
                    d.MetricaID.HasValue &&
                    d.VariableID.HasValue &&
                    d.Anio.HasValue &&
                    d.NumeroPeriodo.HasValue &&
                    d.Valor.HasValue &&
                    (d.TipoCaptura == null || d.TipoCaptura.Equals("Manual", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (!detallesValidos.Any())
            {
                TempData["Mensaje"] = "No hay datos manuales válidos para confirmar.";
                TempData["Tipo"] = "warning";
                return RedirectToAction("PrevisualizarImportacion", new { id = importacionId });
            }

            int usuarioId = importacion.UsuarioID;
            int registrosInsertados = 0;
            int registrosActualizados = 0;
            int valoresInsertados = 0;
            int valoresActualizados = 0;

            try
            {
                var grupos = detallesValidos
                    .GroupBy(d => new
                    {
                        MetricaID = d.MetricaID!.Value,
                        Anio = d.Anio!.Value,
                        NumeroPeriodo = d.NumeroPeriodo!.Value
                    })
                    .ToList();

                foreach (var grupo in grupos)
                {
                    var metrica = _context.CatMetricas
                        .Include(m => m.VariablesConfiguradas)
                        .FirstOrDefault(m => m.MetricaID == grupo.Key.MetricaID && m.Activo);

                    if (metrica == null)
                        continue;

                    if (!UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID))
                        continue;

                    var registro = _context.RegistroKpis
                        .Include(r => r.DetallesValores)
                        .FirstOrDefault(r =>
                            r.MetricaID == grupo.Key.MetricaID &&
                            r.Anio == grupo.Key.Anio &&
                            r.NumeroPeriodo == grupo.Key.NumeroPeriodo);

                    if (registro == null)
                    {
                        registro = new RegistroKpi
                        {
                            MetricaID = grupo.Key.MetricaID,
                            Anio = grupo.Key.Anio,
                            NumeroPeriodo = grupo.Key.NumeroPeriodo,
                            FechaCaptura = DateTime.Now,
                            UsuarioID = usuarioId,
                            Activo = true
                        };

                        _context.RegistroKpis.Add(registro);
                        registrosInsertados++;
                    }
                    else
                    {
                        registro.Activo = true;
                        registro.FechaCaptura = DateTime.Now;
                        registro.UsuarioID = usuarioId;
                        registrosActualizados++;
                    }

                    await _context.SaveChangesAsync();

                    foreach (var detalle in grupo)
                    {
                        UpsertValorRegistro(
                            registro,
                            detalle.VariableID!.Value,
                            detalle.Valor!.Value,
                            ref valoresInsertados,
                            ref valoresActualizados);
                    }

                    AgregarVariablesFijasYCalculadas(
                        registro,
                        metrica,
                        ref valoresInsertados,
                        ref valoresActualizados);
                }

                importacion.Estado = "Confirmada";
                importacion.Observaciones =
                    $"Importación confirmada. {registrosInsertados} registros nuevos, {registrosActualizados} registros actualizados.";

                await _context.SaveChangesAsync();

                TempData["Mensaje"] =
                    $"Importación confirmada correctamente: {registrosInsertados} registros nuevos, {registrosActualizados} registros actualizados, {valoresInsertados} valores nuevos, {valoresActualizados} valores actualizados.";

                TempData["Tipo"] = "success";

                return RedirectToAction("Historial");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar importación de KPIs.");

                TempData["Mensaje"] = "Error al confirmar la importación: " + ex.Message;
                TempData["Tipo"] = "danger";

                return RedirectToAction("PrevisualizarImportacion", new { id = importacionId });
            }
        }

        private void AplicarEstiloEncabezadoExcel(IXLRange rangoEncabezado)
        {
            rangoEncabezado.Style.Font.Bold = true;
            rangoEncabezado.Style.Font.FontColor = XLColor.White;
            rangoEncabezado.Style.Font.FontSize = 11;
            rangoEncabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#154360");

            rangoEncabezado.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangoEncabezado.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            rangoEncabezado.Style.Alignment.WrapText = true;

            rangoEncabezado.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rangoEncabezado.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rangoEncabezado.Style.Border.OutsideBorderColor = XLColor.White;
            rangoEncabezado.Style.Border.InsideBorderColor = XLColor.White;
        }

        private void CrearHojaCargaExcel(XLWorkbook workbook, List<CatMetricas> metricas)
        {
            var hoja = workbook.Worksheets.Add("Carga");

            hoja.Cell("A1").Value = "CARGA DE HISTÓRICOS KPI";
            hoja.Cell("A1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 16;
            hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
            hoja.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1a6c45");
            hoja.Range("A1:N1").Merge();

            hoja.Cell("A2").Value = "Instrucciones rápidas: filtra por Departamento o KPI, borra filas que no usarás, no modifiques columnas grises. En las filas Manuales que quieras cargar escribe SI en Importar y llena Valor.";
            hoja.Cell("A2").Style.Font.Bold = true;
            hoja.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#0F5132");
            hoja.Cell("A2").Style.Fill.BackgroundColor = XLColor.FromHtml("#D1E7DD");
            hoja.Cell("A2").Style.Alignment.WrapText = true;
            hoja.Range("A2:N2").Merge();

            hoja.Cell("A3").Value = "Gris = referencia de base de datos. Azul = marca SI para importar. Verde = valor a capturar. Amarillo = no llenar / revisar.";
            hoja.Cell("A3").Style.Font.Italic = true;
            hoja.Cell("A3").Style.Font.FontColor = XLColor.FromHtml("#664D03");
            hoja.Cell("A3").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
            hoja.Cell("A3").Style.Alignment.WrapText = true;
            hoja.Range("A3:N3").Merge();

            string[] encabezados =
            {
                "Depto ID",
                "Departamento",
                "KPI ID",
                "KPI",
                "Frecuencia",
                "Variable ID",
                "Variable",
                "Tipo",
                "Año",
                "Periodo",
                "Fecha",
                "Importar",
                "Valor",
                "Notas"
            };

            int filaEncabezado = 5;

            for (int i = 0; i < encabezados.Length; i++)
            {
                hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
            }

            var rangoEncabezado = hoja.Range(filaEncabezado, 1, filaEncabezado, encabezados.Length);
            AplicarEstiloEncabezadoExcel(rangoEncabezado);
            hoja.Row(filaEncabezado).Height = 30;

            int fila = filaEncabezado + 1;
            int anioActual = DateTime.Now.Year;

            foreach (var metrica in metricas)
            {
                var variables = metrica.VariablesConfiguradas
                    .OrderBy(v => v.VariableID)
                    .ToList();

                if (!variables.Any())
                {
                    hoja.Cell(fila, 1).Value = metrica.DepartamentoID;
                    hoja.Cell(fila, 2).Value = metrica.Departamento?.NombreDepartamento ?? "";
                    hoja.Cell(fila, 3).Value = metrica.MetricaID;
                    hoja.Cell(fila, 4).Value = metrica.NombreMetrica;
                    hoja.Cell(fila, 5).Value = metrica.Frecuencia;
                    hoja.Cell(fila, 6).Value = "";
                    hoja.Cell(fila, 7).Value = "Sin variables configuradas";
                    hoja.Cell(fila, 8).Value = "";
                    hoja.Cell(fila, 9).Value = anioActual;
                    hoja.Cell(fila, 10).Value = "";
                    hoja.Cell(fila, 11).Value = "";
                    hoja.Cell(fila, 12).Value = "";
                    hoja.Cell(fila, 13).Value = "";
                    hoja.Cell(fila, 14).Value = "Esta métrica no tiene variables configuradas.";
                    fila++;
                    continue;
                }

                foreach (var variable in variables)
                {
                    string tipoCaptura = string.IsNullOrWhiteSpace(variable.TipoCaptura)
                        ? "Manual"
                        : variable.TipoCaptura.Trim();

                    hoja.Cell(fila, 1).Value = metrica.DepartamentoID;
                    hoja.Cell(fila, 2).Value = metrica.Departamento?.NombreDepartamento ?? "";
                    hoja.Cell(fila, 3).Value = metrica.MetricaID;
                    hoja.Cell(fila, 4).Value = metrica.NombreMetrica;
                    hoja.Cell(fila, 5).Value = metrica.Frecuencia;
                    hoja.Cell(fila, 6).Value = variable.VariableID;
                    hoja.Cell(fila, 7).Value = variable.NombreVariable;
                    hoja.Cell(fila, 8).Value = tipoCaptura;
                    hoja.Cell(fila, 9).Value = anioActual;

                    if (metrica.Frecuencia == "Mensual")
                    {
                        hoja.Cell(fila, 10).Value = 1;
                        hoja.Cell(fila, 11).Value = "";
                    }
                    else if (metrica.Frecuencia == "Semanal")
                    {
                        hoja.Cell(fila, 10).Value = 1;
                        hoja.Cell(fila, 11).Value = "";
                    }
                    else if (metrica.Frecuencia == "Diario")
                    {
                        hoja.Cell(fila, 10).Value = "";
                        hoja.Cell(fila, 11).Value = DateTime.Now.Date;
                        hoja.Cell(fila, 11).Style.DateFormat.Format = "yyyy-mm-dd";
                    }
                    else
                    {
                        hoja.Cell(fila, 10).Value = "";
                        hoja.Cell(fila, 11).Value = "";
                    }

                    if (tipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                    {
                        hoja.Cell(fila, 12).Value = "";
                        hoja.Cell(fila, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
                        hoja.Cell(fila, 12).Style.Font.FontColor = XLColor.Gray;

                        hoja.Cell(fila, 13).Value = "";
                        hoja.Cell(fila, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#BFC9CA");
                        hoja.Cell(fila, 13).Style.Font.FontColor = XLColor.FromHtml("#566573");
                        hoja.Cell(fila, 13).Style.Font.Bold = true;

                        hoja.Cell(fila, 14).Value = "Fijo: no llenar. El sistema lo agregará automáticamente si importas un valor manual de este KPI y periodo.";
                    }
                    else if (tipoCaptura.Equals("Calculado", StringComparison.OrdinalIgnoreCase))
                    {
                        hoja.Cell(fila, 12).Value = "";
                        hoja.Cell(fila, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");

                        hoja.Cell(fila, 13).Value = "NO LLENAR";
                        hoja.Cell(fila, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#F7DC6F");
                        hoja.Cell(fila, 13).Style.Font.FontColor = XLColor.FromHtml("#7D6608");
                        hoja.Cell(fila, 13).Style.Font.Bold = true;

                        hoja.Cell(fila, 14).Value = "Calculado: no llenar. Se revisará después.";
                    }
                    else
                    {
                        hoja.Cell(fila, 12).Value = "";
                        hoja.Cell(fila, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
                        hoja.Cell(fila, 12).Style.Font.FontColor = XLColor.FromHtml("#154360");
                        hoja.Cell(fila, 12).Style.Font.Bold = true;

                        hoja.Cell(fila, 13).Value = "";
                        hoja.Cell(fila, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#82E0AA");
                        hoja.Cell(fila, 13).Style.Font.FontColor = XLColor.FromHtml("#145A32");
                        hoja.Cell(fila, 13).Style.Font.Bold = true;

                        hoja.Cell(fila, 14).Value = "Manual: escribe SI en Importar y captura el valor histórico.";
                    }

                    fila++;
                }
            }

            var columnasReferencia = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            foreach (var col in columnasReferencia)
            {
                hoja.Column(col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
                hoja.Column(col).Style.Font.FontColor = XLColor.FromHtml("#495057");
            }

            hoja.Column(9).Style.Fill.BackgroundColor = XLColor.White; // Año
            hoja.Column(10).Style.Fill.BackgroundColor = XLColor.White; // Periodo
            hoja.Column(11).Style.Fill.BackgroundColor = XLColor.White; // Fecha
            hoja.Column(12).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8"); // Importar
            hoja.Column(13).Style.Fill.BackgroundColor = XLColor.FromHtml("#A9DFBF"); // Valor
            hoja.Column(14).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F3F4"); // Notas

            for (int r = filaEncabezado + 1; r < fila; r++)
            {
                string tipo = hoja.Cell(r, 8).GetString();

                if (tipo.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                {
                    hoja.Cell(r, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
                    hoja.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#BFC9CA");
                    hoja.Cell(r, 13).Style.Font.FontColor = XLColor.FromHtml("#566573");
                    hoja.Cell(r, 13).Style.Font.Bold = true;
                }
                else if (tipo.Equals("Calculado", StringComparison.OrdinalIgnoreCase))
                {
                    hoja.Cell(r, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
                    hoja.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#F7DC6F");
                    hoja.Cell(r, 13).Style.Font.FontColor = XLColor.FromHtml("#7D6608");
                    hoja.Cell(r, 13).Style.Font.Bold = true;
                }
                else
                {
                    hoja.Cell(r, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
                    hoja.Cell(r, 12).Style.Font.FontColor = XLColor.FromHtml("#154360");
                    hoja.Cell(r, 12).Style.Font.Bold = true;

                    hoja.Cell(r, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#82E0AA");
                    hoja.Cell(r, 13).Style.Font.FontColor = XLColor.FromHtml("#145A32");
                    hoja.Cell(r, 13).Style.Font.Bold = true;
                }
            }

            hoja.Column(9).Style.NumberFormat.Format = "0";
            hoja.Column(10).Style.NumberFormat.Format = "0";
            hoja.Column(11).Style.DateFormat.Format = "yyyy-mm-dd";
            hoja.Column(13).Style.NumberFormat.Format = "0.00";

            var rangoDatos = hoja.Range(filaEncabezado, 1, Math.Max(filaEncabezado, fila - 1), encabezados.Length);

            rangoDatos.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rangoDatos.Style.Border.OutsideBorderColor = XLColor.FromHtml("#AAB7B8");
            rangoDatos.Style.Border.InsideBorderColor = XLColor.FromHtml("#D5DBDB");

            rangoDatos.SetAutoFilter();

            hoja.SheetView.FreezeRows(filaEncabezado);
            hoja.SheetView.FreezeColumns(4);

            hoja.Columns().AdjustToContents(1, 60);

            hoja.Column(1).Width = 12;   // Depto ID
            hoja.Column(2).Width = 20;   // Departamento
            hoja.Column(3).Width = 10;   // KPI ID
            hoja.Column(4).Width = 34;   // KPI
            hoja.Column(5).Width = 14;   // Frecuencia
            hoja.Column(6).Width = 12;   // Variable ID
            hoja.Column(7).Width = 34;   // Variable
            hoja.Column(8).Width = 14;   // Tipo
            hoja.Column(9).Width = 10;   // Año
            hoja.Column(10).Width = 12;  // Periodo
            hoja.Column(11).Width = 14;  // Fecha
            hoja.Column(12).Width = 12;  // Importar
            hoja.Column(13).Width = 16;  // Valor
            hoja.Column(14).Width = 60;  // Notas

            AplicarEstiloEncabezadoExcel(rangoEncabezado);
            hoja.Row(filaEncabezado).Height = 30;

            hoja.SetTabActive();
        }

        private void CrearHojaTutorialExcel(XLWorkbook workbook)
        {
            var hoja = workbook.Worksheets.Add("Tutorial");

            hoja.Cell("A1").Value = "GUÍA PARA IMPORTAR HISTÓRICOS KPI";
            hoja.Cell("A1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 18;
            hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
            hoja.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#198754");
            hoja.Range("A1:D1").Merge();

            hoja.Cell("A3").Value = "Objetivo";
            hoja.Cell("A3").Style.Font.Bold = true;
            hoja.Cell("A3").Style.Fill.BackgroundColor = XLColor.FromHtml("#D1E7DD");

            hoja.Cell("B3").Value = "Esta plantilla sirve para cargar datos históricos de KPIs existentes. Primero se previsualizan y después se confirman.";
            hoja.Range("B3:D3").Merge();
            hoja.Cell("B3").Style.Alignment.WrapText = true;

            hoja.Cell("A5").Value = "Qué puedes hacer";
            hoja.Cell("A5").Style.Font.Bold = true;
            hoja.Cell("A5").Style.Fill.BackgroundColor = XLColor.FromHtml("#CFE2FF");

            var puedes = new List<string>
            {
                "Filtrar por Departamento, KPI o Tipo.",
                "Borrar filas completas de KPIs que no vas a importar.",
                "Copiar filas de una misma métrica para capturar varios periodos históricos.",
                "Llenar Año, Periodo, Fecha, Importar y Valor.",
                "Dejar filas sin SI en Importar; el sistema las ignorará."
            };

            int fila = 6;
            foreach (var item in puedes)
            {
                hoja.Cell(fila, 1).Value = "✓";
                hoja.Cell(fila, 1).Style.Font.FontColor = XLColor.Green;
                hoja.Cell(fila, 2).Value = item;
                hoja.Range(fila, 2, fila, 4).Merge();
                fila++;
            }

            fila += 1;

            hoja.Cell(fila, 1).Value = "Qué NO debes modificar";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8D7DA");

            fila++;

            var noModificar = new List<string>
            {
                "Depto ID",
                "KPI ID",
                "Variable ID",
                "Tipo",
                "Nombre del KPI",
                "Nombre de la variable"
            };

            foreach (var item in noModificar)
            {
                hoja.Cell(fila, 1).Value = "✗";
                hoja.Cell(fila, 1).Style.Font.FontColor = XLColor.Red;
                hoja.Cell(fila, 2).Value = item;
                hoja.Range(fila, 2, fila, 4).Merge();
                fila++;
            }

            fila += 1;

            hoja.Cell(fila, 1).Value = "Cómo llenar la hoja Carga";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");

            fila++;

            var pasos = new List<string>
            {
                "1. Ve a la hoja Carga.",
                "2. Filtra por Departamento o KPI si solo quieres importar un área.",
                "3. Borra o ignora las filas que no vas a usar.",
                "4. Llena Año y Periodo para métricas Mensuales o Semanales.",
                "5. Para métricas Diarias, llena Fecha.",
                "6. En variables Manuales que sí quieras cargar, escribe SI en Importar y captura Valor.",
                "7. Si una fila Manual no tiene SI en Importar, el sistema la ignorará aunque tenga valor.",
                "8. No llenes variables Fijas ni Calculadas.",
                "9. Guarda el archivo.",
                "10. Súbelo al sistema para previsualizar.",
                "11. Revisa errores o advertencias antes de confirmar."
            };

            foreach (var paso in pasos)
            {
                hoja.Cell(fila, 1).Value = paso;
                hoja.Range(fila, 1, fila, 4).Merge();
                hoja.Cell(fila, 1).Style.Alignment.WrapText = true;
                fila++;
            }

            fila += 1;

            hoja.Cell(fila, 1).Value = "Reglas de periodo";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D1E7DD");

            fila++;

            hoja.Cell(fila, 1).Value = "Mensual";
            hoja.Cell(fila, 2).Value = "Periodo 1 a 12. Ejemplo: Enero = 1, Febrero = 2.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila++;

            hoja.Cell(fila, 1).Value = "Semanal";
            hoja.Cell(fila, 2).Value = "Periodo 1 a 53. Ejemplo: Semana 12 = 12.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila++;

            hoja.Cell(fila, 1).Value = "Diario";
            hoja.Cell(fila, 2).Value = "Llena Fecha. El sistema calculará el periodo automáticamente.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila += 2;

            hoja.Cell(fila, 1).Value = "Colores";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#CFE2FF");

            fila++;

            hoja.Cell(fila, 1).Value = "Gris";
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
            hoja.Cell(fila, 2).Value = "Información de referencia. No modificar.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila++;

            hoja.Cell(fila, 1).Value = "Azul";
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
            hoja.Cell(fila, 2).Value = "Columna Importar. Escribe SI solo en las filas manuales que quieres cargar.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila++;

            hoja.Cell(fila, 1).Value = "Verde";
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#48ca90");
            hoja.Cell(fila, 2).Value = "Campo principal para capturar valor manual.";
            hoja.Range(fila, 2, fila, 4).Merge();

            fila++;

            hoja.Cell(fila, 1).Value = "Amarillo";
            hoja.Cell(fila, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#ffd54a");
            hoja.Cell(fila, 2).Value = "Campo calculado o de revisión. No llenar manualmente.";
            hoja.Range(fila, 2, fila, 4).Merge();

            hoja.Columns().AdjustToContents();
            hoja.Column(1).Width = 28;
            hoja.Column(2).Width = 55;
            hoja.Column(3).Width = 25;
            hoja.Column(4).Width = 25;

            hoja.SheetView.FreezeRows(1);
        }

        private void CrearHojaEjemplosExcel(XLWorkbook workbook)
        {
            var hoja = workbook.Worksheets.Add("Ejemplos");

            hoja.Cell("A1").Value = "EJEMPLOS DE CAPTURA";
            hoja.Cell("A1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 16;
            hoja.Cell("A1").Style.Font.FontColor = XLColor.White;
            hoja.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#0B5ED7");
            hoja.Range("A1:I1").Merge();

            string[] encabezados =
            {
                "Departamento",
                "KPI",
                "Frecuencia",
                "Año",
                "Periodo",
                "Variable",
                "Tipo",
                "Importar",
                "Valor"
            };

            for (int i = 0; i < encabezados.Length; i++)
            {
                hoja.Cell(3, i + 1).Value = encabezados[i];
            }

            var header = hoja.Range(3, 1, 3, encabezados.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#212529");
            header.Style.Font.FontColor = XLColor.White;

            var ejemplos = new List<object[]>
            {
                new object[] { "CALIDAD", "Total de Tiempo Extra", "Semanal", 2026, 12, "Cantidad de tiempo extra", "Manual", "SI", 3900 },
                new object[] { "CALIDAD", "Total de Tiempo Extra", "Semanal", 2026, 12, "Meta", "Fijo", "", "" },
                new object[] { "COMERCIAL", "Entrega de Pedidos", "Mensual", 2026, 3, "Pedidos Entregados a Tiempo", "Manual", "SI", 85 },
                new object[] { "COMERCIAL", "Entrega de Pedidos", "Mensual", 2026, 3, "Pedidos Fuera de Tiempo", "Manual", "SI", 15 },
                new object[] { "COMERCIAL", "Entrega de Pedidos", "Mensual", 2026, 3, "Suma de Porcentajes", "Calculado", "", "NO LLENAR" },
                new object[] { "LOGISTICA", "Tiempo de Atención", "Diario", 2026, "Usar Fecha", "Tiempo de Atención", "Manual", "SI", 2 }
            };

            int fila = 4;

            foreach (var ejemplo in ejemplos)
            {
                for (int i = 0; i < ejemplo.Length; i++)
                {
                    hoja.Cell(fila, i + 1).Value = XLCellValue.FromObject(ejemplo[i]);
                }

                string tipo = ejemplo[6]?.ToString() ?? "";

                if (tipo.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    hoja.Cell(fila, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
                    hoja.Cell(fila, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#D1E7DD");
                }
                else if (tipo.Equals("Calculado", StringComparison.OrdinalIgnoreCase))
                {
                    hoja.Cell(fila, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
                }
                else if (tipo.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                {
                    hoja.Cell(fila, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
                }

                fila++;
            }

            hoja.Cell("A12").Value = "Notas:";
            hoja.Cell("A12").Style.Font.Bold = true;

            hoja.Cell("A13").Value = "1. En variables Manuales que quieras cargar escribe SI en Importar y llena Valor.";
            hoja.Cell("A14").Value = "2. En variables Fijas se deja Importar y Valor vacío.";
            hoja.Cell("A15").Value = "3. En variables Calculadas no se debe escribir un valor.";
            hoja.Cell("A16").Value = "4. Puedes copiar filas de una métrica para capturar más periodos.";

            hoja.Columns().AdjustToContents();
            hoja.SheetView.FreezeRows(3);
        }

        private void CrearHojaCatalogoExcel(XLWorkbook workbook, List<CatMetricas> metricas)
        {
            var hoja = workbook.Worksheets.Add("Catalogo");

            string[] encabezados =
            {
                "DepartamentoID",
                "Departamento",
                "MetricaID",
                "Metrica",
                "Frecuencia",
                "TipoValor",
                "MetaEsperada",
                "SentidoMeta",
                "VariableID",
                "Variable",
                "TipoCaptura",
                "ValorFijo",
                "Formula",
                "EsLinea"
            };

            for (int i = 0; i < encabezados.Length; i++)
            {
                hoja.Cell(1, i + 1).Value = encabezados[i];
            }

            var rangoEncabezado = hoja.Range(1, 1, 1, encabezados.Length);
            rangoEncabezado.Style.Font.Bold = true;
            rangoEncabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#082f69");
            rangoEncabezado.Style.Font.FontColor = XLColor.White;
            rangoEncabezado.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangoEncabezado.Style.Font.Bold = true;
            rangoEncabezado.Style.Font.FontColor = XLColor.White;
            rangoEncabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#154360");
            rangoEncabezado.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangoEncabezado.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            int fila = 2;

            foreach (var metrica in metricas)
            {
                var variables = metrica.VariablesConfiguradas
                    .OrderBy(v => v.VariableID)
                    .ToList();

                if (!variables.Any())
                {
                    hoja.Cell(fila, 1).Value = metrica.DepartamentoID;
                    hoja.Cell(fila, 2).Value = metrica.Departamento?.NombreDepartamento ?? "";
                    hoja.Cell(fila, 3).Value = metrica.MetricaID;
                    hoja.Cell(fila, 4).Value = metrica.NombreMetrica;
                    hoja.Cell(fila, 5).Value = metrica.Frecuencia;
                    hoja.Cell(fila, 6).Value = metrica.TipoValor;
                    hoja.Cell(fila, 7).Value = metrica.MetaEsperada;
                    hoja.Cell(fila, 8).Value = metrica.SentidoMeta;
                    hoja.Cell(fila, 9).Value = "";
                    hoja.Cell(fila, 10).Value = "Sin variables configuradas";
                    hoja.Cell(fila, 11).Value = "";
                    hoja.Cell(fila, 12).Value = "";
                    hoja.Cell(fila, 13).Value = "";
                    hoja.Cell(fila, 14).Value = "";

                    fila++;
                    continue;
                }

                foreach (var variable in variables)
                {
                    hoja.Cell(fila, 1).Value = metrica.DepartamentoID;
                    hoja.Cell(fila, 2).Value = metrica.Departamento?.NombreDepartamento ?? "";
                    hoja.Cell(fila, 3).Value = metrica.MetricaID;
                    hoja.Cell(fila, 4).Value = metrica.NombreMetrica;
                    hoja.Cell(fila, 5).Value = metrica.Frecuencia;
                    hoja.Cell(fila, 6).Value = metrica.TipoValor;
                    hoja.Cell(fila, 7).Value = metrica.MetaEsperada;
                    hoja.Cell(fila, 8).Value = metrica.SentidoMeta;
                    hoja.Cell(fila, 9).Value = variable.VariableID;
                    hoja.Cell(fila, 10).Value = variable.NombreVariable;
                    hoja.Cell(fila, 11).Value = string.IsNullOrWhiteSpace(variable.TipoCaptura)
                        ? "Manual"
                        : variable.TipoCaptura;
                    hoja.Cell(fila, 12).Value = variable.ValorFijo;
                    hoja.Cell(fila, 13).Value = variable.Formula;
                    hoja.Cell(fila, 14).Value = variable.EsLinea ? "Sí" : "No";

                    fila++;
                }
            }

            hoja.Columns().AdjustToContents();
            hoja.RangeUsed().SetAutoFilter();
            hoja.SheetView.FreezeRows(1);
        }

        #endregion

        #region 3. HISTORIAL DE CAPTURAS (EDICIÓN Y ELIMINACIÓN)

        [HttpGet("Historial")]
        public IActionResult Historial()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return RedirectToAction("Login", "Login");

            var permisos = CargarPermisosSubMenuUsuario();
            ViewBag.MisPermisos = permisos;
            if (!permisos.Contains(PermisoHistorial))
                return Forbid();

            var query = _context.RegistroKpis
                .Include(r => r.Metrica)
                    .ThenInclude(m => m.Departamento)
                .Include(r => r.DetallesValores)
                    .ThenInclude(v => v.Variable)
                .Where(r => r.Activo == true);

            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    query = query.Where(r => datosAcceso.Departamentos.Contains(r.Metrica.DepartamentoID));
                else
                    query = query.Where(r => false);
            }

            ViewBag.PuedeGestionarTodosDepartamentos = puedeGestionarTodos;
            ViewBag.EsUsuarioLimitado = !puedeGestionarTodos;

            var registros = query.OrderByDescending(r => r.FechaCaptura).Take(100).ToList();

            return View(registros);
        }

        // --- BORRADO LÓGICO ---
        [HttpPost("EliminarKpi")]
        public IActionResult EliminarKpi(int id)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Historial");
                }

                var registro = _context.RegistroKpis
                    .Include(r => r.Metrica)
                    .FirstOrDefault(r => r.RegistroID == id);

                if (registro != null)
                {
                    if (!UsuarioPuedeAccederDepartamento(usuarioId, registro.Metrica.DepartamentoID))
                    {
                        TempData["Mensaje"] = "No tienes permiso para archivar registros de este departamento.";
                        TempData["Tipo"] = "danger";
                        return RedirectToAction("Historial");
                    }

                    registro.Activo = false;
                    _context.SaveChanges();
    
                    TempData["Mensaje"] = "Registro archivado correctamente (Borrado lógico).";
                    TempData["Tipo"] = "success";
                }
            }
            catch (Exception ex)
            {
                TempData["Mensaje"] = "Error al archivar: " + ex.Message;
                TempData["Tipo"] = "danger";
            }
            return RedirectToAction("Historial");
        }

        [HttpPost("ActualizarKpi")]
        public IActionResult ActualizarKpi(int registroId, int? anio, int? numeroPeriodo, DateTime? fechaDiaria, Dictionary<int, decimal> valores)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("Historial");
                }

                var registro = _context.RegistroKpis
                    .Include(r => r.Metrica)
                    .Include(r => r.DetallesValores)
                    .FirstOrDefault(r => r.RegistroID == registroId);

                if (registro != null)
                {
                    if (!UsuarioPuedeAccederDepartamento(usuarioId, registro.Metrica.DepartamentoID))
                    {
                        TempData["Mensaje"] = "No tienes permiso para editar registros de este departamento.";
                        TempData["Tipo"] = "danger";
                        return RedirectToAction("Historial");
                    }

                    int periodoFinal = numeroPeriodo ?? registro.NumeroPeriodo;
                    int anioFinal = anio ?? registro.Anio;

                    if (fechaDiaria.HasValue)
                    {
                        periodoFinal = fechaDiaria.Value.DayOfYear;
                        anioFinal = fechaDiaria.Value.Year;
                    }

                    registro.Anio = anioFinal;
                    registro.NumeroPeriodo = periodoFinal;

                    if (valores != null)
                    {
                        foreach (var item in valores)
                        {
                            var detalle = registro.DetallesValores.FirstOrDefault(d => d.VariableID == item.Key);
                            if (detalle != null)
                                detalle.Valor = item.Value;
                        }
                    }

                    _context.SaveChanges();
                    TempData["Mensaje"] = "Registro editado y actualizado correctamente.";
                    TempData["Tipo"] = "success";
                }
            }
            catch (Exception ex)
            {
                TempData["Mensaje"] = "Error al actualizar: " + ex.Message;
                TempData["Tipo"] = "danger";
            }
            return RedirectToAction("Historial");
        }

        #endregion

        #region 4. GESTIÓN Y CONFIGURACIÓN DE MÉTRICAS (CRUD)

        [HttpGet("GestorMetricas")]
        public IActionResult GestorMetricas()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return RedirectToAction("Login", "Login");

            var permisos = CargarPermisosSubMenuUsuario();
            ViewBag.MisPermisos = permisos;
            if (!TieneAccesoGestor())
                return Forbid();

            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            var metricasQuery = _context.CatMetricas
                .Include(m => m.Departamento)
                .Include(m => m.VariablesConfiguradas)
                .AsQueryable();

            var departamentosQuery = _context.Departamentos
                .Where(d => d.Activo)
                .AsQueryable();

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                {
                    metricasQuery = metricasQuery.Where(m => datosAcceso.Departamentos.Contains(m.DepartamentoID));
                    departamentosQuery = departamentosQuery.Where(d => datosAcceso.Departamentos.Contains(d.DepartamentoID));
                }
                else
                {
                    metricasQuery = metricasQuery.Where(m => false);
                    departamentosQuery = departamentosQuery.Where(d => false);
                }
            }

            ViewBag.Departamentos = departamentosQuery
                .OrderBy(d => d.NombreDepartamento)
                .ToList();
            ViewBag.EsUsuarioLimitado = !puedeGestionarTodos;
            ViewBag.PuedeGestionarTodosDepartamentos = puedeGestionarTodos;

            return View(metricasQuery
                .OrderBy(m => m.Departamento.NombreDepartamento)
                .ThenBy(m => m.NombreMetrica)
                .ToList());
        }

        [HttpGet("NuevaMetrica")]
        public IActionResult NuevaMetrica()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return RedirectToAction("Login", "Login");

            var permisos = CargarPermisosSubMenuUsuario();
            ViewBag.MisPermisos = permisos;
            if (!permisos.Contains(PermisoConfigurar))
                return Forbid();

            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);
            bool puedeGestionarTodos = UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso);

            var departamentosQuery = _context.Departamentos
                .Where(d => d.Activo)
                .AsQueryable();

            if (!puedeGestionarTodos)
            {
                if (datosAcceso.Departamentos.Any())
                    departamentosQuery = departamentosQuery.Where(d => datosAcceso.Departamentos.Contains(d.DepartamentoID));
                else
                    departamentosQuery = departamentosQuery.Where(d => false);
            }

            ViewBag.PuedeGestionarTodosDepartamentos = puedeGestionarTodos;
            ViewBag.EsUsuarioLimitado = !puedeGestionarTodos;

            return View(departamentosQuery.OrderBy(d => d.NombreDepartamento).ToList());
        }

        [HttpPost("GuardarMetrica")]
        public IActionResult GuardarMetrica(
            CatMetricas nuevaMetrica,
            string? UnidadMedidaPersonalizada,
            List<string> nombresVariables,
            List<bool> variablesEsLinea,
            List<string> tiposCaptura,
            List<decimal?> valoresFijos,
            List<string> formulas,
            List<string>? tiposValorVariables,
            List<string>? unidadesMedidaVariables,
            List<string>? unidadesMedidaVariablesPersonalizadas)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("NuevaMetrica");
                }

                if (!UsuarioPuedeAccederDepartamento(usuarioId, nuevaMetrica.DepartamentoID))
                {
                    TempData["Mensaje"] = "No tienes permiso para crear KPIs en este departamento.";
                    TempData["Tipo"] = "danger";
                    return RedirectToAction("NuevaMetrica");
                }

                nuevaMetrica.UnidadMedida = ResolverUnidadMedida(nuevaMetrica.TipoValor, nuevaMetrica.UnidadMedida, UnidadMedidaPersonalizada);

                if (string.IsNullOrWhiteSpace(nuevaMetrica.TipoGraficaDefecto))
                    nuevaMetrica.TipoGraficaDefecto = "ComboChart";

                nuevaMetrica.Activo = true;

                if (nombresVariables != null)
                {
                    for (int i = 0; i < nombresVariables.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(nombresVariables[i]))
                        {
                            string? tipoValorVariable = ObtenerValorLista(tiposValorVariables, i);
                            string? unidadVariable = ObtenerValorLista(unidadesMedidaVariables, i);
                            string? unidadPersonalizadaVariable = ObtenerValorLista(unidadesMedidaVariablesPersonalizadas, i);

                            string? tipoValorFinalVariable = string.IsNullOrWhiteSpace(tipoValorVariable) || tipoValorVariable.Equals("Heredar", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : tipoValorVariable.Trim();

                            string? unidadFinalVariable = null;

                            if (!string.IsNullOrWhiteSpace(tipoValorFinalVariable))
                                unidadFinalVariable = ResolverUnidadMedida(tipoValorFinalVariable, unidadVariable, unidadPersonalizadaVariable);

                            nuevaMetrica.VariablesConfiguradas.Add(new CatMetricas_Variables
                            {
                                NombreVariable = nombresVariables[i].Trim(),
                                EsLinea = variablesEsLinea != null && variablesEsLinea.Count > i && variablesEsLinea[i],
                                TipoCaptura = tiposCaptura != null && tiposCaptura.Count > i ? tiposCaptura[i] : "Manual",
                                ValorFijo = valoresFijos != null && valoresFijos.Count > i ? valoresFijos[i] : null,
                                Formula = formulas != null && formulas.Count > i ? formulas[i] ?? string.Empty : string.Empty,
                                TipoValor = tipoValorFinalVariable,
                                UnidadMedida = unidadFinalVariable
                            });
                        }
                    }
                }

                _context.CatMetricas.Add(nuevaMetrica);
                _context.SaveChanges();

                TempData["MostrarModalExito"] = true;
                TempData["Mensaje"] = "Métrica y reglas configuradas con éxito.";
                TempData["Tipo"] = "success";

                return RedirectToAction("NuevaMetrica");
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Mensaje"] = "Error al guardar: " + innerEx;
                TempData["Tipo"] = "danger";
                return RedirectToAction("NuevaMetrica");
            }
        }

        [HttpPost("ActualizarMetricaCompleta")]
        public IActionResult ActualizarMetricaCompleta(
            int metricaId,
            string nombreMetrica,
            int departamentoId,
            string frecuencia,
            string tipoValor,
            string? unidadMedida,
            string? unidadMedidaPersonalizada,
            string tipoGraficaDefecto,
            decimal? metaEsperada,
            string sentidoMeta,

            int? variableTarjetaID,
            string? modoTarjeta,

            List<int> variablesIds,
            List<string> nombresVariables,
            List<bool> variablesEsLinea,
            List<string> tiposCaptura,
            List<decimal?> valoresFijos,
            List<string> formulas,
            List<string>? tiposValorVariables,
            List<string>? unidadesMedidaVariables,
            List<string>? unidadesMedidaVariablesPersonalizadas)
        {
            try
            {
                var metrica = _context.CatMetricas
                    .Include(m => m.VariablesConfiguradas)
                    .FirstOrDefault(m => m.MetricaID == metricaId);

                int usuarioId = ObtenerUsuarioIdActual();

                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("GestorMetricas");
                }

                if (metrica == null)
                {
                    TempData["Mensaje"] = "No se encontró la métrica.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("GestorMetricas");
                }

                if (!UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID) ||
                    !UsuarioPuedeAccederDepartamento(usuarioId, departamentoId))
                {
                    TempData["Mensaje"] = "No tienes permiso para editar o mover KPIs de este departamento.";
                    TempData["Tipo"] = "danger";
                    return RedirectToAction("GestorMetricas");
                }

                metrica.NombreMetrica = nombreMetrica;
                metrica.DepartamentoID = departamentoId;
                metrica.Frecuencia = frecuencia;
                metrica.TipoValor = tipoValor;
                metrica.UnidadMedida = ResolverUnidadMedida(tipoValor, unidadMedida, unidadMedidaPersonalizada);
                metrica.TipoGraficaDefecto = string.IsNullOrWhiteSpace(tipoGraficaDefecto)
                    ? "ComboChart"
                    : tipoGraficaDefecto;
                metrica.MetaEsperada = metaEsperada;
                metrica.SentidoMeta = sentidoMeta;

                // NUEVO: tarjeta configurable del tablero
                metrica.VariableTarjetaID = variableTarjetaID;
                metrica.ModoTarjeta = string.IsNullOrWhiteSpace(modoTarjeta)
                    ? "Automatico"
                    : modoTarjeta;

                if (nombresVariables != null)
                {
                    for (int i = 0; i < nombresVariables.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(nombresVariables[i]))
                            continue;

                        int varId = variablesIds != null && variablesIds.Count > i
                            ? variablesIds[i]
                            : 0;

                        bool esLinea = variablesEsLinea != null &&
                                        variablesEsLinea.Count > i &&
                                        variablesEsLinea[i];

                        string tipoCaptura = tiposCaptura != null &&
                                            tiposCaptura.Count > i &&
                                            !string.IsNullOrWhiteSpace(tiposCaptura[i])
                            ? tiposCaptura[i]
                            : "Manual";

                        decimal? valorFijo = valoresFijos != null && valoresFijos.Count > i
                            ? valoresFijos[i]
                            : null;

                        string formula = formulas != null && formulas.Count > i
                            ? formulas[i] ?? string.Empty
                            : string.Empty;

                        string? tipoValorVariableFormulario = tiposValorVariables != null && tiposValorVariables.Count > i
                            ? tiposValorVariables[i]
                            : null;

                        string? unidadVariableFormulario = unidadesMedidaVariables != null && unidadesMedidaVariables.Count > i
                            ? unidadesMedidaVariables[i]
                            : null;

                        string? unidadVariablePersonalizada = unidadesMedidaVariablesPersonalizadas != null &&
                                                            unidadesMedidaVariablesPersonalizadas.Count > i
                            ? unidadesMedidaVariablesPersonalizadas[i]
                            : null;

                        string? tipoValorVariable = ResolverTipoValorVariable(tipoValorVariableFormulario);

                        string? unidadMedidaVariable = ResolverUnidadMedidaVariable(
                            tipoValor,
                            metrica.UnidadMedida,
                            tipoValorVariable,
                            unidadVariableFormulario,
                            unidadVariablePersonalizada);

                        if (varId > 0)
                        {
                            var variableExistente = metrica.VariablesConfiguradas
                                .FirstOrDefault(v => v.VariableID == varId);

                            if (variableExistente != null)
                            {
                                variableExistente.NombreVariable = nombresVariables[i].Trim();
                                variableExistente.EsLinea = esLinea;
                                variableExistente.TipoCaptura = tipoCaptura;
                                variableExistente.ValorFijo = valorFijo;
                                variableExistente.Formula = formula;
                                variableExistente.TipoValor = tipoValorVariable;
                                variableExistente.UnidadMedida = unidadMedidaVariable;
                            }
                        }
                        else
                        {
                            metrica.VariablesConfiguradas.Add(new CatMetricas_Variables
                            {
                                NombreVariable = nombresVariables[i].Trim(),
                                EsLinea = esLinea,
                                TipoCaptura = tipoCaptura,
                                ValorFijo = valorFijo,
                                Formula = formula,
                                TipoValor = tipoValorVariable,
                                UnidadMedida = unidadMedidaVariable
                            });
                        }
                    }
                }

                // Si el usuario dejó como tarjeta una variable que ya no existe, limpiamos la configuración.
                if (metrica.VariableTarjetaID.HasValue &&
                    !metrica.VariablesConfiguradas.Any(v => v.VariableID == metrica.VariableTarjetaID.Value))
                {
                    metrica.VariableTarjetaID = null;
                    metrica.ModoTarjeta = "Automatico";
                }

                _context.SaveChanges();

                TempData["Mensaje"] = "Métrica, variables, metas, unidades y tarjeta del tablero actualizadas correctamente.";
                TempData["Tipo"] = "success";
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Mensaje"] = "Error al actualizar: " + innerEx;
                TempData["Tipo"] = "danger";
            }

            return RedirectToAction("GestorMetricas");
        }

        private string? ResolverTipoValorVariable(string? tipoValorVariable)
        {
            if (string.IsNullOrWhiteSpace(tipoValorVariable))
                return null;

            tipoValorVariable = tipoValorVariable.Trim();

            if (tipoValorVariable.Equals("Heredar", StringComparison.OrdinalIgnoreCase))
                return null;

            if (tipoValorVariable.Equals("Porcentaje", StringComparison.OrdinalIgnoreCase))
                return "Porcentaje";

            if (tipoValorVariable.Equals("Moneda", StringComparison.OrdinalIgnoreCase))
                return "Moneda";

            if (tipoValorVariable.Equals("Entero", StringComparison.OrdinalIgnoreCase))
                return "Entero";

            return null;
        }

        private string? ResolverUnidadMedidaVariable(
            string tipoValorKpi,
            string? unidadMedidaKpi,
            string? tipoValorVariable,
            string? unidadMedidaVariable,
            string? unidadMedidaVariablePersonalizada)
        {
            if (string.IsNullOrWhiteSpace(unidadMedidaVariable))
                return null;

            unidadMedidaVariable = unidadMedidaVariable.Trim();

            if (unidadMedidaVariable.Equals("Heredar", StringComparison.OrdinalIgnoreCase))
                return null;

            string tipoValorEfectivo = !string.IsNullOrWhiteSpace(tipoValorVariable) ? tipoValorVariable : tipoValorKpi;

            string unidadResuelta = ResolverUnidadMedida(tipoValorEfectivo, unidadMedidaVariable, unidadMedidaVariablePersonalizada);

            if (!string.IsNullOrWhiteSpace(unidadMedidaKpi) &&
                unidadResuelta.Equals(unidadMedidaKpi, StringComparison.OrdinalIgnoreCase))
                return null;

            return unidadResuelta;
        }

        [HttpPost("EliminarMetrica")]
        public IActionResult EliminarMetrica(int id)
        {
            try
            {
                int usuarioId = ObtenerUsuarioIdActual();
                if (usuarioId == 0)
                {
                    TempData["Mensaje"] = "Sesión expirada.";
                    TempData["Tipo"] = "warning";
                    return RedirectToAction("GestorMetricas");
                }

                var metrica = _context.CatMetricas.Find(id);
                if (metrica != null)
                {
                    if (!UsuarioPuedeAccederDepartamento(usuarioId, metrica.DepartamentoID))
                    {
                        TempData["Mensaje"] = "No tienes permiso para eliminar KPIs de este departamento.";
                        TempData["Tipo"] = "danger";
                        return RedirectToAction("GestorMetricas");
                    }

                    _context.CatMetricas.Remove(metrica);
                    _context.SaveChanges();

                    TempData["Mensaje"] = "Métrica eliminada permanentemente.";
                    TempData["Tipo"] = "success";
                }
            }
            catch (Exception)
            {
                TempData["Mensaje"] = "No se puede eliminar la métrica porque ya tiene historial capturado. Contacte a TI o elimine primero sus registros.";
                TempData["Tipo"] = "warning";
            }
            return RedirectToAction("GestorMetricas");
        }

        #endregion

        #region 5. MÉTODOS AUXILIARES (HELPERS)

        private bool UsuarioTieneDepartamentoOperaciones((int RolID, List<int> Departamentos) datosAcceso)
        {
            if (datosAcceso.Departamentos == null || !datosAcceso.Departamentos.Any())
                return false;

            return _context.Departamentos.Any(d =>
                d.Activo &&
                datosAcceso.Departamentos.Contains(d.DepartamentoID) &&
                d.NombreDepartamento != null &&
                d.NombreDepartamento.ToUpper().Contains("OPERACIONES"));
        }

        private bool UsuarioPuedeGestionarTodosLosDepartamentos((int RolID, List<int> Departamentos) datosAcceso)
        {
            if (datosAcceso.RolID == 1)
                return true;

            return UsuarioTieneDepartamentoOperaciones(datosAcceso);
        }

        private bool UsuarioPuedeAccederDepartamento(int usuarioId, int departamentoId)
        {
            var datosAcceso = ObtenerDatosAccesoUsuario(usuarioId);

            if (UsuarioPuedeGestionarTodosLosDepartamentos(datosAcceso))
                return true;

            return datosAcceso.Departamentos.Contains(departamentoId);
        }

        private void UpsertValorRegistro(
            RegistroKpi registro,
            int variableId,
            decimal valor,
            ref int valoresInsertados,
            ref int valoresActualizados)
        {
            var valorExistente = registro.DetallesValores
                .FirstOrDefault(v => v.VariableID == variableId);

            if (valorExistente == null)
            {
                registro.DetallesValores.Add(new RegistroKpis_Valores
                {
                    RegistroID = registro.RegistroID,
                    VariableID = variableId,
                    Valor = valor
                });

                valoresInsertados++;
            }
            else
            {
                valorExistente.Valor = valor;
                valoresActualizados++;
            }
        }

        private void AgregarVariablesFijasYCalculadas(
            RegistroKpi registro,
            CatMetricas metrica,
            ref int valoresInsertados,
            ref int valoresActualizados)
        {
            var variables = metrica.VariablesConfiguradas
                .OrderBy(v => v.VariableID)
                .ToList();

            foreach (var variableFija in variables.Where(v =>
                v.TipoCaptura != null &&
                v.TipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase) &&
                v.ValorFijo.HasValue))
            {
                UpsertValorRegistro(
                    registro,
                    variableFija.VariableID,
                    variableFija.ValorFijo.Value,
                    ref valoresInsertados,
                    ref valoresActualizados);
            }

            foreach (var variableCalculada in variables.Where(v =>
                v.TipoCaptura != null &&
                v.TipoCaptura.Equals("Calculado", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(v.Formula)))
            {
                var valorCalculado = EvaluarFormulaKpi(
                    variableCalculada.Formula,
                    variables,
                    registro.DetallesValores.ToList());

                if (valorCalculado.HasValue)
                {
                    UpsertValorRegistro(
                        registro,
                        variableCalculada.VariableID,
                        valorCalculado.Value,
                        ref valoresInsertados,
                        ref valoresActualizados);
                }
            }
        }

        private decimal? EvaluarFormulaKpi(
            string formula,
            List<CatMetricas_Variables> variablesOrdenadas,
            List<RegistroKpis_Valores> valoresRegistro)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return null;

            string expresion = formula;

            for (int i = 0; i < variablesOrdenadas.Count; i++)
            {
                int indiceFormula = i + 1;
                int variableId = variablesOrdenadas[i].VariableID;

                var valor = valoresRegistro
                    .FirstOrDefault(v => v.VariableID == variableId)
                    ?.Valor;

                if (!valor.HasValue)
                    return null;

                expresion = expresion.Replace(
                    $"[{indiceFormula}]",
                    valor.Value.ToString(CultureInfo.InvariantCulture));
            }

            try
            {
                var resultado = new DataTable().Compute(expresion, null);

                if (resultado == null || resultado == DBNull.Value)
                    return null;

                return Convert.ToDecimal(resultado, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        // Busca el RolID y todos los departamentos activos asignados al usuario en EmpleadoDepartamentos
        private (int RolID, List<int> Departamentos) ObtenerDatosAccesoUsuario(int usuarioId)
        {
            int rolId = 0;
            var deptos = new List<int>();

            string cnnString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(cnnString);
            conn.Open();
            
            // 1. Obtener Rol
            string sqlRol = "SELECT TOP 1 RolID FROM Usuarios WHERE UsuarioID = @UsuarioID";
            using (var cmdRol = new SqlCommand(sqlRol, conn))
            {
                cmdRol.Parameters.AddWithValue("@UsuarioID", usuarioId);
                var res = cmdRol.ExecuteScalar();
                if (res != null && res != DBNull.Value) rolId = Convert.ToInt32(res);
            }

            // 2. Obtener todos los departamentos activos asignados a ese usuario
            string sqlDeptos = "SELECT DepartamentoID FROM EmpleadoDepartamentos WHERE UsuarioID = @UsuarioID AND Activo = 1";
            using (var cmdDeptos = new SqlCommand(sqlDeptos, conn))
            {
                cmdDeptos.Parameters.AddWithValue("@UsuarioID", usuarioId);
                using var rd = cmdDeptos.ExecuteReader();
                while (rd.Read())
                {
                    if (rd["DepartamentoID"] != DBNull.Value)
                        deptos.Add(Convert.ToInt32(rd["DepartamentoID"]));
                }
            }

            return (rolId, deptos);
        }

        private int ObtenerUsuarioIdActual()
        {
            var claim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int usuarioId))
                return usuarioId;

            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private int? ObtenerEmpresaIdActual()
        {
            var empresaSeleccionada = HttpContext.Session.GetInt32("EmpresaSeleccionada");
            if (empresaSeleccionada.HasValue)
                return empresaSeleccionada.Value;
            var empresaId = HttpContext.Session.GetInt32("EmpresaID");
            if (empresaId.HasValue)
                return empresaId.Value;

            var claim = User?.FindFirst("EmpresaID")?.Value;
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int empresaIdClaim))
                return empresaIdClaim;

            return null;
        }

        private List<int> ObtenerPermisosSubMenuUsuario()
        {
            int usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId == 0)
                return new List<int>();

            var empresaId = ObtenerEmpresaIdActual();
            var permisos = new List<int>();

            // Usar IConfiguration para evitar errores "sa"
            string cnnString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(cnnString);
            conn.Open();

            var sql = @"
                SELECT SubMenuID
                FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID, @EmpresaID)
                WHERE TienePermiso = 1;
            ";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            var pEmpresa = cmd.Parameters.Add("@EmpresaID", SqlDbType.Int);
            pEmpresa.Value = (object?)empresaId ?? DBNull.Value;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                permisos.Add(reader.GetInt32(0));
            }

            return permisos;
        }

        private List<int> CargarPermisosSubMenuUsuario()
        {
            if (_misPermisosCache != null)
                return _misPermisosCache;

            _misPermisosCache = ObtenerPermisosSubMenuUsuario();
            return _misPermisosCache;
        }

        private bool TienePermisoSubMenu(int subMenuId)
        {
            return CargarPermisosSubMenuUsuario().Contains(subMenuId);
        }

        private bool TieneAccesoCaptura()
        {
            return TienePermisoSubMenu(PermisoCapturar)
                || TienePermisoSubMenu(PermisoConfigurar)
                || TienePermisoSubMenu(PermisoGestor);
        }

        private bool TieneAccesoGestor()
        {
            return TienePermisoSubMenu(PermisoGestor)
                || TienePermisoSubMenu(PermisoConfigurar);
        }

        private void CargarPermisosViewBag()
        {
            ViewBag.MisPermisos = CargarPermisosSubMenuUsuario();
        }

        private string FormatearEtiquetaPeriodo(string frecuencia, int numeroPeriodo, int anio)
        {
            if (frecuencia == "Mensual")
            {
                string[] meses = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
                return (numeroPeriodo >= 1 && numeroPeriodo <= 12) ? meses[numeroPeriodo - 1] : $"M-{numeroPeriodo}";
            }
            else if (frecuencia == "Semanal")
            {
                return $"Semana {numeroPeriodo}";
            }
            else if (frecuencia == "Diario")
            {
                try {
                    DateTime fecha = new DateTime(anio, 1, 1).AddDays(numeroPeriodo - 1);
                    return fecha.ToString("dd/MMM");
                } catch {
                    return $"Día {numeroPeriodo}";
                }
            }
            return $"P-{numeroPeriodo}";
        }

        private int ObtenerSemanaDelAno(DateTime fecha)
        {
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(fecha, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        private (int inicio, int fin) ObtenerRangoDiasDelAnoPorSemana(int semana)
        {
            int inicio = (semana - 1) * 7 + 1;
            int fin = semana * 7;
            int diasMaximos = DateTime.IsLeapYear(DateTime.Now.Year) ? 366 : 365;
            return (Math.Max(1, inicio), Math.Min(fin, diasMaximos));
        }

        private int ObtenerIndiceColumna(DataTable tabla, params string[] nombres)
        {
            foreach (DataColumn columna in tabla.Columns)
            {
                string nombreColumna = NormalizarTexto(columna.ColumnName);

                foreach (var nombre in nombres)
                {
                    if (nombreColumna == NormalizarTexto(nombre))
                        return columna.Ordinal;
                }
            }

            return -1;
        }

        private string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            texto = texto.Trim().ToLowerInvariant();

            texto = texto
                .Replace("á", "a")
                .Replace("é", "e")
                .Replace("í", "i")
                .Replace("ó", "o")
                .Replace("ú", "u")
                .Replace("ñ", "n")
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "");

            return texto;
        }


        private bool EsFilaMarcadaParaImportar(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return false;

            string texto = NormalizarTexto(valor);

            return texto == "si"
                || texto == "s"
                || texto == "x"
                || texto == "1"
                || texto == "true"
                || texto == "yes";
        }

        private int? ConvertirEntero(object valor)
        {
            if (valor == null || valor == DBNull.Value)
                return null;

            string texto = valor.ToString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(texto))
                return null;

            if (int.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out int entero))
                return entero;

            if (decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decimalValor))
                return Convert.ToInt32(decimalValor);

            if (decimal.TryParse(texto, NumberStyles.Any, new CultureInfo("es-MX"), out decimal decimalMx))
                return Convert.ToInt32(decimalMx);

            return null;
        }

        private decimal? ConvertirDecimal(object valor)
        {
            if (valor == null || valor == DBNull.Value)
                return null;

            string texto = valor.ToString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(texto))
                return null;

            texto = texto.Replace("%", "").Trim();

            if (decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decimalValor))
                return decimalValor;

            if (decimal.TryParse(texto, NumberStyles.Any, new CultureInfo("es-MX"), out decimal decimalMx))
                return decimalMx;

            return null;
        }

        private DateTime? ConvertirFecha(object valor)
        {
            if (valor == null || valor == DBNull.Value)
                return null;

            if (valor is DateTime fechaDirecta)
                return fechaDirecta;

            string texto = valor.ToString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(texto))
                return null;

            if (DateTime.TryParse(texto, new CultureInfo("es-MX"), DateTimeStyles.None, out DateTime fechaMx))
                return fechaMx;

            if (DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fechaInvariant))
                return fechaInvariant;

            if (double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out double numeroExcel))
            {
                try
                {
                    return DateTime.FromOADate(numeroExcel);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private int ObtenerPeriodoImportacion(string frecuencia, int anio, int? periodo, DateTime? fecha)
        {
            frecuencia = frecuencia?.Trim() ?? "";

            if (frecuencia.Equals("Diario", StringComparison.OrdinalIgnoreCase))
            {
                if (fecha.HasValue)
                    return fecha.Value.DayOfYear;

                return periodo ?? 0;
            }

            if (frecuencia.Equals("Mensual", StringComparison.OrdinalIgnoreCase))
            {
                int valor = periodo ?? 0;

                if (valor >= 1 && valor <= 12)
                    return valor;

                return 0;
            }

            if (frecuencia.Equals("Semanal", StringComparison.OrdinalIgnoreCase))
            {
                int valor = periodo ?? 0;

                if (valor >= 1 && valor <= 53)
                    return valor;

                return 0;
            }

            return periodo ?? 0;
        }

        private void RecalcularEstadosPorGrupoImportacion(List<ImportacionKpiDetalle> detalles)
        {
            var grupos = detalles
                .Where(d =>
                    d.MetricaID.HasValue &&
                    d.Anio.HasValue &&
                    d.NumeroPeriodo.HasValue &&
                    d.Estado != "Error")
                .GroupBy(d => new
                {
                    MetricaID = d.MetricaID.Value,
                    Anio = d.Anio.Value,
                    NumeroPeriodo = d.NumeroPeriodo.Value
                });

            foreach (var grupo in grupos)
            {
                bool tieneManualValida = grupo.Any(d =>
                    d.TipoCaptura != null &&
                    d.TipoCaptura.Equals("Manual", StringComparison.OrdinalIgnoreCase) &&
                    d.Estado == "Valida" &&
                    d.Valor.HasValue);

                if (!tieneManualValida)
                {
                    foreach (var detalle in grupo)
                    {
                        if (detalle.TipoCaptura != null &&
                            detalle.TipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase) &&
                            detalle.Estado == "Valida")
                        {
                            detalle.Estado = "Ignorada";
                            detalle.Observacion = "Valor fijo ignorado porque el periodo no tiene ningún dato manual válido.";
                        }

                        if (detalle.TipoCaptura != null &&
                            detalle.TipoCaptura.Equals("Calculado", StringComparison.OrdinalIgnoreCase) &&
                            detalle.Estado == "Advertencia")
                        {
                            detalle.Estado = "Ignorada";
                            detalle.Observacion = "Variable calculada ignorada porque el periodo no tiene ningún dato manual válido.";
                        }
                    }
                }
            }
        }

        private string ResolverUnidadMedida(string? tipoValor, string? unidadMedida, string? unidadMedidaPersonalizada = null)
        {
            tipoValor = tipoValor?.Trim() ?? "";
            unidadMedida = unidadMedida?.Trim() ?? "";
            unidadMedidaPersonalizada = unidadMedidaPersonalizada?.Trim() ?? "";

            if (tipoValor.Equals("Porcentaje", StringComparison.OrdinalIgnoreCase))
                return "%";

            if (tipoValor.Equals("Moneda", StringComparison.OrdinalIgnoreCase))
                return "$";

            if (tipoValor.Equals("Entero", StringComparison.OrdinalIgnoreCase))
            {
                if (unidadMedida.Equals("Otro", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(unidadMedidaPersonalizada))
                    return unidadMedidaPersonalizada;

                if (!string.IsNullOrWhiteSpace(unidadMedida) && !unidadMedida.Equals("Otro", StringComparison.OrdinalIgnoreCase))
                    return unidadMedida;

                return "Unidades";
            }

            return string.IsNullOrWhiteSpace(unidadMedida) ? "Unidades" : unidadMedida;
        }

        private string? ObtenerValorLista(List<string>? lista, int index)
        {
            if (lista == null || lista.Count <= index)
                return null;

            return string.IsNullOrWhiteSpace(lista[index]) ? null : lista[index].Trim();
        }

        #endregion
    }

}