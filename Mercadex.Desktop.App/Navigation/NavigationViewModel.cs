using CommunityToolkit.Mvvm.ComponentModel;

namespace Mercadex.Desktop.App.Navigation;

public partial class NavigationViewModel : ObservableObject
{
    [ObservableProperty]
    private object? currentView;
}