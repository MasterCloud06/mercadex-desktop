
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class KardexView : UserControl
{
private readonly HttpClient _api = new()
{
    BaseAddress = ApiConfiguration.BaseUri,
    Timeout = TimeSpan.FromSeconds(45)
};

    private bool _cargando;

    public KardexView()
    {
        InitializeComponent();

        EstadoVida.SelectedIndex = 0;
        Documento.SelectedIndex = 0;
        Aporte.SelectedIndex = 0;
    }

    // INICIAR SESIÓN

    private async void Entrar_Click(
        object sender,
        RoutedEventArgs e)
    {
        BtnLogin.IsEnabled = false;

        Estado.Text = "Iniciando sesión...";

        try
        {
            var respuesta = await _api.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    usuario = Usuario.Text.Trim(),
                    password = Clave.Password
                });

            Clave.Clear();

            if (!respuesta.IsSuccessStatusCode)
            {
                Estado.Text =
                    respuesta.StatusCode == HttpStatusCode.Unauthorized
                    ? "Usuario o contraseña incorrectos."
                    : $"Error HTTP {(int)respuesta.StatusCode}.";

                return;
            }

            var sesion = await respuesta.Content
                .ReadFromJsonAsync<SesionKardex>();

            if (string.IsNullOrWhiteSpace(sesion?.Token))
            {
                throw new Exception(
                    "No se recibió el token de acceso.");
            }

            _api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    sesion.Token);

            Casetas.IsEnabled = true;

            await CargarCasetasYSocios();
        }
        catch (Exception ex)
        {
            MostrarError(ex);
        }
        finally
        {
            BtnLogin.IsEnabled = true;
        }
    }

    // CARGAR CASETAS Y SOCIOS

    private async Task CargarCasetasYSocios()
    {
        _cargando = true;

        try
        {
            Estado.Text = "Cargando casetas y socios...";

            var unidades = await _api.GetFromJsonAsync<
                List<UnidadKardex>>(
                    "/api/unidades-comerciales")
                ?? new();

            Casetas.ItemsSource = unidades
                .Where(u => u.Tipo == 1)
                .OrderBy(u =>
                    int.TryParse(u.Codigo, out var numero)
                        ? numero
                        : int.MaxValue)
                .ThenBy(u => u.Codigo)
                .ToList();

            var socios = await _api.GetFromJsonAsync<
                List<SocioModel>>(
                    "/api/socios")
                ?? new();

            var opciones = new List<SocioOpcion>
            {
                new SocioOpcion
                {
                    Id = null,
                    NombreCompleto = "Sin asignar"
                }
            };

            opciones.AddRange(
                socios
                    .OrderBy(s => s.NombreCompleto)
                    .Select(s => new SocioOpcion
                    {
                        Id = s.Id,
                        NombreCompleto = s.NombreCompleto
                    }));

            Socios.ItemsSource = opciones;
            Socios.SelectedIndex = 0;

            Estado.Text =
                $"Casetas disponibles: " +
                $"{Casetas.Items.Count}. Selecciona una caseta.";
        }
        finally
        {
            _cargando = false;
        }
    }

    // SELECCIONAR CASETA

    private async void Casetas_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_cargando ||
            Casetas.SelectedItem is not UnidadKardex unidad)
        {
            return;
        }

        Formulario.IsEnabled = false;

        Estado.Text =
            $"Consultando caseta {unidad.Codigo}...";

        Ubicacion.Text =
            $"Caseta {unidad.Codigo} - " +
            $"Bloque: {unidad.Bloque ?? "Sin bloque"}";

        try
        {
            var ficha = await _api.GetFromJsonAsync<
                RespuestaKardex>(
                    $"/api/kardex/caseta/{unidad.Id}");

            var opciones =
                Socios.ItemsSource as List<SocioOpcion>;

            if (opciones != null)
            {
                Socios.SelectedItem = opciones
                    .FirstOrDefault(s =>
                        s.Id == ficha?.Kardex?.SocioId)
                    ?? opciones[0];
            }

            FechaIngreso.SelectedDate =
                ficha?.Kardex?.FechaIngreso is DateOnly fecha
                    ? fecha.ToDateTime(TimeOnly.MinValue)
                    : null;

            Seleccionar(
                EstadoVida,
                ficha?.Kardex?.EstadoVida);

            Seleccionar(
                Documento,
                ficha?.Kardex?.TipoDocumentoPropiedad);

            string? aporte = null;

            if (ficha?.Kardex?.AporteSocioPagado == true)
                aporte = "SI";

            if (ficha?.Kardex?.AporteSocioPagado == false)
                aporte = "NO";

            Seleccionar(Aporte, aporte);

            PropietarioAnterior.Text =
                ficha?.Kardex?.PropietarioAnterior ?? "";

            Observaciones.Text =
                ficha?.Kardex?.Observaciones ?? "";

            Formulario.IsEnabled = true;

            Estado.Text = ficha?.Kardex == null
                ? "La caseta todavía no tiene Kardex."
                : "Kardex cargado correctamente.";
        }
        catch (Exception ex)
        {
            MostrarError(ex);
        }
    }

    // GUARDAR KARDEX

    private async void Guardar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Casetas.SelectedItem is not UnidadKardex unidad)
            return;

        var confirmar = MessageBox.Show(
            $"¿Guardar el Kardex de la caseta {unidad.Codigo}?",
            "Mercadex",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
            return;

        BtnGuardar.IsEnabled = false;

        try
        {
            var solicitud = new
            {
                socioId =
                    (Socios.SelectedItem as SocioOpcion)?.Id,

                fechaIngreso =
                    FechaIngreso.SelectedDate.HasValue
                    ? DateOnly.FromDateTime(
                        FechaIngreso.SelectedDate.Value)
                    : (DateOnly?)null,

                estadoVida = ObtenerValor(EstadoVida),

                tipoDocumentoPropiedad =
                    ObtenerValor(Documento),

                propietarioAnterior =
                    PropietarioAnterior.Text.Trim(),

                aporteSocioPagado =
                    ObtenerValor(Aporte) switch
                    {
                        "SI" => (bool?)true,
                        "NO" => false,
                        _ => null
                    },

                observaciones =
                    Observaciones.Text.Trim()
            };

            var respuesta = await _api.PutAsJsonAsync(
                $"/api/kardex/caseta/{unidad.Id}",
                solicitud);

            if (!respuesta.IsSuccessStatusCode)
            {
                Estado.Text =
                    $"No se pudo guardar. " +
                    $"HTTP {(int)respuesta.StatusCode}: " +
                    await respuesta.Content.ReadAsStringAsync();

                return;
            }

            Estado.Text =
                $"Kardex de la caseta {unidad.Codigo} " +
                "guardado correctamente.";

            MessageBox.Show(
                Estado.Text,
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MostrarError(ex);
        }
        finally
        {
            BtnGuardar.IsEnabled = true;
        }
    }

    private static string? ObtenerValor(
        ComboBox combo)
    {
        var valor =
            (combo.SelectedItem as ComboBoxItem)?.Tag
                as string;

        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor;
    }

    private static void Seleccionar(
        ComboBox combo,
        string? valor)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(
                    item.Tag as string,
                    valor ?? "",
                    StringComparison.OrdinalIgnoreCase))
            ?? combo.Items[0];
    }

    private void MostrarError(Exception ex)
    {
        Estado.Text = "Error: " + ex.Message;
    }

    private sealed class SesionKardex
    {
        public string Token { get; set; } = "";
    }

    private sealed class UnidadKardex
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";

        public int Tipo { get; set; }

        public string? Bloque { get; set; }

        public string Etiqueta =>
            $"Caseta {Codigo} - {Bloque ?? "Sin bloque"}";
    }

    private sealed class SocioOpcion
    {
        public Guid? Id { get; set; }

        public string NombreCompleto { get; set; } = "";
    }

    private sealed class RespuestaKardex
    {
        public FichaKardex? Kardex { get; set; }
    }

    private sealed class FichaKardex
    {
        public Guid? SocioId { get; set; }

        public DateOnly? FechaIngreso { get; set; }

        public string? EstadoVida { get; set; }

        public string? TipoDocumentoPropiedad { get; set; }

        public string? PropietarioAnterior { get; set; }

        public bool? AporteSocioPagado { get; set; }

        public string? Observaciones { get; set; }
    }
}
