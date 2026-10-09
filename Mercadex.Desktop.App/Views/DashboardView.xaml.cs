
using Mercadex.Desktop.App.Models;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class DashboardView : UserControl
{
    private static readonly HttpClient Http = new()
    {
BaseAddress = ApiConfiguration.BaseUri,

        Timeout = TimeSpan.FromSeconds(30)
    };

    private bool _cargando;

    public DashboardView()
    {
        InitializeComponent();

        TxtFecha.Text = DateTime.Now.ToString(
            "dddd, dd 'de' MMMM 'de' yyyy",
            new CultureInfo("es-BO"));

        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= DashboardView_Loaded;

        await CargarDatosAsync();
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarDatosAsync();
    }

    private async Task CargarDatosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;

        TxtEstado.Text =
            "Consultando los datos de Mercadex...";

        try
        {
            // Consultar las unidades comerciales.

            var unidades = await Http.GetFromJsonAsync<
                List<UnidadIngresoModel>>(
                    "/api/unidades-comerciales")
                ?? new List<UnidadIngresoModel>();

            var activas = unidades
                .Where(u => u.Activa)
                .ToList();

            TxtCasetas.Text = activas
                .Count(u => u.Tipo == 1)
                .ToString("N0");

            TxtComedores.Text = activas
                .Count(u => u.Tipo == 2)
                .ToString("N0");

            TxtAmbulantes.Text = activas
                .Count(u => u.Tipo == 3)
                .ToString("N0");

            TxtTotalUnidades.Text =
                activas.Count.ToString("N0");

            // Consultar las monedas registradas.

            var monedas = await Http.GetFromJsonAsync<
                List<MonedaIngresoModel>>(
                    "/api/monedas")
                ?? new List<MonedaIngresoModel>();

            var bob = monedas.FirstOrDefault(
                m => m.Codigo.Equals(
                    "BOB",
                    StringComparison.OrdinalIgnoreCase));

            var usd = monedas.FirstOrDefault(
                m => m.Codigo.Equals(
                    "USD",
                    StringComparison.OrdinalIgnoreCase));

            // Consultar todas las obligaciones.

            var obligaciones = await Http.GetFromJsonAsync<
                List<ObligacionIngresoModel>>(
                    "/api/Obligaciones")
                ?? new List<ObligacionIngresoModel>();

            var pendientes = obligaciones
                .Where(o => o.Estado.Equals(
                    "Pendiente",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            TxtPendientes.Text =
                pendientes.Count.ToString("N0");

            // No se mezclan monedas.

            var totalBob = bob == null
                ? 0m
                : pendientes
                    .Where(o => o.MonedaId == bob.Id)
                    .Sum(o => o.Monto);

            var totalUsd = usd == null
                ? 0m
                : pendientes
                    .Where(o => o.MonedaId == usd.Id)
                    .Sum(o => o.Monto);

            TxtPendienteBob.Text =
                $"Bs {totalBob:N2}";

            TxtPendienteUsd.Text =
                $"$ {totalUsd:N2}";

            // Consultar recibos registrados.

            var recibos = await Http.GetFromJsonAsync<
                List<ReciboRegistradoIngresoModel>>(
                    "/api/Caja/recibos")
                ?? new List<ReciboRegistradoIngresoModel>();

            TxtRecibos.Text =
                recibos.Count.ToString("N0");

            TxtEstado.Text =
                "Datos actualizados: " +
                DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }
        catch (OperationCanceledException)
        {
            TxtEstado.Text =
                "La API tardó demasiado en responder. " +
                "Pulsa 'Actualizar datos' para intentarlo nuevamente.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No fue posible actualizar el panel: " +
                ex.Message;
        }
        finally
        {
            _cargando = false;
        }
    }
}
