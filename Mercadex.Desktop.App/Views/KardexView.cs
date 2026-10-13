using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Kardex (manual: "Ingreso para llenado de Kardex de cada caseta").
/// A la izquierda, todas las casetas con el estado de su expediente; a la
/// derecha, la ficha en el orden de la tabla del manual: datos del socio,
/// fecha de ingreso, vivo o fallecido, documento de propiedad, propietario
/// anterior si es Minuta, foto 3x2, aporte, y qué documentos están escaneados.
/// </summary>
public sealed class KardexView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(45));

    private readonly bool _admin = SesionActual.EsAdministrador || !SesionActual.Iniciada;

    private readonly TextBox _buscar = UiKit.Texto(200, 80);
    private readonly ComboBox _filtro = UiKit.Combo(170);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _ficha = new();

    private List<CasetaFila> _todas = new();
    private Guid? _seleccionarAlCargar;
    private FichaDto? _actual;

    public KardexView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Kardex"));
        arriba.Children.Add(UiKit.Ayuda(
            "El expediente de cada caseta, como pide el manual: datos del socio, fecha de ingreso, propiedad, aporte y documentos escaneados. Elige una caseta y completa lo que falta.", 10));

        _filtro.ItemsSource = new List<UiKit.Item<string>>
        {
            new("todas", "Todas"),
            new("incompletas", "Les falta algo"),
            new("completas", "Completas"),
            new("sin-kardex", "Sin Kardex todavía"),
            new("sin-titular", "Sin titular")
        };
        _filtro.SelectedIndex = 0;
        _filtro.SelectionChanged += (_, _) => Filtrar();
        _buscar.TextChanged += (_, _) => Filtrar();

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("N.º de caseta o titular", _buscar));
        filtros.Children.Add(UiKit.Campo("Mostrar", _filtro));
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });

        _tabla.Columns.Add(UiKit.Columna("Caseta", nameof(CasetaFila.Codigo), 0.6));
        _tabla.Columns.Add(UiKit.Columna("Bloque", nameof(CasetaFila.BloqueTexto), 0.6));
        _tabla.Columns.Add(UiKit.Columna("Titular", nameof(CasetaFila.TitularTexto), 2));
        _tabla.Columns.Add(UiKit.Columna("Expediente", nameof(CasetaFila.EstadoTexto), 1));

        var estilo = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        var completo = new DataTrigger { Binding = new Binding(nameof(CasetaFila.Completo)), Value = true };
        completo.Setters.Add(new Setter(ForegroundProperty, ColoresPanel.Hex("#0B5A0B")));
        estilo.Triggers.Add(completo);
        _tabla.RowStyle = estilo;

        _tabla.SelectionChanged += async (_, _) => await MostrarFichaAsync();
        Grid.SetColumn(_tabla, 0);
        cuerpo.Children.Add(_tabla);

        var panel = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _ficha }
        };
        Grid.SetColumn(panel, 2);
        cuerpo.Children.Add(panel);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        MostrarVacio();
        Loaded += async (_, _) => await CargarListaAsync();
    }

    /// <summary>Abre el Kardex con esa caseta elegida (desde Casetas o Socios).</summary>
    public void SeleccionarAlCargar(Guid unidadId) => _seleccionarAlCargar = unidadId;

    // =====================================================================
    // LISTA
    // =====================================================================

    private async Task CargarListaAsync()
    {
        var seleccion = _seleccionarAlCargar ?? (_tabla.SelectedItem as CasetaFila)?.UnidadId;

        try
        {
            _resumen.Text = "Cargando casetas…";
            var r = await ApiJson.GetAsync<ResumenRespuesta>("/api/kardex/resumen");
            _todas = r?.Casetas ?? new();

            _resumen.Text = r is null || r.Total == 0
                ? "Todavía no hay casetas registradas. Regístralas en Casetas."
                : $"{r.Total} casetas   ·   expediente completo: {r.Completos}   ·   sin Kardex: {r.SinKardex}   ·   sin titular: {r.SinTitular}";

            // Si la caseta pedida queda fuera del filtro, se muestran todas.
            if (_seleccionarAlCargar is { } pedida && !Visibles().Any(f => f.UnidadId == pedida))
            {
                _buscar.Text = "";
                _filtro.SelectedIndex = 0;
            }

            Filtrar(seleccion);
            _seleccionarAlCargar = null;
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo cargar el Kardex: " + ex.Message;
        }
    }

    private IEnumerable<CasetaFila> Visibles()
    {
        var texto = _buscar.Text.Trim();
        var filtro = (_filtro.SelectedItem as UiKit.Item<string>)?.Valor ?? "todas";

        return _todas.Where(f =>
            (texto.Length == 0 ||
             string.Equals(f.Codigo, texto, StringComparison.OrdinalIgnoreCase) ||
             f.Codigo.StartsWith(texto, StringComparison.OrdinalIgnoreCase) ||
             (f.Titular ?? "").Contains(texto, StringComparison.OrdinalIgnoreCase)) &&
            filtro switch
            {
                "incompletas" => f.Faltan > 0,
                "completas" => f.Faltan == 0,
                "sin-kardex" => !f.TieneKardex,
                "sin-titular" => f.Titular is null,
                _ => true
            });
    }

    private void Filtrar(Guid? seleccion = null)
    {
        seleccion ??= (_tabla.SelectedItem as CasetaFila)?.UnidadId;
        var filas = Visibles().ToList();
        _tabla.ItemsSource = filas;

        if (seleccion is { } id && filas.FirstOrDefault(f => f.UnidadId == id) is { } fila)
        {
            _tabla.SelectedItem = fila;
            _tabla.ScrollIntoView(fila);
        }
        else if (filas.Count == 1)
        {
            _tabla.SelectedItem = filas[0];
        }
        else if (_tabla.SelectedItem is null)
        {
            MostrarVacio();
        }
    }

    // =====================================================================
    // FICHA
    // =====================================================================

    private void MostrarVacio()
    {
        _actual = null;
        _ficha.Children.Clear();
        _ficha.Children.Add(new TextBlock
        {
            Text = "Elige una caseta de la lista para ver y completar su Kardex. Escribe el número arriba para encontrarla rápido.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private async Task MostrarFichaAsync()
    {
        if (_tabla.SelectedItem is not CasetaFila fila)
        {
            if (_tabla.ItemsSource is not null)
                MostrarVacio();
            return;
        }

        try
        {
            var f = await ApiJson.GetAsync<FichaDto>($"/api/kardex/caseta/{fila.UnidadId}");
            if (f is null || (_tabla.SelectedItem as CasetaFila)?.UnidadId != fila.UnidadId)
                return;

            _actual = f;
            ConstruirFicha(fila, f);
        }
        catch (Exception ex)
        {
            _ficha.Children.Clear();
            _ficha.Children.Add(new TextBlock { Text = "No se pudo abrir el Kardex: " + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#A12A2A") });
        }
    }

    private void ConstruirFicha(CasetaFila fila, FichaDto f)
    {
        _ficha.Children.Clear();
        var k = f.Kardex;

        // ---------- Encabezado ----------

        var faltan = f.Faltantes.Count;
        var cabeza = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ColoresPanel.Hex(faltan == 0 ? "#E7F5E7" : "#FFF4D6"),
            Child = new TextBlock
            {
                Text = faltan == 0 ? "Expediente completo" : $"Faltan {faltan}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColoresPanel.Hex(faltan == 0 ? "#0B5A0B" : "#7A5200")
            }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock { Text = $"Caseta {fila.Codigo}", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo });
        _ficha.Children.Add(cabeza);

        var ubicacion = new List<string>();
        if (f.Pasillo is { } p) ubicacion.Add($"Pasillo {p.Codigo} · {p.Nombre}");
        else ubicacion.Add("Sin pasillo");
        ubicacion.Add(string.IsNullOrWhiteSpace(fila.Bloque) ? "Sin bloque" : "Bloque " + fila.Bloque);
        _ficha.Children.Add(UiKit.Ayuda(string.Join("  ·  ", ubicacion), 10));

        if (faltan > 0)
        {
            var aviso = UiKit.Aviso(out var texto, "aviso");
            texto.Text = "Falta completar:\n" + string.Join("\n", f.Faltantes.Select(x => "• " + x.Texto));
            _ficha.Children.Add(aviso);
        }

        // ---------- 1-3. Socio: nombre, CI, celular, foto ----------

        _ficha.Children.Add(Seccion("Socio"));

        if (f.Titular is { } t)
        {
            var datos = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var marco = new Border
            {
                Width = 84,
                Height = 112,
                CornerRadius = new CornerRadius(6),
                Background = ColoresPanel.Hex("#EEF2F7"),
                BorderBrush = ColoresPanel.Hex("#DFE6EE"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0),
                ClipToBounds = true,
                Child = new TextBlock { Text = "Sin foto 3x2", FontSize = 11.5, Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) }
            };
            DockPanel.SetDock(marco, Dock.Left);
            datos.Children.Add(marco);

            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = t.Nombre, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, TextWrapping = TextWrapping.Wrap });
            texto.Children.Add(new TextBlock
            {
                Text = $"CI: {Texto(t.Documento)}\nCelular: {Texto(t.Telefono)}\nRegistrado el {t.FechaRegistro.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}",
                Foreground = UiKit.Suave,
                LineHeight = 21,
                Margin = new Thickness(0, 2, 0, 4)
            });
            var corregir = new Button { Content = "Corregir nombre, CI o celular en Socios", Style = UiKit.Estilo("Mx.NavLink"), HorizontalAlignment = HorizontalAlignment.Left };
            corregir.Click += (_, _) => Navegacion.AbrirSocio(t.Id);
            texto.Children.Add(corregir);
            datos.Children.Add(texto);
            _ficha.Children.Add(datos);

            _ = CargarFotoAsync(f, marco);

            if (k?.SocioId is { } socioKardex && socioKardex != t.Id)
                _ficha.Children.Add(UiKit.Ayuda("Este Kardex estaba registrado a nombre de otro socio. Al guardar quedará a nombre del titular actual.", 6));
        }
        else
        {
            var aviso = UiKit.Aviso(out var texto, "info");
            texto.Text = "La caseta no tiene titular. Primero asígnale su titular; después completa el Kardex.";
            _ficha.Children.Add(aviso);
            var ir = UiKit.Boton("Asignar titular en Casetas", principal: true);
            ir.HorizontalAlignment = HorizontalAlignment.Left;
            ir.Margin = new Thickness(0, 0, 0, 6);
            ir.Click += (_, _) => Navegacion.AbrirCaseta(fila.UnidadId);
            _ficha.Children.Add(ir);
        }

        // ---------- 4-11. Datos del Kardex ----------

        _ficha.Children.Add(Seccion("Datos del Kardex"));

        var fechaIngreso = UiKit.Fecha(k?.FechaIngreso?.ToDateTime(TimeOnly.MinValue));
        fechaIngreso.Width = 200;

        var vida = Combo(new() { new(null, "Sin especificar"), new("VIVO", "Vivo"), new("FALLECIDO", "Fallecido") }, k?.EstadoVida);
        var propiedad = Combo(new() { new(null, "Sin especificar"), new("DERECHOS_REALES", "Derechos Reales"), new("MINUTA", "Minuta"), new("OTRO", "Otro") }, k?.TipoDocumentoPropiedad);
        var aporte = Combo(new() { new(null, "Sin verificar"), new("SI", "Sí, pagó el aporte"), new("NO", "No pagó todavía") },
            k?.AporteSocioPagado switch { true => "SI", false => "NO", _ => null });

        var anterior = UiKit.Texto(380, 300);
        anterior.Text = k?.PropietarioAnterior ?? "";
        var campoAnterior = UiKit.Campo("Propietario anterior (si es Minuta, o lista antes del incendio)", anterior, 0);

        void ActualizarAnterior()
        {
            var esMinuta = (propiedad.SelectedItem as UiKit.Item<string?>)?.Valor == "MINUTA";
            campoAnterior.Visibility = esMinuta || anterior.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        propiedad.SelectionChanged += (_, _) => ActualizarAnterior();
        ActualizarAnterior();

        var observaciones = new TextBox
        {
            Style = UiKit.Estilo("Mx.TextBox"),
            Height = 70,
            Width = 380,
            MaxLength = 1000,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = k?.Observaciones ?? ""
        };

        var fila1 = new WrapPanel();
        fila1.Children.Add(UiKit.Campo("Fecha de ingreso a la asociación", fechaIngreso));
        fila1.Children.Add(UiKit.Campo("Vivo o fallecido", vida, 0));
        _ficha.Children.Add(fila1);
        var fila2 = new WrapPanel();
        fila2.Children.Add(UiKit.Campo("Documento que acredita la propiedad", propiedad));
        fila2.Children.Add(UiKit.Campo("Aporte para ser socio", aporte, 0));
        _ficha.Children.Add(fila2);
        _ficha.Children.Add(campoAnterior);
        _ficha.Children.Add(UiKit.Campo("Observaciones", observaciones, 0));

        if (k is not null)
            _ficha.Children.Add(UiKit.Ayuda($"Kardex registrado el {k.FechaRegistro.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}.", 8));

        if (_admin)
        {
            var guardar = UiKit.Boton(k is null ? "Crear Kardex" : "Guardar cambios", principal: true);
            guardar.HorizontalAlignment = HorizontalAlignment.Left;
            guardar.Margin = new Thickness(0, 0, 0, 6);
            guardar.Click += async (_, _) => await GuardarAsync(fila, f, guardar,
                fechaIngreso.SelectedDate, Valor(vida), Valor(propiedad), anterior.Text, Valor(aporte), observaciones.Text);
            _ficha.Children.Add(guardar);
        }
        else
        {
            foreach (var c in new Control[] { fechaIngreso, vida, propiedad, aporte, anterior, observaciones })
                c.IsEnabled = false;
            _ficha.Children.Add(UiKit.Ayuda("Solo el administrador puede modificar el Kardex.", 6));
        }

        // ---------- Documentos escaneados ----------

        _ficha.Children.Add(Seccion("Documentos escaneados"));

        var requeridos = new List<(int Tipo, string Nombre, bool Necesario)>
        {
            (1, "CI", true),
            (5, "Foto 3x2", true),
            (3, "Documento de propiedad (Derechos Reales o Minuta)", !string.IsNullOrWhiteSpace(k?.TipoDocumentoPropiedad)),
            (2, "Certificado de óbito", string.Equals(k?.EstadoVida, "FALLECIDO", StringComparison.OrdinalIgnoreCase)),
            (4, "Comprobante del aporte de socio", k?.AporteSocioPagado == true)
        };

        var mostrados = new HashSet<Guid>();
        foreach (var (tipo, nombre, necesario) in requeridos)
        {
            var doc = f.Documentos.FirstOrDefault(d => d.Tipo == tipo);
            if (doc is null && !necesario)
                continue;
            if (doc is not null)
                mostrados.Add(doc.Id);
            _ficha.Children.Add(FilaDocumento(nombre, doc, tipo, f));
        }

        // Otros escaneos (por ejemplo, una segunda hoja del documento).
        foreach (var d in f.Documentos.Where(d => !mostrados.Contains(d.Id)))
            _ficha.Children.Add(FilaDocumento(NombreTipo(d.Tipo), d, d.Tipo, f));

        if (_admin && f.SocioDocumentosId is not null)
        {
            var escanear = UiKit.Boton("Escanear o adjuntar otro documento");
            escanear.HorizontalAlignment = HorizontalAlignment.Left;
            escanear.Margin = new Thickness(0, 8, 0, 0);
            escanear.Click += async (_, _) => await EscanearAsync(fila, f, null);
            _ficha.Children.Add(escanear);
        }
    }

    private Border FilaDocumento(string nombre, DocumentoDto? doc, int tipo, FichaDto f)
    {
        var fila = new DockPanel();

        if (doc is not null)
        {
            var ver = Pequeno("Ver", () => _ = AbrirDocumentoAsync(doc));
            DockPanel.SetDock(ver, Dock.Right);
            fila.Children.Add(ver);
        }
        else if (_admin && f.SocioDocumentosId is not null)
        {
            var escanear = Pequeno("Escanear", () => _ = EscanearAsync((CasetaFila)_tabla.SelectedItem, f, tipo), principal: true);
            DockPanel.SetDock(escanear, Dock.Right);
            fila.Children.Add(escanear);
        }

        var marca = new TextBlock
        {
            Text = doc is null ? "✗" : "✓",
            FontWeight = FontWeights.Bold,
            Width = 20,
            Foreground = ColoresPanel.Hex(doc is null ? "#B4501B" : "#0B5A0B"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(marca, Dock.Left);
        fila.Children.Add(marca);

        var texto = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texto.Children.Add(new TextBlock { Text = nombre, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, TextWrapping = TextWrapping.Wrap });
        texto.Children.Add(new TextBlock
        {
            Text = doc is null
                ? "Falta escanear"
                : $"{doc.FechaRegistroUtc.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)} · {doc.RegistradoPor}",
            FontSize = 12,
            Foreground = doc is null ? ColoresPanel.Hex("#B4501B") : UiKit.Suave
        });
        fila.Children.Add(texto);

        return new Border
        {
            Background = UiKit.Fondo,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Child = fila
        };
    }

    // =====================================================================
    // ACCIONES
    // =====================================================================

    private async Task GuardarAsync(CasetaFila fila, FichaDto f, Button boton,
        DateTime? fechaIngreso, string? vida, string? propiedad, string anterior, string? aporte, string observaciones)
    {
        if (fechaIngreso is { } fi && fi.Date > DateTime.Today)
        {
            Dialogo.Mostrar(Window.GetWindow(this), "La fecha de ingreso no puede ser futura.", "Revisa la fecha", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        boton.IsEnabled = false;
        try
        {
            await ApiJson.PutAsync<object>($"/api/kardex/caseta/{fila.UnidadId}", new
            {
                // El Kardex es del titular actual de la caseta.
                socioId = f.Titular?.Id ?? f.Kardex?.SocioId,
                fechaIngreso = fechaIngreso is { } fecha ? DateOnly.FromDateTime(fecha) : (DateOnly?)null,
                estadoVida = vida,
                tipoDocumentoPropiedad = propiedad,
                propietarioAnterior = anterior.Trim(),
                aporteSocioPagado = aporte switch { "SI" => (bool?)true, "NO" => false, _ => null },
                observaciones = observaciones.Trim()
            });

            await CargarListaAsync();
            await MostrarFichaAsync();
            _resumen.Text = $"✓ Se guardó el Kardex de la caseta {fila.Codigo}.";
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se guardó el Kardex", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    private async Task EscanearAsync(CasetaFila? fila, FichaDto f, int? tipo)
    {
        if (fila is null || f.SocioDocumentosId is not { } socioId)
            return;

        new DocumentosKardexWindow(Api)
        {
            Owner = Window.GetWindow(this),
            SocioInicial = socioId,
            UnidadInicial = fila.UnidadId,
            TipoInicial = tipo ?? f.Faltantes.FirstOrDefault(x => x.Tipo.HasValue)?.Tipo
        }.ShowDialog();

        await CargarListaAsync();
        await MostrarFichaAsync();
    }

    private async Task CargarFotoAsync(FichaDto f, Border marco)
    {
        var foto = f.Documentos.FirstOrDefault(d => d.Tipo == 5);
        if (foto is null)
            return;

        try
        {
            var bytes = await Api.GetByteArrayAsync($"/api/kardex-documentos/{foto.Id}/descargar");
            var imagen = new BitmapImage();
            using (var flujo = new MemoryStream(bytes))
            {
                imagen.BeginInit();
                imagen.CacheOption = BitmapCacheOption.OnLoad;
                imagen.DecodePixelWidth = 200;
                imagen.StreamSource = flujo;
                imagen.EndInit();
            }
            imagen.Freeze();

            if (ReferenceEquals(_actual, f))
                marco.Child = new Image { Source = imagen, Stretch = Stretch.UniformToFill };
        }
        catch
        {
            // Si no se puede mostrar, el documento sigue en la lista.
        }
    }

    private async Task AbrirDocumentoAsync(DocumentoDto d)
    {
        try
        {
            var bytes = await Api.GetByteArrayAsync($"/api/kardex-documentos/{d.Id}/descargar");
            var extension = Path.GetExtension(d.NombreArchivo);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 6)
                extension = d.TipoContenido == "application/pdf" ? ".pdf" : ".jpg";

            var ruta = Path.Combine(Path.GetTempPath(), $"mercadex-{NombreTipo(d.Tipo).Replace(' ', '-')}-{d.Id.ToString("N")[..8]}{extension}");
            await File.WriteAllBytesAsync(ruta, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo abrir el documento", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    // AYUDANTES
    // =====================================================================

    private static ComboBox Combo(List<UiKit.Item<string?>> opciones, string? valor)
    {
        var combo = UiKit.Combo(200);
        combo.ItemsSource = opciones;
        combo.SelectedItem = opciones.FirstOrDefault(o => string.Equals(o.Valor, valor, StringComparison.OrdinalIgnoreCase)) ?? opciones[0];
        return combo;
    }

    private static string? Valor(ComboBox combo) => (combo.SelectedItem as UiKit.Item<string?>)?.Valor;

    private static TextBlock Seccion(string texto) => new()
    {
        Text = texto,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiKit.Titulo,
        Margin = new Thickness(0, 14, 0, 8)
    };

    private static Button Pequeno(string texto, Action accion, bool principal = false)
    {
        var b = UiKit.Boton(texto, principal);
        b.Height = 30;
        b.Padding = new Thickness(12, 0, 12, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Click += (_, _) => accion();
        return b;
    }

    private static string Texto(string? valor) =>
        string.IsNullOrWhiteSpace(valor) || valor == "SIN_DOCUMENTO" ? "sin registrar" : valor!;

    private static string NombreTipo(int tipo) => tipo switch
    {
        1 => "CI",
        2 => "Certificado de óbito",
        3 => "Documento de propiedad",
        4 => "Comprobante de aporte",
        5 => "Foto 3x2",
        _ => "Documento"
    };

    // =====================================================================
    // MODELOS
    // =====================================================================

    private sealed class ResumenRespuesta
    {
        public int Total { get; set; }
        public int Completos { get; set; }
        public int SinKardex { get; set; }
        public int SinTitular { get; set; }
        public List<CasetaFila> Casetas { get; set; } = new();
    }

    public sealed class CasetaFila
    {
        public Guid UnidadId { get; set; }
        public string Codigo { get; set; } = "";
        public string? Bloque { get; set; }
        public string? Titular { get; set; }
        public bool TieneKardex { get; set; }
        public bool Fallecido { get; set; }
        public int Faltan { get; set; }

        public bool Completo => Faltan == 0;
        public string BloqueTexto => string.IsNullOrWhiteSpace(Bloque) ? "—" : Bloque!;
        public string TitularTexto => Titular is null ? "Sin titular" : Titular + (Fallecido ? " (fallecido)" : "");
        public string EstadoTexto => Faltan == 0 ? "✓ Completo" : !TieneKardex ? $"Sin Kardex · faltan {Faltan}" : $"Faltan {Faltan}";
    }

    private sealed class FichaDto
    {
        public KardexDto? Kardex { get; set; }
        public PasilloDto? Pasillo { get; set; }
        public TitularDto? Titular { get; set; }
        public Guid? SocioDocumentosId { get; set; }
        public List<DocumentoDto> Documentos { get; set; } = new();
        public List<FaltanteDto> Faltantes { get; set; } = new();
    }

    private sealed class KardexDto
    {
        public Guid? SocioId { get; set; }
        public DateOnly? FechaIngreso { get; set; }
        public DateTimeOffset FechaRegistro { get; set; }
        public string? EstadoVida { get; set; }
        public string? TipoDocumentoPropiedad { get; set; }
        public string? PropietarioAnterior { get; set; }
        public bool? AporteSocioPagado { get; set; }
        public string? Observaciones { get; set; }
    }

    private sealed class PasilloDto
    {
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
    }

    private sealed class TitularDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Documento { get; set; }
        public string? Telefono { get; set; }
        public DateTimeOffset FechaRegistro { get; set; }
    }

    private sealed class DocumentoDto
    {
        public Guid Id { get; set; }
        public int Tipo { get; set; }
        public string NombreArchivo { get; set; } = "";
        public string TipoContenido { get; set; } = "";
        public string RegistradoPor { get; set; } = "";
        public DateTimeOffset FechaRegistroUtc { get; set; }
    }

    private sealed class FaltanteDto
    {
        public int? Tipo { get; set; }
        public string Texto { get; set; } = "";
    }
}
