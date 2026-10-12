using Mercadex.Desktop.App.Configuration;
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

public partial class DeudoresConsolidadosWindow : Window
{
    // MERCADEX_DEUDAS_CONSOLIDADAS_WPF_V1

    private static readonly Guid MonedaBOB =
        Guid.Parse("c1922a82-4f86-4210-9a4b-15719a6b3f3a");

    private static readonly Guid ConceptoMensualidad =
        Guid.Parse("0b2371a7-6712-437d-8e74-6b539f3a2cee");

    private readonly HttpClient _api;

    private List<ObligacionApi> _obligaciones = new();
    private Dictionary<Guid, string> _casetas = new();
    private List<FilaResumen> _resumen = new();

    private bool _ocupado;
    private bool _datosCargados;
    private string _descripcionFiltro = "";

    public DeudoresConsolidadosWindow(HttpClient api)
    {
        InitializeComponent();

        _api = api;
        FechaLimite.SelectedDate = DateTime.Today;

        Loaded += async (_, _) => await ActualizarAsync();
    }

    private bool EntornoPermitido()
    {
        return EntornoApi.ConexionAutenticada(_api);
    }

    private static string Moneda(Guid id)
    {
        return id == MonedaBOB
            ? "BOB"
            : "ID " + id.ToString("N")[..8];
    }

    private static string NombreConcepto(
        Guid id,
        string? nombre = null)
    {
        if (!string.IsNullOrWhiteSpace(nombre))
            return nombre.Trim();

        if (id == ConceptoMensualidad)
            return "Mensualidad";

        return "Concepto " + id.ToString("N")[..8];
    }

    private static string Periodo(ObligacionApi o)
    {
        if (
            !o.PeriodoAnio.HasValue ||
            !o.PeriodoMes.HasValue
        )
        {
            return "Sin período";
        }

        return $"{o.PeriodoMes.Value:00}/{o.PeriodoAnio.Value:0000}";
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ActualizarAsync();
    }

