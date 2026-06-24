using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class OperacionesKpis
    {
    }

    // ==========================================================
    // INDEX / DASHBOARD PRINCIPAL
    // ==========================================================

    public class OperacionesIndexVm
    {
        public bool PuedeCapturar { get; set; }
        public bool PuedeConfigurar { get; set; }
        public bool PuedeVerHistorial { get; set; }
        public bool PuedeGestionarTodosDepartamentos { get; set; }

        public int? DepartamentoSeleccionadoID { get; set; }
        public string FiltroTiempo { get; set; } = "anio";
        public int? Mes { get; set; }
        public int? Semana { get; set; }
        public string? Dia { get; set; }

        public List<OperacionesDepartamentoVm> Departamentos { get; set; } = new();
        public OperacionesDashboardVm Dashboard { get; set; } = new();
    }

    public class OperacionesDepartamentoVm
    {
        public int DepartamentoID { get; set; }
        public string NombreDepartamento { get; set; } = string.Empty;
        public int TotalKpis { get; set; }
        public int KpisConDatos { get; set; }
        public decimal PorcentajeCumplimiento { get; set; }
        public bool Seleccionado { get; set; }
    }

    public class OperacionesDashboardVm
    {
        public int? DepartamentoID { get; set; }
        public string Departamento { get; set; } = "Vista general";

        public int TotalKpis { get; set; }
        public int KpisCumplidos { get; set; }
        public int KpisNoCumplidos { get; set; }
        public int KpisSinMeta { get; set; }
        public decimal PorcentajeCumplimiento { get; set; }

        public List<OperacionesTarjetaResumenVm> Tarjetas { get; set; } = new();
        public List<OperacionesKpiDashboardVm> Kpis { get; set; } = new();
    }

    public class OperacionesTarjetaResumenVm
    {
        public string Titulo { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
        public string Subtitulo { get; set; } = string.Empty;
        public string Icono { get; set; } = "fas fa-chart-line";
        public string EstadoVisual { get; set; } = "neutral";
    }

    // ==========================================================
    // KPI DASHBOARD
    // ==========================================================

    public class OperacionesKpiDashboardVm
    {
        public int MetricaID { get; set; }
        public int DepartamentoID { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public string NombreMetrica { get; set; } = string.Empty;
        public string Frecuencia { get; set; } = "Mensual";
        public string TipoValor { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public string TipoGrafica { get; set; } = "ComboChart";
        public string TamanoGrafica { get; set; } = "Normal";

        public decimal? MetaEsperada { get; set; }
        public string? SentidoMeta { get; set; }
        public bool MostrarMetaEnGrafica { get; set; } = true;

        public string ModoCalculoKpi { get; set; } = "Promedio";
        public int? VariableTarjetaID { get; set; }
        public string ModoTarjeta { get; set; } = "Automatico";
        public int? VariableNumeradorID { get; set; }
        public int? VariableDenominadorID { get; set; }
        public int? VariablePesoID { get; set; }

        public OperacionesResultadoKpiVm Resultado { get; set; } = new();
        public List<OperacionesKpiPeriodoVm> Historial { get; set; } = new();
    }

    public class OperacionesResultadoKpiVm
    {
        public int MetricaID { get; set; }
        public string NombreMetrica { get; set; } = string.Empty;
        public decimal Resultado { get; set; }
        public string ResultadoFormateado { get; set; } = string.Empty;
        public decimal? Meta { get; set; }
        public string MetaFormateada { get; set; } = string.Empty;
        public bool? CumpleMeta { get; set; }
        public string TipoValor { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public string ModoCalculo { get; set; } = string.Empty;
        public string DescripcionCalculo { get; set; } = string.Empty;
        public string EstadoVisual { get; set; } = "neutral";
    }

    public class OperacionesKpiPeriodoVm
    {
        public string Periodo { get; set; } = string.Empty;
        public int Anio { get; set; }
        public int NumeroPeriodo { get; set; }
        public decimal? ResultadoKpi { get; set; }
        public string ResultadoKpiFormateado { get; set; } = string.Empty;
        public List<OperacionesKpiVariablePeriodoVm> Variables { get; set; } = new();
    }

    public class OperacionesKpiVariablePeriodoVm
    {
        public int VariableID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string ValorFormateado { get; set; } = string.Empty;
        public bool EsLinea { get; set; }
        public string TipoCaptura { get; set; } = "Manual";
        public string TipoValor { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public string TipoAgregacion { get; set; } = "Promedio";
    }

    // ==========================================================
    // GESTOR DE KPIS
    // ==========================================================

    public class OperacionesGestorIndexVm
    {
        public bool PuedeGestionarTodosDepartamentos { get; set; }
        public List<SelectListItem> Departamentos { get; set; } = new();
        public List<OperacionesKpiEditorVm> Kpis { get; set; } = new();
    }

    public class OperacionesKpiEditorVm
    {
        public int MetricaID { get; set; }

        [Required(ErrorMessage = "El nombre del KPI es obligatorio.")]
        [StringLength(200)]
        public string NombreMetrica { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debe seleccionar un departamento.")]
        public int DepartamentoID { get; set; }

        public string NombreDepartamento { get; set; } = string.Empty;

        [Required]
        public string Frecuencia { get; set; } = "Mensual";

        [Required]
        public string TipoValor { get; set; } = "Porcentaje";

        public string? UnidadMedida { get; set; }
        public decimal? MetaEsperada { get; set; }
        public string? SentidoMeta { get; set; }
        public string TipoGraficaDefecto { get; set; } = "ComboChart";

        public int? VariableTarjetaID { get; set; }
        public string ModoTarjeta { get; set; } = "Automatico";
        public string ModoCalculoKpi { get; set; } = "Promedio";
        public int? VariableNumeradorID { get; set; }
        public int? VariableDenominadorID { get; set; }
        public int? VariablePesoID { get; set; }
        public bool MostrarMetaEnGrafica { get; set; } = true;
        public string TamanoGrafica { get; set; } = "Normal";
        public bool Activo { get; set; } = true;

        public List<OperacionesVariableEditorVm> Variables { get; set; } = new();
        public List<SelectListItem> DepartamentosDisponibles { get; set; } = new();
    }

    public class OperacionesVariableEditorVm
    {
        public int VariableID { get; set; }
        public int MetricaID { get; set; }

        [Required(ErrorMessage = "El nombre de la variable es obligatorio.")]
        [StringLength(200)]
        public string NombreVariable { get; set; } = string.Empty;

        public bool EsLinea { get; set; }
        public string? TipoValor { get; set; }
        public string? UnidadMedida { get; set; }
        public string TipoCaptura { get; set; } = "Manual";
        public decimal? ValorFijo { get; set; }
        public string? Formula { get; set; }
        public string TipoAgregacion { get; set; } = "Promedio";
        public int OrdenVisual { get; set; }
        public bool Activa { get; set; } = true;
    }

    public class OperacionesGuardarKpiPostVm
    {
        public int MetricaID { get; set; }
        public string NombreMetrica { get; set; } = string.Empty;
        public int DepartamentoID { get; set; }
        public string Frecuencia { get; set; } = "Mensual";
        public string TipoValor { get; set; } = "Porcentaje";
        public string? UnidadMedida { get; set; }
        public string? UnidadMedidaPersonalizada { get; set; }
        public decimal? MetaEsperada { get; set; }
        public string? SentidoMeta { get; set; }
        public string TipoGraficaDefecto { get; set; } = "ComboChart";
        public int? VariableTarjetaID { get; set; }
        public string ModoTarjeta { get; set; } = "Automatico";
        public string ModoCalculoKpi { get; set; } = "Promedio";
        public int? VariableNumeradorID { get; set; }
        public int? VariableDenominadorID { get; set; }
        public int? VariablePesoID { get; set; }
        public bool MostrarMetaEnGrafica { get; set; } = true;
        public string TamanoGrafica { get; set; } = "Normal";
        public List<OperacionesVariableEditorVm> Variables { get; set; } = new();
    }

    // ==========================================================
    // CAPTURA MANUAL
    // ==========================================================

    public class OperacionesCapturaIndexVm
    {
        public bool PuedeGestionarTodosDepartamentos { get; set; }
        public bool DepartamentoBloqueado { get; set; }
        public int? DepartamentoUsuarioID { get; set; }
        public List<SelectListItem> Departamentos { get; set; } = new();
        public List<OperacionesKpiCapturaResumenVm> Kpis { get; set; } = new();
    }

    public class OperacionesKpiCapturaResumenVm
    {
        public int MetricaID { get; set; }
        public int DepartamentoID { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public string NombreMetrica { get; set; } = string.Empty;
        public string Frecuencia { get; set; } = "Mensual";
        public string TipoValor { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public List<OperacionesVariableCapturaVm> VariablesManuales { get; set; } = new();
    }

    public class OperacionesVariableCapturaVm
    {
        public int VariableID { get; set; }
        public string NombreVariable { get; set; } = string.Empty;
        public string TipoValor { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public bool Requerida { get; set; } = true;
    }

    public class OperacionesGuardarCapturaPostVm
    {
        [Required]
        public int MetricaID { get; set; }
        public int? Anio { get; set; }
        public int? NumeroPeriodo { get; set; }
        public DateTime? FechaDiaria { get; set; }
        public Dictionary<int, decimal> Valores { get; set; } = new();
    }

    // ==========================================================
    // HISTORIAL
    // ==========================================================

    public class OperacionesHistorialIndexVm
    {
        public bool PuedeGestionarTodosDepartamentos { get; set; }
        public List<SelectListItem> Departamentos { get; set; } = new();
        public List<OperacionesRegistroHistorialVm> Registros { get; set; } = new();
    }

    public class OperacionesRegistroHistorialVm
    {
        public int RegistroID { get; set; }
        public int MetricaID { get; set; }
        public string NombreMetrica { get; set; } = string.Empty;
        public int DepartamentoID { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int Anio { get; set; }
        public int NumeroPeriodo { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public DateTime FechaCaptura { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public List<OperacionesKpiVariablePeriodoVm> Valores { get; set; } = new();
    }

    // ==========================================================
    // IMPORTACION EXCEL
    // ==========================================================

    public class OperacionesImportarIndexVm
    {
        public bool PuedeGestionarTodosDepartamentos { get; set; }
        public List<SelectListItem> Departamentos { get; set; } = new();
        public List<SelectListItem> Kpis { get; set; } = new();
        public List<OperacionesImportacionResumenVm> ImportacionesRecientes { get; set; } = new();
    }

    public class OperacionesImportacionResumenVm
    {
        public int ImportacionID { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public DateTime FechaImportacion { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int TotalFilas { get; set; }
        public int FilasValidas { get; set; }
        public int FilasConError { get; set; }
        public string? Observaciones { get; set; }
    }

    public class OperacionesImportacionDetalleVm
    {
        public int DetalleImportacionID { get; set; }
        public int ImportacionID { get; set; }
        public int NumeroFilaExcel { get; set; }
        public int? DepartamentoID { get; set; }
        public string? Departamento { get; set; }
        public int? MetricaID { get; set; }
        public string? NombreMetrica { get; set; }
        public int? VariableID { get; set; }
        public string? NombreVariable { get; set; }
        public string? Frecuencia { get; set; }
        public int? Anio { get; set; }
        public int? NumeroPeriodo { get; set; }
        public DateTime? FechaMedicion { get; set; }
        public decimal? Valor { get; set; }
        public string Estado { get; set; } = "Pendiente";
        public string? Observacion { get; set; }
    }

    public class OperacionesPrevisualizarImportacionVm
    {
        public OperacionesImportacionResumenVm Importacion { get; set; } = new();
        public List<OperacionesImportacionDetalleVm> Detalles { get; set; } = new();
    }

    public class OperacionesComprasKpiVm
    {
        public int TotalSolicitudes { get; set; }
        public int TotalCerradas { get; set; }
        public int TotalRetrasadas { get; set; }
        public int TotalATiempo { get; set; }

        public decimal CumplimientoSla { get; set; }
        public decimal MetaCumplimientoSla { get; set; } = 90;

        public decimal PromedioDiasCompra { get; set; }
        public decimal PromedioDiasPermitidos { get; set; }

        public List<OperacionesComprasKpiDepartamentoVm> PorDepartamento { get; set; } = new();
        public List<OperacionesComprasKpiEtapaVm> CuelloBotella { get; set; } = new();
        public List<OperacionesComprasKpiCompradorVm> PorComprador { get; set; } = new();
    }

    public class OperacionesComprasKpiDepartamentoVm
    {
        public string Departamento { get; set; } = "";
        public int TotalSolicitudes { get; set; }
        public int Cerradas { get; set; }
        public int Retrasadas { get; set; }
        public int ATiempo { get; set; }
        public decimal PromedioDias { get; set; }
        public decimal PromedioDiasPermitidos { get; set; }
        public decimal CumplimientoSla { get; set; }
    }

    public class OperacionesComprasKpiEtapaVm
    {
        public string Etapa { get; set; } = "";
        public decimal PromedioDias { get; set; }
    }

    public class OperacionesComprasKpiCompradorVm
    {
        public string Comprador { get; set; } = "";
        public int TotalSolicitudes { get; set; }
        public int Cerradas { get; set; }
        public int Retrasadas { get; set; }
        public decimal PromedioDias { get; set; }
        public decimal CumplimientoSla { get; set; }
    }

    public class OperacionesComprasSeguimientoRowVm
    {
        public int SolicitudID { get; set; }
        public string Folio { get; set; } = "";
        public DateTime FechaCreacion { get; set; }

        public string Departamento { get; set; } = "";
        public string CompradorAsignado { get; set; } = "";
        public string Estatus { get; set; } = "";
        public int EstatusID { get; set; }

        public int DiasPermitidos { get; set; }
        public int DiasHabilesTranscurridos { get; set; }
        public string SemaforoTexto { get; set; } = "";

        public int DiasCompras { get; set; }
        public int DiasPresupuesto { get; set; }
        public int DiasOC { get; set; }
        public int DiasProveedor { get; set; }
        public int DiasAlmacen { get; set; }
    }
}
