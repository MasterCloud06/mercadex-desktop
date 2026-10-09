
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
}
