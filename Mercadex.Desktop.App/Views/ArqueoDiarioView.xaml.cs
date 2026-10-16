using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class ArqueoDiarioView : UserControl
{
    private readonly ApiClient _api = new();

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-BO");

    private ArqueoPreliminarResponse? _datos;
    private DateOnly _fechaConsultada;
    private bool _ocupado;

    /// <summary>Caja consultada: «mia», «general» o el id de una cajera.</summary>
    private string _caja = "mia";
    private bool _llenandoCajas;

    /// <summary>Administrador y consulta pueden ver la caja de cada cajera y la general.</summary>
    private static bool VeTodasLasCajas =>
        Sesion.SesionActual.Iniciada && (Sesion.SesionActual.EsAdministrador || Sesion.SesionActual.SoloConsulta);

    public ArqueoDiarioView()
    {
        InitializeComponent();

        // El responsable del cierre es el usuario con sesión iniciada.
        if (Sesion.SesionActual.Iniciada)
        {
            TxtResponsable.Text = Sesion.SesionActual.Nombre;
            TxtResponsable.IsReadOnly = true;
            TxtResponsable.ToolTip = "Se registra a nombre del usuario que inició sesión.";
        }

        // Consulta no tiene caja propia: empieza viendo la caja general.
        if (Sesion.SesionActual.Iniciada && Sesion.SesionActual.SoloConsulta)
            _caja = "general";

        PanelCaja.Visibility = VeTodasLasCajas ? Visibility.Visible : Visibility.Collapsed;

        FechaArqueo.SelectedDate = DateTime.Today;
        HistHasta.SelectedDate = DateTime.Today;
        HistDesde.SelectedDate = DateTime.Today.AddDays(-30);

        Loaded += async (_, _) =>
        {
            if (_datos == null)
            {
                await ConsultarAsync();
                await CargarHistorialAsync();
            }
        };
    }

    // =============================================================
    // CONSULTA DEL DÍA
    // =============================================================

    private async void Consultar_Click(object sender, RoutedEventArgs e)
    {
        await ConsultarAsync();
    }

    private async Task ConsultarAsync(Guid? monedaPreferida = null)
    {
        if (_ocupado)
            return;

        if (FechaArqueo.SelectedDate is not DateTime fecha)
        {
            TxtEstado.Text = "Selecciona una fecha válida.";
            return;
        }

        _ocupado = true;
        BtnConsultar.IsEnabled = false;
        TxtEstado.Text = "Consultando la recaudación...";

        try
        {
            _fechaConsultada = DateOnly.FromDateTime(fecha);

            _datos = await _api.GetAsync<ArqueoPreliminarResponse>(
                "/api/arqueo-diario/preliminar?fecha=" +
                fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                (Sesion.SesionActual.Iniciada ? "&caja=" + Uri.EscapeDataString(_caja) : ""));

            if (_datos == null)
            {
                TxtEstado.Text = "La API no devolvió información.";
                return;
            }

            var resumen = _datos.Resumen ?? new();

            // Si no hubo cobros, igual se permite cerrar en bolivianos (total 0).
            SelectorMoneda.ItemsSource = resumen;
            SelectorMoneda.SelectedItem =
                resumen.FirstOrDefault(r => r.MonedaId == monedaPreferida)
                ?? resumen.FirstOrDefault();

            var mensaje = $"{_datos.CantidadPagosValidos} cobros válidos.";

            if (_datos.CantidadIncidencias > 0)
            {
                mensaje += $" {_datos.CantidadIncidencias} pagos requieren revisión: " +
                           string.Join(" / ", (_datos.PagosPorRevisar ?? new())
                               .Select(x => $"{x.Moneda} {x.Monto.ToString("N2", Cultura)} ({x.Cantidad})"));
            }

            if (resumen.Count == 0)
                mensaje = "No hay cobros válidos registrados en esta fecha.";

            if (Sesion.SesionActual.Iniciada && !string.IsNullOrWhiteSpace(_datos.Caja))
                mensaje = (_datos.EsCajaGeneral
                    ? "Caja general (todas las cajeras). "
                    : EsMiCaja ? "Tu caja. " : $"Caja de {_datos.CajaNombre}. ") + mensaje;

            TxtEstado.Text = mensaje;

            MostrarCajas();

            MostrarMoneda();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            TxtEstado.Text = "La función de arqueo no está publicada en el servidor.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo consultar el arqueo: " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            BtnConsultar.IsEnabled = true;
        }
    }

    /// <summary>La caja consultada es la de quien inició sesión.</summary>
    private bool EsMiCaja =>
        _caja == "mia" || (_datos?.Cajas ?? new()).Any(c => c.EsMia && c.CajeroId == _datos?.Caja);

    /// <summary>
    /// Puede registrar el cierre de lo que está viendo: su propia caja,
    /// o la caja general si es administrador. Sin sesión, como antes.
    /// </summary>
    private bool PuedeCerrarEstaCaja =>
        !Sesion.SesionActual.Iniciada ||
        string.IsNullOrWhiteSpace(_datos?.Caja) ||
        (!Sesion.SesionActual.SoloConsulta &&
         (EsMiCaja || (_datos!.EsCajaGeneral && Sesion.SesionActual.EsAdministrador)));

    private void MostrarCajas()
    {
        if (_datos == null || !VeTodasLasCajas)
        {
            TarjetaCajas.Visibility = Visibility.Collapsed;
            return;
        }

        // Selector: mi caja, caja general y la de cada cajera del día.
        _llenandoCajas = true;
        try
        {
            var opciones = new List<OpcionCaja>();
            if (!Sesion.SesionActual.SoloConsulta)
                opciones.Add(new OpcionCaja("mia", "Mi caja"));
            opciones.Add(new OpcionCaja("general", "Caja general (todo el día)"));
            opciones.AddRange(_datos.Cajas
                .Where(c => !c.EsMia || Sesion.SesionActual.SoloConsulta)
                .Select(c => new OpcionCaja(c.CajeroId, "Caja de " + c.Nombre)));

            // Si se consultó una caja que hoy no tiene movimientos, se conserva en la lista.
            if (opciones.All(o => o.Valor != _caja))
                opciones.Add(new OpcionCaja(_caja, "Caja de " + (_datos.CajaNombre ?? "cajera")));

            SelectorCaja.ItemsSource = opciones;
            SelectorCaja.SelectedItem = opciones.First(o => o.Valor == _caja);
        }
        finally
        {
            _llenandoCajas = false;
        }

        ListaCajas.Children.Clear();

        if (_datos.Cajas.Count == 0)
        {
            TarjetaCajas.Visibility = Visibility.Collapsed;
            return;
        }

        TarjetaCajas.Visibility = Visibility.Visible;

        foreach (var c in _datos.Cajas)
        {
            var totales = string.Join(" · ", (c.Totales ?? new()).Select(t => Monto(t.Total, t.Moneda)));
            var detalle = $"{Pagos(c.CantidadCobros)}" +
                          (c.CantidadEgresos > 0 ? $" · {c.CantidadEgresos} {(c.CantidadEgresos == 1 ? "egreso" : "egresos")}" : "") +
                          (totales.Length > 0 ? "\n" + totales : "");

            var estado = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = ColoresPanel.Hex(c.Cerrada ? "#E7F5E7" : "#FFF4D6"),
                Child = new TextBlock
                {
                    Text = c.Cerrada ? "Cerrada" : "Abierta",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ColoresPanel.Hex(c.Cerrada ? "#0B5A0B" : "#8A5A00")
                }
            };

            var contenido = new StackPanel();
            contenido.Children.Add(new TextBlock
            {
                Text = c.Nombre + (c.EsMia ? " (tú)" : ""),
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = ColoresPanel.Hex("#173B5C")
            });
            contenido.Children.Add(new TextBlock
            {
                Text = detalle,
                FontSize = 12,
                Foreground = ColoresPanel.Hex("#60748A"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            contenido.Children.Add(estado);

            var seleccionada = c.CajeroId == _datos.Caja;
            var boton = new Button
            {
                Content = contenido,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 10),
                MinWidth = 200,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = ColoresPanel.Hex(seleccionada ? "#E7F0F9" : "#FFFFFF"),
                BorderBrush = ColoresPanel.Hex(seleccionada ? "#1F4E79" : "#DFE6EE"),
                BorderThickness = new Thickness(seleccionada ? 2 : 1),
                ToolTip = "Ver el arqueo de esta caja"
            };

            var id = c.EsMia && !Sesion.SesionActual.SoloConsulta ? "mia" : c.CajeroId;
            boton.Click += async (_, _) =>
            {
                _caja = id;
                await ConsultarAsync(MonedaActual?.MonedaId);
            };

            ListaCajas.Children.Add(boton);
        }

        TxtAyudaCajas.Text = "Cada cajera cierra su propia caja. Haz clic en una para ver su arqueo." +
                             (_datos.SinCajera > 0
                                 ? $" Hay {Pagos(_datos.SinCajera)} sin cajera identificada (registrados sin sesión o antes de la 1.6.3): solo aparecen en la caja general."
                                 : "");
    }

    private async void Caja_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_llenandoCajas || SelectorCaja.SelectedItem is not OpcionCaja opcion || opcion.Valor == _caja)
            return;

        _caja = opcion.Valor;
        await ConsultarAsync(MonedaActual?.MonedaId);
    }

    private void Moneda_Changed(object sender, SelectionChangedEventArgs e)
    {
        MostrarMoneda();
    }

    private ArqueoMonedaResumen? MonedaActual =>
        SelectorMoneda?.SelectedItem as ArqueoMonedaResumen;

    private void MostrarMoneda()
    {
        if (TileEfectivo == null)
            return;

        var m = MonedaActual;

        if (m == null)
        {
            TileEfectivo.Text = TileQr.Text = TileOtros.Text = TileTotal.Text = "—";
            TileEfectivoN.Text = TileQrN.Text = TileOtrosN.Text = TileTotalN.Text = " ";
            TxtEsperado.Text = "Elige una fecha con movimientos para ver cuánto efectivo debe haber en caja.";
            AvisoCambios.Visibility = Visibility.Collapsed;
            PanelCerrado.Visibility = Visibility.Collapsed;
            Chip("Sin cobros", "#EEF2F7", "#5A6B7D");
            BtnCerrar.IsEnabled = false;
            BtnActa.IsEnabled = false;
            PanelEntradas.IsEnabled = false;
            TxtDiferencia.Text = "Sin calcular";
            return;
        }

        TileEfectivo.Text = Monto(m.Efectivo, m.Moneda);
        TileQr.Text = Monto(m.Qr, m.Moneda);
        TileOtros.Text = Monto(m.Otros, m.Moneda);
        TileTotal.Text = Monto(m.Total, m.Moneda);
        TileEfectivoN.Text = Pagos(m.CantidadEfectivo);
        TileQrN.Text = Pagos(m.CantidadQr);
        TileOtrosN.Text = Pagos(m.CantidadOtros);
        TileTotalN.Text = Pagos(m.Cantidad);

        TxtEsperado.Text = m.EgresosEfectivo > 0
            ? $"Efectivo que debe haber en caja: {Monto(m.EfectivoEsperado, m.Moneda)}  " +
              $"(cobros en efectivo {Monto(m.Efectivo, m.Moneda)} − gastos pagados en efectivo " +
              $"{Monto(m.EgresosEfectivo, m.Moneda)}, {m.CantidadEgresos} {(m.CantidadEgresos == 1 ? "egreso" : "egresos")})"
            : $"Efectivo que debe haber en caja: {Monto(m.EfectivoEsperado, m.Moneda)}  (no hubo gastos pagados en efectivo)";

        BtnActa.IsEnabled = true;

        if (m.Cerrado && m.Cierre != null)
        {
            var c = m.Cierre;
            Chip($"Caja cerrada · {m.Moneda}", "#E7F5E7", "#0B5A0B");

            PanelEntradas.IsEnabled = false;
            BtnCerrar.IsEnabled = false;
            PanelCerrado.Visibility = Visibility.Visible;
            TxtCerrado.Text =
                $"Cierre registrado el {c.RegistradoTexto} por {c.Responsable}.\n" +
                $"Total cerrado: {Monto(c.Total, m.Moneda)} · Efectivo contado: {Monto(c.EfectivoContado, m.Moneda)} · " +
                $"Diferencia: {Monto(c.Diferencia, m.Moneda)}" +
                (string.IsNullOrWhiteSpace(c.Observacion) ? "" : $"\nObservación: {c.Observacion}");

            TxtContado.Text = c.EfectivoContado.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            TxtResponsable.Text = c.Responsable;
            TxtObservacion.Text = c.Observacion ?? "";

            if (m.CambiosPosteriores)
            {
                AvisoCambios.Visibility = Visibility.Visible;
                TxtAvisoCambios.Text =
                    $"Hubo cobros o anulaciones después del cierre: el total cerrado fue {Monto(c.Total, m.Moneda)} " +
                    $"({Pagos(c.CantidadPagos)}) y hoy suma {Monto(m.Total, m.Moneda)} ({Pagos(m.Cantidad)}). Revíselo con Tesorería.";
            }
            else
            {
                AvisoCambios.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            Chip($"Caja abierta · {m.Moneda}", "#FFF4D6", "#8A5A00");
            PanelEntradas.IsEnabled = true;

            if (Sesion.SesionActual.Iniciada)
                TxtResponsable.Text = Sesion.SesionActual.Nombre;
            BtnCerrar.IsEnabled = PuedeCerrarEstaCaja &&
                                  (_datos?.CantidadIncidencias == 0 ||
                                   !(_datos?.PagosPorRevisar ?? new()).Any(x => x.Moneda == m.Moneda));
            PanelEntradas.IsEnabled = PuedeCerrarEstaCaja;
            PanelCerrado.Visibility = Visibility.Collapsed;
            AvisoCambios.Visibility = Visibility.Collapsed;
            TxtContado.Text = "";
            TxtObservacion.Text = "";
        }

        TxtAyudaCierre.Text = !Sesion.SesionActual.Iniciada || string.IsNullOrWhiteSpace(_datos?.Caja)
            ? "Cuenta el efectivo de caja, escríbelo y registra el cierre. Un cierre registrado no se puede modificar."
            : PuedeCerrarEstaCaja
                ? (_datos!.EsCajaGeneral
                    ? "Caja general: todos los cobros y gastos del día, de todas las cajeras. Úsala si hay una sola caja. Un cierre registrado no se puede modificar."
                    : "Tu caja: solo los cobros que registraste tú y los gastos que pagaste en efectivo. Cuenta tu efectivo, escríbelo y registra el cierre. Un cierre registrado no se puede modificar.")
                : Sesion.SesionActual.SoloConsulta
                    ? "Tu usuario es de consulta: puedes ver el arqueo, pero el cierre lo registra cada cajera."
                    : $"Esta es la caja de {_datos!.CajaNombre}. Cada cajera registra el cierre de su propia caja.";

        BtnCerrar.Content = _datos?.EsCajaGeneral == false && EsMiCaja ? "Registrar cierre de mi caja" : "Registrar cierre del día";

        ActualizarDiferencia();
    }

    private void Chip(string texto, string fondo, string color)
    {
        TxtChipEstado.Text = texto;
        ChipEstado.Background = ColoresPanel.Hex(fondo);
        TxtChipEstado.Foreground = ColoresPanel.Hex(color);
    }

    private void Contado_Changed(object sender, TextChangedEventArgs e)
    {
        ActualizarDiferencia();
    }

    private bool LeerContado(out decimal contado)
    {
        contado = 0;
        var texto = TxtContado.Text.Trim().Replace(',', '.');

        return texto.Length > 0 &&
               decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out contado) &&
               contado >= 0;
    }

    private void ActualizarDiferencia()
    {
        if (TxtDiferencia == null || TxtContado == null)
            return;

        var m = MonedaActual;

        if (m == null)
        {
            TxtDiferencia.Text = "Sin calcular";
            TxtDiferencia.Foreground = ColoresPanel.TextoPrincipal;
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtContado.Text))
        {
            TxtDiferencia.Text = $"Esperado: {Monto(m.EfectivoEsperado, m.Moneda)}";
            TxtDiferencia.Foreground = ColoresPanel.TextoPrincipal;
            return;
        }

        if (!LeerContado(out var contado))
        {
            TxtDiferencia.Text = "Importe no válido";
            TxtDiferencia.Foreground = Brushes.DarkRed;
            return;
        }

        var diferencia = contado - m.EfectivoEsperado;

        TxtDiferencia.Text = diferencia == 0
            ? "Sin diferencia"
            : (diferencia > 0 ? "Sobrante " : "Faltante ") + Monto(Math.Abs(diferencia), m.Moneda);

        TxtDiferencia.Foreground = diferencia == 0
            ? ColoresPanel.Hex("#006300")
            : ColoresPanel.Hex("#B42318");
    }

    // =============================================================
    // REGISTRAR CIERRE
    // =============================================================

    private async void Cerrar_Click(object sender, RoutedEventArgs e)
    {
        var m = MonedaActual;

        if (_ocupado || m == null || m.Cerrado)
            return;

        if (!LeerContado(out var contado))
        {
            Dialogo.Mostrar("Escribe el efectivo contado (por ejemplo 850,50).", "Cierre de caja",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtContado.Focus();
            return;
        }

        var responsable = TxtResponsable.Text.Trim();

        if (responsable.Length < 3)
        {
            Dialogo.Mostrar("Escribe el nombre del responsable del cierre.", "Cierre de caja",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtResponsable.Focus();
            return;
        }

        var diferencia = contado - m.EfectivoEsperado;
        var observacion = TxtObservacion.Text.Trim();

        if (diferencia != 0 && observacion.Length == 0)
        {
            Dialogo.Mostrar("Hay una diferencia de efectivo. Escribe una observación que la explique.",
                "Cierre de caja", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtObservacion.Focus();
            return;
        }

        var confirmar = Dialogo.Mostrar(
            $"¿Registrar el cierre de {(_datos?.EsCajaGeneral == false ? "tu caja" : "caja")} del {_fechaConsultada:dd/MM/yyyy} en {m.Moneda}?\n\n" +
            $"Efectivo: {Monto(m.Efectivo, m.Moneda)}\n" +
            $"QR: {Monto(m.Qr, m.Moneda)}\n" +
            $"Otros: {Monto(m.Otros, m.Moneda)}\n" +
            $"Total: {Monto(m.Total, m.Moneda)}\n\n" +
            (m.EgresosEfectivo > 0 ? $"Gastos en efectivo: {Monto(m.EgresosEfectivo, m.Moneda)}\n" : "") +
            $"Debe haber en caja: {Monto(m.EfectivoEsperado, m.Moneda)}\n" +
            $"Efectivo contado: {Monto(contado, m.Moneda)}\n" +
            $"Diferencia: {Monto(diferencia, m.Moneda)}\n\n" +
            "Un cierre registrado no se puede modificar.",
            "Confirmar cierre de caja",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _ocupado = true;
        BtnCerrar.IsEnabled = false;

        try
        {
            await _api.PostAsync<RegistrarCierreRequest, CierreCajaModel>(
                "/api/arqueo-diario/cierres",
                new RegistrarCierreRequest(
                    _fechaConsultada,
                    m.MonedaId,
                    contado,
                    responsable,
                    observacion.Length == 0 ? null : observacion,
                    Sesion.SesionActual.Iniciada ? _caja : null));

            _ocupado = false;

            await ConsultarAsync(m.MonedaId);
            await CargarHistorialAsync();

            if (Dialogo.Mostrar("Cierre registrado.\n\n¿Deseas imprimir o guardar el acta ahora?",
                    "Cierre de caja", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                AbrirActa();
            }
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(ex.Message, "No se pudo registrar el cierre",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            BtnCerrar.IsEnabled = true;
        }
        finally
        {
            _ocupado = false;
        }
    }

    // =============================================================
    // ACTA (Vista previa / imprimir / PDF)
    // =============================================================

    private void Acta_Click(object sender, RoutedEventArgs e) => AbrirActa();

    private void AbrirActa()
    {
        var m = MonedaActual;

        if (m == null)
            return;

        decimal? contado = null;
        if (m.Cerrado && m.Cierre != null)
            contado = m.Cierre.EfectivoContado;
        else if (LeerContado(out var c))
            contado = c;

        var acta = new ActaArqueo(
            _fechaConsultada,
            m.Moneda,
            m.Efectivo, m.CantidadEfectivo,
            m.Qr, m.CantidadQr,
            m.Otros, m.CantidadOtros,
            m.Total, m.Cantidad,
            contado,
            m.Cerrado && m.Cierre != null ? m.Cierre.Diferencia : contado - m.EfectivoEsperado,
            m.Cerrado ? m.Cierre?.Responsable : NullSiVacio(TxtResponsable.Text),
            m.Cerrado ? m.Cierre?.Observacion : NullSiVacio(TxtObservacion.Text),
            m.Cerrado,
            m.Cierre?.FechaRegistro,
            m.Cerrado && m.Cierre != null ? m.Cierre.EgresosEfectivo : m.EgresosEfectivo,
            m.Cerrado && m.Cierre != null ? m.Cierre.CantidadEgresos : m.CantidadEgresos,
            _datos == null || _datos.EsCajaGeneral ? null : _datos.CajaNombre);

        new ActaArqueoWindow(acta) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    // =============================================================
    // HISTORIAL
    // =============================================================

    private async void Historial_Click(object sender, RoutedEventArgs e)
    {
        await CargarHistorialAsync();
    }

    private async Task CargarHistorialAsync()
    {
        if (HistDesde.SelectedDate is not DateTime desde || HistHasta.SelectedDate is not DateTime hasta)
            return;

        try
        {
            var lista = await _api.GetAsync<List<CierreCajaModel>>(
                "/api/arqueo-diario/cierres?desde=" +
                desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                "&hasta=" + hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            TablaHistorial.ItemsSource = lista ?? new();
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo cargar el historial: " + ex.Message;
        }
    }

    private void Historial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnActaHistorial.IsEnabled = TablaHistorial.SelectedItem is CierreCajaModel;
    }

    private void ActaHistorial_Click(object sender, RoutedEventArgs e)
    {
        if (TablaHistorial.SelectedItem is not CierreCajaModel c)
            return;

        DateOnly.TryParse(c.Fecha, CultureInfo.InvariantCulture, out var fecha);

        var acta = new ActaArqueo(
            fecha, c.Moneda,
            c.Efectivo, c.CantidadEfectivo,
            c.Qr, c.CantidadQr,
            c.Otros, c.CantidadOtros,
            c.Total, c.CantidadPagos,
            c.EfectivoContado, c.Diferencia,
            c.Responsable, c.Observacion,
            true, c.FechaRegistro,
            c.EgresosEfectivo, c.CantidadEgresos,
            c.EsCajaGeneral ? null : c.CajaNombre);

        new ActaArqueoWindow(acta) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    // =============================================================

    private static string Monto(decimal v, string moneda) =>
        $"{v.ToString("N2", Cultura)} {moneda}";

    private static string Pagos(int n) => n == 1 ? "1 pago" : $"{n:N0} pagos";

    private static string? NullSiVacio(string texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
