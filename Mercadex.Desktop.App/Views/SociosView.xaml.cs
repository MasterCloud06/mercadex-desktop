using System.Windows.Controls;

using Mercadex.Desktop.App.ViewModels;

namespace Mercadex.Desktop.App.Views;

public partial class SociosView : UserControl
{
    private readonly SociosViewModel _viewModel;

    private bool _cargado;

    public SociosView()
    {
        InitializeComponent();

        _viewModel =
            new SociosViewModel();

        DataContext =
            _viewModel;

        Loaded += SociosView_Loaded;
    }

    private async void SociosView_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        if (_cargado)
            return;

        _cargado = true;

        await _viewModel
            .ConsultarCommand
            .ExecuteAsync(null);
    }

    private void AccesoApp_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (TablaSocios.SelectedItem is not Mercadex.Desktop.App.Models.SocioModel socio)
        {
            System.Windows.MessageBox.Show("Selecciona primero un socio en la tabla.", "Acceso a la app",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        new AccesoAppWindow(socio.Id, socio.NombreCompleto, socio.DocumentoIdentidad)
        {
            Owner = System.Windows.Window.GetWindow(this)
        }.ShowDialog();
    }
}
