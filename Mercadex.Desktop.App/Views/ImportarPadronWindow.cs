using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Services;
using Microsoft.Win32;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Importar el padrón desde Excel: casetas, bloques, socios y titulares.
/// Paso 1: elegir el archivo (se valida en el servidor sin guardar).
/// Paso 2: si no hay errores, confirmar la importación.
/// </summary>
public sealed class ImportarPadronWindow : Window
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromMinutes(3));
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private static readonly string[] Encabezados =
        { "Caseta", "Tipo", "Bloque", "Nombres", "Apellidos", "Documento", "Teléfono" };

    // Nombres de columna aceptados (sin tildes ni mayúsculas).
    private static readonly Dictionary<string, string> Sinonimos = new()
    {
        ["caseta"] = "codigo", ["numero"] = "codigo", ["numero de caseta"] = "codigo", ["n caseta"] = "codigo",
        ["nro caseta"] = "codigo", ["codigo"] = "codigo", ["puesto"] = "codigo", ["local"] = "codigo",
        ["tipo"] = "tipo",
        ["bloque"] = "bloque",
        ["nombres"] = "nombres", ["nombre"] = "nombres",
        ["apellidos"] = "apellidos", ["apellido"] = "apellidos",
        ["documento"] = "documento", ["ci"] = "documento", ["carnet"] = "documento", ["carnet de identidad"] = "documento",
        ["documento de identidad"] = "documento", ["cedula"] = "documento",
        ["telefono"] = "telefono", ["celular"] = "telefono", ["telf"] = "telefono"
    };

    private readonly TextBlock _txtArchivo;
    private readonly TextBlock _txtResumen;
    private readonly DataGrid _tabla;
    private readonly Button _btnImportar;
    private readonly Button _btnElegir;

    private List<Dictionary<string, object?>> _filas = new();

    public bool SeImporto { get; private set; }

    public ImportarPadronWindow()
    {
        Title = "Importar padrón desde Excel";
        Width = 980;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ColoresPanel.Hex("#F3F6FA");
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        // ---------- Encabezado e instrucciones ----------

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);

        arriba.Children.Add(new TextBlock
        {
            Text = "Importar padrón desde Excel",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = ColoresPanel.Hex("#12304D")
        });

        arriba.Children.Add(new TextBlock
        {
            Text = "Registra de una vez las casetas, sus bloques, los socios y quién es el titular de cada caseta. " +
                   "Primero se revisa la planilla sin guardar nada; solo se importa si no hay errores. Nunca se borra información.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            Margin = new Thickness(0, 4, 0, 14),
            MaxWidth = 860,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        var columnas = new Border
        {
            Style = (Style)Application.Current.FindResource("Mx.Card"),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var columnasTexto = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#1E2A38") };
        columnasTexto.Inlines.Add(new System.Windows.Documents.Run("Columnas de la planilla: ") { FontWeight = FontWeights.SemiBold });
        columnasTexto.Inlines.Add(new System.Windows.Documents.Run(
            "Caseta (obligatoria) · Tipo (Caseta, Comedor o Ambulante; vacío = Caseta) · Bloque · Nombres · Apellidos · Documento · Teléfono. " +
            "Si una fila no tiene datos del titular, solo se registra o actualiza la caseta. El documento identifica al socio: " +
            "si ya existe, se usa ese socio en vez de crear otro."));
        columnas.Child = columnasTexto;
        arriba.Children.Add(columnas);

        var acciones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        var btnPlantilla = new Button
        {
            Content = "Descargar plantilla",
            Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnPlantilla.Click += (_, _) => GuardarPlantilla();
        acciones.Children.Add(btnPlantilla);

        _btnElegir = new Button
        {
            Content = "Elegir archivo y revisar",
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton"),
            Margin = new Thickness(0, 0, 14, 0)
        };
        _btnElegir.Click += async (_, _) => await ElegirYRevisarAsync();
        acciones.Children.Add(_btnElegir);

        _txtArchivo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 460
        };
        acciones.Children.Add(_txtArchivo);
        arriba.Children.Add(acciones);

        raiz.Children.Add(arriba);

        // ---------- Pie: resumen y confirmación ----------

        var abajo = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(abajo, Dock.Bottom);

        var botonesPie = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(botonesPie, Dock.Right);

        var btnCerrar = new Button
        {
            Content = "Cerrar",
            Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnCerrar.Click += (_, _) => Close();
        botonesPie.Children.Add(btnCerrar);

        _btnImportar = new Button
        {
            Content = "Importar",
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton"),
            Background = ColoresPanel.Hex("#287A42"),
            BorderBrush = ColoresPanel.Hex("#287A42"),
            IsEnabled = false
        };
        _btnImportar.Click += async (_, _) => await ImportarAsync();
        botonesPie.Children.Add(_btnImportar);

        abajo.Children.Add(botonesPie);

        _txtResumen = new TextBlock
        {
            Text = "Elige un archivo para ver qué se importará.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ColoresPanel.Hex("#12304D"),
            Margin = new Thickness(0, 0, 16, 0)
        };
        abajo.Children.Add(_txtResumen);

        raiz.Children.Add(abajo);

        // ---------- Tabla de revisión ----------

        _tabla = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            RowHeight = double.NaN,
            MinRowHeight = 38
        };
        _tabla.Columns.Add(new DataGridTextColumn { Header = "Fila", Binding = new Binding("[fila]"), Width = 60 });
        _tabla.Columns.Add(new DataGridTextColumn { Header = "Caseta", Binding = new Binding("[codigo]"), Width = 90 });
        _tabla.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Estado",
            Width = 110,
            CellTemplate = PlantillaEstado()
        });
        _tabla.Columns.Add(new DataGridTextColumn
        {
            Header = "Qué se hará",
            Binding = new Binding("[detalle]"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = EstiloTextoAjustado()
        });

        raiz.Children.Add(_tabla);

        Content = raiz;
    }

    // ------------------------------------------------------------------

    private void GuardarPlantilla()
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar plantilla",
            FileName = "Plantilla padron Mercadex.xlsx",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx"
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            PlanillaExcel.EscribirXlsx(
                dialogo.FileName,
                "Padron",
                Encabezados,
                new[]
                {
                    new[] { "101", "Caseta", "CLAVELES", "Juana", "Chávez Mamani", "1234567", "70000001" },
                    new[] { "102", "Caseta", "CLAVELES", "", "", "", "" },
                    new[] { "C-1", "Comedor", "ROSAS", "Esteban", "Arce Quispe", "7654321 LP", "" }
                },
                new double[] { 12, 12, 16, 22, 26, 16, 14 });

            _txtArchivo.Text = "Plantilla guardada. Reemplaza las filas de ejemplo por tus datos.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo guardar la plantilla: " + ex.Message, "Mercadex",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ElegirYRevisarAsync()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Elegir planilla del padrón",
            Filter = "Planillas (*.xlsx;*.csv)|*.xlsx;*.csv|Todos los archivos (*.*)|*.*"
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        _btnImportar.IsEnabled = false;
        _tabla.ItemsSource = null;
        _txtArchivo.Text = Path.GetFileName(dialogo.FileName);

        try
        {
            _filas = LeerFilas(dialogo.FileName);
            _txtResumen.Text = $"Revisando {_filas.Count} filas...";
            _btnElegir.IsEnabled = false;

            var respuesta = await EnviarAsync(aplicar: false);
            Mostrar(respuesta);
        }
        catch (Exception ex)
        {
            _txtResumen.Text = ex.Message;
            _txtResumen.Foreground = ColoresPanel.Hex("#B42318");
        }
        finally
        {
            _btnElegir.IsEnabled = true;
        }
    }

    private async Task ImportarAsync()
    {
        var confirmar = MessageBox.Show(this,
            "Se guardarán los cambios mostrados en la tabla.\n\n¿Deseas importar ahora?",
            "Importar padrón", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _btnImportar.IsEnabled = false;
        _btnElegir.IsEnabled = false;
        _txtResumen.Text = "Importando...";

        try
        {
            var respuesta = await EnviarAsync(aplicar: true);
            Mostrar(respuesta);

            if (respuesta.GetProperty("aplicado").GetBoolean())
            {
                SeImporto = true;
                _btnImportar.IsEnabled = false;
                _txtResumen.Text = "Importación terminada. " + Resumen(respuesta);
                _txtResumen.Foreground = ColoresPanel.Hex("#1D5C21");
            }
        }
        catch (Exception ex)
        {
            _txtResumen.Text = "No se pudo importar: " + ex.Message;
            _txtResumen.Foreground = ColoresPanel.Hex("#B42318");
        }
        finally
        {
            _btnElegir.IsEnabled = true;
        }
    }

    private async Task<JsonElement> EnviarAsync(bool aplicar)
    {
        using var respuesta = await Http.PostAsJsonAsync(
            $"/api/importacion/padron?aplicar={(aplicar ? "true" : "false")}",
            new { filas = _filas });

        var texto = await respuesta.Content.ReadAsStringAsync();

        if (!respuesta.IsSuccessStatusCode)
        {
            var mensaje = texto;
            try
            {
                using var doc = JsonDocument.Parse(texto);
                if (doc.RootElement.TryGetProperty("mensaje", out var m))
                    mensaje = m.GetString() ?? texto;
            }
            catch (JsonException)
            {
            }

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(mensaje)
                ? $"El servidor respondió {(int)respuesta.StatusCode}."
                : mensaje);
        }

        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private void Mostrar(JsonElement r)
    {
        var filas = r.GetProperty("resultados").EnumerateArray()
            .Select(x => new Dictionary<string, object?>
            {
                ["fila"] = x.GetProperty("fila").GetInt32(),
                ["codigo"] = x.GetProperty("codigo").GetString(),
                ["valida"] = x.GetProperty("valida").GetBoolean(),
                ["detalle"] = x.GetProperty("detalle").GetString()
            })
            // Errores primero para que se vean de inmediato.
            .OrderBy(x => (bool)x["valida"]! ? 1 : 0)
            .ThenBy(x => (int)x["fila"]!)
            .ToList();

        _tabla.ItemsSource = filas;

        var errores = r.GetProperty("errores").GetInt32();

        if (errores > 0)
        {
            _txtResumen.Text = $"{errores} {(errores == 1 ? "fila tiene" : "filas tienen")} errores (arriba en la tabla). " +
                               "Corrígelas en Excel, guarda y vuelve a elegir el archivo. No se guardó nada.";
            _txtResumen.Foreground = ColoresPanel.Hex("#B42318");
            _btnImportar.IsEnabled = false;
        }
        else if (!r.GetProperty("aplicado").GetBoolean())
        {
            _txtResumen.Text = "Todo correcto. " + Resumen(r) + " Pulsa Importar para guardar.";
            _txtResumen.Foreground = ColoresPanel.Hex("#12304D");
            _btnImportar.IsEnabled = true;
            _btnImportar.Content = $"Importar {r.GetProperty("filas").GetInt32()} filas";
        }
    }

    private static string Resumen(JsonElement r)
    {
        var partes = new List<string>();
        void Agregar(string propiedad, string uno, string varios)
        {
            var n = r.GetProperty(propiedad).GetInt32();
            if (n > 0)
                partes.Add($"{n} {(n == 1 ? uno : varios)}");
        }

        Agregar("unidadesNuevas", "caseta nueva", "casetas nuevas");
        Agregar("bloquesActualizados", "bloque actualizado", "bloques actualizados");
        Agregar("sociosNuevos", "socio nuevo", "socios nuevos");
        Agregar("titularesAsignados", "titular asignado", "titulares asignados");

        return partes.Count == 0 ? "No hay cambios que hacer." : string.Join(", ", partes) + ".";
    }

    // ------------------------------------------------------------------

    private static List<Dictionary<string, object?>> LeerFilas(string ruta)
    {
        var filas = PlanillaExcel.Leer(ruta);

        if (filas.Count < 2)
            throw new InvalidOperationException("La planilla está vacía. La primera fila debe tener los encabezados.");

        var mapa = new Dictionary<string, int>();
        var encabezados = filas[0];

        for (var i = 0; i < encabezados.Length; i++)
        {
            var clave = Normalizar(encabezados[i]);
            if (Sinonimos.TryGetValue(clave, out var campo) && !mapa.ContainsKey(campo))
                mapa[campo] = i;
        }

        if (!mapa.ContainsKey("codigo"))
        {
            throw new InvalidOperationException(
                "No se encontró la columna \"Caseta\" en la primera fila. Usa la plantilla o escribe los encabezados: " +
                string.Join(", ", Encabezados) + ".");
        }

        string? Valor(string[] fila, string campo) =>
            mapa.TryGetValue(campo, out var i) && i < fila.Length && !string.IsNullOrWhiteSpace(fila[i])
                ? fila[i].Trim()
                : null;

        var resultado = new List<Dictionary<string, object?>>();

        for (var n = 1; n < filas.Count; n++)
        {
            var fila = filas[n];
            if (fila.All(string.IsNullOrWhiteSpace))
                continue;

            resultado.Add(new Dictionary<string, object?>
            {
                ["fila"] = n + 1,
                ["codigo"] = Valor(fila, "codigo"),
                ["tipo"] = Valor(fila, "tipo"),
                ["bloque"] = Valor(fila, "bloque"),
                ["nombres"] = Valor(fila, "nombres"),
                ["apellidos"] = Valor(fila, "apellidos"),
                ["documento"] = Valor(fila, "documento"),
                ["telefono"] = Valor(fila, "telefono")
            });
        }

        if (resultado.Count == 0)
            throw new InvalidOperationException("La planilla no tiene filas con datos debajo de los encabezados.");

        return resultado;
    }

    private static string Normalizar(string texto)
    {
        var sinTildes = new StringBuilder();
        foreach (var c in texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sinTildes.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', sinTildes.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static DataTemplate PlantillaEstado()
    {
        // Etiqueta "Correcta" (verde) o "Error" (rojo) según [valida].
        var borde = new FrameworkElementFactory(typeof(Border));
        borde.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        borde.SetValue(Border.PaddingProperty, new Thickness(9, 2, 9, 3));
        borde.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        borde.SetBinding(Border.BackgroundProperty, new Binding("[valida]") { Converter = new ConvertidorEstado("#EAF6EA", "#FDE8E8") });

        var texto = new FrameworkElementFactory(typeof(TextBlock));
        texto.SetValue(TextBlock.FontSizeProperty, 12.5);
        texto.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        texto.SetBinding(TextBlock.TextProperty, new Binding("[valida]") { Converter = new ConvertidorTexto() });
        texto.SetBinding(TextBlock.ForegroundProperty, new Binding("[valida]") { Converter = new ConvertidorEstado("#1D5C21", "#9B1C1C") });

        borde.AppendChild(texto);
        return new DataTemplate { VisualTree = borde };
    }

    private static Style EstiloTextoAjustado()
    {
        var estilo = new Style(typeof(TextBlock));
        estilo.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        estilo.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 6)));
        return estilo;
    }

    private sealed class ConvertidorEstado(string si, string no) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            ColoresPanel.Hex(value is true ? si : no);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ConvertidorTexto : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? "Correcta" : "Error";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
