using System.IO;
using Mercadex.Desktop.App.Services;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>PDF de los reportes en tabla.</summary>
public sealed class ReportePdfTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void El_pdf_se_genera_vertical_u_horizontal_y_con_cualquier_pie(bool soloPagina, bool horizontal)
    {
        var reporte = new Reporte
        {
            Titulo = "Detalle de cobros diarios",
            Filtros = "Del día 03/11/2026",
            Resumen = new() { "Total recaudado: BOB 71,00 (1 recibo)", "Efectivo: BOB 71,00" },
            PieSoloPagina = soloPagina,
            Horizontal = horizontal,
            Columnas = new() { new("Fecha", 1), new("Recibo", 0.7, Derecha: true), new("Detalle", 2.5), new("Monto Bs", 1, Derecha: true, Numero: true) },
            Filas = Enumerable.Range(1, 120)
                .Select(n => new FilaReporte(new[] { "03/11/2026", n.ToString(), "APORTE MENSUAL NOV/2026", "71,00" }))
                .Prepend(new FilaReporte(new[] { "ESTADO: VIGENTE", "", "", "" }, Grupo: true))
                .ToList()
        };

        var ruta = Path.Combine(Path.GetTempPath(), $"reporte-{Guid.NewGuid():N}.pdf");
        try
        {
            ReportePdfService.EscribirPdf(ruta, reporte);
            Assert.True(new FileInfo(ruta).Length > 1000);
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
