using System.Windows;
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Updates;

namespace Mercadex.Desktop.App;

public partial class App : Application
{
    public static ApiClient ApiClient { get; private set; } = null!;

    public App()
    {
        ApiClient = new ApiClient();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // MERCADEX_TARIFAS_LOCAL_WINDOW
        // Inicio directo exclusivo para pruebas locales.
        // Evita ejecutar el actualizador de produccion.

        if (Environment.GetEnvironmentVariable(
                "MERCADEX_TARIFAS_DEV_MODE") == "SI")
        {
            var ventanaTarifas =
                new Mercadex.Desktop.App.Views.TarifasUnidadWindow();

            MainWindow = ventanaTarifas;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            ventanaTarifas.Show();
            return;
        }

        while (true)
        {
            DatosVersion datos;

            try
            {
                datos = await ActualizadorObligatorio.ConsultarAsync();
            }
            catch (Exception ex)
            {
                var respuesta = MessageBox.Show(
                    "No se pudo comprobar la version de Mercadex." +
                    "\n\n" + ex.Message +
                    "\n\nSe requiere conexion a Internet." +
                    "\n\n¿Deseas reintentar?\n\n" +
                    "Si seleccionas No, Mercadex se cerrara.",
                    "Verificacion de actualizaciones",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (respuesta == MessageBoxResult.Yes)
                    continue;

                Shutdown();
                return;
            }

            if (ActualizadorObligatorio.RequiereActualizacion(datos))
            {
                var ventanaActualizacion =
                    new VentanaActualizacionObligatoria(datos);

                ventanaActualizacion.ShowDialog();

                Shutdown();
                return;
            }

            var ventanaPrincipal = new MainWindow();

            MainWindow = ventanaPrincipal;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            ventanaPrincipal.Show();
            return;
        }
    }
}