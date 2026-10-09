using System.Windows.Controls;

using Mercadex.Desktop.App.ViewModels;

namespace Mercadex.Desktop.App.Views;

public partial class EgresosView : UserControl
{
    public EgresosView()
    {
        InitializeComponent();

        DataContext =
            new EgresosViewModel();
    }
}