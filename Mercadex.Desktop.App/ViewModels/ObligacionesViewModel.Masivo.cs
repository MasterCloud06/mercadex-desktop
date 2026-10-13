using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

using System.Collections.ObjectModel;
using System.Windows;

namespace Mercadex.Desktop.App.ViewModels;

public partial class ObligacionesViewModel
{
    private readonly ApiClient _apiMasivo = App.ApiClient;

    public ObservableCollection<PasilloItem> PasillosMasivo { get; }
        = new();

    public IReadOnlyList<string> AlcancesMasivo { get; } =
    [
        "Todas las casetas",
        "Solo un pasillo"
    ];

    [ObservableProperty]
    private string alcanceMasivoSeleccionado =
        "Todas las casetas";

    [ObservableProperty]
    private PasilloItem? pasilloMasivoSeleccionado;

    [ObservableProperty]
    private ConceptoIngresoModel? conceptoMasivoSeleccionado;

    [ObservableProperty]
    private MonedaIngresoModel? monedaMasivaSeleccionada;

    [ObservableProperty]
    private int mesMasivoSeleccionado =
        DateTime.Today.Month;

    [ObservableProperty]
    private int anioMasivoSeleccionado =
        DateTime.Today.Year;

    [ObservableProperty]
    private string montoMasivoTexto =
        string.Empty;

    [ObservableProperty]
    private DateTime? fechaEmisionMasiva =
        DateTime.Today;

    [ObservableProperty]
    private DateTime? fechaVencimientoMasiva =
        DateTime.Today.AddDays(30);

    [ObservableProperty]
    private int casetasActivasMasivo;

    [ObservableProperty]
    private int casetasConDeudaMasivo;

    [ObservableProperty]
    private int casetasPorGenerarMasivo;

    [ObservableProperty]
    private string resumenMasivo =
        "Selecciona un concepto y período para calcular las casetas.";

    [ObservableProperty]
    private string progresoMasivo =
        string.Empty;

    public bool MostrarPasilloMasivo =>
        AlcanceMasivoSeleccionado == "Solo un pasillo";

    partial void OnAlcanceMasivoSeleccionadoChanged(string value)
    {
        OnPropertyChanged(nameof(MostrarPasilloMasivo));
        RecalcularGeneracionMasiva();
    }

    partial void OnPasilloMasivoSeleccionadoChanged(
        PasilloItem? value)
    {
        RecalcularGeneracionMasiva();
    }

    partial void OnConceptoMasivoSeleccionadoChanged(
        ConceptoIngresoModel? value)
    {
        RecalcularGeneracionMasiva();
    }

    partial void OnMesMasivoSeleccionadoChanged(int value)
    {
        RecalcularGeneracionMasiva();
    }

    partial void OnAnioMasivoSeleccionadoChanged(int value)
    {
        RecalcularGeneracionMasiva();
    }

    public async Task CargarPasillosMasivoAsync()
    {
        try
        {
            var pasillos =
                await _apiMasivo
                    .GetAsync<List<PasilloItem>>(
                        "/api/pasillos")
                ?? new();

            var seleccionadoAnterior =
                PasilloMasivoSeleccionado?.Id;

            PasillosMasivo.Clear();

foreach (var pasillo in pasillos
    .OrderBy(x => x.Codigo))
{
    PasillosMasivo.Add(pasillo);
}

            PasilloMasivoSeleccionado =
                PasillosMasivo.FirstOrDefault(
                    x => x.Id == seleccionadoAnterior)
                ?? PasillosMasivo.FirstOrDefault();

            RecalcularGeneracionMasiva();
        }
catch (Exception ex)
{
    Mensaje =
        "No fue posible cargar los pasillos: " +
        ex.Message;
}
    }

