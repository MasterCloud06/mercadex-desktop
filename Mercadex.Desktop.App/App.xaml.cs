using System.Windows;
using Mercadex.Desktop.App.Api;

namespace Mercadex.Desktop.App;

public partial class App : Application
{
    public static ApiClient ApiClient { get; private set; } = null!;

    public App()
    {
        ApiClient = new ApiClient();
    }
}