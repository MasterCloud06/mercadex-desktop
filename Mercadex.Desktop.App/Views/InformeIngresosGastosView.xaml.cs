using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class InformeIngresosGastosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly ApiClient _api = new();
    private InformeIngresosGastos? _informe;
    private bool _ocupado;

    public InformeIngresosGastosView()
    {
        InitializeComponent();
        EsteMes();
    }

    private void EsteMes()
    {
        var hoy = DateTime.Today;
        Desde.SelectedDate = new DateTime(hoy.Year, hoy.Month, 1);
        Hasta.SelectedDate = hoy;
    }

    private async void EsteMes_Click(object sender, RoutedEventArgs e)
    {
        EsteMes();
        await GenerarAsync();
    }

    private async void MesAnterior_Click(object sender, RoutedEventArgs e)
    {
        var inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        Desde.SelectedDate = inicio;
        Hasta.SelectedDate = inicio.AddMonths(1).AddDays(-1);
        await GenerarAsync();
    }

    private async void Generar_Click(object sender, RoutedEventArgs e) => await GenerarAsync();

    private async Task GenerarAsync()
    {
        if (_ocupado)
            return;

        if (Desde.SelectedDate is not DateTime desde || Hasta.SelectedDate is not DateTime hasta)
        {
            TxtEstado.Text = "Selecciona las dos fechas.";
            return;
        }

        if (desde > hasta)
        {
            TxtEstado.Text = "La fecha inicial no puede ser posterior a la final.";
            return;
        }

        _ocupado = true;
        BtnGenerar.IsEnabled = false;
        TxtEstado.Text = "Generando informe...";

        try
        {
            _informe = await _api.GetAsync<InformeIngresosGastos>(
                "/api/informes/ingresos-gastos?desde=" +
                desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                "&hasta=" + hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var monedas = _informe?.Monedas ?? new();

            SelectorMoneda.ItemsSource = monedas;
            SelectorMoneda.SelectedItem =
                monedas.FirstOrDefault(m => m.Moneda == "BOB") ?? monedas.FirstOrDefault();

            TxtEstado.Text = monedas.Count == 0
                ? $"No hay ingresos ni egresos entre el {desde:dd/MM/yyyy} y el {hasta:dd/MM/yyyy}."
                : $"Período del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}." +
                  (monedas.Count > 1 ? " Hay movimientos en más de una moneda: elige la moneda arriba." : "");

            Mostrar();
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo generar el informe: " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnGenerar.IsEnabled = true;
        }
    }

    private void Moneda_Changed(object sender, SelectionChangedEventArgs e) => Mostrar();

    private InformeMoneda? Actual => SelectorMoneda?.SelectedItem as InformeMoneda;

    private void Mostrar()
    {
        if (TotIngresos == null)
            return;

        var m = Actual;
        BtnImprimir.IsEnabled = BtnExcel.IsEnabled = m != null;

        if (m == null)
        {
            TotIngresos.Text = TotEgresos.Text = TotSaldo.Text = "—";
            TotIngresosN.Text = TotEgresosN.Text = " ";
            TablaIngresos.ItemsSource = null;
            TablaEgresos.ItemsSource = null;
            return;
        }

        TotIngresos.Text = Monto(m.TotalIngresos, m.Moneda);
        TotIngresosN.Text = $"{m.Recibos:N0} recibos · {m.Ingresos.Sum(x => x.Cantidad):N0} conceptos cobrados";
        TotEgresos.Text = Monto(m.TotalEgresos, m.Moneda);
        TotEgresosN.Text = $"{m.Egresos.Sum(x => x.Cantidad):N0} egresos";
        TotSaldo.Text = Monto(m.Saldo, m.Moneda);
        TotSaldoN.Text = m.Saldo >= 0 ? "Superávit (ingresos − egresos)" : "Déficit (ingresos − egresos)";
        CajaSaldo.Background = ColoresPanel.Hex(m.Saldo >= 0 ? "#1F4E79" : "#8E2A20");

        TablaIngresos.ItemsSource = m.Ingresos;
        TablaEgresos.ItemsSource = m.Egresos;
    }

    private void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        if (_informe == null || Actual == null)
            return;

        new InformeIngresosGastosWindow(_informe, Actual) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Excel_Click(object sender, RoutedEventArgs e)
    {
        if (_informe == null || Actual is not InformeMoneda m)
            return;

        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar informe de ingresos y gastos",
            Filter = "Archivo para Excel (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"Ingresos_Gastos_{_informe.Desde:yyyyMMdd}_{_informe.Hasta:yyyyMMdd}_{m.Moneda}.csv",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialogo.ShowDialog() != true)
            return;

        try
        {
            var sep = ";";
            var num = CultureInfo.CurrentCulture;
            var sb = new StringBuilder();

            string C(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

            sb.AppendLine($"Informe de Ingresos y Gastos{sep}{C($"{_informe.Desde:dd/MM/yyyy} al {_informe.Hasta:dd/MM/yyyy}")}{sep}{C(m.Moneda)}");
            sb.AppendLine();
            sb.AppendLine($"Tipo{sep}Concepto o categoría{sep}Cantidad{sep}Total");
            foreach (var i in m.Ingresos)
                sb.AppendLine($"Ingreso{sep}{C(i.Concepto)}{sep}{i.Cantidad}{sep}{i.Total.ToString("0.00", num)}");
            foreach (var g in m.Egresos)
                sb.AppendLine($"Egreso{sep}{C(g.Categoria)}{sep}{g.Cantidad}{sep}{g.Total.ToString("0.00", num)}");
            sb.AppendLine();
            sb.AppendLine($"Total ingresos{sep}{sep}{sep}{m.TotalIngresos.ToString("0.00", num)}");
            sb.AppendLine($"Total egresos{sep}{sep}{sep}{m.TotalEgresos.ToString("0.00", num)}");
            sb.AppendLine($"Saldo{sep}{sep}{sep}{m.Saldo.ToString("0.00", num)}");

            File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true));

            Dialogo.Mostrar("Archivo exportado. Ábrelo con Excel.\n\nPara entregar a socios o directiva usa el PDF.",
                "Informe de ingresos y gastos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar("No se pudo exportar:\n\n" + ex.Message, "Mercadex",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Monto(decimal v, string moneda) => $"{v.ToString("N2", Bo)} {moneda}";
}
