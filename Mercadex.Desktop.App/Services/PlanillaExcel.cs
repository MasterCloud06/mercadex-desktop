using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Lectura y escritura mínima de planillas .xlsx (primera hoja) y .csv,
/// sin librerías externas. Suficiente para importar el padrón.
/// </summary>
public static class PlanillaExcel
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Devuelve las filas de la primera hoja como texto (fila 1 = encabezados).</summary>
    public static List<string[]> Leer(string ruta)
    {
        var extension = Path.GetExtension(ruta).ToLowerInvariant();

        return extension switch
        {
            ".xlsx" => LeerXlsx(ruta),
            ".csv" or ".txt" => LeerCsv(ruta),
            ".xls" => throw new InvalidOperationException(
                "El formato .xls (Excel antiguo) no se puede leer. En Excel use Archivo → Guardar como → Libro de Excel (.xlsx)."),
            _ => throw new InvalidOperationException("Use un archivo de Excel (.xlsx) o CSV.")
        };
    }

    // ------------------------------------------------------------------ XLSX

    private static List<string[]> LeerXlsx(string ruta)
    {
        // FileShare.ReadWrite: permite leer aunque el archivo esté abierto en Excel.
        using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(flujo, ZipArchiveMode.Read);

        var compartidos = new List<string>();
        var entradaTextos = zip.GetEntry("xl/sharedStrings.xml");
        if (entradaTextos != null)
        {
            using var s = entradaTextos.Open();
            var doc = XDocument.Load(s);
            foreach (var si in doc.Root!.Elements(Ns + "si"))
                compartidos.Add(string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)));
        }

        var rutaHoja = RutaPrimeraHoja(zip);
        var entradaHoja = zip.GetEntry(rutaHoja)
                          ?? throw new InvalidOperationException("La planilla no tiene hojas.");

        XDocument hoja;
        using (var s = entradaHoja.Open())
            hoja = XDocument.Load(s);

        var filas = new SortedDictionary<int, Dictionary<int, string>>();
        var ultimaFila = 0;

        foreach (var row in hoja.Descendants(Ns + "row"))
        {
            var numeroFila = int.TryParse((string?)row.Attribute("r"), out var nf) ? nf : ultimaFila + 1;
            ultimaFila = numeroFila;

            var celdas = new Dictionary<int, string>();
            var ultimaColumna = -1;

            foreach (var c in row.Elements(Ns + "c"))
            {
                var referencia = (string?)c.Attribute("r");
                var columna = referencia != null ? Columna(referencia) : ultimaColumna + 1;
                ultimaColumna = columna;

                var tipo = (string?)c.Attribute("t");
                string valor;

                if (tipo == "s")
                {
                    var indice = int.TryParse(c.Element(Ns + "v")?.Value, out var i) ? i : -1;
                    valor = indice >= 0 && indice < compartidos.Count ? compartidos[indice] : "";
                }
                else if (tipo == "inlineStr")
                {
                    valor = string.Concat(c.Descendants(Ns + "t").Select(t => t.Value));
                }
                else
                {
                    valor = c.Element(Ns + "v")?.Value ?? "";

                    // Números: 106 en vez de 106.0 o 1.06E2.
                    if (tipo is null or "n" &&
                        decimal.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero))
                    {
                        valor = numero.ToString("0.##########", CultureInfo.InvariantCulture);
                    }
                }

                celdas[columna] = valor.Trim();
            }

            filas[numeroFila] = celdas;
        }

        if (filas.Count == 0)
            return new List<string[]>();

        var columnas = filas.Values.Where(f => f.Count > 0).Select(f => f.Keys.Max() + 1).DefaultIfEmpty(0).Max();

        return filas.Values
            .Select(f => Enumerable.Range(0, columnas).Select(i => f.GetValueOrDefault(i, "")).ToArray())
            .ToList();
    }

    private static string RutaPrimeraHoja(ZipArchive zip)
    {
        try
        {
            var libro = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
            var idRel = (string?)libro.Descendants(Ns + "sheet").First().Attribute(NsRel + "id");
            var rels = XDocument.Load(zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
            var destino = (string?)rels.Root!.Elements(NsPkgRel + "Relationship")
                .First(r => (string?)r.Attribute("Id") == idRel)
                .Attribute("Target");

            if (!string.IsNullOrEmpty(destino))
                return destino.StartsWith('/') ? destino.TrimStart('/') : "xl/" + destino;
        }
        catch
        {
            // Si el libro no sigue la estructura habitual se usa la ruta por defecto.
        }

        return "xl/worksheets/sheet1.xml";
    }

    private static int Columna(string referencia)
    {
        var columna = 0;
        foreach (var ch in referencia)
        {
            if (!char.IsLetter(ch))
                break;
            columna = columna * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return columna - 1;
    }

    // ------------------------------------------------------------------ CSV

    private static List<string[]> LeerCsv(string ruta)
    {
        string texto;
        using (var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var lector = new StreamReader(flujo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            texto = lector.ReadToEnd();

        var primeraLinea = texto.Split('\n').FirstOrDefault() ?? "";
        var separador = primeraLinea.Count(c => c == ';') >= primeraLinea.Count(c => c == ',') ? ';' : ',';

        var filas = new List<string[]>();
        var fila = new List<string>();
        var campo = new StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];

            if (entreComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else if (c == '"') entreComillas = false;
                else campo.Append(c);
            }
            else if (c == '"') entreComillas = true;
            else if (c == separador) { fila.Add(campo.ToString().Trim()); campo.Clear(); }
            else if (c == '\n')
            {
                fila.Add(campo.ToString().Trim()); campo.Clear();
                filas.Add(fila.ToArray()); fila = new List<string>();
            }
            else if (c != '\r') campo.Append(c);
        }

        if (campo.Length > 0 || fila.Count > 0)
        {
            fila.Add(campo.ToString().Trim());
            filas.Add(fila.ToArray());
        }

        return filas;
    }

    // ------------------------------------------------------------------ Plantilla

    /// <summary>Crea un .xlsx con encabezados en negrita y filas de ejemplo.</summary>
    public static void EscribirXlsx(string ruta, string nombreHoja, string[] encabezados, IEnumerable<string[]> filas, double[] anchos, bool[]? numericas = null)
    {
        static string Celda(string referencia, string valor, int estilo) =>
            $"<c r=\"{referencia}\" t=\"inlineStr\" s=\"{estilo}\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(valor)}</t></is></c>";

        static string Letra(int i) => ((char)('A' + i)).ToString();

        var hoja = new StringBuilder();
        hoja.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        hoja.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        hoja.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        hoja.Append("<cols>");
        for (var i = 0; i < anchos.Length; i++)
            hoja.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{anchos[i].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        hoja.Append("</cols><sheetData>");

        hoja.Append("<row r=\"1\">");
        for (var i = 0; i < encabezados.Length; i++)
            hoja.Append(Celda($"{Letra(i)}1", encabezados[i], 1));
        hoja.Append("</row>");

        var numero = 2;
        foreach (var fila in filas)
        {
            hoja.Append($"<row r=\"{numero}\">");
            for (var i = 0; i < fila.Length; i++)
            {
                // Columnas de importes: número real (se puede sumar en Excel).
                if (numericas is not null && i < numericas.Length && numericas[i] &&
                    decimal.TryParse(fila[i], NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
                    hoja.Append($"<c r=\"{Letra(i)}{numero}\" s=\"2\"><v>{valor.ToString(CultureInfo.InvariantCulture)}</v></c>");
                else
                    hoja.Append(Celda($"{Letra(i)}{numero}", fila[i], 0));
            }
            hoja.Append("</row>");
            numero++;
        }

        hoja.Append("</sheetData></worksheet>");

        var partes = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>",
            ["_rels/.rels"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>",
            ["xl/workbook.xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<sheets><sheet name=\"{SecurityElement.Escape(nombreHoja)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>",
            ["xl/styles.xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"0\"/>" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F4E79\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"3\"><xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyNumberFormat=\"1\"/>" +
                "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>",
            ["xl/worksheets/sheet1.xml"] = hoja.ToString()
        };

        using var flujo = new FileStream(ruta, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(flujo, ZipArchiveMode.Create);
        foreach (var (nombre, contenido) in partes)
        {
            var entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
            using var escritor = new StreamWriter(entrada.Open(), new UTF8Encoding(false));
            escritor.Write(contenido);
        }
    }
}
