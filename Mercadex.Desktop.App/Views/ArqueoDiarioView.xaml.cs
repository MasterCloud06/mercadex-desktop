using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class ArqueoDiarioView : UserControl
{
    private readonly ApiClient _api = new();

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-BO");

    private ArqueoPreliminarResponse? _datos;
    private DateOnly _fechaConsultada;
    private bool _ocupado;

    public ArqueoDiarioView()
    {
        InitializeComponent();

        FechaArqueo.SelectedDate = DateTime.Today;
        HistHasta.SelectedDate = DateTime.Today;
        HistDesde.SelectedDate = DateTime.Today.AddDays(-30);

        Loaded += async (_, _) =>
        {
            if (_datos == null)
            {
                await ConsultarAsync();
                await CargarHistorialAsync();
            }
        };
    }

    // =============================================================
    // CONSULTA DEL DÍA
    // =============================================================

    private async void Consultar_Click(object sender, RoutedEventArgs e)
    {
        await ConsultarAsync();
    }

    private async Task ConsultarAsync(Guid? monedaPreferida = null)
    {
        if (_ocupado)
            return;

        if (FechaArqueo.SelectedDate is not DateTime fecha)
        {
            TxtEstado.Text = "Selecciona una fecha válida.";
            return;
        }

        _ocupado = true;
        BtnConsultar.IsEnabled = false;
        TxtEstado.Text = "Consultando la recaudación...";

        try
        {
            _fechaConsultada = DateOnly.FromDateTime(fecha);

            _datos = await _api.GetAsync<ArqueoPreliminarResponse>(
                "/api/arqueo-diario/preliminar?fecha=" +
                fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (_datos == null)
            {
                TxtEstado.Text = "La API no devolvió información.";
                return;
            }

            var resumen = _datos.Resumen ?? new();

            // Si no hubo cobros, igual se permite cerrar en bolivianos (total 0).
            SelectorMoneda.ItemsSource = resumen;
            SelectorMoneda.SelectedItem =
                resumen.FirstOrDefault(r => r.MonedaId == monedaPreferida)
                ?? resumen.FirstOrDefault();

            var mensaje = $"{_datos.CantidadPagosValidos} cobros válidos.";

            if (_datos.CantidadIncidencias > 0)
            {
                mensaje += $" {_datos.CantidadIncidencias} pagos requieren revisión: " +
                           string.Join(" / ", (_datos.PagosPorRevisar ?? new())
                               .Select(x => $"{x.Moneda} {x.Monto.ToString("N2", Cultura)} ({x.Cantidad})"));
            }

            if (resumen.Count == 0)
                mensaje = "No hay cobros válidos registrados en esta fecha.";

            TxtEstado.Text = mensaje;

            MostrarMoneda();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            TxtEstado.Text = "La función de arqueo no está publicada en el servidor.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo consultar el arqueo: " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnConsultar.IsEnabled = true;
        }
    }

    private void Moneda_Changed(object sender, SelectionChangedEventArgs e)
    {
        MostrarMoneda();
    }

    private ArqueoMonedaResumen? MonedaActual =>
        SelectorMoneda?.SelectedItem as ArqueoMonedaResumen;

    private void MostrarMoneda()
    {
        if (TileEfectivo == null)
            return;

        var m = MonedaActual;

        if (m == null)
        {
            TileEfectivo.Text = TileQr.Text = TileOtros.Text = TileTotal.Text = "—";
            TileEfectivoN.Text = TileQrN.Text = TileOtrosN.Text = TileTotalN.Text = " ";
            AvisoCambios.Visibility = Visibility.Collapsed;
            PanelCerrado.Visibility = Visibility.Collapsed;
            Chip("Sin cobros", "#EEF2F7", "#5A6B7D");
            BtnCerrar.IsEnabled = false;
            BtnActa.IsEnabled = false;
            PanelEntradas.IsEnabled = false;
            TxtDiferencia.Text = "Sin calcular";
            return;
        }

        TileEfectivo.Text = Monto(m.Efectivo, m.Moneda);
        TileQr.Text = Monto(m.Qr, m.Moneda);
        TileOtros.Text = Monto(m.Otros, m.Moneda);
        TileTotal.Text = Monto(m.Total, m.Moneda);
        TileEfectivoN.Text = Pagos(m.CantidadEfectivo);
        TileQrN.Text = Pagos(m.CantidadQr);
        TileOtrosN.Text = Pagos(m.CantidadOtros);
        TileTotalN.Text = Pagos(m.Cantidad);

        BtnActa.IsEnabled = true;

        if (m.Cerrado && m.Cierre != null)
        {
            var c = m.Cierre;
            Chip($"Caja cerrada · {m.Moneda}", "#E7F5E7", "#0B5A0B");

            PanelEntradas.IsEnabled = false;
            BtnCerrar.IsEnabled = false;
            PanelCerrado.Visibility = Visibility.Visible;
            TxtCerrado.Text =
                $"Cierre registrado el {c.RegistradoTexto} por {c.Responsable}.\n" +
                $"Total cerrado: {Monto(c.Total, m.Moneda)} · Efectivo contado: {Monto(c.EfectivoContado, m.Moneda)} · " +
                $"Diferencia: {Monto(c.Diferencia, m.Moneda)}" +
                (string.IsNullOrWhiteSpace(c.Observacion) ? "" : $"\nObservación: {c.Observacion}");

            TxtContado.Text = c.EfectivoContado.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            TxtResponsable.Text = c.Responsable;
            TxtObservacion.Text = c.Observacion ?? "";

            if (m.CambiosPosteriores)
            {
                AvisoCambios.Visibility = Visibility.Visible;
                TxtAvisoCambios.Text =
                    $"Hubo cobros o anulaciones después del cierre: el total cerrado fue {Monto(c.Total, m.Moneda)} " +
                    $"({Pagos(c.CantidadPagos)}) y hoy suma {Monto(m.Total, m.Moneda)} ({Pagos(m.Cantidad)}). Revíselo con Tesorería.";
            }
            else
            {
                AvisoCambios.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            Chip($"Caja abierta · {m.Moneda}", "#FFF4D6", "#8A5A00");
            PanelEntradas.IsEnabled = true;
            BtnCerrar.IsEnabled = _datos?.CantidadIncidencias == 0 ||
                                  !(_datos?.PagosPorRevisar ?? new()).Any(x => x.Moneda == m.Moneda);
            PanelCerrado.Visibility = Visibility.Collapsed;
            AvisoCambios.Visibility = Visibility.Collapsed;
            TxtContado.Text = "";
            TxtObservacion.Text = "";
        }

        ActualizarDiferencia();
    }

    private void Chip(string texto, string fondo, string color)
    {
        TxtChipEstado.Text = texto;
        ChipEstado.Background = ColoresPanel.Hex(fondo);
        TxtChipEstado.Foreground = ColoresPanel.Hex(color);
    }

    private void Contado_Changed(object sender, TextChangedEventArgs e)
    {
        ActualizarDiferencia();
    }

    private bool LeerContado(out decimal contado)
    {
        contado = 0;
        var texto = TxtContado.Text.Trim().Replace(',', '.');

        return texto.Length > 0 &&
               decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out contado) &&
               contado >= 0;
    }

    private void ActualizarDiferencia()
    {
        if (TxtDiferencia == null || TxtContado == null)
            return;

        var m = MonedaActual;

        if (m == null)
        {
            TxtDiferencia.Text = "Sin calcular";
            TxtDiferencia.Foreground = ColoresPanel.TextoPrincipal;
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtContado.Text))
        {
            TxtDiferencia.Text = $"Esperado: {Monto(m.Efectivo, m.Moneda)}";
            TxtDiferencia.Foreground = ColoresPanel.TextoPrincipal;
            return;
        }

        if (!LeerContado(out var contado))
        {
            TxtDiferencia.Text = "Importe no válido";
            TxtDiferencia.Foreground = Brushes.DarkRed;
            return;
        }

        var diferencia = contado - m.Efectivo;

        TxtDiferencia.Text = diferencia == 0
            ? "Sin diferencia"
            : (diferencia > 0 ? "Sobrante " : "Faltante ") + Monto(Math.Abs(diferencia), m.Moneda);

        TxtDiferencia.Foreground = diferencia == 0
            ? ColoresPanel.Hex("#006300")
            : ColoresPanel.Hex("#B42318");
    }

    // =============================================================
    // REGISTRAR CIERRE
    // =============================================================

    private async void Cerrar_Click(object sender, RoutedEventArgs e)
    {
        var m = MonedaActual;

        if (_ocupado || m == null || m.Cerrado)
            return;

        if (!LeerContado(out var contado))
        {
            MessageBox.Show("Escribe el efectivo contado (por ejemplo 850,50).", "Cierre de caja",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtContado.Focus();
            return;
        }

        var responsable = TxtResponsable.Text.Trim();

        if (responsable.Length < 3)
        {
            MessageBox.Show("Escribe el nombre del responsable del cierre.", "Cierre de caja",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtResponsable.Focus();
            return;
        }

        var diferencia = contado - m.Efectivo;
        var observacion = TxtObservacion.Text.Trim();

        if (diferencia != 0 && observacion.Length == 0)
        {
            MessageBox.Show("Hay una diferencia de efectivo. Escribe una observación que la explique.",
                "Cierre de caja", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtObservacion.Focus();
            return;
        }

        var confirmar = MessageBox.Show(
            $"¿Registrar el cierre de caja del {_fechaConsultada:dd/MM/yyyy} en {m.Moneda}?\n\n" +
            $"Efectivo: {Monto(m.Efectivo, m.Moneda)}\n" +
            $"QR: {Monto(m.Qr, m.Moneda)}\n" +
            $"Otros: {Monto(m.Otros, m.Moneda)}\n" +
            $"Total: {Monto(m.Total, m.Moneda)}\n\n" +
            $"Efectivo contado: {Monto(contado, m.Moneda)}\n" +
            $"Diferencia: {Monto(diferencia, m.Moneda)}\n\n" +
            "Un cierre registrado no se puede modificar.",
            "Confirmar cierre de caja",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _ocupado = true;
        BtnCerrar.IsEnabled = false;

        try
        {
            await _api.PostAsync<RegistrarCierreRequest, CierreCajaModel>(
                "/api/arqueo-diario/cierres",
                new RegistrarCierreRequest(
                    _fechaConsultada,
                    m.MonedaId,
                    contado,
                    responsable,
                    observacion.Length == 0 ? null : observacion));

            _ocupado = false;

            await ConsultarAsync(m.MonedaId);
            await CargarHistorialAsync();

            if (MessageBox.Show("Cierre registrado.\n\n¿Deseas imprimir o guardar el acta ahora?",
                    "Cierre de caja", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                AbrirActa();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo registrar el cierre",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            BtnCerrar.IsEnabled = true;
        }
        finally
        {
            _ocupado = false;
        }
    }

    // =============================================================
    // ACTA (VISTA PREVIA / IMPRIMIR / PDF)
    // =============================================================

    private void Acta_Click(object sender, RoutedEventArgs e) => AbrirActa();

    private void AbrirActa()
    {
        var m = MonedaActual;

        if (m == null)
            return;

        decimal? contado = null;
        if (m.Cerrado && m.Cierre != null)
            contado = m.Cierre.EfectivoContado;
        else if (LeerContado(out var c))
            contado = c;

        var acta = new ActaArqueo(
            _fechaConsultada,
            m.Moneda,
            m.Efectivo, m.CantidadEfectivo,
            m.Qr, m.CantidadQr,
            m.Otros, m.CantidadOtros,
            m.Total, m.Cantidad,
            contado,
            m.Cerrado && m.Cierre != null ? m.Cierre.Diferencia : contado - m.Efectivo,
            m.Cerrado ? m.Cierre?.Responsable : NullSiVacio(TxtResponsable.Text),
            m.Cerrado ? m.Cierre?.Observacion : NullSiVacio(TxtObservacion.Text),
            m.Cerrado,
            m.Cierre?.FechaRegistro);

        new ActaArqueoWindow(acta) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    // =============================================================
    // HISTORIAL
    // =============================================================

    private async void Historial_Click(object sender, RoutedEventArgs e)
    {
        await CargarHistorialAsync();
    }

    private async Task CargarHistorialAsync()
    {
        if (HistDesde.SelectedDate is not DateTime desde || HistHasta.SelectedDate is not DateTime hasta)
            return;

        try
        {
            var lista = await _api.GetAsync<List<CierreCajaModel>>(
                "/api/arqueo-diario/cierres?desde=" +
                desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                "&hasta=" + hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            TablaHistorial.ItemsSource = lista ?? new();
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo cargar el historial: " + ex.Message;
        }
    }

    private void Historial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnActaHistorial.IsEnabled = TablaHistorial.SelectedItem is CierreCajaModel;
    }

    private void ActaHistorial_Click(object sender, RoutedEventArgs e)
    {
        if (TablaHistorial.SelectedItem is not CierreCajaModel c)
            return;

        DateOnly.TryParse(c.Fecha, CultureInfo.InvariantCulture, out var fecha);

        var acta = new ActaArqueo(
            fecha, c.Moneda,
            c.Efectivo, c.CantidadEfectivo,
            c.Qr, c.CantidadQr,
            c.Otros, c.CantidadOtros,
            c.Total, c.CantidadPagos,
            c.EfectivoContado, c.Diferencia,
            c.Responsable, c.Observacion,
            true, c.FechaRegistro);

        new ActaArqueoWindow(acta) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    // =============================================================

    private static string Monto(decimal v, string moneda) =>
        $"{v.ToString("N2", Cultura)} {moneda}";

    private static string Pagos(int n) => n == 1 ? "1 pago" : $"{n:N0} pagos";

    private static string? NullSiVacio(string texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
