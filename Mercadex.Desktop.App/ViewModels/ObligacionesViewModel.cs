using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

using System.Collections.ObjectModel;
using System.Globalization;

namespace Mercadex.Desktop.App.ViewModels;

public partial class ObligacionesViewModel : ObservableObject
{
    private readonly ObligacionesService _obligacionesService = new();
    private readonly IngresosService _ingresosService = new();

    private readonly List<FilaObligacion> _todas = new();

    public ObservableCollection<FilaObligacion> Obligaciones { get; }
        = new();

    public ObservableCollection<UnidadIngresoModel> Unidades { get; }
        = new();

    public ObservableCollection<ConceptoIngresoModel> Conceptos { get; }
        = new();

    public ObservableCollection<MonedaIngresoModel> Monedas { get; }
        = new();

    public List<int> Meses { get; } =
        Enumerable.Range(1, 12).ToList();

    public List<int> Anios { get; } =
        Enumerable.Range(
                DateTime.Today.Year - 2,
                6)
            .ToList();

    public List<string> Estados { get; } =
    [
        "Todos",
        "Pendiente",
        "Pagada",
        "Anulada"
    ];

    [ObservableProperty]
    private UnidadIngresoModel? unidadSeleccionada;

    [ObservableProperty]
    private ConceptoIngresoModel? conceptoSeleccionado;

    [ObservableProperty]
    private MonedaIngresoModel? monedaSeleccionada;

    [ObservableProperty]
    private int mesSeleccionado = DateTime.Today.Month;

    [ObservableProperty]
    private int anioSeleccionado = DateTime.Today.Year;

    // Cargo eventual: sin mes/año. Permite registrar el mismo
    // concepto varias veces en el mes (p. ej. ingreso para acomodar,
    // excedente de luz). Las mensualidades siguen protegidas
    // contra duplicados por período.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodoHabilitado))]
    private bool cargoEventual;

    public bool PeriodoHabilitado => !CargoEventual;

    [ObservableProperty]
    private string montoTexto = string.Empty;

    [ObservableProperty]
    private DateTime? fechaEmision = DateTime.Today;

    [ObservableProperty]
    private DateTime? fechaVencimiento =
        DateTime.Today.AddDays(30);

    [ObservableProperty]
    private string mensaje =
        "Preparado para consultar obligaciones.";

    [ObservableProperty]
    private string busqueda = string.Empty;

    [ObservableProperty]
    private string estadoSeleccionado = "Todos";

    [ObservableProperty]
    private bool cargando;

    [ObservableProperty]
    private int totalObligaciones;

    [ObservableProperty]
    private int totalPendientes;

    [ObservableProperty]
    private decimal totalPendienteMonto;

    partial void OnBusquedaChanged(string value)
    {
        Filtrar();
    }

    partial void OnEstadoSeleccionadoChanged(string value)
    {
        Filtrar();
    }

