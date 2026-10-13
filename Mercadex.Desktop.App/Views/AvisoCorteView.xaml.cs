using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Models;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mercadex.Desktop.App.Views;

public partial class AvisoCorteView : UserControl
{
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    private readonly CultureInfo _cultura =
        CultureInfo.GetCultureInfo("es-BO");

    private List<ObligacionIngresoModel> _obligaciones = new();
    private List<UnidadIngresoModel> _unidades = new();
    private List<MonedaIngresoModel> _monedas = new();

    private CatalogosIngresosModel? _catalogos;

    private List<FilaAvisoCorte> _filas = new();

    private string _listadoNumeros = string.Empty;
    private DateTime? _fechaDatos;

    private bool _cargando;
    private bool _inicializado;

    private readonly Button _btnNumeros = new()
    {
        Content = "LISTADO DE NÚMEROS",
        Height = 40,
        MinWidth = 170,
        Padding = new Thickness(15, 0, 15, 0),
        Margin = new Thickness(12, 0, 0, 10),
        IsEnabled = false
    };

    public AvisoCorteView()
    {
        InitializeComponent();

        BtnImprimir.Content = "VISTA PREVIA DEL AVISO";

        _btnNumeros.Click += ListadoNumeros_Click;

        if (BtnImprimir.Parent is Panel panelBotones)
        {
            panelBotones.Children.Add(_btnNumeros);
        }

        Loaded += async (_, _) =>
        {
            if (_inicializado)
                return;

            _inicializado = true;
            await CargarDatosAsync();
        };
    }

    private void LimpiarResultados()
    {
        _filas.Clear();
        _listadoNumeros = string.Empty;

        TablaAvisos.ItemsSource = null;

        TxtResumen.Text = string.Empty;
        TxtTotales.Text = string.Empty;

        BtnImprimir.IsEnabled = false;
        _btnNumeros.IsEnabled = false;
    }

    private async Task CargarDatosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;

        BtnGenerar.IsEnabled = false;

        LimpiarResultados();

        // Si falla la actualización, no habilitar resultados antiguos.
        _catalogos = null;
        _fechaDatos = null;

        TxtEstado.Text =
            "Consultando obligaciones, casetas y conceptos...";

