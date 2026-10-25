using System.Globalization;
using Newtonsoft.Json;
using Spectre.Console;

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
                var table = new Table().Border(TableBorder.Rounded);
                foreach (var name in sample.Columns) table.AddColumn(new TableColumn(new Text(Cell(name))));
                foreach (var row in sample.Rows) table.AddRow(row.Select(value => new Text(Cell(value is null ? "(null)" : Display(value)))).ToArray());
                (console ?? AnsiConsole.Console).Write(table);
                writer.WriteLine($"{sample.Rows.Count} row(s).");
                break;
        }
    }

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
