using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class DashboardView : UserControl
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private DashboardRespuesta? _datos;
    private bool _cargando;
    private bool? _compacto;

    public DashboardView()
    {
        InitializeComponent();

        TxtFecha.Text = Capitalizar(DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy", Bo));

        Loaded += DashboardView_Loaded;
        SizeChanged += (_, _) => AjustarDisposicion();
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DashboardView_Loaded;
        AjustarDisposicion();
        await CargarDatosAsync();
    }

    private async void Actualizar_Click(object sender, RoutedEventArgs e)
    {
        await CargarDatosAsync();
    }

    private void Periodo_Checked(object sender, RoutedEventArgs e)
    {
        if (_datos != null)
            MostrarRecaudacion(_datos);
    }

    // ---------------------------------------------------------------

    private async Task CargarDatosAsync()
    {
        if (_cargando)
            return;

        _cargando = true;
        BtnActualizar.IsEnabled = false;
        TxtEstado.Text = "Consultando datos...";

        try
        {
            _datos = await Http.GetFromJsonAsync<DashboardRespuesta>("/api/dashboard")
                     ?? throw new InvalidOperationException("Respuesta vacía.");

            Mostrar(_datos);

            TxtEstado.Text = "Actualizado a las " + DateTime.Now.ToString("HH:mm");

            await MostrarRecordatorioCargoAsync();
        }
        catch (OperationCanceledException)
        {
            TxtEstado.Text = "El servidor tardó demasiado. Pulsa Actualizar datos.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo actualizar: " + ex.Message;
        }
        finally
        {
            _cargando = false;
            BtnActualizar.IsEnabled = true;
        }
    }

    // ---------------- Recordatorio del cargo mensual ----------------
    // Desde el día 5, si ninguna caseta tiene deudas de este mes, se recuerda
    // al administrador que genere la mensualidad.

    private Border? _recordatorio;

    private static readonly string[] Meses =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    private async Task MostrarRecordatorioCargoAsync()
    {
        if (_recordatorio is not null)
        {
            Contenido.Children.Remove(_recordatorio);
            _recordatorio = null;
        }

        if (!(Sesion.SesionActual.EsAdministrador || !Sesion.SesionActual.Iniciada))
            return;

        EstadoMes? e;
        try
        {
            e = await Services.ApiJson.GetAsync<EstadoMes>("/api/cobranza/estado-mes");
        }
        catch
        {
            return;
        }

        if (e is null || e.CargoGenerado || e.Dia < 5 || e.UnidadesActivas == 0)
            return;

        var aviso = UiKit.Aviso(out var texto, "aviso");
        aviso.Margin = new Thickness(0, 0, 14, 16);
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = $"Todavía no se cargó la mensualidad de {Meses[e.Mes - 1]} {e.Anio}",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#7A5200")
        });
        texto.Text = $"Hoy es {e.Dia} y ninguna caseta tiene deudas de este mes. Mientras no se carguen, no aparecen al cobrar ni en la app de los socios.";
        texto.Margin = new Thickness(0, 4, 0, 8);
        aviso.Child = null; // el texto pasa del aviso a la pila
        pila.Children.Add(texto);
        var ir = UiKit.Boton("Ir al cargo mensual", principal: true);
        ir.HorizontalAlignment = HorizontalAlignment.Left;
        ir.Click += (_, _) => Navegacion.AbrirCargoMensual();
        pila.Children.Add(ir);
        aviso.Child = pila;

        _recordatorio = aviso;
        Contenido.Children.Insert(Math.Min(1, Contenido.Children.Count), aviso);
    }

    private sealed class EstadoMes
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public int Dia { get; set; }
        public int UnidadesActivas { get; set; }
        public bool CargoGenerado { get; set; }
    }

    private void Mostrar(DashboardRespuesta d)
    {
        MostrarKpi(d);
        MostrarRecaudacion(d);
        MostrarAntiguedad(d);
        MostrarConceptos(d);
        MostrarFormas(d);
        MostrarDeudores(d);
        MostrarUltimos(d);
        MostrarResumen(d);
    }

    // ---------------------------------------------------------------
    // KPI

    private void MostrarKpi(DashboardRespuesta d)
    {
        var bob = d.Recaudacion.FirstOrDefault(r => r.Moneda == "BOB") ?? new DashboardRecaudacion { Moneda = "BOB" };
        var otras = d.Recaudacion.Where(r => r.Moneda != "BOB").ToList();

        KpiHoy.Text = Bs(bob.Hoy);
        KpiHoyNota.Text = Plural(bob.RecibosHoy, "recibo", "recibos") +
                          OtrasMonedas(otras.Select(o => (o.Moneda, o.Hoy)));

        KpiMes.Text = Bs(bob.Mes);
        KpiMesNota.Text = Plural(bob.RecibosMes, "recibo", "recibos") +
                          OtrasMonedas(otras.Select(o => (o.Moneda, o.Mes)));

        // Comparación justa: mismo tramo de días del mes anterior.
        if (bob.MesAnteriorMismoTramo > 0)
        {
            var delta = (bob.Mes - bob.MesAnteriorMismoTramo) / bob.MesAnteriorMismoTramo;
            var sube = delta >= 0;
            KpiMesDelta.Text = (sube ? "▲ " : "▼ ") + Math.Abs(delta).ToString("P0", Bo);
            KpiMesDelta.Foreground = ColoresPanel.Hex(sube ? "#006300" : "#B42318");
            KpiMesDeltaFondo.Background = ColoresPanel.Hex(sube ? "#E7F5E7" : "#FDECEC");
            KpiMesDeltaFondo.ToolTip =
                $"Frente al mismo período del mes anterior ({Bs(bob.MesAnteriorMismoTramo)}).";
            KpiMesDeltaFondo.Visibility = Visibility.Visible;
        }
        else
        {
            KpiMesDeltaFondo.Visibility = Visibility.Collapsed;
        }

        var deudaBob = d.Deuda.FirstOrDefault(x => x.Moneda == "BOB");
        KpiDeuda.Text = Bs(deudaBob?.Total ?? 0);
        KpiDeudaNota.Text =
            Plural(deudaBob?.Obligaciones ?? 0, "obligación", "obligaciones") + " en " +
            Plural(deudaBob?.Casetas ?? 0, "unidad", "unidades") +
            OtrasMonedas(d.Deuda.Where(x => x.Moneda != "BOB").Select(x => (x.Moneda, x.Total)));

        var u = d.Unidades;
        var porcentaje = u.Activas == 0 ? 0 : (double)u.AlDia / u.Activas;
        KpiAlDia.Text = porcentaje.ToString("P0", Bo);
        AlDiaLleno.Width = new GridLength(Math.Max(porcentaje, 0.0001), GridUnitType.Star);
        AlDiaVacio.Width = new GridLength(Math.Max(1 - porcentaje, 0.0001), GridUnitType.Star);
        KpiAlDiaNota.Text = $"{u.AlDia:N0} de {u.Activas:N0} activas sin deuda pendiente";
    }

    // ---------------------------------------------------------------
    // Gráfico de recaudación

    private void MostrarRecaudacion(DashboardRespuesta d)
    {
        if (Rb12.IsChecked == true)
        {
            var puntos = d.Mensual
                .Select(m =>
                {
                    var fecha = new DateTime(m.Anio, m.Mes, 1);
                    var corto = Capitalizar(fecha.ToString("MMM", Bo).TrimEnd('.'));
                    return new PuntoGrafico(
                        corto,
                        m.Total,
                        $"{Capitalizar(fecha.ToString("MMMM yyyy", Bo))}\n{Bs(m.Total)} · {Plural(m.Recibos, "recibo", "recibos")}");
                })
                .ToList();

            GraficoRecaudacion.Mostrar(puntos, EjeCorto, 1);
            TxtRecaudacionSub.Text =
                $"Total 12 meses: {Bs(d.Mensual.Sum(m => m.Total))} · cobros válidos, sin anulados";
        }
        else
        {
            var puntos = d.Diario
                .Select(x =>
                {
                    var fecha = x.Fecha.ToDateTime(TimeOnly.MinValue);
                    return new PuntoGrafico(
                        fecha.ToString("dd/MM", Bo),
                        x.Total,
                        $"{Capitalizar(fecha.ToString("dddd dd/MM", Bo))}\n{Bs(x.Total)} · {Plural(x.Recibos, "recibo", "recibos")}");
                })
                .ToList();

            GraficoRecaudacion.Mostrar(puntos, EjeCorto, 5);
            TxtRecaudacionSub.Text =
                $"Total 30 días: {Bs(d.Diario.Sum(x => x.Total))} · cobros válidos, sin anulados";
        }
    }

    // ---------------------------------------------------------------
    // Antigüedad de la deuda (estado: color + etiqueta)

    private void MostrarAntiguedad(DashboardRespuesta d)
    {
        var colores = new[] { ColoresPanel.Bueno, ColoresPanel.Aviso, ColoresPanel.Serio, ColoresPanel.Critico };

        var filas = d.Antiguedad
            .OrderBy(t => t.Orden)
            .Select(t => new FilaBarra(
                t.Tramo,
                t.Total,
                Bs(t.Total),
                colores[Math.Clamp(t.Orden, 0, 3)],
                $"{t.Tramo}: {Plural(t.Obligaciones, "obligación", "obligaciones")} · {Bs(t.Total)}"))
            .ToList();

        BarrasHorizontales.Llenar(PanelAntiguedad, filas, "No hay deuda pendiente en bolivianos.");

        var total = d.Antiguedad.Sum(t => t.Total);
        var vencida = d.Antiguedad.Where(t => t.Orden >= 2).Sum(t => t.Total);
        TxtAntiguedadNota.Text = total > 0
            ? $"{(vencida / total):P0} de la deuda tiene más de 60 días."
            : "";
    }

    // ---------------------------------------------------------------
    // Desgloses del mes

    private void MostrarConceptos(DashboardRespuesta d)
    {
        var filas = d.PorConcepto
            .Select(c => new FilaBarra(
                c.Concepto,
                c.Total,
                Bs(c.Total),
                ColoresPanel.Serie,
                $"{c.Concepto}: {Plural(c.Pagos, "pago", "pagos")} · {Bs(c.Total)}"))
            .ToList();

        BarrasHorizontales.Llenar(PanelConceptos, filas, "Todavía no hay cobros en bolivianos este mes.");
    }

    private void MostrarFormas(DashboardRespuesta d)
    {
        var total = d.FormasPago.Sum(f => f.Total);

        var filas = d.FormasPago
            .Select(f => new FilaBarra(
                NombreForma(f.Forma),
                f.Total,
                total == 0 ? Bs(f.Total) : (f.Total / total).ToString("P0", Bo),
                ColoresPanel.Serie,
                $"{NombreForma(f.Forma)}: {Plural(f.Cantidad, "pago", "pagos")} · {Bs(f.Total)}"))
            .ToList();

        BarrasHorizontales.Llenar(PanelFormas, filas, "Todavía no hay cobros en bolivianos este mes.");
    }

    private void MostrarDeudores(DashboardRespuesta d)
    {
        PanelDeudores.Children.Clear();

        if (d.TopDeudores.Count == 0)
        {
            PanelDeudores.Children.Add(Nota("Ninguna unidad tiene deuda en bolivianos."));
            return;
        }

        var posicion = 1;
        foreach (var x in d.TopDeudores)
        {
            var desde = x.Desde.ToDateTime(TimeOnly.MinValue).ToString("dd/MM/yyyy", Bo);
            var fila = FilaLista(
                $"{posicion}",
                string.IsNullOrWhiteSpace(x.Bloque) ? $"Unidad {x.Caseta}" : $"Unidad {x.Caseta} · {x.Bloque}",
                $"{Plural(x.Obligaciones, "obligación", "obligaciones")} · desde {desde}",
                Bs(x.Total));
            PanelDeudores.Children.Add(fila);
            posicion++;
        }
    }

    // ---------------------------------------------------------------
    // Actividad

    private void MostrarUltimos(DashboardRespuesta d)
    {
        PanelUltimos.Children.Clear();

        if (d.UltimosCobros.Count == 0)
        {
            PanelUltimos.Children.Add(Nota("Todavía no hay cobros registrados."));
            return;
        }

        foreach (var c in d.UltimosCobros)
        {
            var fecha = c.Fecha.ToLocalTime().ToString("dd/MM HH:mm", Bo);
            PanelUltimos.Children.Add(FilaLista(
                $"N.º {c.Numero}",
                $"Unidad {c.Caseta} · {c.Pagador}",
                fecha,
                c.Moneda == "BOB" ? Bs(c.Total) : $"{c.Moneda} {c.Total.ToString("N2", Bo)}"));
        }
    }

    private void MostrarResumen(DashboardRespuesta d)
    {
        PanelResumen.Children.Clear();

        var ingresos = d.Recaudacion.FirstOrDefault(r => r.Moneda == "BOB")?.Mes ?? 0;
        var egresos = d.EgresosMes.FirstOrDefault(e => e.Moneda == "BOB")?.Total ?? 0;
        var saldo = ingresos - egresos;

        PanelResumen.Children.Add(FilaResumen("Ingresos", Bs(ingresos), ColoresPanel.TextoPrincipal));
        PanelResumen.Children.Add(FilaResumen("Egresos", Bs(egresos), ColoresPanel.TextoPrincipal));
        PanelResumen.Children.Add(new Border { Height = 1, Background = ColoresPanel.Rejilla, Margin = new Thickness(0, 4, 0, 8) });
        PanelResumen.Children.Add(FilaResumen("Saldo", Bs(saldo),
            saldo >= 0 ? ColoresPanel.Hex("#006300") : ColoresPanel.Hex("#B42318"), true));

        PanelResumen.Children.Add(new Border { Height = 16 });
        PanelResumen.Children.Add(FilaResumen("Recibos anulados", d.AnulacionesMes.ToString("N0"), ColoresPanel.TextoPrincipal));
        PanelResumen.Children.Add(FilaResumen("Casetas activas", d.Unidades.Casetas.ToString("N0"), ColoresPanel.TextoPrincipal));
        PanelResumen.Children.Add(FilaResumen("Comedores", d.Unidades.Comedores.ToString("N0"), ColoresPanel.TextoPrincipal));
        PanelResumen.Children.Add(FilaResumen("Ambulantes", d.Unidades.Ambulantes.ToString("N0"), ColoresPanel.TextoPrincipal));
    }

    // ---------------------------------------------------------------
    // Disposición adaptable

    private void AjustarDisposicion()
    {
        var compacto = ActualWidth < 1150;

        if (_compacto == compacto)
            return;

        _compacto = compacto;

        FilaKpi.Columns = compacto ? 2 : 4;

        Colocar(Fila2, TarjetaRecaudacion, 0, 0);
        Colocar(Fila2, TarjetaAntiguedad, compacto ? 1 : 0, compacto ? 0 : 1);
        AnchoColumnas(Fila2, compacto);

        Colocar(Fila3, TarjetaConceptos, 0, 0);
        Colocar(Fila3, TarjetaFormas, compacto ? 1 : 0, compacto ? 0 : 1);
        Colocar(Fila3, TarjetaDeudores, compacto ? 2 : 0, compacto ? 0 : 2);
        AnchoColumnas(Fila3, compacto);

        Colocar(Fila4, TarjetaUltimos, 0, 0);
        Colocar(Fila4, TarjetaResumen, compacto ? 1 : 0, compacto ? 0 : 1);
        AnchoColumnas(Fila4, compacto);
    }

    private static void Colocar(Grid grid, UIElement e, int fila, int columna)
    {
        Grid.SetRow(e, fila);
        Grid.SetColumn(e, columna);
    }

    private static void AnchoColumnas(Grid grid, bool compacto)
    {
        // La primera columna conserva su proporción; las demás
        // valen 1* en modo amplio y 0 en modo compacto (apiladas).
        for (var i = 1; i < grid.ColumnDefinitions.Count; i++)
        {
            grid.ColumnDefinitions[i].Width = compacto
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
        }
    }

    // ---------------------------------------------------------------
    // Utilidades de presentación

    private static Grid FilaLista(string marca, string titulo, string detalle, string valor)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chip = new Border
        {
            Background = ColoresPanel.Pista,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 28,
            Child = new TextBlock
            {
                Text = marca,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColoresPanel.TextoSecundario,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        g.Children.Add(chip);

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock
        {
            Text = titulo,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.TextoPrincipal,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textos.Children.Add(new TextBlock
        {
            Text = detalle,
            FontSize = 12,
            Foreground = ColoresPanel.TextoTenue,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textos, 1);
        g.Children.Add(textos);

        var monto = new TextBlock
        {
            Text = valor,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.TextoPrincipal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(monto, 2);
        g.Children.Add(monto);

        return g;
    }

    private static Grid FilaResumen(string etiqueta, string valor, Brush color, bool destacado = false)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        g.Children.Add(new TextBlock
        {
            Text = etiqueta,
            FontSize = 13,
            Foreground = ColoresPanel.TextoSecundario
        });

        var v = new TextBlock
        {
            Text = valor,
            FontSize = destacado ? 16 : 13,
            FontWeight = destacado ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = color
        };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    private static TextBlock Nota(string texto) => new()
    {
        Text = texto,
        FontSize = 13,
        Foreground = ColoresPanel.TextoTenue,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 8)
    };

    private static string Bs(decimal v) => BarrasHorizontales.Bs(v);

    private static string EjeCorto(decimal v) =>
        v >= 1_000_000 ? (v / 1_000_000m).ToString("0.#", Bo) + " M"
        : v >= 1_000 ? (v / 1_000m).ToString("0.#", Bo) + " mil"
        : v.ToString("N0", Bo);

    private static string Plural(int n, string uno, string varios) =>
        $"{n:N0} {(n == 1 ? uno : varios)}";

    private static string OtrasMonedas(IEnumerable<(string Moneda, decimal Total)> otras)
    {
        var partes = otras
            .Where(o => o.Total != 0)
            .Select(o => $"{o.Moneda} {o.Total.ToString("N2", Bo)}")
            .ToList();

        return partes.Count == 0 ? "" : " · además " + string.Join(", ", partes);
    }

    private static string NombreForma(string forma) => forma switch
    {
        "Efectivo" => "Efectivo",
        "Transferencia" => "Transferencia",
        "QR" => "QR",
        _ => "Otro"
    };

    private static string Capitalizar(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Bo) + s[1..];
}
