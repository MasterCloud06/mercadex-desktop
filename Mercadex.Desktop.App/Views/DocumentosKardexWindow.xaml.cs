using Mercadex.Desktop.App.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mercadex.Desktop.App.Views;

public partial class DocumentosKardexWindow : Window
{
    // MERCADEX_KARDEX_WPF_V1

    private readonly HttpClient _api;

    private List<AsignacionItem> _asignaciones = new();
    private List<UnidadItem> _unidades = new();

    private string? _archivoSeleccionado;
    private bool _ocupado;
    private int _versionSeleccion;

    /// <summary>Socio, caseta y tipo de documento a mostrar al abrir (desde la ficha del Kardex).</summary>
    public Guid? SocioInicial { get; init; }

    public Guid? UnidadInicial { get; init; }

    public int? TipoInicial { get; init; }

    public DocumentosKardexWindow(HttpClient api)
    {
        InitializeComponent();

        _api = api;

        CmbTipos.ItemsSource = new List<TipoItem>
        {
            new(1, "Identidad"),
            new(2, "Defunción"),
            new(3, "Titularidad"),
            new(4, "Comprobante de aporte"),
            new(5, "Fotografía")
        };

        CmbTipos.SelectedIndex = 0;

        Loaded += async (_, _) => await IniciarAsync();
    }

    private bool EntornoLocal()
    {
        return EntornoApi.ConexionValida(_api);
    }

    private async Task IniciarAsync()
    {
        if (!EntornoLocal())
        {
            TxtEstado.Text =
                "La URL de la API configurada no es valida.";
            return;
        }

        try
        {
            TxtEstado.Text = "Cargando socios...";

            var socios = await _api
                .GetFromJsonAsync<List<SocioItem>>("/api/socios")
                ?? new();

            _unidades = await _api
                .GetFromJsonAsync<List<UnidadItem>>(
                    "/api/unidades-comerciales")
                ?? new();

            try
            {
                _asignaciones = await _api
                    .GetFromJsonAsync<List<AsignacionItem>>(
                        "/api/asignaciones-socio-caseta")
                    ?? new();
            }
            catch (HttpRequestException)
            {
                _asignaciones = new();
            }

            var sociosOrdenados = socios
                .OrderBy(x => x.Etiqueta)
                .ToList();

            CmbSocios.ItemsSource = sociosOrdenados;

            Guid? socioPrueba = SocioInicial ?? LeerSocioPrueba();

            var seleccionado = sociosOrdenados.FirstOrDefault(
                x => socioPrueba.HasValue &&
                     x.Id == socioPrueba.Value)
                ?? sociosOrdenados.FirstOrDefault();

            CmbSocios.SelectedItem = seleccionado;

            if (seleccionado is null)
            {
                TxtEstado.Text =
                    "No existen socios registrados.";
                return;
            }

            ActualizarCasetas();

            if (UnidadInicial is Guid unidadInicial &&
                CmbCasetas.ItemsSource is List<CasetaItem> casetasIniciales)
            {
                CmbCasetas.SelectedItem =
                    casetasIniciales.FirstOrDefault(c => c.Id == unidadInicial)
                    ?? CmbCasetas.SelectedItem;
            }

            if (TipoInicial is int tipoInicial &&
                CmbTipos.ItemsSource is List<TipoItem> tiposIniciales)
            {
                CmbTipos.SelectedItem =
                    tiposIniciales.FirstOrDefault(t => t.Id == tipoInicial)
                    ?? CmbTipos.SelectedItem;
            }

            await ConsultarDocumentosAsync();
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo cargar el kardex: " + ex.Message;
        }
    }

    private static Guid? LeerSocioPrueba()
    {
        try
        {
            var ruta = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MercadexDev",
                "KardexPruebas",
                "ultima-prueba.json");

            if (!File.Exists(ruta))
                return null;

            using var json = JsonDocument.Parse(
                File.ReadAllText(ruta));

            var valor = json.RootElement
                .GetProperty("socioId")
                .GetString();

            return Guid.TryParse(valor, out var id)
                ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private void Socio_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        ActualizarCasetas();

        TablaDocumentos.ItemsSource = null;
        VistaImagen.Source = null;
        BtnDescargar.IsEnabled = false;
        BtnAdjuntar.IsEnabled = false;
    }

