using System.IO;
using Mercadex.Desktop.App.Services;
using Xunit;

namespace Mercadex.Desktop.Tests;

public sealed class PlanillaExcelTests
{
    [Fact]
    public void La_plantilla_se_lee_igual_que_se_escribio()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"padron-{Guid.NewGuid():N}.xlsx");
        try
        {
            PlanillaExcel.EscribirXlsx(ruta, "Padron",
                new[] { "Caseta", "Bloque", "Nombres" },
                new[] { new[] { "101", "CLAVELES", "Juana & Cía <prueba>" }, new[] { "C-1", "", "Ñandú" } },
                new double[] { 10, 10, 20 });

            var filas = PlanillaExcel.Leer(ruta);

            Assert.Equal(3, filas.Count);
            Assert.Equal(new[] { "Caseta", "Bloque", "Nombres" }, filas[0]);
            Assert.Equal(new[] { "101", "CLAVELES", "Juana & Cía <prueba>" }, filas[1]);
            Assert.Equal("C-1", filas[2][0]);
            Assert.Equal("Ñandú", filas[2][2]);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public void Lee_csv_con_punto_y_coma_y_comillas()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"padron-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(ruta, "Caseta;Nombres\r\n106;\"Pérez; Ana\"\r\n");
            var filas = PlanillaExcel.Leer(ruta);

            Assert.Equal(2, filas.Count);
            Assert.Equal("106", filas[1][0]);
            Assert.Equal("Pérez; Ana", filas[1][1]);
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
