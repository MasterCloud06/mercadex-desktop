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

        // Anular egresos: solo administradores (igual que anular cobros).
        if (Sesion.SesionActual.Iniciada && !Sesion.SesionActual.EsAdministrador)
            BtnAnular.Visibility = System.Windows.Visibility.Collapsed;
    }
}