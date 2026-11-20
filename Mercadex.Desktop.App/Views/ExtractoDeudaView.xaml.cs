
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

    private string _socio = "";

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

        // Lista de conceptos con su número (orden de caja), incluidos los inactivos.
        var conceptos =
            await Api.GetFromJsonAsync<
                List<ConceptoExtracto>>(
                    "/api/conceptos-cobro")
            ?? new();

        try
        {
            _socio = await new Services.TitularCasetaService().ObtenerAsync(unidad.Id);
        }
        catch (Exception)
        {
            _socio = "";
        }

        var monedasPorId = monedas
            .GroupBy(m => m.Id)
            .ToDictionary(
                g => g.Key,
                g => g.First().Codigo);

        var conceptosPorId = conceptos
            .GroupBy(c => c.Id)
            .ToDictionary(
                g => g.Key,
                g => g.First());

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
                    ? concepto.Nombre
                    : "Sin identificar",

                NumeroConcepto = conceptosPorId.TryGetValue(
                    o.ConceptoCobroId,
                    out var c2)
                    ? c2.Numero
                    : null,

                Anio = o.PeriodoAnio,

                Mes = o.PeriodoMes,

                TieneAbonos = o.MontoPagado > 0,

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

                // Lo que falta pagar (con pagos parciales, menos que el monto).
                Monto = o.Saldo ?? o.Monto - o.MontoPagado,

                Nota = o.MontoPagado > 0
                    ? $"Pagó {o.MontoPagado:N2} de {o.Monto:N2}"
                    : o.Modificaciones > 0 ? "✎ Modificada" : "",

                NotaDetalle = o.MontoPagado > 0 ? "Tiene pagos parciales." : o.UltimaModificacion
            })
            .ToList();

        // Mes por mes y, dentro del mes, en el orden de los conceptos (como el aviso anterior).
        var orden = Services.AvisoCobranza.Ordenar(_deudas.Select(d => d.Aviso)).ToList();
        _deudas = _deudas.OrderBy(d => orden.IndexOf(d.Aviso)).ToList();

        _unidadSeleccionada = unidad;

        TxtUnidad.Text =
            $"Local: {unidad.Codigo}    " +
            $"Bloque: {unidad.Bloque ?? "Sin bloque"}" +
            (string.IsNullOrWhiteSpace(_socio) ? "" : $"    Socio: {_socio}");

        TxtFecha.Text =
            $"Fecha de consulta: " +
            DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        TablaDeudas.ItemsSource = _deudas;

        var totales = _deudas
            .GroupBy(d => d.Moneda)
            .OrderBy(g => g.Key)
            .Select(g =>
                $"{Services.AvisoCobranza.Moneda(g.Key)}: {g.Sum(d => d.Monto):N2}");

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

        var aviso = Services.AvisoCobranza.Crear(
            _unidadSeleccionada.Codigo,
            _unidadSeleccionada.Bloque,
            _socio,
            _deudas.Select(d => d.Aviso),
            DateTime.Now);

        var documento = AvisoCobranzaDocumento.Crear(aviso);

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

        public decimal MontoPagado { get; set; }

        public decimal? Saldo { get; set; }

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

    private sealed class ConceptoExtracto
    {
        public Guid Id { get; set; }

        public string Nombre { get; set; } = "";

        public int? Numero { get; set; }
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

    public int? NumeroConcepto { get; set; }

    public int? Anio { get; set; }

    public int? Mes { get; set; }

    public bool TieneAbonos { get; set; }

    private Services.DeudaAviso? _aviso;

    /// <summary>La deuda para el aviso de cobranza (se arma una sola vez).</summary>
    public Services.DeudaAviso Aviso =>
        _aviso ??= new(Concepto, NumeroConcepto, Anio, Mes, Moneda, Monto, TieneAbonos);

    /// <summary>«APORTE MENSUAL feb/2024», como en el aviso impreso.</summary>
    public string Servicio => Services.AvisoCobranza.Servicio(Aviso);
}
