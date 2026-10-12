using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Views;

namespace Mercadex.Desktop.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object? currentView;

    public string Titulo => "Mercadex";

    public MainViewModel()
    {
        CurrentView = new DashboardView();
    }

    [RelayCommand]
    private void MostrarDashboard()
    {
        CurrentView = new DashboardView();
    }

    [RelayCommand]
    private void MostrarMapa()
    {
        CurrentView = new MapaMercadoView();
    }

    [RelayCommand]
    private void MostrarCaja()
    {
        CurrentView = new CajaView();
    }

    [RelayCommand]
    private void MostrarObligaciones()
    {
        CurrentView = new ObligacionesView();
    }

    [RelayCommand]
    private void MostrarIngresos()
    {
        CurrentView = new IngresosView();
    }

    [RelayCommand]
    private void MostrarEgresos()
    {
        CurrentView = new EgresosView();
    }

    [RelayCommand]
    private void MostrarSocios()
    {
        CurrentView = new SociosView();
    }

    [RelayCommand]
    private void MostrarCasetas()
    {
        CurrentView = new CasetasView();
    }

    [RelayCommand]
    private void MostrarRecibos()
    {
        CurrentView = new RecibosView();
    }

    [RelayCommand]
    private void MostrarReportes()
    {
        CurrentView = new ReportesView();
    }

    [RelayCommand]
    private void MostrarKardex()
    {
        CurrentView = new KardexView();
    }
}