    [RelayCommand]
    private async Task CargarAsync()
    {
        if (Cargando)
            return;

        try
        {
            Cargando = true;

            Mensaje =
                "Consultando obligaciones y catálogos...";

            var obligaciones =
                await _obligacionesService.ObtenerAsync();

            var unidades =
                await _ingresosService.ObtenerUnidadesAsync();

            var monedas =
                await _ingresosService.ObtenerMonedasAsync();

            var catalogos =
                await _ingresosService.ObtenerCatalogosAsync();

            CargarCatalogos(
                unidades,
                monedas,
                catalogos);

            ConstruirFilas(obligaciones);

            Filtrar();

            Mensaje =
                $"Información actualizada. " +
                $"Pendientes: {TotalPendientes}.";
        }
        catch (Exception ex)
        {
            Mensaje =
                "No fue posible cargar las obligaciones. " +
                ex.Message;
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private async Task RegistrarAsync()
    {
        if (Cargando)
            return;

        if (UnidadSeleccionada is null)
        {
            Mensaje =
                "Selecciona la caseta o unidad comercial.";

            return;
        }

        if (ConceptoSeleccionado is null)
        {
            Mensaje =
                "Selecciona el concepto de la deuda.";

            return;
        }

        if (MonedaSeleccionada is null)
        {
            Mensaje =
                "Selecciona la moneda.";

            return;
        }

        if (!FechaEmision.HasValue)
        {
            Mensaje =
                "Selecciona la fecha de emisión.";

            return;
        }

        if (!TryObtenerMonto(
            MontoTexto,
            out var monto))
        {
            Mensaje =
                "Introduce un monto válido mayor a cero.";

            return;
        }

        if (!CargoEventual &&
            (MesSeleccionado < 1 ||
             MesSeleccionado > 12))
        {
            Mensaje =
                "Selecciona un mes válido.";

            return;
        }

        if (!CargoEventual &&
            (AnioSeleccionado < 2000 ||
             AnioSeleccionado > 2200))
        {
            Mensaje =
                "Selecciona un año válido.";

            return;
        }

        if (FechaVencimiento.HasValue &&
            FechaVencimiento.Value.Date <
            FechaEmision.Value.Date)
        {
            Mensaje =
                "La fecha de vencimiento no puede ser anterior a la emisión.";

            return;
        }

        var duplicada =
            !CargoEventual &&
            _todas.Any(x =>
                x.UnidadId ==
                    UnidadSeleccionada.Id &&
                x.ConceptoId ==
                    ConceptoSeleccionado.Id &&
                x.PeriodoAnio ==
                    AnioSeleccionado &&
                x.PeriodoMes ==
                    MesSeleccionado &&
                !x.Estado.Equals(
                    "Anulada",
                    StringComparison.OrdinalIgnoreCase));

        if (duplicada)
        {
            Mensaje =
                $"Ya existe una obligación de " +
                $"{ConceptoSeleccionado.Nombre} para " +
                $"{UnidadSeleccionada.Codigo}, período " +
                $"{MesSeleccionado:00}/{AnioSeleccionado}.";

            return;
        }

        try
        {
            Cargando = true;

            Mensaje =
                "Registrando obligación...";

            var solicitud =
                new CrearObligacionRequest
                {
                    UnidadComercialId =
                        UnidadSeleccionada.Id,

                    ConceptoCobroId =
                        ConceptoSeleccionado.Id,

                    MonedaId =
                        MonedaSeleccionada.Id,

                    Monto = monto,

                    FechaEmision =
                        DateOnly.FromDateTime(
                            FechaEmision.Value),

                    FechaVencimiento =
                        FechaVencimiento.HasValue
                            ? DateOnly.FromDateTime(
                                FechaVencimiento.Value)
                            : null,

                    PeriodoAnio =
                        CargoEventual ? null : AnioSeleccionado,

                    PeriodoMes =
                        CargoEventual ? null : MesSeleccionado
                };

            await _obligacionesService
                .CrearAsync(solicitud);

            Mensaje =
                $"Obligación registrada: " +
                $"{UnidadSeleccionada.Codigo} - " +
                $"{ConceptoSeleccionado.Nombre} - " +
                $"{MonedaSeleccionada.Codigo} {monto:N2}.";

            MontoTexto = string.Empty;

            await RecargarObligacionesAsync();
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudo registrar la obligación. " +
                ex.Message;
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private void LimpiarFormulario()
    {
        MontoTexto = string.Empty;

        MesSeleccionado =
            DateTime.Today.Month;

        AnioSeleccionado =
            DateTime.Today.Year;

        FechaEmision =
            DateTime.Today;

        FechaVencimiento =
            DateTime.Today.AddDays(30);

        Mensaje =
            "Formulario preparado para una nueva obligación.";
    }

    private async Task RecargarObligacionesAsync()
    {
        var obligaciones =
            await _obligacionesService.ObtenerAsync();

        ConstruirFilas(obligaciones);

        Filtrar();
    }
private void CargarCatalogos(
    List<UnidadIngresoModel> unidades,
    List<MonedaIngresoModel> monedas,
    CatalogosIngresosModel? catalogos)
{
    var unidadAnterior =
        UnidadSeleccionada?.Id;

    var conceptoAnterior =
        ConceptoSeleccionado?.Id;

    var monedaAnterior =
        MonedaSeleccionada?.Id;

    var conceptoMasivoAnterior =
        ConceptoMasivoSeleccionado?.Id;

    var monedaMasivaAnterior =
        MonedaMasivaSeleccionada?.Id;

    Unidades.Clear();

    foreach (var unidad in unidades
        .Where(x => x.Activa)
        .OrderBy(x => x.Codigo))
    {
        Unidades.Add(unidad);
    }

    Conceptos.Clear();

    foreach (var concepto in
        (catalogos?.Conceptos ?? new())
            .OrderBy(x => x.Nombre))
    {
        Conceptos.Add(concepto);
    }

    Monedas.Clear();

    foreach (var moneda in monedas
        .OrderBy(x => x.Codigo))
    {
        Monedas.Add(moneda);
    }

    UnidadSeleccionada =
        Unidades.FirstOrDefault(
            x => x.Id == unidadAnterior)
        ?? Unidades.FirstOrDefault();

    ConceptoSeleccionado =
        Conceptos.FirstOrDefault(
            x => x.Id == conceptoAnterior)
        ?? Conceptos.FirstOrDefault();

    MonedaSeleccionada =
        Monedas.FirstOrDefault(
            x => x.Id == monedaAnterior)
        ?? Monedas.FirstOrDefault(
            x => x.Codigo.Equals(
                "BOB",
                StringComparison.OrdinalIgnoreCase))
        ?? Monedas.FirstOrDefault();

    ConceptoMasivoSeleccionado =
        Conceptos.FirstOrDefault(
            x => x.Id == conceptoMasivoAnterior)
        ?? Conceptos.FirstOrDefault();

    MonedaMasivaSeleccionada =
        Monedas.FirstOrDefault(
            x => x.Id == monedaMasivaAnterior)
        ?? Monedas.FirstOrDefault(
            x => x.Codigo.Equals(
                "BOB",
                StringComparison.OrdinalIgnoreCase))
        ?? Monedas.FirstOrDefault();

    RecalcularGeneracionMasiva();
}

    private void ConstruirFilas(
        List<ObligacionModel> obligaciones)
    {
        var unidadesPorId =
            Unidades
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Codigo);

        var monedasPorId =
            Monedas
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Codigo);

        var conceptosPorId =
            Conceptos
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Nombre);

        _todas.Clear();

        foreach (var obligacion in obligaciones)
        {
            _todas.Add(
                new FilaObligacion
                {
                    Id = obligacion.Id,

                    UnidadId =
                        obligacion.UnidadComercialId,

                    ConceptoId =
                        obligacion.ConceptoCobroId,

                    MonedaId =
                        obligacion.MonedaId,

                    Unidad =
                        unidadesPorId.TryGetValue(
                            obligacion.UnidadComercialId,
                            out var unidad)
                            ? unidad
                            : "Sin identificar",

                    Concepto =
                        conceptosPorId.TryGetValue(
                            obligacion.ConceptoCobroId,
                            out var concepto)
                            ? concepto
                            : "Concepto no disponible",

                    Moneda =
                        monedasPorId.TryGetValue(
                            obligacion.MonedaId,
                            out var moneda)
                            ? moneda
                            : "Sin moneda",

                    Monto =
                        obligacion.Monto,

                    FechaEmision =
                        obligacion.FechaEmision,

                    FechaVencimiento =
                        obligacion.FechaVencimiento,

                    PeriodoAnio =
                        obligacion.PeriodoAnio,

                    PeriodoMes =
                        obligacion.PeriodoMes,

                    Estado =
                        obligacion.Estado
                });
        }

        TotalObligaciones =
            _todas.Count;

        var pendientes =
            _todas
                .Where(x =>
                    x.Estado.Equals(
                        "Pendiente",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        TotalPendientes =
            pendientes.Count;

        TotalPendienteMonto =
            pendientes.Sum(x => x.Monto);
    }

    private void Filtrar()
    {
        var texto =
            Busqueda.Trim();

        IEnumerable<FilaObligacion> resultado =
            _todas;

        if (!string.IsNullOrWhiteSpace(texto))
        {
            resultado =
                resultado.Where(x =>
                    x.Unidad.Contains(
                        texto,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    x.Concepto.Contains(
                        texto,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    x.Moneda.Contains(
                        texto,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (EstadoSeleccionado != "Todos")
        {
            resultado =
                resultado.Where(x =>
                    x.Estado.Equals(
                        EstadoSeleccionado,
                        StringComparison.OrdinalIgnoreCase));
        }

        Obligaciones.Clear();

        foreach (var item in resultado
            .OrderByDescending(x => x.PeriodoAnio)
            .ThenByDescending(x => x.PeriodoMes)
            .ThenBy(x => x.Unidad))
        {
            Obligaciones.Add(item);
        }
    }

    private static bool TryObtenerMonto(
        string texto,
        out decimal monto)
    {
        texto = texto
            .Trim()
            .Replace("Bs", "",
                StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(
            texto,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out monto)
            && monto > 0)
        {
            return true;
        }

        var normalizado =
            texto.Replace(',', '.');

        return decimal.TryParse(
                   normalizado,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out monto)
               && monto > 0;
    }
}

public sealed class FilaObligacion
{
    public Guid Id { get; set; }

    public Guid UnidadId { get; set; }

    public Guid ConceptoId { get; set; }

    public Guid MonedaId { get; set; }

    public string Unidad { get; set; }
        = string.Empty;

    public string Concepto { get; set; }
        = string.Empty;

    public string Moneda { get; set; }
        = string.Empty;

    public decimal Monto { get; set; }

    public DateOnly FechaEmision { get; set; }

    public DateOnly? FechaVencimiento { get; set; }

    public int? PeriodoAnio { get; set; }

    public int? PeriodoMes { get; set; }

    public string Estado { get; set; }
        = string.Empty;

    public string Periodo =>
        PeriodoAnio.HasValue &&
        PeriodoMes.HasValue
            ? $"{PeriodoMes:00}/{PeriodoAnio}"
            : "Sin período";

    public string Vencimiento =>
        FechaVencimiento?
            .ToString("dd/MM/yyyy")
        ?? "Sin vencimiento";
}