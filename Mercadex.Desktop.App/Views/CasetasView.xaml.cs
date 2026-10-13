
using System.Windows.Controls;
using Mercadex.Desktop.App.ViewModels;

namespace Mercadex.Desktop.App.Views;

public partial class CasetasView : UserControl
{
    private readonly CasetasViewModel _viewModel;
    private bool _cargado;

    public CasetasView()
    {
        InitializeComponent();

        // Importar el padrón: solo administradores.
        if (!Sesion.SesionActual.EsAdministrador)
            BtnImportarExcel.Visibility = System.Windows.Visibility.Collapsed;

        _viewModel = new CasetasViewModel();
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            if (_cargado)
                return;

            _cargado = true;

            await _viewModel.CargarAsync();
        };
    }

    private async void ImportarExcel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var ventana = new ImportarPadronWindow { Owner = System.Windows.Window.GetWindow(this) };
        ventana.ShowDialog();

        if (ventana.SeImporto)
            await _viewModel.CargarAsync();
    }
}
