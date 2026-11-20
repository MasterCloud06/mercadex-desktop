using Mercadex.Desktop.App.Services;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>Extracto de deuda con el formato del «Aviso de cobranza» anterior.</summary>
public sealed class AvisoCobranzaTests
{
    private static DeudaAviso D(string concepto, int numero, int mes, decimal saldo, int anio = 2024, bool abonos = false) =>
        new(concepto, numero, anio, mes, "BOB", saldo, abonos);

    [Fact]
    public void Mes_por_mes_y_en_el_orden_de_los_conceptos()
    {
        var deudas = new[]
        {
            D("VENTILADOR PASILLO", 3, 3, 10), D("APORTE A LA FEDERACION", 2, 2, 1),
            D("Aporte mensual", 1, 3, 60), D("APORTE MENSUAL", 1, 2, 60),
            D("VENTILADOR PASILLO", 3, 2, 10), D("APORTE A LA FEDERACION", 2, 3, 1)
        };

        var a = AvisoCobranza.Crear("120", "Girasol", "Luna Nancy Mery", deudas, new DateTime(2024, 6, 5, 18, 2, 0));

        var s = Assert.Single(a.Secciones);
        Assert.Equal("BS", s.Moneda);
        Assert.Equal(new[]
        {
            "APORTE MENSUAL feb/2024", "APORTE A LA FEDERACION feb/2024", "VENTILADOR PASILLO feb/2024",
            "APORTE MENSUAL mar/2024", "APORTE A LA FEDERACION mar/2024", "VENTILADOR PASILLO mar/2024"
        }, s.Lineas.Select(l => l.Servicio));
        Assert.Equal("142,00", s.Total);
        Assert.Equal("GIRASOL", a.Bloque);
        Assert.Equal("LUNA NANCY MERY", a.Socio);
        Assert.Equal("05/06/2024", a.ImpresoFecha);
        Assert.Equal("18:02", a.ImpresoHora);
    }

    [Fact]
    public void Una_deuda_con_abonos_muestra_su_saldo()
    {
        Assert.Equal("TECHO-ESCOMBRO ago/2026 (saldo)", AvisoCobranza.Servicio(D("Techo-escombro", 5, 8, 450, 2026, abonos: true)));
    }

    [Fact]
    public void Sin_periodo_va_al_final_y_solo_con_el_nombre()
    {
        var a = AvisoCobranza.Crear("26", null, "", new[] { new DeudaAviso("Multa", null, null, null, "BOB", 50), D("APORTE MENSUAL", 1, 10, 71, 2026) }, DateTime.Now);

        Assert.Equal(new[] { "APORTE MENSUAL oct/2026", "MULTA" }, a.Secciones[0].Lineas.Select(l => l.Servicio));
        Assert.Null(a.Bloque);
    }

    [Fact]
    public void Otra_moneda_va_en_su_propia_seccion()
    {
        var a = AvisoCobranza.Crear("26", "Claveles", "X", new[] { D("APORTE MENSUAL", 1, 10, 71), new DeudaAviso("ALQUILER", 9, 2024, 10, "USD", 20) }, DateTime.Now);

        Assert.Equal(new[] { "BS", "USD" }, a.Secciones.Select(x => x.Moneda));
    }
}
