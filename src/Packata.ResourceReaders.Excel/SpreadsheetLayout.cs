using ExcelDataReader;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders.Excel;

internal static class SpreadsheetLayout
{
    internal static void SelectSheet(IExcelDataReader reader, string? name, int? number)
    {
        if (name is not null && number is not null)
            throw new ArgumentException("sheetName and sheetNumber are mutually exclusive.");
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number), "Sheet numbers start at one.");
        for (var index = 1; name is null ? index < (number ?? 1) : reader.Name != name; index++)
            if (!reader.NextResult()) throw new InvalidOperationException("The configured spreadsheet sheet was not found.");
    }

    internal static string[] Names(IReadOnlyList<object?> values)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return values.Select((value, index) =>
        {
            var basis = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(basis)) basis = $"column{index + 1}";
            var name = basis;
            var suffix = 2;
            while (!used.Add(name)) name = $"{basis}_{suffix++}";
            return name;
        }).ToArray();
    }

    internal static object? Option(DataFormat? format, string key) => format?.Options.GetValueOrDefault(key);
    internal static int? Number(DataFormat? format, string key) => Option(format, key) is { } value
        ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) : null;
    internal static bool? Header(DataFormat? format) => Option(format, "header") is { } value
        ? Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    internal static int[] Rows(DataFormat? format, string key)
    {
        var value = Option(format, key);
        if (value is string text) return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (value is System.Collections.IEnumerable values)
            return values.Cast<object>().Select(item => Convert.ToInt32(item, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return [];
    }

    internal static int LeadingRows(DataFormat? format)
    {
        var selection = new[] { "table", "range", "cellRange" }.FirstOrDefault(key => Option(format, key) is not null);
        if (selection is not null) throw new NotSupportedException($"Spreadsheet {selection} selection is not supported.");
        var comments = Rows(format, "commentRows");
        if (!comments.SequenceEqual(Enumerable.Range(1, comments.Length)))
            throw new NotSupportedException("Only contiguous leading commentRows are supported for spreadsheets.");
        var headers = Rows(format, "headerRows");
        if (headers.Length > 1) throw new NotSupportedException("Multi-row spreadsheet headers are unsupported.");
        if (headers.Length == 1 && headers[0] < 1) throw new ArgumentException("headerRows must be positive.");
        return Math.Max(comments.Length, headers.Length == 1 ? headers[0] - 1 : 0);
    }
}
