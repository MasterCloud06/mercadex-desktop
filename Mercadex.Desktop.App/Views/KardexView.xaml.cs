
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
private readonly HttpClient _api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(45));

    private bool _cargando;
    private bool _inicializado;
    private Guid? _unidadAutorizadaId;

    public KardexView()
    {
        InitializeComponent();

        EstadoVida.SelectedIndex = 0;
        Documento.SelectedIndex = 0;
        Aporte.SelectedIndex = 0;

        // Editar el Kardex y adjuntar documentos: solo administradores.
        if (!Sesion.SesionActual.EsAdministrador)
        {
            BtnGuardar.Visibility = Visibility.Collapsed;
            BtnDocumentos.Visibility = Visibility.Collapsed;
        }

        Loaded += KardexView_Loaded;

        Unloaded += (_, _) =>
        {
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
        PanelExpediente.Visibility = Visibility.Collapsed;
        FotoSocio.Source = null;
        _expediente = null;

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

        // Sin autorizacion de administrador: carga la ficha directamente.
        Casetas_SelectionChanged(this, null!);

        await Task.CompletedTask;
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
            Formulario.IsEnabled = Sesion.SesionActual.EsAdministrador;

            await MostrarExpedienteAsync(unidad, ficha);

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

        if (_unidadAutorizadaId != unidad.Id)
        {
            Estado.Text =
                "Espera a que se cargue la ficha antes de guardarla.";

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

            // Lo que falta depende de lo guardado (óbito, propiedad, aporte).
            await RecargarExpedienteAsync(unidad);

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

    // ---------------- Expediente: socio y documentos escaneados ----------------

    private RespuestaKardex? _expediente;

    private static readonly System.Globalization.CultureInfo Bo =
        System.Globalization.CultureInfo.GetCultureInfo("es-BO");

    private static string NombreTipo(int tipo) => tipo switch
    {
        1 => "CI",
        2 => "Certificado de óbito",
        3 => "Documento de propiedad",
        4 => "Comprobante de aporte",
        5 => "Foto 3x2",
        _ => "Documento"
    };

    private async Task RecargarExpedienteAsync(UnidadKardex unidad)
    {
        try
        {
            var ficha = await _api.GetFromJsonAsync<RespuestaKardex>($"/api/kardex/caseta/{unidad.Id}");

            if (Casetas.SelectedItem is UnidadKardex actual && actual.Id == unidad.Id)
                await MostrarExpedienteAsync(unidad, ficha);
        }
        catch (Exception ex)
        {
            MostrarError(ex);
        }
    }

    private async Task MostrarExpedienteAsync(UnidadKardex unidad, RespuestaKardex? ficha)
    {
        _expediente = ficha;

        if (ficha is null)
        {
            PanelExpediente.Visibility = Visibility.Collapsed;
            return;
        }

        // ---------- Datos del titular ----------

        var t = ficha.Titular;
        var pasillo = ficha.Pasillo is null ? "Sin pasillo" : $"{ficha.Pasillo.Codigo} · {ficha.Pasillo.Nombre}";

        DatosTitular.Text = t is null
            ? $"Caseta {unidad.Codigo}\nPasillo: {pasillo}\nBloque: {unidad.Bloque ?? "Sin bloque"}\n\nLa caseta no tiene titular asignado."
            : $"{t.Nombre}\n" +
              $"CI: {(string.IsNullOrWhiteSpace(t.Documento) || t.Documento == "SIN_DOCUMENTO" ? "sin registrar" : t.Documento)}\n" +
              $"Celular: {(string.IsNullOrWhiteSpace(t.Telefono) ? "sin registrar" : t.Telefono)}\n" +
              $"Fecha de registro: {t.FechaRegistro.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}\n" +
              $"Pasillo: {pasillo} · Bloque: {unidad.Bloque ?? "Sin bloque"} · Caseta {unidad.Codigo}";

        // ---------- Documentos ----------

        ListaDocumentos.Children.Clear();

        if (ficha.Documentos.Count == 0)
        {
            ListaDocumentos.Children.Add(new TextBlock
            {
                Text = "Todavía no hay documentos escaneados.",
                Foreground = ColoresPanel.TextoTenue
            });
        }

        foreach (var d in ficha.Documentos)
        {
            var fila = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            var ver = new Button
            {
                Content = "Ver",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Style = (Style)FindResource("Mx.SecondaryButton")
            };
            ver.Click += async (_, _) => await AbrirDocumentoAsync(d);
            DockPanel.SetDock(ver, Dock.Right);
            fila.Children.Add(ver);

            fila.Children.Add(new TextBlock
            {
                Text = $"{NombreTipo(d.Tipo)} · {d.NombreArchivo} · " +
                       $"{d.FechaRegistroUtc.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)} · {d.RegistradoPor}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            ListaDocumentos.Children.Add(fila);
        }

        // ---------- Lo que falta ----------

        TxtFaltantes.Text = string.Join("\n", ficha.Faltantes.Select(f => "• " + f.Texto));
        AvisoFaltantes.Visibility = ficha.Faltantes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AvisoCompleto.Visibility = ficha.Faltantes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        PanelExpediente.Visibility = Visibility.Visible;

        // ---------- Foto 3x2 ----------

        FotoSocio.Source = null;
        SinFoto.Visibility = Visibility.Visible;

        var foto = ficha.Documentos.FirstOrDefault(d => d.Tipo == 5);

        if (foto is not null)
        {
            try
            {
                var bytes = await _api.GetByteArrayAsync($"/api/kardex-documentos/{foto.Id}/descargar");
                var imagen = new System.Windows.Media.Imaging.BitmapImage();
                using (var flujo = new System.IO.MemoryStream(bytes))
                {
                    imagen.BeginInit();
                    imagen.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    imagen.DecodePixelWidth = 240;
                    imagen.StreamSource = flujo;
                    imagen.EndInit();
                }
                imagen.Freeze();

                if (_expediente == ficha)
                {
                    FotoSocio.Source = imagen;
                    SinFoto.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // Sin foto visible; el documento sigue en la lista.
            }
        }
    }

    private async Task AbrirDocumentoAsync(DocumentoExpediente d)
    {
        try
        {
            var bytes = await _api.GetByteArrayAsync($"/api/kardex-documentos/{d.Id}/descargar");

            var extension = System.IO.Path.GetExtension(d.NombreArchivo);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 6)
                extension = d.TipoContenido == "application/pdf" ? ".pdf" : ".jpg";

            var ruta = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mercadex-{NombreTipo(d.Tipo).Replace(' ', '-')}-{d.Id.ToString("N")[..8]}{extension}");

            await System.IO.File.WriteAllBytesAsync(ruta, bytes);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Estado.Text = "No se pudo abrir el documento: " + ex.Message;
        }
    }

    private async void Documentos_Click(object sender, RoutedEventArgs e)
    {
        if (Casetas.SelectedItem is not UnidadKardex unidad || _expediente is null)
            return;

        if (_expediente.SocioDocumentosId is not Guid socioId)
        {
            MessageBox.Show(
                "Primero asigna un titular a la caseta o elige el socio del Kardex y guarda la ficha.",
                "Documentos del Kardex", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ventana = new DocumentosKardexWindow(_api)
        {
            Owner = Window.GetWindow(this),
            SocioInicial = socioId,
            UnidadInicial = unidad.Id,
            TipoInicial = _expediente.Faltantes.FirstOrDefault(f => f.Tipo.HasValue)?.Tipo
        };

        ventana.ShowDialog();

        await RecargarExpedienteAsync(unidad);
    }

    private sealed class RespuestaKardex
    {
        public FichaKardex? Kardex { get; set; }

        public PasilloKardex? Pasillo { get; set; }

        public TitularKardex? Titular { get; set; }

        public Guid? SocioDocumentosId { get; set; }

        public List<DocumentoExpediente> Documentos { get; set; } = new();

        public List<FaltanteKardex> Faltantes { get; set; } = new();
    }

    private sealed class PasilloKardex
    {
        public string Codigo { get; set; } = "";

        public string Nombre { get; set; } = "";
    }

    private sealed class TitularKardex
    {
        public Guid Id { get; set; }

        public string Nombre { get; set; } = "";

        public string? Documento { get; set; }

        public string? Telefono { get; set; }

        public DateTimeOffset FechaRegistro { get; set; }
    }

    private sealed class DocumentoExpediente
    {
        public Guid Id { get; set; }

        public int Tipo { get; set; }

        public string NombreArchivo { get; set; } = "";

        public string TipoContenido { get; set; } = "";

        public string RegistradoPor { get; set; } = "";

        public DateTimeOffset FechaRegistroUtc { get; set; }
    }

    private sealed class FaltanteKardex
    {
        public int? Tipo { get; set; }

        public string Texto { get; set; } = "";
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
