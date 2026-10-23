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

        Loaded += (_, _) => PlegarMenu(Preferencias.MenuPlegado);
        BtnModoOscuro.Content = Tema.Oscuro ? "Modo claro" : "Modo oscuro";

        Navegacion.CobroSolicitado += AbrirCobro;
        Navegacion.DeudasSolicitadas += AbrirDeudas;
        Navegacion.KardexSolicitado += AbrirKardex;
        Navegacion.SocioSolicitado += AbrirSocio;
        Navegacion.CasetaSolicitada += AbrirCaseta;
        Navegacion.PagosQrSolicitados += AbrirPagosQr;
        Navegacion.CargoMensualSolicitado += AbrirCargoMensual;
        Closed += (_, _) =>
        {
            Navegacion.CobroSolicitado -= AbrirCobro;
            Navegacion.DeudasSolicitadas -= AbrirDeudas;
            Navegacion.KardexSolicitado -= AbrirKardex;
            Navegacion.SocioSolicitado -= AbrirSocio;
            Navegacion.CasetaSolicitada -= AbrirCaseta;
            Navegacion.PagosQrSolicitados -= AbrirPagosQr;
            Navegacion.CargoMensualSolicitado -= AbrirCargoMensual;
        };

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
        // Registro de cambios: administrador y consulta (directiva).
        var veRegistro = admin || Sesion.SesionActual.SoloConsulta;
        GrupoAdministracion.Visibility = veRegistro ? Visibility.Visible : Visibility.Collapsed;
        NavUsuarios.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        NavRegistroCambios.Visibility = veRegistro ? Visibility.Visible : Visibility.Collapsed;

        // Solo consulta: no cobra.
        BtnCobroRapido.Visibility = Sesion.SesionActual.PuedeCobrar ? Visibility.Visible : Visibility.Collapsed;

        // Conceptos de cobro: administrador. Pagos QR: administrador y cajero.
        NavConceptos.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        NavPagosQr.Visibility = Sesion.SesionActual.PuedeCobrar ? Visibility.Visible : Visibility.Collapsed;
        IniciarAvisoPagosQr();
    }

    // ---------------- Aviso de pagos QR de la app ----------------
    // Observación de la directiva: cuando un socio paga con QR desde la app,
    // el escritorio avisa a la cajera para que lo verifique y lo confirme.

    private System.Windows.Threading.DispatcherTimer? _relojPagosQr;
    private readonly HashSet<Guid> _pagosQrVistos = new();
    private bool _primeraConsultaQr = true;

    private void IniciarAvisoPagosQr()
    {
        _pagosQrVistos.Clear();
        _primeraConsultaQr = true;
        AvisoPagoQr.Visibility = Visibility.Collapsed;
        BadgePagosQr.Visibility = Visibility.Collapsed;

        if (!Sesion.SesionActual.PuedeCobrar)
        {
            _relojPagosQr?.Stop();
            return;
        }

        if (_relojPagosQr is null)
        {
            _relojPagosQr = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _relojPagosQr.Tick += async (_, _) => await ConsultarPagosQrAsync();
            Views.PagosQrView.PagosRevisados += () => _ = ConsultarPagosQrAsync();
            Closed += (_, _) => _relojPagosQr.Stop();
        }

        _relojPagosQr.Start();
        _ = ConsultarPagosQrAsync();
    }

    private async Task ConsultarPagosQrAsync()
    {
        if (!Sesion.SesionActual.PuedeCobrar)
            return;

        PendientesQr? r;
        try
        {
            r = await Services.ApiJson.GetAsync<PendientesQr>("/api/panel/pagos/pendientes");
        }
        catch
        {
            return; // sin conexión: se vuelve a intentar en un minuto
        }

        if (r is null)
            return;

        BadgePagosQr.Visibility = r.Cantidad > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtPagosQr.Text = r.Cantidad > 99 ? "99+" : r.Cantidad.ToString();
        Title = r.Cantidad > 0 ? $"({r.Cantidad}) Mercadex" : "Mercadex";

        var nuevos = r.Ids.Where(id => !_pagosQrVistos.Contains(id)).ToList();
        foreach (var id in r.Ids)
            _pagosQrVistos.Add(id);

        if (r.Cantidad == 0)
        {
            AvisoPagoQr.Visibility = Visibility.Collapsed;
            return;
        }

        if (nuevos.Count == 0 || DataContext is ViewModels.MainViewModel { CurrentView: Views.PagosQrView })
            return;

        TituloAvisoPagoQr.Text = _primeraConsultaQr
            ? "Pagos QR por revisar"
            : nuevos.Count == 1 ? "Nuevo pago QR" : $"{nuevos.Count} pagos QR nuevos";
        TxtAvisoPagoQr.Text = (r.Cantidad == 1
                ? "Un socio pagó con QR desde la app."
                : $"Hay {r.Cantidad} pagos con QR desde la app esperando revisión.") +
            " Verifica en la banca que el dinero llegó y confírmalo para emitir el recibo.";
        AvisoPagoQr.Visibility = Visibility.Visible;
        _primeraConsultaQr = false;

        System.Media.SystemSounds.Asterisk.Play();
        if (WindowState == WindowState.Minimized || !IsActive)
            FlashWindow();
    }

    private void FlashWindow()
    {
        try
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
                hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                dwFlags = 3 | 12, // bandeja y título, hasta que se active la ventana
                uCount = 5
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            // Solo es un aviso visual adicional.
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void AvisoPagoQr_Cerrar(object sender, RoutedEventArgs e) =>
        AvisoPagoQr.Visibility = Visibility.Collapsed;

    private void AvisoPagoQr_Revisar(object sender, RoutedEventArgs e) => AbrirPagosQr();

    private void AbrirCargoMensual()
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.CobranzaView();
        vista.MostrarCargoMensual();
        NavObligaciones.IsChecked = true;
        vm.CurrentView = vista;
    }

    private void AbrirPagosQr()
    {
        AvisoPagoQr.Visibility = Visibility.Collapsed;
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        NavPagosQr.IsChecked = true;
        vm.CurrentView = new Views.PagosQrView();
    }

    private sealed class PendientesQr
    {
        public int Cantidad { get; set; }
        public List<Guid> Ids { get; set; } = new();
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

    private void ModoOscuro_Click(object sender, RoutedEventArgs e)
    {
        var nuevo = !Tema.Oscuro;
        var respuesta = Dialogo.Mostrar(this,
            $"Mercadex se volverá a abrir en {(nuevo ? "modo oscuro" : "modo claro")}. Lo que no hayas guardado se pierde. ¿Continuar?",
            nuevo ? "Modo oscuro" : "Modo claro", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (respuesta != MessageBoxResult.Yes)
            return;

        Preferencias.ModoOscuro = nuevo;

        var ruta = Environment.ProcessPath;
        if (string.IsNullOrEmpty(ruta))
        {
            Dialogo.Mostrar(this, "Se aplicará la próxima vez que abras Mercadex.",
                "Mercadex", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = false });
        Application.Current.Shutdown();
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

    private void AbrirDeudas(string codigo)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.CobranzaView();
        vista.FiltrarCaseta(codigo);
        NavObligaciones.IsChecked = true;
        vm.CurrentView = vista;
    }

    private void AbrirKardex(Guid unidadId)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.KardexView();
        vista.SeleccionarAlCargar(unidadId);
        NavKardex.IsChecked = true;
        vm.CurrentView = vista;
    }

    private void AbrirSocio(Guid socioId)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.SociosView();
        vista.SeleccionarAlCargar(socioId);
        NavSocios.IsChecked = true;
        vm.CurrentView = vista;
    }

    private void AbrirCaseta(Guid unidadId)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;

        var vista = new Views.CasetasView();
        vista.SeleccionarAlCargar(unidadId);
        NavCasetas.IsChecked = true;
        vm.CurrentView = vista;
    }

    // ---------------- Cobro rápido (Ctrl + K) ----------------

    // ---------------- Menú plegable y actualizar la pantalla ----------------

    private bool _menuPlegado;

    private void Plegar_Click(object sender, RoutedEventArgs e)
    {
        PlegarMenu(!_menuPlegado);
        Preferencias.MenuPlegado = _menuPlegado;
    }

    private void Recargar_Click(object sender, RoutedEventArgs e) => RecargarPantalla();

    /// <summary>Vuelve a abrir la pantalla actual para traer los datos de nuevo.</summary>
    private void RecargarPantalla()
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.RecargarVista();
    }

    /// <summary>
    /// Menú plegado: solo los íconos (con el nombre al pasar el mouse).
    /// Desplegado: íconos y nombres, como siempre.
    /// </summary>
    private void PlegarMenu(bool plegado)
    {
        _menuPlegado = plegado;
        ColumnaMenu.Width = new GridLength(plegado ? 64 : 224);
        MarcaTexto.Visibility = plegado ? Visibility.Collapsed : Visibility.Visible;
        PieMenu.Visibility = plegado ? Visibility.Collapsed : Visibility.Visible;
        // Plegado: solo queda el botón para volver a mostrar el menú, centrado y bien visible.
        // "Actualizar" sigue con F5 y con el botón de cada pantalla.
        BarraMenu.Margin = plegado ? new Thickness(0, 4, 0, 6) : new Thickness(10, 0, 10, 4);
        BarraMenu.HorizontalAlignment = plegado ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        BtnRecargar.Visibility = plegado ? Visibility.Collapsed : Visibility.Visible;
        TxtPlegar.Visibility = plegado ? Visibility.Collapsed : Visibility.Visible;
        IconoPlegar.Text = plegado ? "\uE76C" : "\uE76B";
        IconoPlegar.Width = plegado ? double.NaN : 20;
        IconoPlegar.FontSize = plegado ? 15 : 13;
        BtnPlegar.Width = plegado ? 40 : double.NaN;
        BtnPlegar.Height = plegado ? 36 : 30;
        BtnPlegar.Padding = plegado ? new Thickness(0) : new Thickness(6, 0, 6, 0);
        BtnPlegar.HorizontalContentAlignment = HorizontalAlignment.Center;
        BtnPlegar.Background = plegado ? (Brush)new BrushConverter().ConvertFrom("#24496C")! : Brushes.Transparent;
        BtnPlegar.Foreground = plegado ? Brushes.White : (Brush)new BrushConverter().ConvertFrom("#B9CCDD")!;
        BtnPlegar.ToolTip = plegado ? "Mostrar el menú completo" : "Esconder el menú para tener más espacio";

        // Cobro rápido: en plegado queda como un cuadro amarillo con el ícono al centro.
        BtnCobroRapido.Margin = plegado ? new Thickness(12, 4, 12, 10) : new Thickness(14, 4, 14, 10);
        BtnCobroRapido.Padding = plegado ? new Thickness(0) : new Thickness(12, 0, 12, 0);
        BtnCobroRapido.HorizontalContentAlignment = plegado ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;

        if (MenuScroll.Content is not Panel menu)
            return;

        foreach (var hijo in menu.Children.OfType<FrameworkElement>())
        {
            switch (hijo)
            {
                case TextBlock grupo:
                    // Títulos de grupo (Socios y casetas, Cobranza…). Se achican en vez de
                    // ocultarse para no pisar lo que se oculta según el rol del usuario.
                    grupo.Height = plegado ? 0 : double.NaN;
                    grupo.Margin = plegado ? new Thickness(0, 6, 0, 0) : (Thickness)grupo.GetValue(MarginProperty);
                    if (!plegado) grupo.ClearValue(MarginProperty);
                    break;

                case ContentControl opcion when opcion.Content is Panel contenido:
                    var nombre = "";
                    foreach (var parte in contenido.Children.OfType<FrameworkElement>())
                    {
                        var esIcono = parte is TextBlock t && t.FontFamily.Source.Contains("MDL2");
                        if (parte is TextBlock texto && !esIcono && !string.IsNullOrEmpty(texto.Text) && texto.Text != "Ctrl K")
                            nombre = texto.Text;
                        if (!esIcono && parte is TextBlock)
                            parte.Visibility = plegado ? Visibility.Collapsed : Visibility.Visible;
                    }

                    if (opcion.Tag is null && nombre.Length > 0)
                        opcion.Tag = opcion.ToolTip ?? "";
                    opcion.ToolTip = plegado ? nombre : (opcion.Tag as string is { Length: > 0 } original ? original : null);
                    break;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            RecargarPantalla();
            return;
        }

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