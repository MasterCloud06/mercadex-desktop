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
}