        try
        {
            var obligaciones =
                await Api.GetFromJsonAsync<
                    List<ObligacionIngresoModel>>(
                    "/api/Obligaciones")
                ?? throw new InvalidOperationException(
                    "No se recibieron las obligaciones.");

            var unidades =
                await Api.GetFromJsonAsync<
                    List<UnidadIngresoModel>>(
                    "/api/unidades-comerciales")
                ?? throw new InvalidOperationException(
                    "No se recibieron las unidades comerciales.");

            var monedas =
                await Api.GetFromJsonAsync<
                    List<MonedaIngresoModel>>(
                    "/api/monedas")
                ?? throw new InvalidOperationException(
                    "No se recibieron las monedas.");

            var catalogos =
                await Api.GetFromJsonAsync<CatalogosIngresosModel>(
                    "/api/ingresos/catalogos")
                ?? throw new InvalidOperationException(
                    "No se recibió el catálogo de conceptos.");

            _obligaciones = obligaciones;
            _unidades = unidades;
            _monedas = monedas;
            _catalogos = catalogos;

            PrepararFiltros();

            _fechaDatos = DateTime.Now;

            TxtEstado.Text =
                "Datos cargados. Selecciona los conceptos " +
                "y pulsa GENERAR AVISO.";
        }
        catch (Exception ex)
        {
            _catalogos = null;

            TxtEstado.Text =
                "No se pudieron cargar los datos: " + ex.Message;
        }
        finally
        {
            _cargando = false;
            BtnGenerar.IsEnabled = _catalogos != null;
        }
    }

    private void PrepararFiltros()
    {
        var bloqueAnterior =
            FiltroBloque.SelectedItem as string;

        var monedaAnterior =
            FiltroMoneda.SelectedItem as string;

        var seleccionAnterior = PanelConceptos.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => c.Tag)
            .OfType<Guid>()
            .ToHashSet();

        bool primeraCarga =
            PanelConceptos.Children.Count == 0;

        FiltroBloque.Items.Clear();
        FiltroBloque.Items.Add("Todos");

        foreach (var bloque in _unidades
            .Where(u => u.Tipo == 1)
            .Select(u => u.Bloque)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b))
        {
            FiltroBloque.Items.Add(bloque);
        }

        FiltroBloque.SelectedItem =
            bloqueAnterior != null &&
            FiltroBloque.Items.Contains(bloqueAnterior)
                ? bloqueAnterior
                : "Todos";

        FiltroMoneda.Items.Clear();
        FiltroMoneda.Items.Add("Todas");

        foreach (var codigo in _monedas
            .Select(m => m.Codigo)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c))
        {
            FiltroMoneda.Items.Add(codigo);
        }

        FiltroMoneda.SelectedItem =
            monedaAnterior != null &&
            FiltroMoneda.Items.Contains(monedaAnterior)
                ? monedaAnterior
                : "Todas";

        PanelConceptos.Children.Clear();

        foreach (var concepto in
            (_catalogos?.Conceptos ?? new())
                .OrderBy(c => c.Nombre))
        {
            PanelConceptos.Children.Add(new CheckBox
            {
                Content = concepto.Nombre,
                Tag = concepto.Id,

                IsChecked = primeraCarga ||
                    seleccionAnterior.Contains(concepto.Id),

                Margin = new Thickness(0, 0, 22, 12),
                MinWidth = 165
            });
        }
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarDatosAsync();
    }

    private void Generar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_cargando)
            return;

        try
        {
            GenerarAviso();
        }
        catch (Exception ex)
        {
            LimpiarResultados();
            TxtEstado.Text = "No se pudo generar el aviso: " + ex.Message;
        }
    }

    private bool GenerarAviso()
    {
        LimpiarResultados();

        if (_catalogos == null)
        {
            TxtEstado.Text = "Primero debes actualizar los datos.";
            return false;
        }

        var seleccionados = PanelConceptos.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => c.Tag)
            .OfType<Guid>()
            .ToHashSet();

        if (seleccionados.Count == 0)
        {
            TxtEstado.Text = "Selecciona al menos un concepto.";
            return false;
        }

        var desde = FechaDesde.SelectedDate?.Date;
        var hasta = FechaHasta.SelectedDate?.Date;

        if (desde.HasValue &&
            hasta.HasValue &&
            hasta.Value < desde.Value)
        {
            TxtEstado.Text =
                "La fecha final no puede ser anterior a la inicial.";

            return false;
        }

        var bloque =
            FiltroBloque.SelectedItem as string ?? "Todos";

        var moneda =
            FiltroMoneda.SelectedItem as string ?? "Todas";

        var unidadesPorId = _unidades
            .Where(u => u.Tipo == 1)
            .GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var monedasPorId = _monedas
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First().Codigo);

        var conceptosPorId = _catalogos.Conceptos
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Nombre);

        var pendientes = _obligaciones
            .Where(o => string.Equals(
                o.Estado,
                "Pendiente",
                StringComparison.OrdinalIgnoreCase))
            .Where(o =>
                unidadesPorId.ContainsKey(o.UnidadComercialId))
            .Where(o =>
                seleccionados.Contains(o.ConceptoCobroId))
            .ToList();

        if (bloque != "Todos")
        {
            pendientes = pendientes
                .Where(o => string.Equals(
                    unidadesPorId[o.UnidadComercialId].Bloque,
                    bloque,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (moneda != "Todas")
        {
            pendientes = pendientes
                .Where(o =>
                    monedasPorId.TryGetValue(
                        o.MonedaId,
                        out var codigo) &&
                    string.Equals(
                        codigo,
                        moneda,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (desde.HasValue)
        {
            var fecha = DateOnly.FromDateTime(desde.Value);

            pendientes = pendientes
                .Where(o => o.FechaEmision >= fecha)
                .ToList();
        }

        if (hasta.HasValue)
        {
            var fecha = DateOnly.FromDateTime(hasta.Value);

            pendientes = pendientes
                .Where(o => o.FechaEmision <= fecha)
                .ToList();
        }

        string NombreConcepto(Guid id)
        {
            return conceptosPorId.TryGetValue(id, out var nombre)
                ? nombre
                : "Concepto sin identificar";
        }

        string TotalesPorMoneda(
            IEnumerable<ObligacionIngresoModel> datos)
        {
            return string.Join(
                " | ",
                datos
                    .GroupBy(o => o.MonedaId)
                    .OrderBy(g =>
                        monedasPorId.TryGetValue(g.Key, out var codigo)
                            ? codigo
                            : g.Key.ToString())
                    .Select(g =>
                    {
                        var codigo =
                            monedasPorId.TryGetValue(g.Key, out var valor)
                                ? valor
                                : $"Moneda sin identificar ({g.Key})";

                        return $"{codigo}: " +
                            g.Sum(o => o.Monto).ToString("N2", _cultura);
                    }));
        }

        // Aviso detallado: una fila por unidad comercial.
        _filas = pendientes
            .GroupBy(o => o.UnidadComercialId)
            .Select(grupo =>
            {
                var unidad = unidadesPorId[grupo.Key];

                return new FilaAvisoCorte
                {
                    Caseta = unidad.Codigo,
                    Bloque = unidad.Bloque ?? "Sin bloque",

                    Servicios = string.Join(
                        ", ",
                        grupo
                            .Select(o => NombreConcepto(o.ConceptoCobroId))
                            .Distinct()
                            .OrderBy(n => n)),

                    Cantidad = grupo.Count(),
                    Totales = TotalesPorMoneda(grupo)
                };
            })
            .OrderBy(f => f.Bloque)
            .ThenBy(f => NumeroOrden(f.Caseta))
            .ThenBy(f => f.Caseta)
            .ToList();

        // Listado simple: una línea por concepto.
        // Una caseta aparece una sola vez por concepto,
        // aunque tenga varios meses pendientes.
        var lineas = new List<string>();

        foreach (var grupo in pendientes
            .GroupBy(o => o.ConceptoCobroId)
            .OrderBy(g => NombreConcepto(g.Key)))
        {
            var casetas = grupo
                .Select(o => o.UnidadComercialId)
                .Distinct()
                .Select(id => unidadesPorId[id])
                .OrderBy(u => NumeroOrden(u.Codigo))
                .ThenBy(u => u.Codigo)
                .ThenBy(u => u.Bloque)
                .ToList();

            // No confundir unidades distintas con el mismo número.
            var codigosRepetidos = casetas
                .GroupBy(
                    u => u.Codigo.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var numeros = casetas.Select(u =>
                codigosRepetidos.Contains(u.Codigo.Trim())
                    ? $"{u.Codigo} ({u.Bloque ?? "Sin bloque"})"
                    : u.Codigo);

            lineas.Add(
                $"{NombreConcepto(grupo.Key)} — pendientes: " +
                string.Join(" - ", numeros));
        }

        _listadoNumeros = string.Join(
            Environment.NewLine + Environment.NewLine,
            lineas);

        TablaAvisos.ItemsSource = _filas;

        TxtResumen.Text =
            $"Casetas: {_filas.Count} | " +
            $"Obligaciones pendientes: {pendientes.Count}";

        TxtTotales.Text = pendientes.Count == 0
            ? "Sin deudas para los filtros seleccionados."
            : TotalesPorMoneda(pendientes);

        TxtEstado.Text = pendientes.Count == 0
            ? "No se encontraron deudas con esos filtros."
            : "Listado generado. Puedes abrir el aviso detallado " +
              "o el listado de números.";

        BtnImprimir.IsEnabled = _filas.Count > 0;
        _btnNumeros.IsEnabled = _filas.Count > 0;

        return _filas.Count > 0;
    }

    private static int NumeroOrden(string codigo)
    {
        return int.TryParse(codigo, out var numero)
            ? numero
            : int.MaxValue;
    }

    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        AbrirDocumento(soloNumeros: false);
    }

    private void ListadoNumeros_Click(
        object sender,
        RoutedEventArgs e)
    {
        AbrirDocumento(soloNumeros: true);
    }

    private void AbrirDocumento(bool soloNumeros)
    {
        if (_cargando)
            return;

        try
        {
            // Recalcular para que encabezado y contenido
            // correspondan a los filtros actuales.
            if (!GenerarAviso())
                return;

            var titulo = soloNumeros
                ? "Listado de casetas por concepto"
                : "Aviso para corte";

            var documento = CrearDocumento(soloNumeros);

            MostrarVistaPrevia(
                documento,
                titulo,
                soloNumeros ? _listadoNumeros : null);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(
                ex.Message,
                "No se pudo abrir la vista previa",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private FlowDocument CrearDocumento(bool soloNumeros)
    {
        var documento = new FlowDocument
        {
            PageWidth = 793.7,
            PageHeight = 1122.5,
            PagePadding = new Thickness(45),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Arial"),
            FontSize = 13,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        documento.Blocks.Add(new Paragraph(new Run(
            soloNumeros
                ? "CASETAS CON DEUDA POR CONCEPTO"
                : "AVISO PARA CORTE"))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        AgregarLinea(
            documento,
            $"Datos cargados: {_fechaDatos:dd/MM/yyyy HH:mm}");

        AgregarLinea(
            documento,
            $"Bloque: {FiltroBloque.SelectedItem}");

        AgregarLinea(
            documento,
            $"Moneda: {FiltroMoneda.SelectedItem}");

        AgregarLinea(
            documento,
            "Rango de emisión: " +
            $"{FechaDesde.SelectedDate?.ToString("dd/MM/yyyy") ?? "Sin límite"}" +
            " - " +
            $"{FechaHasta.SelectedDate?.ToString("dd/MM/yyyy") ?? "Sin límite"}");

        if (soloNumeros)
        {
            documento.Blocks.Add(
                new Paragraph(new Run(_listadoNumeros))
                {
                    FontSize = 15,
                    Margin = new Thickness(0, 20, 0, 10)
                });
        }
        else
        {
            AgregarLinea(documento, TxtResumen.Text);
            AgregarLinea(documento, TxtTotales.Text);

            foreach (var fila in _filas)
            {
                documento.Blocks.Add(new Paragraph(new Run(
                    $"Caseta {fila.Caseta} - {fila.Bloque}\n" +
                    $"Conceptos: {fila.Servicios}\n" +
                    $"Obligaciones: {fila.Cantidad}\n" +
                    $"Totales: {fila.Totales}"))
                {
                    Margin = new Thickness(0, 15, 0, 8)
                });
            }

            documento.Blocks.Add(new Paragraph(new Run(
                "Documento informativo para revisión. " +
                "La inclusión en este listado no ejecuta ni autoriza " +
                "automáticamente un corte."))
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }

        return documento;
    }

    private void MostrarVistaPrevia(
        FlowDocument documento,
        string titulo,
        string? textoParaCopiar)
    {
        var ventana = new Window
        {
            Title = $"Vista previa — {titulo}",
            Width = Math.Min(
                1050,
                SystemParameters.WorkArea.Width),
            Height = Math.Min(
                850,
                SystemParameters.WorkArea.Height),
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var propietaria = Window.GetWindow(this);

        if (propietaria != null)
            ventana.Owner = propietaria;

        var contenedor = new DockPanel
        {
            Margin = new Thickness(12)
        };

        var visor = new FlowDocumentReader
        {
            Document = documento,
            ViewingMode =
                FlowDocumentReaderViewingMode.Scroll,
            Zoom = 125,
            MinZoom = 60,
            MaxZoom = 300,
            ZoomIncrement = 15,
            IsPrintEnabled = false,
            IsFindEnabled = true
        };

        var botones = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        Button NuevoBoton(string texto)
        {
            return new Button
            {
                Content = texto,
                Padding = new Thickness(13, 8, 13, 8),
                Margin = new Thickness(0, 0, 9, 6),
                MinHeight = 36
            };
        }

        var imprimir = NuevoBoton("Imprimir");

        imprimir.Click += (_, _) =>
        {
            var anchoAnterior = documento.PageWidth;
            var altoAnterior = documento.PageHeight;

            try
            {
                var dialogo = new PrintDialog();

                if (dialogo.ShowDialog() != true)
                    return;

                documento.PageWidth =
                    dialogo.PrintableAreaWidth;

                documento.PageHeight =
                    dialogo.PrintableAreaHeight;

                dialogo.PrintDocument(
                    ((IDocumentPaginatorSource)documento)
                        .DocumentPaginator,
                    titulo + " - Mercadex");
            }
            catch (Exception ex)
            {
                Dialogo.Mostrar(
                    ventana,
                    "No se pudo imprimir.`n`n" +
                    ex.Message,
                    "Error de impresión",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                documento.PageWidth = anchoAnterior;
                documento.PageHeight = altoAnterior;
            }
        };

        botones.Children.Add(imprimir);

        if (!string.IsNullOrWhiteSpace(textoParaCopiar))
        {
            var copiar = NuevoBoton("Copiar listado");

            copiar.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(textoParaCopiar);
                    copiar.Content = "Listado copiado";
                }
                catch (Exception ex)
                {
                    Dialogo.Mostrar(
                        ventana,
                        ex.Message,
                        "Error al copiar",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            };

            botones.Children.Add(copiar);
        }

        var vistaLegible = NuevoBoton("Vista legible");

        vistaLegible.Click += (_, _) =>
        {
            visor.ViewingMode =
                FlowDocumentReaderViewingMode.Scroll;

            visor.Zoom = 125;
        };

        botones.Children.Add(vistaLegible);

        var vistaA4 = NuevoBoton("Ver hoja A4");

        vistaA4.Click += (_, _) =>
        {
            visor.ViewingMode =
                FlowDocumentReaderViewingMode.Page;

            visor.Zoom = 175;
        };

        botones.Children.Add(vistaA4);

        var acercar = NuevoBoton("Zoom +");

        acercar.Click += (_, _) =>
        {
            visor.Zoom = Math.Min(
                visor.MaxZoom,
                visor.Zoom + 25);
        };

        botones.Children.Add(acercar);

        var alejar = NuevoBoton("Zoom -");

        alejar.Click += (_, _) =>
        {
            visor.Zoom = Math.Max(
                visor.MinZoom,
                visor.Zoom - 25);
        };

        botones.Children.Add(alejar);

        var cerrar = NuevoBoton("Cerrar");

        cerrar.IsCancel = true;

        cerrar.Click += (_, _) =>
        {
            ventana.Close();
        };

        botones.Children.Add(cerrar);

        DockPanel.SetDock(botones, Dock.Top);

        contenedor.Children.Add(botones);
        contenedor.Children.Add(visor);

        ventana.Content = contenedor;

        ventana.ShowDialog();
    }
    private static void AgregarLinea(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(new Paragraph(new Run(texto))
        {
            Margin = new Thickness(0, 3, 0, 3)
        });
    }
}

public sealed class FilaAvisoCorte
{
    public string Caseta { get; set; } = string.Empty;
    public string Bloque { get; set; } = string.Empty;
    public string Servicios { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string Totales { get; set; } = string.Empty;
}