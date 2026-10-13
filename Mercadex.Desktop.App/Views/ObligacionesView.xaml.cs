using System.Windows.Controls;

using Mercadex.Desktop.App.ViewModels;

namespace Mercadex.Desktop.App.Views;

public partial class ObligacionesView : UserControl
{
    private readonly ObligacionesViewModel _viewModel;

    private bool _cargado;

    public ObligacionesView()
    {
        InitializeComponent();

        // Tarifas, generación y modificación de deudas: solo administradores.
        if (!Sesion.SesionActual.EsAdministrador)
        {
            BtnTarifas.Visibility = System.Windows.Visibility.Collapsed;
            BtnGenerarObligaciones.Visibility = System.Windows.Visibility.Collapsed;
        }

        _viewModel =
            new ObligacionesViewModel();

        DataContext =
            _viewModel;

        Loaded += ObligacionesView_Loaded;
    }

    private async void ObligacionesView_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        if (_cargado)
            return;

        _cargado = true;

        await _viewModel
            .CargarCommand
            .ExecuteAsync(null);

        await _viewModel
            .CargarPasillosMasivoAsync();
    }

    private void Tarifas_Click(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        var ventana = new TarifasUnidadWindow
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        ventana.ShowDialog();
    }
}