    [RelayCommand]
    private async Task GenerarMasivoAsync()
    {
        if (Cargando)
            return;

        if (ConceptoMasivoSeleccionado is null)
        {
            Mensaje =
                "Selecciona el concepto que deseas generar.";
            return;
        }

        if (MonedaMasivaSeleccionada is null)
        {
            Mensaje =
                "Selecciona la moneda.";
            return;
        }

        if (!TryObtenerMonto(
            MontoMasivoTexto,
            out var monto))
        {
            Mensaje =
                "Introduce un monto válido mayor a cero.";
            return;
        }

        if (MesMasivoSeleccionado < 1 ||
            MesMasivoSeleccionado > 12)
        {
            Mensaje =
                "Selecciona un mes válido.";
            return;
        }

        if (AnioMasivoSeleccionado < 2000 ||
            AnioMasivoSeleccionado > 2200)
        {
            Mensaje =
                "Selecciona un año válido.";
            return;
        }

        if (!FechaEmisionMasiva.HasValue)
        {
            Mensaje =
                "Selecciona la fecha de emisión.";
            return;
        }

        if (FechaVencimientoMasiva.HasValue &&
            FechaVencimientoMasiva.Value.Date <
            FechaEmisionMasiva.Value.Date)
        {
            Mensaje =
                "La fecha de vencimiento no puede ser anterior a la emisión.";
            return;
        }

        if (AlcanceMasivoSeleccionado == "Solo un pasillo" &&
            PasilloMasivoSeleccionado is null)
        {
            Mensaje =
                "Selecciona el pasillo.";
            return;
        }

        var casetasObjetivo =
            ObtenerCasetasObjetivo();

        if (casetasObjetivo.Count == 0)
        {
            Mensaje =
                "No existen casetas activas para el alcance seleccionado.";
            return;
        }

        var yaTienen =
            _todas
                .Where(x =>
                    x.ConceptoId ==
                        ConceptoMasivoSeleccionado.Id
                    &&
                    x.PeriodoMes ==
                        MesMasivoSeleccionado
                    &&
                    x.PeriodoAnio ==
                        AnioMasivoSeleccionado
                    &&
                    !x.Estado.Equals(
                        "Anulada",
                        StringComparison.OrdinalIgnoreCase))
                .Select(x => x.UnidadId)
                .ToHashSet();

        var candidatas =
            casetasObjetivo
                .Where(x => !yaTienen.Contains(x.Id))
                .OrderBy(x => x.Codigo)
                .ToList();

        if (candidatas.Count == 0)
        {
            Mensaje =
                "Todas las casetas seleccionadas ya tienen esa obligación para el período.";

            RecalcularGeneracionMasiva();
            return;
        }

        var alcanceTexto =
            AlcanceMasivoSeleccionado == "Solo un pasillo"
                ? $"Pasillo: {PasilloMasivoSeleccionado!.Nombre}"
                : "Todas las casetas activas";

        var respuesta =
            Dialogo.Mostrar(
                $"Se generarán {candidatas.Count} obligaciones.\n\n" +
                $"{alcanceTexto}\n" +
                $"Concepto: {ConceptoMasivoSeleccionado.Nombre}\n" +
                $"Período: {MesMasivoSeleccionado:00}/{AnioMasivoSeleccionado}\n" +
                $"Monto por caseta: {MonedaMasivaSeleccionada.Codigo} {monto:N2}\n\n" +
                $"Casetas omitidas porque ya tienen la obligación: " +
                $"{casetasObjetivo.Count - candidatas.Count}\n\n" +
                "¿Deseas continuar?",
                "Confirmar generación masiva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (respuesta != MessageBoxResult.Yes)
            return;

        var creadas = 0;
        var errores = new List<string>();

        try
        {
            Cargando = true;

            Mensaje =
                "Generando obligaciones...";

            ProgresoMasivo =
                $"0 de {candidatas.Count}";

            foreach (var unidad in candidatas)
            {
                try
                {
                    var solicitud =
                        new CrearObligacionRequest
                        {
                            UnidadComercialId =
                                unidad.Id,

                            ConceptoCobroId =
                                ConceptoMasivoSeleccionado.Id,

                            MonedaId =
                                MonedaMasivaSeleccionada.Id,

                            Monto =
                                monto,

                            FechaEmision =
                                DateOnly.FromDateTime(
                                    FechaEmisionMasiva.Value),

                            FechaVencimiento =
                                FechaVencimientoMasiva.HasValue
                                    ? DateOnly.FromDateTime(
                                        FechaVencimientoMasiva.Value)
                                    : null,

                            PeriodoAnio =
                                AnioMasivoSeleccionado,

                            PeriodoMes =
                                MesMasivoSeleccionado
                        };

                    await _obligacionesService
                        .CrearAsync(solicitud);

                    creadas++;
                }
                catch (Exception ex)
                {
                    errores.Add(
                        $"{unidad.Codigo}: {ex.Message}");
                }

                ProgresoMasivo =
                    $"{creadas + errores.Count} de {candidatas.Count}";
            }

            await RecargarObligacionesAsync();

            RecalcularGeneracionMasiva();

            if (errores.Count == 0)
            {
                Mensaje =
                    $"Generación completada: {creadas} obligación(es).";

                Dialogo.Mostrar(
                    $"Generación terminada correctamente.\n\n" +
                    $"Obligaciones creadas: {creadas}\n" +
                    $"Concepto: {ConceptoMasivoSeleccionado.Nombre}\n" +
                    $"Período: {MesMasivoSeleccionado:00}/{AnioMasivoSeleccionado}",
                    "Mercadex",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var detalle =
                    string.Join(
                        "\n",
                        errores.Take(5));

                Mensaje =
                    $"Creadas: {creadas}. " +
                    $"Errores: {errores.Count}.";

                Dialogo.Mostrar(
                    $"Obligaciones creadas: {creadas}\n" +
                    $"No creadas: {errores.Count}\n\n" +
                    detalle,
                    "Mercadex",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            Cargando = false;
            ProgresoMasivo = string.Empty;
        }
    }

    private List<UnidadIngresoModel> ObtenerCasetasObjetivo()
    {
        IEnumerable<UnidadIngresoModel> consulta =
            Unidades.Where(x =>
                x.Activa &&
                x.Tipo == 1);

        if (AlcanceMasivoSeleccionado == "Solo un pasillo")
        {
            if (PasilloMasivoSeleccionado is null)
                return new();

            consulta =
                consulta.Where(x =>
                    x.PasilloId ==
                    PasilloMasivoSeleccionado.Id);
        }

        return consulta.ToList();
    }

    private void RecalcularGeneracionMasiva()
    {
        var casetasObjetivo =
            ObtenerCasetasObjetivo();

        CasetasActivasMasivo =
            casetasObjetivo.Count;

        if (ConceptoMasivoSeleccionado is null)
        {
            CasetasConDeudaMasivo = 0;
            CasetasPorGenerarMasivo =
                CasetasActivasMasivo;

            ResumenMasivo =
                $"Casetas objetivo: {CasetasActivasMasivo}.";

            return;
        }

        var idsObjetivo =
            casetasObjetivo
                .Select(x => x.Id)
                .ToHashSet();

        var yaGeneradas =
            _todas
                .Where(x =>
                    idsObjetivo.Contains(x.UnidadId)
                    &&
                    x.ConceptoId ==
                        ConceptoMasivoSeleccionado.Id
                    &&
                    x.PeriodoMes ==
                        MesMasivoSeleccionado
                    &&
                    x.PeriodoAnio ==
                        AnioMasivoSeleccionado
                    &&
                    !x.Estado.Equals(
                        "Anulada",
                        StringComparison.OrdinalIgnoreCase))
                .Select(x => x.UnidadId)
                .Distinct()
                .Count();

        CasetasConDeudaMasivo =
            yaGeneradas;

        CasetasPorGenerarMasivo =
            Math.Max(
                0,
                CasetasActivasMasivo -
                CasetasConDeudaMasivo);

        var alcance =
            AlcanceMasivoSeleccionado == "Solo un pasillo"
                && PasilloMasivoSeleccionado is not null
                    ? $"Pasillo: {PasilloMasivoSeleccionado.Nombre} · "
                    : "";

        ResumenMasivo =
            $"{alcance}" +
            $"Casetas objetivo: {CasetasActivasMasivo} · " +
            $"Ya tienen la obligación: {CasetasConDeudaMasivo} · " +
            $"Se generarían: {CasetasPorGenerarMasivo}";
    }
}