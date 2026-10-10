
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
    private bool _inicializado;
    private string? _tokenKardex;
    private Guid? _unidadAutorizadaId;

    public KardexView()
    {
        InitializeComponent();

        EstadoVida.SelectedIndex = 0;
        Documento.SelectedIndex = 0;
        Aporte.SelectedIndex = 0;

        Loaded += KardexView_Loaded;

        Unloaded += (_, _) =>
        {
            _tokenKardex = null;
            _unidadAutorizadaId = null;
            _inicializado = false;

            _api.DefaultRequestHeaders.Authorization = null;

            Formulario.IsEnabled = false;
            BtnEditar.IsEnabled = false;

            LimpiarFicha();
            Casetas.SelectedIndex = -1;
        };
    }

    // CARGAR CASETAS SIN INICIAR SESION

    private async void KardexView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_inicializado)
            return;

        _inicializado = true;

        try
        {
            Casetas.IsEnabled = true;

            await CargarCasetasYSocios();
        }
        catch (Exception ex)
        {
            _inicializado = false;
            MostrarError(ex);
        }
    }

    private void LimpiarFicha()
    {
        Socios.SelectedIndex = 0;
        FechaIngreso.SelectedDate = null;

        Seleccionar(EstadoVida, null);
        Seleccionar(Documento, null);
        Seleccionar(Aporte, null);

        PropietarioAnterior.Clear();
        Observaciones.Clear();
    }

    // PEDIR AUTORIZACION SOLO AL ABRIR LA FICHA

    private async void Editar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Casetas.SelectedItem is not UnidadKardex seleccionada)
            return;

        var unidadId = seleccionada.Id;

        var ventana = new Window
        {
            Title = "Mercadex - Autorizar Kardex",
            Width = 420,
            Height = 285,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Autorizacion para consultar la ficha " +
                   "completa y modificar el Kardex.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Usuario administrador"
        });

        var usuario = new TextBox
        {
            Height = 32,
            Margin = new Thickness(0, 5, 0, 10)
        };

        panel.Children.Add(usuario);

        panel.Children.Add(new TextBlock
        {
            Text = "Contrasena"
        });

        var clave = new PasswordBox
        {
            Height = 32,
            Margin = new Thickness(0, 5, 0, 14)
        };

        panel.Children.Add(clave);

        var acciones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelar = new Button
        {
            Content = "Cancelar",
            Width = 100,
            Height = 35,
            IsCancel = true,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var autorizar = new Button
        {
            Content = "Autorizar",
            Width = 110,
            Height = 35,
            IsDefault = true
        };

        autorizar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(usuario.Text) ||
                string.IsNullOrWhiteSpace(clave.Password))
            {
                MessageBox.Show(
                    ventana,
                    "Introduce el usuario y la contrasena.",
                    "Autorizacion requerida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            ventana.DialogResult = true;
        };

        acciones.Children.Add(cancelar);
        acciones.Children.Add(autorizar);

        panel.Children.Add(acciones);
        ventana.Content = panel;

        if (ventana.ShowDialog() != true)
        {
            clave.Clear();
            return;
        }

        var nombre = usuario.Text.Trim();
        var password = clave.Password;

        clave.Clear();

        BtnEditar.IsEnabled = false;

        try
        {
            if (!ApiConfiguration.BaseUri.IsLoopback &&
                ApiConfiguration.BaseUri.Scheme !=
                    Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "La autorizacion remota requiere HTTPS.");
            }

            using var respuesta = await _api.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    usuario = nombre,
                    password
                });

            password = "";

            if (!respuesta.IsSuccessStatusCode)
            {
                Estado.Text =
                    $"Autorizacion rechazada. HTTP {(int)respuesta.StatusCode}.";

                return;
            }

            var sesion = await respuesta.Content
                .ReadFromJsonAsync<SesionKardex>();

            if (string.IsNullOrWhiteSpace(sesion?.Token))
            {
                throw new InvalidOperationException(
                    "No se recibio el token del administrador.");
            }

            if (Casetas.SelectedItem is not UnidadKardex actual ||
                actual.Id != unidadId)
            {
                throw new InvalidOperationException(
                    "Cambio la caseta seleccionada. Vuelve a autorizar.");
            }

            _tokenKardex = sesion.Token;

            _api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    sesion.Token);

            // Reutilizar el metodo original que carga la ficha.
            Casetas_SelectionChanged(this, null!);
        }
        catch (Exception ex)
        {
            _tokenKardex = null;
            _unidadAutorizadaId = null;

            _api.DefaultRequestHeaders.Authorization = null;

            Formulario.IsEnabled = false;
            MostrarError(ex);
        }
        finally
        {
            password = "";
            BtnEditar.IsEnabled =
                Casetas.SelectedItem is UnidadKardex;
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

        _unidadAutorizadaId = null;

        Formulario.IsEnabled = false;
        BtnEditar.IsEnabled = true;

        LimpiarFicha();

        Ubicacion.Text =
            $"Caseta {unidad.Codigo} - " +
            $"Bloque: {unidad.Bloque ?? "Sin bloque"}";

        if (string.IsNullOrWhiteSpace(_tokenKardex))
        {
            Estado.Text =
                "Caseta seleccionada. Para consultar la ficha " +
                "completa o editar, pulsa Ver y editar ficha.";

            return;
        }

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

            if (Casetas.SelectedItem is not UnidadKardex actual ||
                actual.Id != unidad.Id)
            {
                return;
            }

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

            _unidadAutorizadaId = unidad.Id;
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

        if (string.IsNullOrWhiteSpace(_tokenKardex) ||
            _unidadAutorizadaId != unidad.Id)
        {
            Estado.Text =
                "Debes autorizar esta ficha antes de guardarla.";

            return;
        }

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
                if (respuesta.StatusCode == HttpStatusCode.Unauthorized ||
                    respuesta.StatusCode == HttpStatusCode.Forbidden)
                {
                    _tokenKardex = null;
                    _unidadAutorizadaId = null;
                    _api.DefaultRequestHeaders.Authorization = null;
                    Formulario.IsEnabled = false;
                }

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
