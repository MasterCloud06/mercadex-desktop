using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Mercadex.Desktop.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
            VersionTexto.Text = $"Versión {version.Major}.{version.Minor}.{version.Build}";

        Navegacion.CobroSolicitado += AbrirCobro;
        Closed += (_, _) => Navegacion.CobroSolicitado -= AbrirCobro;

        MostrarUsuario();
    }

    // ---------------- Usuario y permisos ----------------

    private void MostrarUsuario()
    {
        var nombre = Sesion.SesionActual.Nombre;

        UsuarioNombre.Text = nombre;
        UsuarioNombre.ToolTip = $"{nombre} ({Sesion.SesionActual.Usuario})";
        UsuarioRol.Text = Sesion.SesionActual.RolTexto;
        UsuarioInicial.Text = nombre.Length > 0 ? char.ToUpperInvariant(nombre[0]).ToString() : "?";

        // El administrador principal cambia su contraseña en Render.
        BtnCambiarContrasena.Visibility = Sesion.SesionActual.Principal ? Visibility.Collapsed : Visibility.Visible;

        // Usuarios: solo administradores.
        var admin = Sesion.SesionActual.EsAdministrador;
        GrupoAdministracion.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        NavUsuarios.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;

        // Solo consulta: no cobra.
        BtnCobroRapido.Visibility = Sesion.SesionActual.PuedeCobrar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CambiarContrasena_Click(object sender, RoutedEventArgs e)
    {
        var ventana = new Sesion.VentanaCambiarContrasena { Owner = this };

        if (ventana.ShowDialog() == true)
        {
            Dialogo.Mostrar(this, "Tu contraseña se cambió. Úsala la próxima vez que ingreses.",
                "Mercadex", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CerrarSesion_Click(object sender, RoutedEventArgs e)
    {
        var respuesta = Dialogo.Mostrar(this,
            "¿Cerrar tu sesión? Lo que no hayas guardado se pierde.",
            "Cerrar sesión", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (respuesta == MessageBoxResult.Yes)
            ((App)Application.Current).CerrarSesion();
    }

    // ---------------- Ir a cobrar una unidad ----------------

    private void AbrirCobro(Guid? unidadId, string codigo)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.IngresosView();
        NavIngresos.IsChecked = true;
        vm.CurrentView = vista;
        vista.PrecargarUnidad(unidadId, codigo);
    }

    // ---------------- Cobro rápido (Ctrl + K) ----------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control && Sesion.SesionActual.PuedeCobrar)
        {
            e.Handled = true;
            MostrarCobroRapido();
        }
        else if (e.Key == Key.Escape && CapaCobroRapido.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            CerrarCobroRapido();
        }
    }

    private void CobroRapido_Click(object sender, RoutedEventArgs e) => MostrarCobroRapido();

    private void MostrarCobroRapido()
    {
        TxtCobroRapido.Text = "";
        CapaCobroRapido.Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() => TxtCobroRapido.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CerrarCobroRapido() => CapaCobroRapido.Visibility = Visibility.Collapsed;

    private void TxtCobroRapido_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        var codigo = TxtCobroRapido.Text.Trim();
        if (codigo.Length == 0)
            return;

        CerrarCobroRapido();
        AbrirCobro(null, codigo);
    }

    private void CapaCobroRapido_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CerrarCobroRapido();

    // Un clic dentro de la tarjeta no la cierra.
    private void TarjetaCobroRapido_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}