    private void ActualizarCasetas()
    {
        if (CmbSocios.SelectedItem is not SocioItem socio)
        {
            CmbCasetas.ItemsSource = null;
            return;
        }

        var ids = _asignaciones
            .Where(x => x.SocioId == socio.Id && x.Activa)
            .Select(x => x.UnidadComercialId)
            .ToHashSet();

        var casetas = new List<CasetaItem>
        {
            new(null, "General / todos los documentos")
        };

        casetas.AddRange(
            _unidades
                .Where(x =>
                    ids.Contains(x.Id) &&
                    x.Tipo == 1 &&
                    x.Activa)
                .OrderBy(x => x.Codigo)
                .Select(x =>
                    new CasetaItem(x.Id, x.Codigo)));

        CmbCasetas.ItemsSource = casetas;
        CmbCasetas.SelectedIndex = 0;
    }

    private async void Consultar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ConsultarDocumentosAsync();
    }

    private async Task ConsultarDocumentosAsync(
        Guid? seleccionarId = null)
    {
        if (_ocupado)
            return;

        if (CmbSocios.SelectedItem is not SocioItem socio)
        {
            TxtEstado.Text = "Selecciona un socio.";
            return;
        }

        _ocupado = true;
        BtnAdjuntar.IsEnabled = false;

        try
        {
            TxtEstado.Text = "Consultando documentos...";

            var url =
                $"/api/kardex-documentos?socioId={socio.Id}";

            if (CmbCasetas.SelectedItem is CasetaItem caseta &&
                caseta.Id.HasValue)
            {
                url +=
                    $"&unidadComercialId={caseta.Id.Value}";
            }

            var respuesta = await _api
                .GetFromJsonAsync<ListadoDocumentos>(url);

            var documentos =
                respuesta?.Documentos ?? new();

            TablaDocumentos.ItemsSource = documentos;

            TablaDocumentos.SelectedItem =
                documentos.FirstOrDefault(x =>
                    seleccionarId.HasValue &&
                    x.Id == seleccionarId.Value)
                ?? documentos.FirstOrDefault();

            TxtEstado.Text =
                $"Documentos encontrados: {documentos.Count}.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "Error al consultar: " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnAdjuntar.IsEnabled =
                _archivoSeleccionado is not null;
        }
    }

    private void Elegir_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Seleccionar documento del kardex",
            Filter =
                "Archivos permitidos|*.pdf;*.jpg;*.jpeg;*.png|" +
                "PDF|*.pdf|Imágenes|*.jpg;*.jpeg;*.png",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        var info = new FileInfo(dialogo.FileName);

        if (info.Length <= 0 ||
            info.Length > 10 * 1024 * 1024)
        {
            TxtEstado.Text =
                "El archivo debe tener entre 1 byte y 10 MB.";
            return;
        }

        _archivoSeleccionado = dialogo.FileName;
        TxtArchivo.Text = info.Name;

        BtnAdjuntar.IsEnabled = true;

        TxtEstado.Text =
            "Archivo listo para adjuntar. " +
            "Selecciona el tipo de documento.";
    }

    private async void Adjuntar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado ||
            _archivoSeleccionado is null ||
            CmbSocios.SelectedItem is not SocioItem socio ||
            CmbTipos.SelectedItem is not TipoItem tipo)
        {
            return;
        }

        if (!EntornoLocal())
        {
            TxtEstado.Text = "La URL de la API configurada no es valida.";
            return;
        }

        var ruta = _archivoSeleccionado;
        var extension = Path.GetExtension(ruta)
            .ToLowerInvariant();

        var contenido = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => ""
        };

        if (string.IsNullOrEmpty(contenido))
        {
            TxtEstado.Text = "Formato no permitido.";
            return;
        }

        if (tipo.Id == 5 &&
            contenido is not ("image/jpeg" or "image/png"))
        {
            TxtEstado.Text =
                "Las fotografías deben ser JPG o PNG.";
            return;
        }

        var casetaId =
            (CmbCasetas.SelectedItem as CasetaItem)?.Id;

        var confirmar = Dialogo.Mostrar(
            this,
            $"Socio: {socio.Etiqueta}\n" +
            $"Tipo: {tipo.Nombre}\n" +
            $"Archivo: {Path.GetFileName(ruta)}\n\n" +
            "¿Confirmas que deseas adjuntar este documento " +
            "al kardex?",
            "Confirmar documento",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _ocupado = true;
        BtnAdjuntar.IsEnabled = false;

        try
        {
            TxtEstado.Text = "Adjuntando documento...";

            var bytes = await File.ReadAllBytesAsync(ruta);

            if (bytes.Length == 0 ||
                bytes.Length > 10 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "El tamaño del archivo no es válido.");
            }

            // Las fotos y escaneos se reducen antes de subir: ocupan mucho
            // menos espacio en el servidor y se ven igual de claros.
            var nombreArchivo = Path.GetFileName(ruta);

            if (contenido is "image/jpeg" or "image/png" &&
                ComprimirImagen(bytes) is { } comprimida &&
                comprimida.Length < bytes.Length)
            {
                bytes = comprimida;
                contenido = "image/jpeg";
                nombreArchivo = Path.ChangeExtension(nombreArchivo, ".jpg");
            }

            using var formulario =
                new MultipartFormDataContent();

            formulario.Add(
                new StringContent(socio.Id.ToString()),
                "SocioId");

            formulario.Add(
                new StringContent(tipo.Id.ToString()),
                "Tipo");

            if (casetaId.HasValue)
            {
                formulario.Add(
                    new StringContent(casetaId.Value.ToString()),
                    "UnidadComercialId");
            }

            var archivoContenido =
                new ByteArrayContent(bytes);

            archivoContenido.Headers.ContentType =
                new MediaTypeHeaderValue(contenido);

            formulario.Add(
                archivoContenido,
                "Archivo",
                nombreArchivo);

            using var respuesta = await _api.PostAsync(
                "/api/kardex-documentos",
                formulario);

            if (!respuesta.IsSuccessStatusCode)
            {
                var error = await respuesta.Content
                    .ReadAsStringAsync();

                throw new InvalidOperationException(
                    $"HTTP {(int)respuesta.StatusCode}: " + error);
            }

            var guardado = await respuesta.Content
                .ReadFromJsonAsync<DocumentoGuardado>();

            _archivoSeleccionado = null;
            TxtArchivo.Clear();

            _ocupado = false;

            await ConsultarDocumentosAsync(guardado?.Id);

            Dialogo.Mostrar(
                this,
                "El documento se adjuntó correctamente " +
                "al kardex.",
                "Documento registrado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo confirmar la subida. " +
                "Consulta el listado antes de repetir. " +
                ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnAdjuntar.IsEnabled =
                _archivoSeleccionado is not null;
        }
    }

    private async void Documento_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        _versionSeleccion++;
        var version = _versionSeleccion;

        VistaImagen.Source = null;

        if (TablaDocumentos.SelectedItem is not DocumentoItem doc)
        {
            BtnDescargar.IsEnabled = false;
            TxtDetalle.Text =
                "Selecciona un documento para visualizarlo.";
            return;
        }

        BtnDescargar.IsEnabled = true;

        TxtDetalle.Text =
            $"Tipo: {doc.Tipo}\n" +
            $"Archivo: {doc.NombreArchivo}\n" +
            $"SHA-256: {doc.Sha256}";

        if (doc.TipoContenido is not (
            "image/png" or "image/jpeg"))
        {
            TxtDetalle.Text +=
                "\nPDF: utiliza Descargar para abrirlo.";
            return;
        }

        try
        {
            var bytes = await DescargarBytesAsync(doc);

            if (version != _versionSeleccion)
                return;

            using var flujo = new MemoryStream(bytes);

            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.StreamSource = flujo;
            imagen.EndInit();
            imagen.Freeze();

            VistaImagen.Source = imagen;
        }
        catch (Exception ex)
        {
            if (version == _versionSeleccion)
            {
                TxtDetalle.Text +=
                    "\nNo se pudo mostrar la imagen: " +
                    ex.Message;
            }
        }
    }

    private async Task<byte[]> DescargarBytesAsync(
        DocumentoItem documento)
    {
        using var respuesta = await _api.GetAsync(
            $"/api/kardex-documentos/{documento.Id}/descargar");

        respuesta.EnsureSuccessStatusCode();

        var bytes = await respuesta.Content
            .ReadAsByteArrayAsync();

        if (bytes.Length == 0 ||
            bytes.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Tamaño descargado no permitido.");
        }

        var sha = Convert.ToHexString(
            SHA256.HashData(bytes));

        if (!string.Equals(
            sha,
            documento.Sha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La integridad SHA-256 no coincide.");
        }

        return bytes;
    }

    private async void Descargar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TablaDocumentos.SelectedItem is not DocumentoItem doc)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "Guardar documento del kardex",
            FileName = Path.GetFileName(doc.NombreArchivo),
            OverwritePrompt = true
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            BtnDescargar.IsEnabled = false;
            TxtEstado.Text = "Descargando documento...";

            var bytes = await DescargarBytesAsync(doc);

            await File.WriteAllBytesAsync(
                dialogo.FileName,
                bytes);

            TxtEstado.Text =
                "Documento descargado. Integridad SHA-256 correcta.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo descargar: " + ex.Message;
        }
        finally
        {
            BtnDescargar.IsEnabled = true;
        }
    }

    /// <summary>
    /// JPEG de hasta 1800 px por lado, respetando la orientación de la foto
    /// del celular. Devuelve null si la imagen no se puede leer.
    /// </summary>
    private static byte[]? ComprimirImagen(byte[] original)
    {
        try
        {
            using var entrada = new MemoryStream(original);

            var decodificador = System.Windows.Media.Imaging.BitmapDecoder.Create(
                entrada,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

            System.Windows.Media.Imaging.BitmapSource marco = decodificador.Frames[0];

            var orientacion = 1;
            try
            {
                if (decodificador.Frames[0].Metadata is System.Windows.Media.Imaging.BitmapMetadata datos &&
                    datos.ContainsQuery("/app1/ifd/{ushort=274}") &&
                    datos.GetQuery("/app1/ifd/{ushort=274}") is ushort valor)
                {
                    orientacion = valor;
                }
            }
            catch
            {
                // Sin metadatos de orientación.
            }

            const double ladoMaximo = 1800;
            var escala = Math.Min(1.0, ladoMaximo / Math.Max(marco.PixelWidth, marco.PixelHeight));

            if (escala < 1.0)
            {
                marco = new System.Windows.Media.Imaging.TransformedBitmap(
                    marco, new System.Windows.Media.ScaleTransform(escala, escala));
            }

            var giro = orientacion switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };

            if (giro != 0)
            {
                marco = new System.Windows.Media.Imaging.TransformedBitmap(
                    marco, new System.Windows.Media.RotateTransform(giro));
            }

            if (marco.Format != System.Windows.Media.PixelFormats.Bgr24)
            {
                marco = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                    marco, System.Windows.Media.PixelFormats.Bgr24, null, 0);
            }

            var codificador = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 82 };
            codificador.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(marco));

            using var salida = new MemoryStream();
            codificador.Save(salida);
            return salida.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public sealed record TipoItem(int Id, string Nombre);

    public sealed record CasetaItem(Guid? Id, string Nombre);

    public sealed class SocioItem
    {
        public Guid Id { get; set; }
        public string NombreCompleto { get; set; } = "";
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string DocumentoIdentidad { get; set; } = "";

        public string Etiqueta
        {
            get
            {
                var nombre = string.IsNullOrWhiteSpace(
                    NombreCompleto)
                    ? $"{Nombres} {Apellidos}".Trim()
                    : NombreCompleto;

                return $"{nombre} - {DocumentoIdentidad}";
            }
        }
    }

    public sealed class UnidadItem
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public int Tipo { get; set; }
        public bool Activa { get; set; }
    }

    public sealed class AsignacionItem
    {
        public Guid SocioId { get; set; }
        public Guid UnidadComercialId { get; set; }
        public bool Activa { get; set; }
    }

    public sealed class ListadoDocumentos
    {
        public int Cantidad { get; set; }
        public List<DocumentoItem> Documentos { get; set; } = new();
    }

    public sealed class DocumentoItem
    {
        public Guid Id { get; set; }
        public string Tipo { get; set; } = "";
        public string NombreArchivo { get; set; } = "";
        public string TipoContenido { get; set; } = "";
        public long TamanoBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public string RegistradoPor { get; set; } = "";
        public DateTimeOffset FechaRegistroUtc { get; set; }

        public double TamanoKb =>
            TamanoBytes / 1024.0;

        public string FechaLocal =>
            FechaRegistroUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm");
    }

    public sealed class DocumentoGuardado
    {
        public Guid Id { get; set; }
    }
}