    private void AplicarFiltros_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado)
            return;

        AplicarFiltros();
    }

    private async Task ActualizarAsync()
    {
        if (_ocupado)
            return;

        if (!EntornoPermitido())
        {
            TxtEstado.Text =
                "Inicia sesion como administrador para esta consulta.";
            return;
        }

        _ocupado = true;
        _datosCargados = false;
        BtnActualizar.IsEnabled = false;
        BtnImprimir.IsEnabled = false;

        try
        {
            TxtEstado.Text = "Consultando obligaciones pendientes...";

            var unidades = await _api
                .GetFromJsonAsync<List<UnidadApi>>(
                    "/api/unidades-comerciales");

            var obligaciones = await _api
                .GetFromJsonAsync<List<ObligacionApi>>(
                    "/api/obligaciones");

            if (unidades is null || obligaciones is null)
                throw new InvalidOperationException(
                    "La API no devolvió los datos esperados.");

            if (obligaciones.Count > 100000)
                throw new InvalidOperationException(
                    "El listado supera el límite de consulta.");

            var codigos = new Dictionary<Guid, string>();

            foreach (var unidad in unidades)
            {
                if (
                    unidad.Id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(unidad.Codigo)
                )
                {
                    throw new InvalidOperationException(
                        "Existe una unidad comercial incompleta.");
                }

                if (!codigos.TryAdd(
                    unidad.Id,
                    unidad.Codigo.Trim()))
                {
                    throw new InvalidOperationException(
                        "Se recibió una unidad comercial duplicada.");
                }
            }

            var ids = new HashSet<Guid>();

            foreach (var o in obligaciones)
            {
                if (
                    o.Id == Guid.Empty ||
                    !ids.Add(o.Id)
                )
                {
                    throw new InvalidOperationException(
                        "Existe una obligación duplicada o sin ID.");
                }

                if (!string.Equals(
                    o.Estado,
                    "Pendiente",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (
                    o.UnidadComercialId == Guid.Empty ||
                    o.MonedaId == Guid.Empty ||
                    o.ConceptoCobroId == Guid.Empty ||
                    o.Monto <= 0 ||
                    !codigos.ContainsKey(o.UnidadComercialId)
                )
                {
                    throw new InvalidOperationException(
                        "Existe una obligación pendiente con datos incompletos.");
                }

                bool tieneAnio = o.PeriodoAnio.HasValue;
                bool tieneMes = o.PeriodoMes.HasValue;

                if (tieneAnio != tieneMes)
                {
                    throw new InvalidOperationException(
                        "Existe una obligación con período incompleto.");
                }

                if (tieneAnio && (
                    o.PeriodoAnio!.Value < 1 ||
                    o.PeriodoAnio.Value > 9999 ||
                    o.PeriodoMes!.Value < 1 ||
                    o.PeriodoMes.Value > 12))
                {
                    throw new InvalidOperationException(
                        "Existe una obligación con período inválido.");
                }
            }

            _casetas = codigos;

            _obligaciones = obligaciones
                .Where(o => string.Equals(
                    o.Estado,
                    "Pendiente",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var conceptoAnterior =
                (CmbConceptos.SelectedItem as OpcionConcepto)?.Id;

            var opciones = new List<OpcionConcepto>
            {
                new()
                {
                    Id = null,
                    Nombre = "Todos los conceptos"
                }
            };

            opciones.AddRange(
                _obligaciones
                    .GroupBy(o => o.ConceptoCobroId)
                    .Select(g => new OpcionConcepto
                    {
                        Id = g.Key,
                        Nombre = NombreConcepto(
                            g.Key,
                            g.Select(x => x.Concepto)
                                .FirstOrDefault(x =>
                                    !string.IsNullOrWhiteSpace(x)))
                    })
                    .OrderBy(o => o.Nombre));

            CmbConceptos.ItemsSource = opciones;
            CmbConceptos.SelectedItem =
                opciones.FirstOrDefault(o =>
                    o.Id == conceptoAnterior) ?? opciones[0];

            _datosCargados = true;

            AplicarFiltros();
        }
        catch (Exception ex)
        {
            _datosCargados = false;
            _resumen.Clear();

            TablaResumen.ItemsSource = null;
            TablaDetalle.ItemsSource = null;

            TxtCasetas.Text = "—";
            TxtObligaciones.Text = "—";
            TxtTotales.Text = "Sin consultar";
            TxtDetalle.Text = "Detalle de obligaciones";
            TxtEstado.Text = "Error de consulta: " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnActualizar.IsEnabled = true;
        }
    }

    private void AplicarFiltros()
    {
        if (!_datosCargados)
            return;

        bool todos = ChkTodos.IsChecked == true;

        if (!todos && !FechaLimite.SelectedDate.HasValue)
        {
            TxtEstado.Text =
                "Selecciona el período límite o marca todos los períodos.";
            BtnImprimir.IsEnabled = false;
            return;
        }

        DateTime limite =
            FechaLimite.SelectedDate?.Date ?? DateTime.Today;

        string buscar = (TxtBuscar.Text ?? "").Trim();

        Guid? concepto =
            (CmbConceptos.SelectedItem as OpcionConcepto)?.Id;

        int limitePeriodo = limite.Year * 12 + limite.Month;

        var filtradas = _obligaciones
            .Where(o =>
                _casetas[o.UnidadComercialId].Contains(
                    buscar,
                    StringComparison.OrdinalIgnoreCase))
            .Where(o =>
                !concepto.HasValue ||
                o.ConceptoCobroId == concepto.Value)
            .Where(o =>
                todos ||
                !o.PeriodoAnio.HasValue ||
                (o.PeriodoAnio.Value * 12 +
                 o.PeriodoMes!.Value) <= limitePeriodo)
            .ToList();

        _resumen = filtradas
            .GroupBy(o => new
            {
                o.UnidadComercialId,
                o.MonedaId
            })
            .Select(g => new FilaResumen
            {
                UnidadId = g.Key.UnidadComercialId,
                MonedaId = g.Key.MonedaId,
                Caseta = _casetas[g.Key.UnidadComercialId],
                Moneda = Moneda(g.Key.MonedaId),
                Cantidad = g.Count(),
                Periodos = g.Select(Periodo).Distinct().Count(),
                Total = g.Sum(o => o.Monto),

                Deudas = g
                    .OrderBy(o => o.PeriodoAnio ?? int.MaxValue)
                    .ThenBy(o => o.PeriodoMes ?? int.MaxValue)
                    .ThenBy(o => o.ConceptoCobroId)
                    .Select(o => new FilaDetalle
                    {
                        Concepto = NombreConcepto(
                            o.ConceptoCobroId, o.Concepto),
                        Periodo = Periodo(o),
                        Moneda = Moneda(o.MonedaId),
                        Monto = o.Monto
                    })
                    .ToList()
            })
            .OrderBy(x => x.Caseta)
            .ThenBy(x => x.Moneda)
            .ToList();

        TablaResumen.ItemsSource = null;
        TablaResumen.ItemsSource = _resumen;

        int casetas = _resumen
            .Select(r => r.UnidadId)
            .Distinct()
            .Count();

        TxtCasetas.Text = casetas.ToString(
            CultureInfo.CurrentCulture);

        TxtObligaciones.Text = filtradas.Count.ToString(
            CultureInfo.CurrentCulture);

        TxtTotales.Text = _resumen.Count == 0
            ? "0,00 — sin pendientes"
            : string.Join(
                "  |  ",
                _resumen
                    .GroupBy(r => r.MonedaId)
                    .OrderBy(g => Moneda(g.Key))
                    .Select(g =>
                        $"{Moneda(g.Key)}: {g.Sum(x => x.Total):N2}"));

        _descripcionFiltro = todos
            ? "Todos los períodos"
            : $"Hasta {limite:MM/yyyy}, inclusive";

        if (!string.IsNullOrWhiteSpace(buscar))
            _descripcionFiltro += $" | Caseta: {buscar}";

        if (concepto.HasValue)
        {
            _descripcionFiltro +=
                $" | Concepto: {NombreConcepto(concepto.Value)}";
        }

        int sinPeriodo = filtradas.Count(o =>
            !o.PeriodoAnio.HasValue);

        TxtEstado.Text =
            $"{_descripcionFiltro}. " +
            $"{casetas} caseta(s), " +
            $"{filtradas.Count} obligación(es). " +
            (sinPeriodo > 0
                ? $"{sinPeriodo} sin período, incluidas."
                : "Todas con período identificado.");

        if (_resumen.Count > 0)
        {
            TablaResumen.SelectedIndex = 0;
        }
        else
        {
            TablaDetalle.ItemsSource = null;
            TxtDetalle.Text =
                "Detalle de obligaciones — sin resultados";
        }

        BtnImprimir.IsEnabled = true;
    }

    private void TablaResumen_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (TablaDetalle is null || TxtDetalle is null)
            return;

        if (TablaResumen.SelectedItem is not FilaResumen fila)
        {
            TablaDetalle.ItemsSource = null;
            TxtDetalle.Text = "Detalle de obligaciones";
            return;
        }

        TablaDetalle.ItemsSource = fila.Deudas;

        TxtDetalle.Text =
            $"Detalle: {fila.Caseta} | {fila.Moneda} | " +
            $"{fila.Cantidad} obligación(es) | {fila.Total:N2}";
    }

    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_datosCargados || !EntornoPermitido())
            return;

        var documento = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            PageWidth = 793,
            PageHeight = 1122,
            PagePadding = new Thickness(42),
            ColumnWidth = 690
        };

        documento.Blocks.Add(new Paragraph(
            new Run("MERCADEX"))
        {
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        documento.Blocks.Add(new Paragraph(
            new Run("CONSOLIDADO DE DEUDAS PENDIENTES"))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        documento.Blocks.Add(new Paragraph(
            new Run(
                _descripcionFiltro + "\n" +
                $"Emitido: {DateTime.Now:dd/MM/yyyy HH:mm}\n" +
                "Documento informativo — no es un recibo de pago"))
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        });

        int cantidadCasetas = _resumen
            .Select(x => x.UnidadId)
            .Distinct()
            .Count();

        int cantidadDeudas = _resumen.Sum(x => x.Cantidad);

        AgregarTitulo(documento,
            $"Casetas: {cantidadCasetas} | " +
            $"Obligaciones: {cantidadDeudas}");

        AgregarTitulo(documento, "TOTALES POR MONEDA");

        foreach (var grupo in _resumen.GroupBy(x => x.MonedaId))
        {
            AgregarLinea(
                documento,
                $"{Moneda(grupo.Key)}: " +
                $"{grupo.Sum(x => x.Total):N2}");
        }

        if (_resumen.Count == 0)
        {
            AgregarLinea(documento,
                "No se encontraron obligaciones pendientes.");
        }
        else
        {
            AgregarTitulo(documento, "CONSOLIDADO POR CASETA");

            AgregarTabla(
                documento,
                new[] {
                    "Caseta", "Moneda", "Pendientes", "Total"
                },
                _resumen.Select(x => new[] {
                    x.Caseta,
                    x.Moneda,
                    x.Cantidad.ToString(),
                    x.Total.ToString("N2")
                }));

            AgregarTitulo(documento, "DETALLE DE OBLIGACIONES");

            foreach (var fila in _resumen)
            {
                AgregarTitulo(
                    documento,
                    $"{fila.Caseta} — {fila.Moneda} " +
                    $"{fila.Total:N2}");

                AgregarTabla(
                    documento,
                    new[] {
                        "Concepto", "Período", "Importe"
                    },
                    fila.Deudas.Select(d => new[] {
                        d.Concepto,
                        d.Periodo,
                        d.Monto.ToString("N2")
                    }));
            }
        }

        documento.Blocks.Add(new Paragraph(
            new Run(
                "El estado pendiente no acredita vencimiento ni mora. " +
                "Las obligaciones de períodos futuros solo se incluyen " +
                "cuando el filtro seleccionado lo permite. " +
                "Los importes de distintas monedas no se suman entre sí."))
        {
            FontSize = 10,
            Margin = new Thickness(0, 8, 0, 0)
        });

        try
        {
            var dialogo = new PrintDialog();

            if (dialogo.ShowDialog() != true)
                return;

            dialogo.PrintDocument(
                ((IDocumentPaginatorSource)documento)
                    .DocumentPaginator,
                "Mercadex - Consolidado de deudas");

            TxtEstado.Text =
                "Listado enviado al sistema de impresión. " +
                _descripcionFiltro;
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo imprimir: " + ex.Message;
        }
    }

    private static void AgregarTitulo(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(new Paragraph(
            new Run(texto))
        {
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 9, 0, 4)
        });
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(new Paragraph(
            new Run(texto))
        {
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    private static void AgregarTabla(
        FlowDocument documento,
        string[] encabezados,
        IEnumerable<string[]> filas)
    {
        var tabla = new Table
        {
            CellSpacing = 0
        };

        for (int i = 0; i < encabezados.Length; i++)
            tabla.Columns.Add(new TableColumn());

        var grupo = new TableRowGroup();
        tabla.RowGroups.Add(grupo);

        AgregarFila(grupo, encabezados, true);

        foreach (var fila in filas)
            AgregarFila(grupo, fila, false);

        documento.Blocks.Add(tabla);
    }

    private static void AgregarFila(
        TableRowGroup grupo,
        string[] valores,
        bool encabezado)
    {
        var fila = new TableRow();

        foreach (var valor in valores)
        {
            var celda = new TableCell(
                new Paragraph(new Run(valor)))
            {
                Padding = new Thickness(5, 2, 5, 2),
                BorderBrush = Brushes.LightGray,
                BorderThickness =
                    new Thickness(0, 0, 0, 0.5)
            };

            if (encabezado)
            {
                celda.Background =
                    new SolidColorBrush(
                        Color.FromRgb(232, 239, 247));

                celda.FontWeight = FontWeights.Bold;
            }

            fila.Cells.Add(celda);
        }

        grupo.Rows.Add(fila);
    }

    private void Cerrar_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    public sealed class UnidadApi
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
    }

    public sealed class ObligacionApi
    {
        public Guid Id { get; set; }
        public Guid UnidadComercialId { get; set; }
        public Guid MonedaId { get; set; }
        public Guid ConceptoCobroId { get; set; }
        public string? Concepto { get; set; }
        public decimal Monto { get; set; }
        public string Estado { get; set; } = "";
        public int? PeriodoAnio { get; set; }
        public int? PeriodoMes { get; set; }
    }

    public sealed class OpcionConcepto
    {
        public Guid? Id { get; set; }
        public string Nombre { get; set; } = "";
    }

    public sealed class FilaResumen
    {
        public Guid UnidadId { get; set; }
        public Guid MonedaId { get; set; }
        public string Caseta { get; set; } = "";
        public string Moneda { get; set; } = "";
        public int Cantidad { get; set; }
        public int Periodos { get; set; }
        public decimal Total { get; set; }
        public List<FilaDetalle> Deudas { get; set; } = new();
    }

    public sealed class FilaDetalle
    {
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
    }
}