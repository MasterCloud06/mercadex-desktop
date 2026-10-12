
using Mercadex.Desktop.App.Dtos;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class RecibosView : UserControl
{
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(45));

    private readonly ObservableCollection<FilaRecibo>
        _visibles = new();

    private List<FilaRecibo> _todos = new();

    private bool _cargando;
    private bool _inicializado;

    public RecibosView()
    {
        InitializeComponent();

        TablaRecibos.ItemsSource = _visibles;

        Loaded += async (_, _) =>
        {
            if (_inicializado)
                return;

            _inicializado = true;

            await CargarRecibosAsync();
        };
    }

    // CONSULTAR RECIBOS EXISTENTES

    private async Task CargarRecibosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;

        BtnImprimir.IsEnabled = false;
        BtnVistaPrevia.IsEnabled = false;

        TxtEstado.Text = "Consultando recibos registrados...";

        try
        {
            var recibos =
                await Api.GetFromJsonAsync<List<ReciboDto>>(
                    "/api/Caja/recibos")
                ?? new();

            var unidades =
                await Api.GetFromJsonAsync<List<UnidadResumen>>(
                    "/api/unidades-comerciales")
                ?? new();

            var monedas =
                await Api.GetFromJsonAsync<List<MonedaResumen>>(
                    "/api/monedas")
                ?? new();

            var unidadesPorId = unidades
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First());

            var monedasPorId = monedas
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Codigo);

            _todos = recibos.Select(recibo =>
            {
                unidadesPorId.TryGetValue(
                    recibo.UnidadComercialId,
                    out var unidad);

                return new FilaRecibo
                {
                    Recibo = recibo,

                    CodigoUnidad =
                        unidad?.Codigo ?? "Sin identificar",

                    Bloque =
                        unidad?.Bloque ?? "Sin bloque",

                    Monedas = monedasPorId
                };
            })
            .OrderByDescending(x => x.Numero)
            .ToList();

            AplicarFiltros();

            TxtEstado.Text =
                $"Recibos registrados: {_todos.Count}.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudieron consultar los recibos: " +
                ex.Message;
        }
        finally
        {
            _cargando = false;
        }
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarRecibosAsync();
    }

    // BUSCAR POR NÃšMERO Y FECHA

    private void Filtros_Changed(
        object sender,
        TextChangedEventArgs e)
    {
        AplicarFiltros();
    }

    private void Fechas_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        AplicarFiltros();
    }

    private void Limpiar_Click(
        object sender,
        RoutedEventArgs e)
    {
        TxtNumero.Clear();

        FechaDesde.SelectedDate = null;
        FechaHasta.SelectedDate = null;

        AplicarFiltros();
    }

    private void AplicarFiltros()
    {
        if (TxtNumero == null ||
            FechaDesde == null ||
            FechaHasta == null ||
            TablaRecibos == null ||
            TablaDetalles == null ||
            BtnImprimir == null ||
            BtnVistaPrevia == null)
        {
            return;
        }

        var numero = TxtNumero.Text.Trim();

        var desde = FechaDesde.SelectedDate?.Date;
        var hasta = FechaHasta.SelectedDate?.Date;

        var filtrados = _todos.Where(fila =>
        {
            var fecha =
                fila.Recibo.FechaEmision.LocalDateTime.Date;

            // NÃºmero exacto para evitar confundir
            // el recibo 1 con el 10, 11 o 100.

            if (!string.IsNullOrWhiteSpace(numero) &&
                fila.Numero.ToString() != numero)
            {
                return false;
            }

            if (desde.HasValue &&
                fecha < desde.Value)
            {
                return false;
            }

            if (hasta.HasValue &&
                fecha > hasta.Value)
            {
                return false;
            }

            return true;
        });

        _visibles.Clear();

        foreach (var fila in filtrados)
            _visibles.Add(fila);

        TablaDetalles.ItemsSource = null;

        BtnImprimir.IsEnabled = false;
        BtnVistaPrevia.IsEnabled = false;

        if (TxtDetalle != null)
        {
            TxtDetalle.Text =
                "Selecciona un recibo para visualizarlo.";
        }
    }

    // CONSULTAR DETALLE DEL RECIBO

    private async void TablaRecibos_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (TablaRecibos.SelectedItem
            is not FilaRecibo fila)
        {
            BtnImprimir.IsEnabled = false;
            BtnVistaPrevia.IsEnabled = false;
            return;
        }

        BtnImprimir.IsEnabled = false;
        BtnVistaPrevia.IsEnabled = false;

        TxtDetalle.Text = "Cargando comprobante...";

        try
        {
            // Consultar el recibo por su identificador,
            // sin crear ningÃºn pago.

            var recibo =
                await Api.GetFromJsonAsync<ReciboDto>(
                    $"/api/Caja/recibos/{fila.Recibo.Id}");

            if (recibo is null)
                throw new Exception("Recibo no encontrado.");

            // Consultar el pago original.

            var pago =
                await Api.GetFromJsonAsync<PagoDto>(
                    $"/api/Caja/pagos/{recibo.PagoId}");

            if (TablaRecibos.SelectedItem != fila)
                return;

            if (pago is null)
                throw new Exception(
                    "No se encontraron los datos del pago.");

            fila.Recibo = recibo;
            fila.Pago = pago;

            fila.Moneda =
                fila.Monedas.TryGetValue(
                    pago.MonedaId,
                    out var codigo)
                    ? codigo
                    : "Sin identificar";

            TxtDetalle.Text =
                $"RECIBO N.º {recibo.Numero}\n" +
                $"Fecha: {fila.FechaTexto}\n" +
                $"Caseta/local: {fila.CodigoUnidad}\n" +
                $"Bloque: {fila.Bloque}\n" +
                $"Pagador: {pago.NombrePagador}\n" +
                $"Forma de pago: {pago.FormaPago}\n" +
                $"Referencia: {pago.Referencia ?? "-"}\n" +
                $"Moneda: {fila.Moneda}\n" +
                $"Tipo de cambio: {pago.TipoCambio:N2}\n" +
                $"Estado: {recibo.Estado}\n" +
                $"Total: {recibo.Total:N2} {fila.Moneda}";

            TablaDetalles.ItemsSource =
                recibo.Detalles ?? new();

            BtnVistaPrevia.IsEnabled = true;
            BtnImprimir.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (TablaRecibos.SelectedItem != fila)
                return;

            TablaDetalles.ItemsSource =
                fila.Recibo.Detalles ?? new();

            TxtDetalle.Text =
                $"Recibo N.º {fila.Numero}\n" +
                $"Caseta: {fila.CodigoUnidad}\n" +
                $"Total: {fila.Total:N2}\n\n" +
                $"No fue posible cargar el comprobante " +
                $"completo: {ex.Message}";

            TxtEstado.Text =
                "No se habilitÃ³ la impresiÃ³n porque " +
                "faltan datos del comprobante.";

            BtnVistaPrevia.IsEnabled = false;
            BtnImprimir.IsEnabled = false;
        }
    }

    // CONSTRUIR EL DOCUMENTO
    // Solo utiliza los datos del recibo ya registrado.

    private static FlowDocument CrearDocumento(
        FilaRecibo fila)
    {
        var recibo = fila.Recibo;
        var pago = fila.Pago
            ?? throw new InvalidOperationException(
                "Faltan los datos del pago.");

        var documento = new FlowDocument
        {
            PagePadding = new Thickness(45),
            FontFamily = new FontFamily("Arial"),
            FontSize = 12,
            ColumnWidth = 650
        };

        AgregarTitulo(
            documento,
            "ASOCIACIÃ“N DE COMERCIANTES 13 DE JULIO",
            18);

        bool anulado = string.Equals(
            recibo.Estado,
            "Anulado",
            StringComparison.OrdinalIgnoreCase);

        AgregarTitulo(
            documento,
            anulado
                ? "RECIBO ANULADO"
                : "RECIBO DE CAJA",
            16);

        AgregarLinea(
            documento,
            $"Recibo N.º: {recibo.Numero}");

        AgregarLinea(
            documento,
            $"Fecha: {fila.FechaTexto}");

        AgregarLinea(
            documento,
            $"Caseta/local: {fila.CodigoUnidad}");

        AgregarLinea(
            documento,
            $"Bloque: {fila.Bloque}");

        AgregarLinea(
            documento,
            $"Pagador: {pago.NombrePagador}");

        AgregarLinea(
            documento,
            $"Forma de pago: {pago.FormaPago}");

        if (!string.IsNullOrWhiteSpace(pago.Referencia))
        {
            AgregarLinea(
                documento,
                $"N.º de transacciÃ³n/referencia: " +
                pago.Referencia);
        }

        AgregarLinea(
            documento,
            $"Moneda: {fila.Moneda}");

        AgregarLinea(
            documento,
            $"Tipo de cambio: {pago.TipoCambio:N2}");

        documento.Blocks.Add(
            new Paragraph(new Run("CONCEPTOS COBRADOS"))
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 18, 0, 8)
            });

        foreach (var detalle in recibo.Detalles ?? new())
        {
            AgregarLinea(
                documento,
                $"{detalle.Concepto} | " +
                $"{detalle.Periodo} | " +
                $"{detalle.Monto:N2} {fila.Moneda}");
        }

        documento.Blocks.Add(
            new Paragraph(
                new Run(
                    $"TOTAL: {recibo.Total:N2} {fila.Moneda}"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 20, 0, 15)
            });

        if (anulado)
        {
            documento.Blocks.Add(
                new Paragraph(
                    new Run(
                        "ANULADO â€” SIN VALIDEZ COMO " +
                        "COMPROBANTE DE PAGO"))
                {
                    Foreground = Brushes.DarkRed,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15
                });
        }
        else
        {
            AgregarLinea(
                documento,
                "REIMPRESIÃ“N DEL COMPROBANTE ORIGINAL");
        }

        return documento;
    }

    private static void AgregarTitulo(
        FlowDocument documento,
        string texto,
        double tamano)
    {
        documento.Blocks.Add(
            new Paragraph(new Run(texto))
            {
                FontSize = tamano,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(
            new Paragraph(new Run(texto))
            {
                Margin = new Thickness(0, 3, 0, 3)
            });
    }

    // VISTA PREVIA

    private void VistaPrevia_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TablaRecibos.SelectedItem is not FilaRecibo fila ||
            fila.Pago is null)
        {
            return;
        }

        try
        {
            var ventana = new VistaPreviaReciboWindow(
                fila.Recibo,
                fila.Pago,
                fila.CodigoUnidad,
                fila.Bloque,
                fila.Moneda);

            ventana.Owner = Window.GetWindow(this);
            ventana.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo abrir la vista previa.\n\n" +
                ex.Message,
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // REIMPRESION
    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TablaRecibos.SelectedItem
            is not FilaRecibo fila ||
            fila.Pago is null)
        {
            return;
        }

        try
        {
            var dialogo = new PrintDialog();

            if (dialogo.ShowDialog() != true)
                return;

            var documento = CrearDocumento(fila);

            documento.PageWidth =
                dialogo.PrintableAreaWidth;

            documento.PageHeight =
                dialogo.PrintableAreaHeight;

            dialogo.PrintDocument(
                ((IDocumentPaginatorSource)documento)
                    .DocumentPaginator,

                $"ReimpresiÃ³n recibo {fila.Numero}");

            TxtEstado.Text =
                $"Se enviÃ³ a imprimir el recibo " +
                $"N.º {fila.Numero}. " +
                "No se registrÃ³ ningÃºn cobro nuevo.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Error al imprimir",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // MODELOS PARA LA CONSULTA

private void RecibosView_SizeChanged(
    object sender,
    SizeChangedEventArgs e)
{
    if (ResultadosGrid == null ||
        TarjetaListado == null ||
        TarjetaDetalle == null)
    {
        return;
    }

    // Se usa el espacio real disponible en Recibos,
    // descontando el menú lateral de Mercadex.

    bool pantallaEstrecha = e.NewSize.Width < 800;

    if (pantallaEstrecha)
    {
        // Una columna: lista arriba, detalle debajo.

        ColListado.Width = new GridLength(1, GridUnitType.Star);
        ColDetalle.Width = new GridLength(0);

        FilaDetalle.Height = GridLength.Auto;

        Grid.SetColumn(TarjetaListado, 0);
        Grid.SetRow(TarjetaListado, 0);

        Grid.SetColumn(TarjetaDetalle, 0);
        Grid.SetRow(TarjetaDetalle, 1);

        TarjetaListado.Margin = new Thickness(0, 0, 0, 12);
        TarjetaDetalle.Margin = new Thickness(0);
    }
    else
    {
        // Dos columnas proporcionales, sin anchos fijos.

        ColListado.Width = new GridLength(2, GridUnitType.Star);
        ColDetalle.Width = new GridLength(3, GridUnitType.Star);

        FilaDetalle.Height = new GridLength(0);

        Grid.SetColumn(TarjetaListado, 0);
        Grid.SetRow(TarjetaListado, 0);

        Grid.SetColumn(TarjetaDetalle, 1);
        Grid.SetRow(TarjetaDetalle, 0);

        TarjetaListado.Margin = new Thickness(0, 0, 12, 0);
        TarjetaDetalle.Margin = new Thickness(0);
    }
}


    private sealed class UnidadResumen
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";

        public string? Bloque { get; set; }
    }

    private sealed class MonedaResumen
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";
    }
}

public sealed class FilaRecibo
{
    public ReciboDto Recibo { get; set; } = new();

    public PagoDto? Pago { get; set; }

    public string CodigoUnidad { get; set; } = "";

    public string Bloque { get; set; } = "";

    public string Moneda { get; set; } = "";

    public Dictionary<Guid, string> Monedas { get; set; }
        = new();

    public long Numero => Recibo.Numero;

    public decimal Total => Recibo.Total;

    public string Estado => Recibo.Estado;

    public string FechaTexto =>
        Recibo.FechaEmision.LocalDateTime
            .ToString("dd/MM/yyyy HH:mm");
}
