using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Packata.Testing;

internal sealed record ExcelTestCell(object? Value, int Style = 0, string? Formula = null, string? Type = null);

internal static class ExcelWorkbookFixture
{
    internal static MemoryStream Create(object?[][] rows, bool date1904 = false, bool merged = false)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Write(zip, "xl/workbook.xml", $"""
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><workbookPr date1904="{(date1904 ? 1 : 0)}"/><sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <numFmts count="1"><numFmt numFmtId="164" formatCode="00000"/></numFmts>
                  <fonts count="1"><font/></fonts><fills count="1"><fill/></fills><borders count="1"><border/></borders>
                  <cellStyleXfs count="1"><xf/></cellStyleXfs>
                  <cellXfs count="3"><xf numFmtId="0"/><xf numFmtId="14" applyNumberFormat="1"/><xf numFmtId="164" applyNumberFormat="1"/></cellXfs>
                </styleSheet>
                """);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var data = new XElement(ns + "sheetData");
            for (var index = 0; index < rows.Length; index++)
            {
                var row = new XElement(ns + "row", new XAttribute("r", index + 1));
                for (var column = 0; column < rows[index].Length; column++)
                {
                    row.Add(Cell(ns, rows[index][column], column, index));
                }
                data.Add(row);
            }
            var sheet = new XElement(ns + "worksheet", data);
            if (merged) sheet.Add(new XElement(ns + "mergeCells", new XAttribute("count", 1),
                new XElement(ns + "mergeCell", new XAttribute("ref", "A1:B1"))));
            Write(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        stream.Position = 0;
        return stream;
    }

    private static XElement Cell(XNamespace ns, object? value, int column, int index)
    {
        var item = value as ExcelTestCell ?? new ExcelTestCell(value);
        var cell = new XElement(ns + "c", new XAttribute("r", $"{ColumnName(column)}{index + 1}"), new XAttribute("s", item.Style));
        if (item.Formula is not null) cell.Add(new XElement(ns + "f", item.Formula));
        if (item.Type is not null)
        { cell.Add(new XAttribute("t", item.Type)); cell.Add(new XElement(ns + "v", item.Value)); }
        else if (item.Value is string text)
        { cell.Add(new XAttribute("t", "inlineStr")); cell.Add(new XElement(ns + "is", new XElement(ns + "t", text))); }
        else if (item.Value is bool flag)
        { cell.Add(new XAttribute("t", "b")); cell.Add(new XElement(ns + "v", flag ? 1 : 0)); }
        else if (item.Value is not null) cell.Add(new XElement(ns + "v", Convert.ToString(item.Value, CultureInfo.InvariantCulture)));
        return cell;
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26) name = (char)('A' + ((value - 1) % 26)) + name;
        return name;
    }

    private static void Write(ZipArchive zip, string path, string value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(value);
    }
}
