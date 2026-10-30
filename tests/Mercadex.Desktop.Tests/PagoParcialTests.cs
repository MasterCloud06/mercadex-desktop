using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Views;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>Cobrar: la columna «A pagar» (pagos parciales).</summary>
public sealed class PagoParcialTests
{
    private static IngresosView.FilaCobro Fila(decimal monto, decimal pagado = 0, decimal? saldo = null) =>
        new(new ObligacionIngresoModel
        {
            Id = Guid.NewGuid(),
            MonedaId = Guid.NewGuid(),
            Monto = monto,
            MontoPagado = pagado,
            Saldo = saldo ?? monto - pagado,
            FechaEmision = new DateOnly(2026, 8, 1),
            PeriodoAnio = 2026,
            PeriodoMes = 8
        }, "TECHO-ESCOMBRO", "BOB");

    [Fact]
    public void Por_defecto_se_paga_todo_el_saldo()
    {
        var f = Fila(750, pagado: 300);

        Assert.Equal(450m, f.Saldo);
        Assert.Equal(450m, f.APagar);
        Assert.False(f.EsParcial);
        Assert.Contains("Pagó 300,00 de 750,00", f.Nota);
    }

    [Theory]
    [InlineData("300", 300)]
    [InlineData("300,50", 300.50)]
    [InlineData("450", 450)]
    public void Escribir_un_monto_valido_lo_toma_y_marca_la_deuda(string texto, double esperado)
    {
        var f = Fila(750, pagado: 300);

        f.APagarTexto = texto;

        Assert.Equal((decimal)esperado, f.APagar);
        Assert.True(f.Seleccionada);
        Assert.Equal((decimal)esperado < 450m, f.EsParcial);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("451")]
    [InlineData("10,555")]
    [InlineData("abc")]
    public void Un_monto_invalido_se_descarta(string texto)
    {
        var f = Fila(750, pagado: 300);

        f.APagarTexto = texto;

        Assert.Equal(450m, f.APagar);
        Assert.False(f.Seleccionada);
    }

    [Fact]
    public void Con_una_API_anterior_el_saldo_es_el_monto()
    {
        var f = new IngresosView.FilaCobro(new ObligacionIngresoModel { Id = Guid.NewGuid(), Monto = 100, Saldo = null }, "X", "BOB");
        Assert.Equal(100m, f.Saldo);
    }
}
