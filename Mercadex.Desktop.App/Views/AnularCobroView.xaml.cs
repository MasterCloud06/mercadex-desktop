
using Mercadex.Desktop.App.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

public partial class AnularCobroView : UserControl
{
    private static readonly HttpClient Api = new()
    {
BaseAddress = ApiConfiguration.BaseUri,

        Timeout = TimeSpan.FromSeconds(60)
    };

    private List<ReciboDto> _recibos = new();

    private bool _cargando;
    private bool _inicializado;

    public AnularCobroView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (_inicializado)
                return;

            _inicializado = true;

            await CargarRecibosAsync();
        };
    }

    private async Task CargarRecibosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;
        BtnAnular.IsEnabled = false;

        TxtEstado.Text =
            "Consultando recibos registrados...";

        try
        {
            _recibos =
                await Api.GetFromJsonAsync<List<ReciboDto>>(
                    "/api/Caja/recibos")
                ?? new();

            FiltrarRecibos();
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

    private void Buscar_Click(
        object sender,
        RoutedEventArgs e)
    {
        FiltrarRecibos();
    }

    private void TxtNumero_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        FiltrarRecibos();
    }

    private void FiltrarRecibos()
    {
        var numeroTexto = TxtNumero.Text.Trim();

        long? numero = null;

        if (numeroTexto.Length > 0)
        {
            if (!long.TryParse(
                numeroTexto,
                out var numeroIngresado))
            {
                TxtEstado.Text =
                    "Introduce un numero de recibo valido.";

                TablaRecibos.ItemsSource = null;
                return;
            }

            numero = numeroIngresado;
        }

        var fecha = FechaFiltro.SelectedDate?.Date;

        var resultado = _recibos
            .Where(r =>
                !numero.HasValue ||
                r.Numero == numero.Value)
            .Where(r =>
                !fecha.HasValue ||
                r.FechaEmision.LocalDateTime.Date ==
                    fecha.Value)
            .OrderByDescending(r => r.Numero)
            .ToList();

        TablaRecibos.ItemsSource = resultado;

        TxtEstado.Text =
            $"Recibos encontrados: {resultado.Count}.";
    }

    private async void TablaRecibos_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        BtnAnular.IsEnabled = false;
        TxtMotivo.IsEnabled = false;

        if (TablaRecibos.SelectedItem
            is not ReciboDto seleccionado)
        {
            TxtEstadoRecibo.Text = "SIN SELECCIONAR";

            TxtDetalle.Text =
                "Selecciona un recibo para consultar sus datos.";

            return;
        }

        TxtDetalle.Text =
            "Consultando comprobante...";

        try
        {
            var recibo =
                await Api.GetFromJsonAsync<ReciboDto>(
                    $"/api/Caja/recibos/{seleccionado.Id}");

            if (recibo is null)
                throw new Exception(
                    "No se encontro el recibo.");

            var pago =
                await Api.GetFromJsonAsync<PagoDto>(
                    $"/api/Caja/pagos/{recibo.PagoId}");

            if (TablaRecibos.SelectedItem
                is not ReciboDto actual ||
                actual.Id != seleccionado.Id)
            {
                return;
            }

            bool anulado = string.Equals(
                recibo.Estado,
                "Anulado",
                StringComparison.OrdinalIgnoreCase);

            TxtEstadoRecibo.Text = anulado
                ? $"RECIBO N. {recibo.Numero} - ANULADO"
                : $"RECIBO N. {recibo.Numero} - " +
                  recibo.Estado.ToUpperInvariant();

            TxtEstadoRecibo.Foreground = anulado
                ? Brushes.DarkRed
                : Brushes.DarkGreen;

            var conceptos = recibo.Detalles?
                .Select(d =>
                    $"{d.Concepto}: {d.Monto:N2}")
                .ToList()
                ?? new List<string>();

            TxtDetalle.Text =
                $"Numero: {recibo.Numero}\n" +
                $"Fecha: " +
                $"{recibo.FechaEmision.LocalDateTime:dd/MM/yyyy HH:mm}\n" +
                $"Pagador: {pago?.NombrePagador ?? "-"}\n" +
                $"Forma de pago: {pago?.FormaPago ?? "-"}\n" +
                $"Referencia: {pago?.Referencia ?? "-"}\n" +
                $"Estado del pago: {pago?.Estado ?? "-"}\n" +
                $"Importe: {recibo.Total:N2}\n\n" +
                "CONCEPTOS:\n" +
                string.Join("\n", conceptos);

            // Solo los recibos emitidos pueden anularse.
            var anulable = string.Equals(
                recibo.Estado,
                "Emitido",
                StringComparison.OrdinalIgnoreCase);

            TxtMotivo.IsEnabled = anulable;
            BtnAnular.IsEnabled = anulable;

            if (!anulable)
                TxtMotivo.Text = "";
        }
        catch (Exception ex)
        {
            if (TablaRecibos.SelectedItem
                is not ReciboDto actual ||
                actual.Id != seleccionado.Id)
            {
                return;
            }

            TxtEstadoRecibo.Text =
                "ERROR AL CONSULTAR";

            TxtEstadoRecibo.Foreground =
                Brushes.DarkRed;

            TxtDetalle.Text = ex.Message;
        }
    }

    private async void Anular_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_cargando ||
            TablaRecibos.SelectedItem is not ReciboDto recibo)
        {
            return;
        }

        var motivo = TxtMotivo.Text.Trim();

        if (motivo.Length < 5)
        {
            MessageBox.Show(
                "Escribe el motivo de la anulacion " +
                "(al menos 5 caracteres).",
                "Anular cobro",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmar = MessageBox.Show(
            $"Se anulara el recibo N. {recibo.Numero} " +
            $"por {recibo.Total:N2}.\n\n" +
            "Las deudas cobradas volveran a quedar pendientes " +
            "y se registrara un comprobante de anulacion.\n\n" +
            "Esta operacion no se puede deshacer. ¿Continuar?",
            "Confirmar anulacion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _cargando = true;
        BtnAnular.IsEnabled = false;
        TxtMotivo.IsEnabled = false;
        TxtEstado.Text = "Anulando recibo...";

        try
        {
            await new AnulacionAutorizadaService()
                .AnularAsync(
                    recibo.Id,
                    motivo);

            TxtMotivo.Text = "";

            MessageBox.Show(
                $"El recibo N. {recibo.Numero} fue anulado.\n" +
                "Puedes imprimir el comprobante en " +
                "Obligaciones > Tarifas por caseta > " +
                "Comprobantes anulados.",
                "Anular cobro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo anular el recibo: " + ex.Message;

            MessageBox.Show(
                ex.Message,
                "No se pudo anular",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            BtnAnular.IsEnabled = true;
            TxtMotivo.IsEnabled = true;
            _cargando = false;
            return;
        }

        _cargando = false;

        await CargarRecibosAsync();

        TablaRecibos.SelectedItem =
            (TablaRecibos.ItemsSource as IEnumerable<ReciboDto>)?
                .FirstOrDefault(r => r.Id == recibo.Id);
    }
}
