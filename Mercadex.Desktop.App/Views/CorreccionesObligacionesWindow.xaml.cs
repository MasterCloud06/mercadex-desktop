using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mercadex.Desktop.App.Views;

public partial class CorreccionesObligacionesWindow : Window
{
    private readonly HttpClient _api;
    private readonly Guid _conceptoId;
    private readonly int _anio;
    private readonly int _mes;

    private bool _cargando;
    private bool _corrigiendo;
    private int _versionSeleccion;

    public CorreccionesObligacionesWindow(
        HttpClient api,
        Guid conceptoId,
        int anio,
        int mes)
    {
        InitializeComponent();

        _api = api;
        _conceptoId = conceptoId;
        _anio = anio;
        _mes = mes;

        TxtPeriodo.Text =
            $"Obligaciones de {mes:00}/{anio}";

        Loaded += async (_, _) => await CargarAsync();
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarAsync();
    }

    private async Task CargarAsync(Guid? seleccionarId = null)
    {
        if (_cargando || _corrigiendo)
            return;

        _cargando = true;
        BtnActualizar.IsEnabled = false;
        BtnCorregir.IsEnabled = false;

        try
        {
            TxtEstado.Text = "Consultando obligaciones...";

            var url = "/api/obligaciones-correcciones/listado" +
                $"?conceptoCobroId={_conceptoId}" +
                $"&anio={_anio}&mes={_mes}";

            var respuesta = await _api
                .GetFromJsonAsync<ListadoRespuesta>(url);

            if (respuesta is null)
                throw new InvalidOperationException(
                    "La API no devolvio las obligaciones.");

            var anterior = seleccionarId ??
                (TablaDeudas.SelectedItem as DeudaFila)?.Id;

            var deudas = respuesta.Obligaciones ?? new();

            TablaDeudas.ItemsSource = deudas;

            var seleccionar = deudas.FirstOrDefault(
                x => anterior.HasValue && x.Id == anterior.Value)
                ?? deudas.FirstOrDefault();

            TablaDeudas.SelectedItem = seleccionar;

            var totales = string.Join(" | ",
                (respuesta.TotalPorMoneda ?? new())
                .Select(x =>
                    $"{x.Moneda} {x.Total:N2}"));

            TxtResumen.Text =
                $"Deudas: {respuesta.Cantidad} | " +
                $"Pendientes: {respuesta.Pendientes} | " +
                $"Modificadas: {respuesta.Modificadas} | " +
                $"Total: {totales}";

            TxtEstado.Text =
                $"Listado actualizado para {_mes:00}/{_anio}.";
        }
        catch (Exception ex)
        {
            BtnCorregir.IsEnabled = false;

            TxtEstado.Text =
                "No se pudo cargar el listado: " + ex.Message;
        }
        finally
        {
            _cargando = false;
            BtnActualizar.IsEnabled = true;
        }
    }

    private async void Deudas_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _versionSeleccion++;

        var version = _versionSeleccion;

        TablaHistorial.ItemsSource = null;

        var deuda = TablaDeudas.SelectedItem as DeudaFila;

        if (deuda is null)
        {
            TxtSeleccion.Text = "Selecciona una deuda.";
            BtnCorregir.IsEnabled = false;
            TxtNuevoMonto.Text = "";
            TxtMotivo.Clear();
            return;
        }

        TxtSeleccion.Text =
            $"Caseta {deuda.Caseta} | " +
            $"Importe actual: {deuda.Moneda} {deuda.Monto:N2} | " +
            $"Estado: {deuda.Estado}";

        TxtNuevoMonto.Text = deuda.Monto.ToString(
            "0.00",
            CultureInfo.InvariantCulture);

        TxtMotivo.Clear();

        BtnCorregir.IsEnabled =
            deuda.PuedeModificar && !_corrigiendo;

