
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class ExtractoDeudaView : UserControl
{
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    private List<FilaExtracto> _deudas = new();

    private UnidadExtracto? _unidadSeleccionada;

    private bool _consultando;

    public ExtractoDeudaView()
    {
        InitializeComponent();

        Loaded += (_, _) => TxtCaseta.Focus();
    }

    private async void Buscar_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        await BuscarAsync();
    }

    private async void Consultar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await BuscarAsync();
    }

    private async Task BuscarAsync()
    {
        if (_consultando)
            return;

        var codigo = TxtCaseta.Text.Trim();

        if (string.IsNullOrWhiteSpace(codigo))
        {
            TxtEstado.Text =
                "Introduce el número de la caseta.";
            return;
        }

        _consultando = true;

        BtnImprimir.IsEnabled = false;
        Coincidencias.Visibility = Visibility.Collapsed;
        Coincidencias.ItemsSource = null;

        _unidadSeleccionada = null;
        _deudas.Clear();

        TablaDeudas.ItemsSource = null;
        TxtUnidad.Text = "Caseta: sin seleccionar";
        TxtTotales.Text = "Sin consultar";

        TxtEstado.Text = "Buscando caseta...";

        try
        {
            var unidades =
                await Api.GetFromJsonAsync<
                    List<UnidadExtracto>>(
                        "/api/unidades-comerciales")
                ?? new();

            // Tipo 1 corresponde a Caseta.
            // Se exige coincidencia exacta.

            var encontradas = unidades
                .Where(u =>
                    u.Tipo == 1 &&
                    string.Equals(
                        u.Codigo.Trim(),
                        codigo,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (encontradas.Count == 0)
            {
                TxtEstado.Text =
                    $"No se encontró la caseta {codigo}.";
                return;
            }

            if (encontradas.Count > 1)
            {
                Coincidencias.ItemsSource = encontradas;
                Coincidencias.Visibility = Visibility.Visible;

                TxtEstado.Text =
                    "Hay varias casetas con ese número. " +
                    "Selecciona la ubicación correcta.";
                return;
            }

            await CargarDeudasAsync(encontradas[0]);
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "Error al consultar: " + ex.Message;
        }
        finally
        {
            _consultando = false;
        }
    }

    private async void Coincidencias_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (Coincidencias.SelectedItem
            is not UnidadExtracto unidad)
        {
            return;
        }

        Coincidencias.Visibility = Visibility.Collapsed;

        try
        {
            await CargarDeudasAsync(unidad);
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "Error al consultar: " + ex.Message;
        }
    }

    private async Task CargarDeudasAsync(
        UnidadExtracto unidad)
    {
        BtnImprimir.IsEnabled = false;

        TxtEstado.Text =
            $"Consultando obligaciones de la caseta " +
            $"{unidad.Codigo}...";

        var obligaciones =
            await Api.GetFromJsonAsync<
                List<ObligacionExtracto>>(
                    $"/api/Obligaciones/unidad/{unidad.Id}/pendientes")
            ?? new();

        var monedas =
            await Api.GetFromJsonAsync<
                List<MonedaExtracto>>(
                    "/api/monedas")
            ?? new();

        var catalogos =
            await Api.GetFromJsonAsync<
                CatalogosExtracto>(
                    "/api/ingresos/catalogos");

        var monedasPorId = monedas
            .GroupBy(m => m.Id)
            .ToDictionary(
                g => g.Key,
                g => g.First().Codigo);

        var conceptosPorId =
            (catalogos?.Conceptos ?? new())
            .GroupBy(c => c.Id)
            .ToDictionary(
                g => g.Key,
                g => g.First().Nombre);

        _deudas = obligaciones
            .Where(o =>
                o.UnidadComercialId == unidad.Id &&
                string.Equals(
                    o.Estado,
                    "Pendiente",
                    StringComparison.OrdinalIgnoreCase))
            .Select(o => new FilaExtracto
            {
                Concepto = conceptosPorId.TryGetValue(
                    o.ConceptoCobroId,
                    out var concepto)
                    ? concepto
                    : "Sin identificar",

                Periodo =
                    o.PeriodoAnio.HasValue &&
                    o.PeriodoMes.HasValue
                    ? $"{o.PeriodoMes.Value:00}/" +
                      $"{o.PeriodoAnio.Value}"
                    : "-",

                Fecha =
                    o.FechaEmision.ToString("dd/MM/yyyy"),

                Moneda = monedasPorId.TryGetValue(
                    o.MonedaId,
                    out var moneda)
                    ? moneda
                    : "Sin identificar",

                Monto = o.Monto,

                Nota = o.Modificaciones > 0 ? "✎ Modificada" : "",

                NotaDetalle = o.UltimaModificacion
            })
            .OrderBy(d => d.Concepto)
            .ThenBy(d => d.Periodo)
            .ToList();

        _unidadSeleccionada = unidad;

        TxtUnidad.Text =
            $"Caseta: {unidad.Codigo}    " +
            $"Bloque: {unidad.Bloque ?? "Sin bloque"}";

        TxtFecha.Text =
            $"Fecha de consulta: " +
            DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        TablaDeudas.ItemsSource = _deudas;

        var totales = _deudas
            .GroupBy(d => d.Moneda)
            .OrderBy(g => g.Key)
            .Select(g =>
                $"{g.Key}: {g.Sum(d => d.Monto):N2}");

        TxtTotales.Text = _deudas.Count == 0
            ? "Sin deudas pendientes"
            : string.Join("     |     ", totales);

        TxtEstado.Text = _deudas.Count == 0
            ? "Esta caseta no tiene obligaciones pendientes."
            : $"Se encontraron {_deudas.Count} " +
              "obligaciones pendientes.";

        BtnImprimir.IsEnabled = true;
    }

    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_unidadSeleccionada is null)
            return;

        var documento = new FlowDocument
        {
            PagePadding = new Thickness(45),
            FontFamily = new FontFamily("Arial"),
            FontSize = 12,
            ColumnWidth = 650
        };

        documento.Blocks.Add(
            new Paragraph(
                new Run("DEUDAS POR PAGAR"))
            {
                FontSize = 23,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkRed,
                TextAlignment = TextAlignment.Center
            });

        documento.Blocks.Add(
            new Paragraph(new Run(
                "EXTRACTO INFORMATIVO - " +
                "NO ES RECIBO DE PAGO"))
            {
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkRed,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

        AgregarLinea(
            documento,
            $"Caseta: {_unidadSeleccionada.Codigo}");

        AgregarLinea(
            documento,
            $"Bloque: " +
            $"{_unidadSeleccionada.Bloque ?? "Sin bloque"}");

        AgregarLinea(
            documento,
            $"Fecha de consulta: " +
            $"{DateTime.Now:dd/MM/yyyy HH:mm}");

        documento.Blocks.Add(
            new Paragraph(
                new Run("OBLIGACIONES PENDIENTES"))
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 20, 0, 10)
            });

        if (_deudas.Count == 0)
        {
            AgregarLinea(
                documento,
                "No se encontraron deudas pendientes.");
        }

        foreach (var deuda in _deudas)
        {
            AgregarLinea(
                documento,
                $"{deuda.Concepto} | " +
                $"{deuda.Periodo} | " +
                $"{deuda.Fecha} | " +
                $"{deuda.Monto:N2} {deuda.Moneda}");
        }

        foreach (var grupo in _deudas.GroupBy(d => d.Moneda))
        {
            documento.Blocks.Add(
                new Paragraph(new Run(
                    $"TOTAL {grupo.Key}: " +
                    $"{grupo.Sum(d => d.Monto):N2}"))
                {
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 18, 0, 0)
                });
        }

        var dialogo = new PrintDialog();

        if (dialogo.ShowDialog() != true)
            return;

        documento.PageWidth =
            dialogo.PrintableAreaWidth;

        documento.PageHeight =
            dialogo.PrintableAreaHeight;

        dialogo.PrintDocument(
            ((IDocumentPaginatorSource)documento)
                .DocumentPaginator,
            "Extracto de deuda");
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(
            new Paragraph(new Run(texto))
            {
                Margin = new Thickness(0, 4, 0, 4)
            });
    }

    private sealed class UnidadExtracto
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";

        public int Tipo { get; set; }

        public string? Bloque { get; set; }

        public string Etiqueta =>
            $"Caseta {Codigo} - " +
            $"{Bloque ?? "Sin bloque"}";
    }

    private sealed class ObligacionExtracto
    {
        public Guid UnidadComercialId { get; set; }

        public Guid ConceptoCobroId { get; set; }

        public Guid MonedaId { get; set; }

        public decimal Monto { get; set; }

        public DateOnly FechaEmision { get; set; }

        public int? PeriodoMes { get; set; }

        public int? PeriodoAnio { get; set; }

        public string Estado { get; set; } = "";

        public int Modificaciones { get; set; }

        public string? UltimaModificacion { get; set; }
    }

    private sealed class MonedaExtracto
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";
    }

    private sealed class CatalogosExtracto
    {
        public List<ConceptoExtracto> Conceptos { get; set; }
            = new();
    }

    private sealed class ConceptoExtracto
    {
        public Guid Id { get; set; }

        public string Nombre { get; set; } = "";
    }
}

public sealed class FilaExtracto
{
    /// <summary>"✎ Modificada" si el monto se corrigió después de registrarse.</summary>
    public string Nota { get; set; } = "";

    public string? NotaDetalle { get; set; }

    public string Concepto { get; set; } = "";

    public string Periodo { get; set; } = "";

    public string Fecha { get; set; } = "";

    public string Moneda { get; set; } = "";

    public decimal Monto { get; set; }
}
