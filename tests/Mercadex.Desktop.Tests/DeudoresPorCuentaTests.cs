using System.IO;
using Mercadex.Desktop.App.Views.Reportes;
using Xunit;
using V = Mercadex.Desktop.App.Views.Reportes.DeudoresPorCuentaView;

namespace Mercadex.Desktop.Tests;

/// <summary>Lista de deudores solo con el número de caseta, cuenta por cuenta.</summary>
public sealed class DeudoresPorCuentaTests
{
    private static V.FilaDeudor Caseta(string codigo, params (string Concepto, string Periodo)[] deudas) => new()
    {
        Codigo = codigo,
        Deudas = deudas.Select(d => new V.DeudaDto { Concepto = d.Concepto, Periodo = d.Periodo }).ToList()
    };

    private static readonly V.Respuesta Datos = new()
    {
        Filas = new()
        {
            Caseta("9", ("TECHO-ESCOMBRO", "08/2026"), ("MENSUALIDAD", "08/2026")),
            Caseta("354", ("TECHO-ESCOMBRO", "08/2026"), ("MENSUALIDAD", "08/2026"), ("MENSUALIDAD", "09/2026")),
            Caseta("26", ("TECHO-ESCOMBRO", "08/2026")),
            Caseta("S/N", ("MENSUALIDAD", "09/2026")),
            Caseta("300", ("MENSUALIDAD", "09/2026"))
        }
    };

    [Fact]
    public void Una_cuenta_por_servicio_y_mes_con_sus_casetas()
    {
        var cuentas = V.Agrupar(Datos);

        Assert.Equal(new[] { "MENSUALIDAD|08/2026", "MENSUALIDAD|09/2026", "TECHO-ESCOMBRO|08/2026" }, cuentas.Select(c => c.Clave));
        Assert.Equal(new[] { "354", "9" }, cuentas[0].Casetas);
        Assert.Equal(new[] { "354", "300", "S/N" }, cuentas[1].Casetas);
        Assert.Equal(new[] { "354", "26", "9" }, cuentas[2].Casetas);
    }

    [Fact]
    public void Titulo_numeros_y_total_como_la_hoja_en_papel()
    {
        var techo = V.Agrupar(Datos).Single(c => c.Concepto == "TECHO-ESCOMBRO");

        Assert.Equal("DEUDORES DE TECHO-ESCOMBRO – AGOSTO 2026", techo.Titulo);
        Assert.Equal("354 - 26 - 9", techo.Numeros);
        Assert.Equal("TOTAL DE CASETAS DEUDORAS: 3", techo.Total);
    }

    [Fact]
    public void Sin_periodo_el_titulo_es_solo_el_servicio()
    {
        var c = new V.Cuenta("Multa", "", new List<string> { "5" });
        Assert.Equal("DEUDORES DE MULTA", c.Titulo);
        Assert.Equal("Multa", c.Nombre);
    }

    [Fact]
    public void Lista_de_corte_cada_local_una_vez_de_menor_a_mayor_con_cuantas_cuentas_debe()
    {
        var cuentas = V.Agrupar(Datos);

        var locales = V.Locales(cuentas);

        Assert.Equal(new[] { "9", "26", "300", "354", "S/N" }, locales.Select(l => l.Caseta));
        Assert.Equal(new[] { "9 (2)", "26", "300", "354 (3)", "S/N" }, locales.Select(V.Local));
    }

    [Fact]
    public void Lista_de_corte_servicios_arriba_y_locales_debajo()
    {
        var cuentas = V.Agrupar(Datos);

        var texto = V.Texto(cuentas);

        Assert.StartsWith("LISTA DE CORTE", texto);
        Assert.Contains("SERVICIOS: MENSUALIDAD ago/2026,  MENSUALIDAD sep/2026,  TECHO-ESCOMBRO ago/2026", texto);
        Assert.True(texto.IndexOf("SERVICIOS:", StringComparison.Ordinal) < texto.IndexOf("LOCALES:", StringComparison.Ordinal));
        Assert.Contains("TOTAL DE CASETAS: 5", texto);
    }

    [Fact]
    public void El_pdf_se_genera_aunque_haya_muchas_casetas()
    {
        var muchas = new V.Cuenta("MENSUALIDAD", "09/2026", V.Ordenar(Enumerable.Range(1, 400).Select(n => n.ToString())));
        var ruta = Path.Combine(Path.GetTempPath(), $"deudores-{Guid.NewGuid():N}.pdf");
        try
        {
            V.EscribirPdf(ruta, new[] { muchas, V.Agrupar(Datos)[0] });
            Assert.True(new FileInfo(ruta).Length > 1000);
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
