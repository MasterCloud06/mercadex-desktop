using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class ArqueoDiarioView : UserControl
{
    private readonly ApiClient _api = new();

    private static readonly CultureInfo Cultura =
        CultureInfo.GetCultureInfo("es-BO");

    private bool _consultando;

    public ArqueoDiarioView()
    {
        InitializeComponent();

        FechaArqueo.SelectedDate = DateTime.Today;
    }

    private async void Consultar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ConsultarAsync();
    }

    private async Task ConsultarAsync()
    {
        if (_consultando)
            return;

        if (FechaArqueo.SelectedDate is not DateTime fecha)
        {
            TxtEstado.Text = "Selecciona una fecha válida.";
            return;
        }

        _consultando = true;
        BtnConsultar.IsEnabled = false;

        TxtEstado.Text = "Consultando la recaudación...";

        TablaResumen.ItemsSource = null;
        SelectorMoneda.ItemsSource = null;
        TxtContado.Text = string.Empty;
        TxtDiferencia.Text = "Sin calcular";

        try
        {
            var fechaTexto = fecha.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            var resultado =
                await _api.GetAsync<ArqueoPreliminarResponse>(
                    "/api/arqueo-diario/preliminar?fecha=" +
                    fechaTexto);

            if (resultado == null)
            {
                TxtEstado.Text =
                    "La API no devolvió información.";
                return;
            }

            var resumen = resultado.Resumen
                ?? new();

            TablaResumen.ItemsSource = resumen;

            SelectorMoneda.ItemsSource = resumen;

            if (resumen.Count > 0)
                SelectorMoneda.SelectedIndex = 0;

            var mensaje =
                $"Fecha: {resultado.Fecha} | " +
                $"Pagos válidos: {resultado.CantidadPagosValidos}";

            if (resultado.CantidadIncidencias > 0)
            {
                mensaje +=
                    $" | Pagos por revisar: " +
                    $"{resultado.CantidadIncidencias}.";

                var pendientes = resultado.PagosPorRevisar
                    ?? new();

                if (pendientes.Count > 0)
                {
                    mensaje += " Incidencias: " +
                        string.Join(" / ",
                            pendientes.Select(x =>
                                $"{x.Moneda}: {x.Monto:N2} " +
                                $"({x.Cantidad} pagos)"));
                }
            }
            else
            {
                mensaje += " | Sin incidencias detectadas.";
            }

            if (resumen.Count == 0)
            {
                mensaje +=
                    " No hay recaudaciones válidas " +
                    "registradas para esta fecha.";
            }

            TxtEstado.Text = mensaje;
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            TxtEstado.Text =
                "La función de arqueo todavía no está " +
                "publicada en el servidor. " +
                "Debemos desplegar el nuevo Backend.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo consultar el arqueo. " +
                ex.Message;
        }
        finally
        {
            _consultando = false;
            BtnConsultar.IsEnabled = true;
        }
    }

    private void Moneda_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (TxtContado != null)
            TxtContado.Text = string.Empty;

        ActualizarDiferencia();
    }

    private void Contado_Changed(
        object sender,
        TextChangedEventArgs e)
    {
        ActualizarDiferencia();
    }

    private void ActualizarDiferencia()
    {
        if (TxtDiferencia == null ||
            TxtContado == null ||
            SelectorMoneda == null)
        {
            return;
        }

        if (SelectorMoneda.SelectedItem
            is not ArqueoMonedaResumen moneda)
        {
            TxtDiferencia.Text = "Sin calcular";
            return;
        }

        var texto = TxtContado.Text.Trim();

        if (string.IsNullOrWhiteSpace(texto))
        {
            TxtDiferencia.Text =
                $"Esperado: {moneda.Efectivo.ToString("N2", Cultura)} " +
                moneda.Moneda;

            TxtDiferencia.Foreground = Brushes.DarkSlateBlue;
            return;
        }

        // Aceptar coma o punto como separador decimal.
        // No se admiten separadores de miles.
        var normalizado = texto.Replace(',', '.');

        if (!decimal.TryParse(
            normalizado,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var contado) ||
            contado < 0)
        {
            TxtDiferencia.Text = "Introduce un importe válido.";
            TxtDiferencia.Foreground = Brushes.DarkRed;
            return;
        }

        var diferencia = contado - moneda.Efectivo;

        TxtDiferencia.Text =
            $"{diferencia.ToString("N2", Cultura)} " +
            moneda.Moneda;

        TxtDiferencia.Foreground = diferencia == 0
            ? Brushes.ForestGreen
            : Brushes.DarkRed;
    }
}