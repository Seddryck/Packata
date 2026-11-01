using System.Globalization;
using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Packata.Cli;

internal static class PreviewPresenter
{
    internal static void Write(PreviewSample sample, string output, IAnsiConsole? console = null, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        switch (output)
        {
            case "json":
                CommandOutput.Json(new { schemaVersion = 1, columns = sample.Columns, rows = sample.Rows }, writer);
                break;
            case "csv":
                writer.WriteLine(string.Join(',', sample.Columns.Select(Csv)));
                foreach (var row in sample.Rows) writer.WriteLine(string.Join(',', row.Select(value => Csv(Display(value)))));
                break;
            default:
                if (sample.Columns.Length == 0) { writer.WriteLine("No columns or rows."); break; }
                WriteTables(sample, console ?? AnsiConsole.Console);
                writer.WriteLine($"{sample.Rows.Count} row(s).");
                break;
        }
    }

    private static void WriteTables(PreviewSample sample, IAnsiConsole console)
    {
        var headers = sample.Columns.Select(Cell).ToArray();
        var rows = sample.Rows.Select(row => row.Select(value => Cell(value is null or DBNull ? "(null)" : Display(value))).ToArray()).ToArray();
        var options = new RenderOptions(console.Profile.Capabilities, new Size(console.Profile.Width, console.Profile.Height));
        int Width(string value) => ((IRenderable)new Text(value)).Measure(options, int.MaxValue).Max;
        var rowWidth = Math.Max(1, sample.Rows.Count.ToString(CultureInfo.InvariantCulture).Length);
        // Borders and padding cost three cells per column, plus the final border.
        var maximumWidth = Math.Max(1, Math.Min(40, console.Profile.Width - rowWidth - 7));
        var widths = headers.Select((header, index) => Math.Clamp(
            rows.Select(row => Width(row[index])).Append(Width(header)).Max(), 1, maximumWidth)).ToArray();
        for (var start = 0; start < headers.Length;)
        {
            var end = start;
            var usedWidth = rowWidth + 4;
            do { usedWidth += widths[end++] + 3; }
            while (end < headers.Length && usedWidth + widths[end] + 3 <= console.Profile.Width);

            if (start > 0) console.WriteLine();
            if (end < headers.Length || start > 0)
                console.WriteLine($"Columns {start + 1}-{end} of {headers.Length}");
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn(new Text("#")) { Width = rowWidth, NoWrap = true, Alignment = Justify.Right });
            for (var column = start; column < end; column++)
                table.AddColumn(new TableColumn(SingleLine(headers[column])) { Width = widths[column], NoWrap = true });
            for (var row = 0; row < rows.Length; row++)
            {
                table.AddRow(new[] { new Text((row + 1).ToString(CultureInfo.InvariantCulture)) }
                    .Concat(rows[row].Skip(start).Take(end - start).Select(SingleLine)).ToArray());
            }

            console.Write(table);
            start = end;
        }
    }

    private static Text SingleLine(string value) => new(value) { Overflow = Overflow.Ellipsis };

    private static string Cell(string value)
    {
        value = value.Replace("\r", "").Replace("\n", "↵").Replace("\t", " ");
        return value.Length > 80 ? value[..77] + "..." : value;
    }

    private static string Display(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        string text => text,
        _ => JsonConvert.SerializeObject(value)
    };

    private static string Csv(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? '"' + value.Replace("\"", "\"\"") + '"' : value;
}
