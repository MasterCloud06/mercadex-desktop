
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class CajaView : UserControl
{
    private static readonly HttpClient Http = new()
    {
BaseAddress = ApiConfiguration.BaseUri,
        Timeout = TimeSpan.FromSeconds(35)
    };

    private readonly ObservableCollection<ReciboCajaFila> _visibles = new();

    private List<ReciboCajaFila> _todos = new();

    private bool _cargando;
    private bool _inicializado;

    public CajaView()
    {
        InitializeComponent();

        TablaRecibos.ItemsSource = _visibles;

        Loaded += CajaView_Loaded;
    }

    private async void CajaView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_inicializado)
            return;

        _inicializado = true;

        await CargarRecibosAsync();
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarRecibosAsync();
    }

    private async Task CargarRecibosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;

        TxtEstado.Text = "Consultando recibos...";

        try
        {
            var recibos = await Http.GetFromJsonAsync<
                List<Mercadex.Desktop.App.Dtos.ReciboDto>>(
                    "/api/Caja/recibos")
                ?? new();

            _todos = recibos
                .OrderByDescending(x => x.Numero)
                .Select(x => new ReciboCajaFila
                {
                    Id = x.Id,
                    Numero = x.Numero,
                    Fecha = x.FechaEmision
                        .ToLocalTime()
                        .ToString("dd/MM/yyyy HH:mm"),
                    Total = x.Total,
                    Estado = x.Estado,
                    UnidadComercialId = x.UnidadComercialId,
                    CantidadConceptos = x.Detalles?.Count ?? 0
                })
                .ToList();

            Filtrar();

            TxtEstado.Text =
                $"Recibos encontrados: {_todos.Count}. " +
                $"Actualizado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        }
        catch (OperationCanceledException)
        {
            TxtEstado.Text =
                "La API tardó demasiado. Pulsa Actualizar para intentarlo nuevamente.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No fue posible consultar los recibos: " +
                ex.Message;
        }
        finally
        {
            _cargando = false;
        }
    }

    private void BuscarRecibo_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (TablaRecibos != null)
            Filtrar();
    }

    private void Filtrar()
    {
        var busqueda = TxtBuscarRecibo.Text.Trim();

        _visibles.Clear();

        foreach (var recibo in _todos.Where(x =>
            string.IsNullOrWhiteSpace(busqueda)
            || x.Numero.ToString().Contains(busqueda)))
        {
            _visibles.Add(recibo);
        }

        TxtDetalle.Text =
            "Selecciona un recibo para consultar su información.";
    }

    private void TablaRecibos_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (TablaRecibos.SelectedItem is not ReciboCajaFila recibo)
        {
            TxtDetalle.Text =
                "Selecciona un recibo para consultar su información.";

            return;
        }

        TxtDetalle.Text =
            $"Recibo N.º: {recibo.Numero}\n" +
            $"Fecha: {recibo.Fecha}\n" +
            $"Importe: {recibo.Total:N2}\n" +
            $"Estado: {recibo.Estado}\n" +
            $"Unidad comercial: {recibo.UnidadComercialId}\n" +
            $"Conceptos: {recibo.CantidadConceptos}";
    }
}

public sealed class ReciboCajaFila
{
    public Guid Id { get; set; }

    public long Numero { get; set; }

    public string Fecha { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string Estado { get; set; } = string.Empty;

    public Guid UnidadComercialId { get; set; }

    public int CantidadConceptos { get; set; }
}
