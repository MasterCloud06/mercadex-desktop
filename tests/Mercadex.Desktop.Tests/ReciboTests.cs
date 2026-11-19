using System.IO;
using Mercadex.Desktop.App.Dtos;
using Mercadex.Desktop.App.Services;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>Recibo oficial de caja como el del sistema anterior.</summary>
public sealed class ReciboTests
{
    private static (ReciboDto Recibo, PagoDto Pago) Datos(string forma = "Efectivo", string? referencia = null, string estado = "Emitido")
    {
        var pago = new PagoDto { Id = Guid.NewGuid(), FormaPago = forma, NombrePagador = "Elizabeth Camacho Castro", Referencia = referencia, TipoCambio = 1, Monto = 71 };
        var recibo = new ReciboDto
        {
            Id = Guid.NewGuid(),
            Numero = 33720,
            PagoId = pago.Id,
            FechaEmision = new DateTimeOffset(2026, 3, 31, 15, 0, 0, TimeSpan.Zero),
            Estado = estado,
            Total = 71,
            RegistradoPor = "Oliver Morales Loza",
            CargoEmisor = "Administrador",
            Detalles = new()
            {
                new() { Concepto = "Aporte mensual", Monto = 60, PeriodoAnio = 2026, PeriodoMes = 3 },
                new() { Concepto = "APORTE A LA FEDERACION", Monto = 1, PeriodoAnio = 2026, PeriodoMes = 3 },
                new() { Concepto = "VENTILADOR PASILLO", Monto = 10, PeriodoAnio = 2026, PeriodoMes = 3 }
            }
        };
        return (recibo, pago);
    }

    [Theory]
    [InlineData(71, "SETENTA Y UN 00/100 BS")]
    [InlineData(120, "CIENTO VEINTE 00/100 BS")]
    [InlineData(100, "CIEN 00/100 BS")]
    [InlineData(1, "UN 00/100 BS")]
    [InlineData(21.5, "VEINTIÚN 50/100 BS")]
    [InlineData(750.05, "SETECIENTOS CINCUENTA 05/100 BS")]
    [InlineData(1000, "MIL 00/100 BS")]
    [InlineData(15850, "QUINCE MIL OCHOCIENTOS CINCUENTA 00/100 BS")]
    [InlineData(2001001, "DOS MILLONES MIL UN 00/100 BS")]
    public void El_total_en_letras(double monto, string esperado)
    {
        Assert.Equal(esperado, FormatoRecibo.EnLetras((decimal)monto, "BOB"));
    }

    [Fact]
    public void Solo_lo_importante_y_en_orden_de_factura()
    {
        var (recibo, pago) = Datos();

        var r = FormatoRecibo.Crear(recibo, pago, "192", "Pinos", "BOB", new DateTime(2026, 6, 19, 10, 38, 0));

        Assert.Equal("RECIBO OFICIAL DE CAJA", r.Titulo);
        Assert.Equal(33720, r.Numero);
        Assert.Equal("PINOS", r.Bloque);
        Assert.Equal("ELIZABETH CAMACHO CASTRO", r.RecibimosDe);
        Assert.Equal("192", r.Caseta);
        Assert.Null(r.FormaPago);
        Assert.Null(r.TipoCambio);
        Assert.Equal(new[] { "APORTE MENSUAL MAR/2026", "APORTE A LA FEDERACION MAR/2026", "VENTILADOR PASILLO MAR/2026" }, r.Lineas.Select(l => l.Detalle));
        Assert.Equal("SETENTA Y UN 00/100 BS", r.Son);
        Assert.Equal("OLIVER MORALES LOZA", r.Emisor);
        Assert.Equal("19/06/2026", r.ImpresoFecha);
        Assert.Equal("10:38", r.ImpresoHora);
        Assert.False(r.Anulado);
    }

    [Fact]
    public void Un_pago_qr_muestra_su_transaccion()
    {
        var (recibo, pago) = Datos("QR", "TX-998");

        var r = FormatoRecibo.Crear(recibo, pago, "192", "", "BOB", DateTime.Now);

        Assert.Equal("QR   TRANSACCIÓN: TX-998", r.FormaPago);
        Assert.Null(r.Bloque);
    }

    [Fact]
    public void Anulado_y_reimpresion_siguen_visibles()
    {
        var (recibo, pago) = Datos(estado: "Anulado");
        recibo.MotivoAnulacion = "Error de cobro";

        var r = FormatoRecibo.Crear(recibo, pago, "192", "", "BOB", DateTime.Now, reimpresion: true);

        Assert.True(r.Anulado);
        Assert.Equal("RECIBO ANULADO", r.Titulo);
        Assert.Contains("Error de cobro", r.Anulacion);
        Assert.NotNull(r.Reimpresion);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(40, true)]
    public void El_pdf_se_genera_con_pocos_o_muchos_conceptos(int conceptos, bool anulado)
    {
        var (recibo, pago) = Datos("QR", "TX-1", anulado ? "Anulado" : "Emitido");
        recibo.Detalles = Enumerable.Range(1, conceptos)
            .Select(n => new DetalleReciboDto { Concepto = "MENSUALIDAD", Monto = 10, PeriodoAnio = 2026, PeriodoMes = n % 12 + 1 })
            .ToList();
        recibo.Total = conceptos * 10;

        var ruta = Path.Combine(Path.GetTempPath(), $"recibo-{Guid.NewGuid():N}.pdf");
        try
        {
            ReciboPdfService.Guardar(ruta, recibo, pago, "192", "Pinos", "BOB", reimpresion: anulado);
            Assert.True(new FileInfo(ruta).Length > 1000);
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