        await CargarHistorialAsync(deuda.Id, version);
    }

    private async Task CargarHistorialAsync(
        Guid obligacionId,
        int version)
    {
        try
        {
            var url =
                $"/api/obligaciones-correcciones/" +
                $"{obligacionId}/historial";

            var respuesta = await _api
                .GetFromJsonAsync<HistorialRespuesta>(url);

            if (version != _versionSeleccion)
                return;

            TablaHistorial.ItemsSource =
                respuesta?.Historial ?? new();
        }
        catch (Exception ex)
        {
            if (version == _versionSeleccion)
            {
                TxtEstado.Text =
                    "No se pudo cargar el historial: " +
                    ex.Message;
            }
        }
    }

    private async void Corregir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_corrigiendo || _cargando)
            return;

        if (TablaDeudas.SelectedItem is not DeudaFila deuda)
            return;

        if (!deuda.PuedeModificar)
        {
            TxtEstado.Text =
                "Esta obligacion no permite corregir su importe.";
            return;
        }

        var textoMonto = TxtNuevoMonto.Text
            .Trim()
            .Replace(',', '.');

        if (!decimal.TryParse(
            textoMonto,
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var nuevoMonto))
        {
            TxtEstado.Text = "El importe ingresado no es valido.";
            return;
        }

        if (nuevoMonto <= 0 ||
            decimal.Round(nuevoMonto, 2) != nuevoMonto ||
            nuevoMonto == deuda.Monto)
        {
            TxtEstado.Text =
                "El nuevo importe debe ser positivo, " +
                "distinto del actual y tener maximo dos decimales.";
            return;
        }

        var motivo = TxtMotivo.Text.Trim();

        if (motivo.Length < 5 || motivo.Length > 500)
        {
            TxtEstado.Text =
                "El motivo debe tener entre 5 y 500 caracteres.";
            return;
        }

        var confirmar = Dialogo.Mostrar(
            this,
            $"Caseta: {deuda.Caseta}\n" +
            $"Período: {_mes:00}/{_anio}\n\n" +
            $"Importe anterior: {deuda.Moneda} {deuda.Monto:N2}\n" +
            $"Importe nuevo: {deuda.Moneda} {nuevoMonto:N2}\n\n" +
            $"Motivo:\n{motivo}\n\n" +
            "Esta corrección quedará registrada " +
            "en el historial de la obligación.\n\n" +
            "¿Confirmas la modificación?",
            "Confirmar modificación de deuda",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _corrigiendo = true;
        BtnCorregir.IsEnabled = false;
        BtnActualizar.IsEnabled = false;

        try
        {
            TxtEstado.Text =
                "Guardando corrección e historial...";

            var solicitud = new
            {
                montoAnteriorEsperado = deuda.Monto,
                montoNuevo = nuevoMonto,
                motivo
            };

            using var respuesta = await _api.PutAsJsonAsync(
                $"/api/obligaciones-correcciones/{deuda.Id}/monto",
                solicitud);

            if (!respuesta.IsSuccessStatusCode)
            {
                TxtEstado.Text =
                    $"Correccion rechazada (HTTP " +
                    $"{(int)respuesta.StatusCode}). " +
                    "Actualiza las deudas antes de intentar nuevamente.";

                return;
            }

            TxtEstado.Text =
                "Correccion guardada. Actualizando listado...";

            _corrigiendo = false;

            await CargarAsync(deuda.Id);

            Dialogo.Mostrar(
                this,
                "Importe corregido y registrado en el historial. " +
                "La tarifa general de la caseta no fue modificada.",
                "Modificación completada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo confirmar la operacion. " +
                "Actualiza el listado antes de repetir: " +
                ex.Message;
        }
        finally
        {
            _corrigiendo = false;
            BtnActualizar.IsEnabled = true;

            if (TablaDeudas.SelectedItem is DeudaFila actual)
            {
                BtnCorregir.IsEnabled = actual.PuedeModificar;
            }
        }
    }

    public sealed class ListadoRespuesta
    {
        public int Cantidad { get; set; }
        public int Pendientes { get; set; }
        public int Modificadas { get; set; }

        public List<DeudaFila> Obligaciones { get; set; } = new();

        public List<TotalMoneda> TotalPorMoneda { get; set; } = new();
    }

    public sealed class TotalMoneda
    {
        public string Moneda { get; set; } = "";
        public decimal Total { get; set; }
    }

    public sealed class DeudaFila
    {
        public Guid Id { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string Estado { get; set; } = "";
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
        public bool Modificado { get; set; }
        public bool PuedeModificar { get; set; }
        public bool TieneRecibo { get; set; }

        public string Distintivo =>
            Modificado ? "Modificado" : "-";

        public string PermisoVisual =>
            PuedeModificar ? "Permitida" : "Bloqueada";
    }

    public sealed class HistorialRespuesta
    {
        public bool Modificado { get; set; }

        public List<CambioFila> Historial { get; set; } = new();
    }

    public sealed class CambioFila
    {
        public decimal MontoAnterior { get; set; }
        public decimal MontoNuevo { get; set; }
        public string Motivo { get; set; } = "";
        public string Responsable { get; set; } = "";
        public DateTime FechaUtc { get; set; }

        public string FechaLocal =>
            FechaUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}