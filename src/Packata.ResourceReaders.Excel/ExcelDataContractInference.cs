using System.Text;
using ExcelDataReader;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders.Excel;

public enum ExcelHeaderMode { Auto, Present, Absent }

/// <summary>Bounds profiling of a single rectangular worksheet. Identity options match other file inferrers.</summary>
public sealed record ExcelDataContractInferenceOptions : FileDataContractInferenceOptions
{
    public string? SheetName { get; init; }
    public int? SheetNumber { get; init; }
    public ExcelHeaderMode Header { get; init; }
    public int MaximumRecords { get; init; } = 1000;
    public long MaximumBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumColumns { get; init; } = 1024;
    public int MaximumLeadingRows { get; init; } = 1000;
    /// <summary>Existing worksheet hints; unsupported ranges/tables/multi-row headers are rejected.</summary>
    public DataFormat? SourceFormat { get; init; }
}

/// <summary>Profiles bounded cell values. Does not evaluate formulas or infer contractual requiredness.</summary>
public sealed class ExcelDataContractInference
{
    private readonly ExcelDataContractInferenceOptions _options;

    public ExcelDataContractInference(ExcelDataContractInferenceOptions? options = null)
    {
        _options = options ?? new();
        if (_options.MaximumRecords <= 0 || _options.MaximumBytes <= 0 || _options.MaximumColumns <= 0
            || _options.MaximumLeadingRows < 0 || !Enum.IsDefined(_options.Header))
            throw new ArgumentOutOfRangeException(nameof(options), "Excel sampling limits and header mode must be valid.");
    }

    public DataContractInferenceResult Infer(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        using var stream = File.OpenRead(sourcePath);
        return Infer(stream, sourcePath, cancellationToken);
    }

    /// <summary>Leaves the readable seekable stream open and restores its position. No non-seekable buffering.</summary>
    public DataContractInferenceResult Infer(Stream stream, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek)
            throw new NotSupportedException("Excel inference requires a readable seekable stream.");
        if (stream.Length > _options.MaximumBytes) throw new InvalidDataException("Workbook exceeds MaximumBytes.");
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { LeaveOpen = true });
            var explicitSheet = _options.SheetName is not null || _options.SheetNumber is not null;
            SpreadsheetLayout.SelectSheet(reader, explicitSheet ? _options.SheetName : SpreadsheetLayout.Option(_options.SourceFormat, "sheetName")?.ToString(),
                explicitSheet ? _options.SheetNumber : SpreadsheetLayout.Number(_options.SourceFormat, "sheetNumber"));
            return Profile(reader, sourcePath, cancellationToken);
        }
        finally { stream.Position = position; }
    }

    private DataContractInferenceResult Profile(IExcelDataReader reader, string sourcePath, CancellationToken cancellationToken)
    {
        var diagnostics = new List<ContractInferenceDiagnostic>();
        var path = $"{sourcePath}/{reader.Name}";
        diagnostics.Add(new("EXCEL001", InferenceSeverity.Warning, path,
            "Types describe sampled cell values, not contractual guarantees. Blank observations do not establish requiredness; all inferred fields are optional."));
        diagnostics.Add(new("EXCEL002", InferenceSeverity.Warning, path,
            "ExcelDataReader supplies stored/cached values and date conversions. Formulas are not evaluated, external links are not refreshed, and cache freshness or formula/error identity cannot be established."));
        var skip = SpreadsheetLayout.LeadingRows(_options.SourceFormat);
        if (skip > _options.MaximumLeadingRows) throw new InvalidDataException("Leading rows exceed MaximumLeadingRows.");
        SkipLeadingRows(reader, skip, cancellationToken);
        var rows = Sample(reader, ref skip, cancellationToken);
        if (reader.MergeCells.Length > 0)
            diagnostics.Add(new("EXCEL003", InferenceSeverity.Warning, path,
                "Merged cells are read at their stored positions without forward filling; the sheet may not be rectangular."));
        var header = ResolveHeader(rows, path, diagnostics);
        var truncated = rows.Count > _options.MaximumRecords + (header ? 1 : 0);
        var data = rows.Skip(header ? 1 : 0).Take(_options.MaximumRecords).ToArray();
        var width = rows.Count == 0 ? 0 : rows.Max(row => LastValue(row) + 1);
        var names = SpreadsheetLayout.Names(Enumerable.Range(0, width)
            .Select(index => header ? rows[0][index] : null).ToArray());
        if (header && names.Where((name, index) => name != Convert.ToString(rows[0][index], System.Globalization.CultureInfo.InvariantCulture)).Any())
            diagnostics.Add(new("EXCEL004", InferenceSeverity.Warning, path,
                "Blank or duplicate headers were assigned deterministic column names. Reading with the inferred schema uses these names."));
        var fields = Enumerable.Range(0, width).Select(index => Column(names[index], index, data, path, diagnostics)).ToArray();
        diagnostics.Add(new("EXCEL005", truncated ? InferenceSeverity.Warning : InferenceSeverity.Information, path,
            $"Profiled {data.Length} data rows; sample {(truncated ? "truncated at MaximumRecords" : "reached worksheet end")}. Leading rows skipped: {skip}."));
        if (width == 0 || data.Length == 0)
            diagnostics.Add(new("EXCEL006", InferenceSeverity.Warning, path,
                "Empty or header-only worksheet supplies no evidence for column types."));
        var format = new DataFormat(IsXls(sourcePath) ? "xls" : "xlsx", Options: new Dictionary<string, object?>
        {
            ["sheetName"] = reader.Name, ["header"] = header,
            ["headerRows"] = header ? new[] { skip + 1 } : Array.Empty<int>(),
            ["commentRows"] = Enumerable.Range(1, skip).ToArray()
        });
        return FileInference.Create(sourcePath, _options, format, fields, diagnostics);
    }

    private static void SkipLeadingRows(IExcelDataReader reader, int skip, CancellationToken cancellationToken)
    {
        for (var index = 0; index < skip; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
        }
    }

    private List<object?[]> Sample(IExcelDataReader reader, ref int skip, CancellationToken cancellationToken)
    {
        var rows = new List<object?[]>();
        while (rows.Count < (long)_options.MaximumRecords + 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
            if (reader.FieldCount > _options.MaximumColumns)
                throw new InvalidDataException("Worksheet exceeds MaximumColumns, including formatted empty columns.");
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            if (rows.Count == 0 && LastValue(values) < 0)
            {
                if (++skip > _options.MaximumLeadingRows) throw new InvalidDataException("Leading blank rows exceed MaximumLeadingRows.");
                continue;
            }
            rows.Add(values);
        }
        return rows;
    }

    private bool ResolveHeader(List<object?[]> rows, string path, List<ContractInferenceDiagnostic> diagnostics)
    {
        if (_options.Header == ExcelHeaderMode.Present) return rows.Count > 0;
        if (_options.Header == ExcelHeaderMode.Absent) return false;
        if (SpreadsheetLayout.Header(_options.SourceFormat) is { } hint) return hint && rows.Count > 0;
        var clear = LooksLikeHeader(rows);
        diagnostics.Add(new("EXCEL007", InferenceSeverity.Warning, path,
            clear ? "Heuristic header detected from text labels followed by typed cells; override with Header/--header if needed."
                : "Header detection is ambiguous; retaining the first row as data. Set Header/--header explicitly."));
        return clear;
    }

    private static bool LooksLikeHeader(List<object?[]> rows)
    {
        var first = rows.FirstOrDefault()?.Where(NotBlank).ToArray() ?? [];
        return first.Length > 0 && first.All(value => value is string)
            && first.Distinct().Count() == first.Length && rows.Skip(1).Any(row => row.Any(IsTypedValue));
    }

    private static bool NotBlank(object? value) => value is not null and not DBNull;
    private static bool IsTypedValue(object? value) => NotBlank(value) && value is not string;

    private static DataField Column(string name, int index, object?[][] rows, string path,
        List<ContractInferenceDiagnostic> diagnostics)
    {
        var values = rows.Select(row => row[index]).Where(NotBlank).ToArray();
        var types = values.Select(value => value!.GetType()).Distinct().ToArray();
        var (logical, format) = ColumnType(types);
        if (logical is null || types.Length > 1)
            diagnostics.Add(new("EXCEL008", InferenceSeverity.Warning, $"{path}/{name}",
                types.Length == 0 ? "No nonblank cells observed; column type is unknown."
                    : "Mixed or unsupported cell types require a conservative mapping; inspect source values before relying on the inferred type."));
        if (types.Any(IsNumeric))
            diagnostics.Add(new("EXCEL009", InferenceSeverity.Information, $"{path}/{name}",
                "Numeric values retain underlying numeric semantics; display formats, leading-zero masks and decimal precision are not inferred."));
        return new(name, logical, Format: format, Required: false, Nullable: true,
            Extensions: ExtensionMetadata.For("packata.inference", new Dictionary<string, object?>
            { ["observedBlank"] = values.Length < rows.Length, ["sampleRows"] = rows.Length }));
    }

    private static (string? Type, string? Format) ColumnType(Type[] types)
    {
        if (types.Length == 0) return (null, null);
        if (types.Length == 1) return FileInference.MapType(types[0]);
        return types.All(IsNumeric) ? ("number", "fp64") : ("string", null);
    }

    private static bool IsNumeric(Type type) => type == typeof(int) || type == typeof(double);
    private static int LastValue(object?[] row) => Array.FindLastIndex(row, value => value is not null and not DBNull);
    private bool IsXls(string path) => (_options.SourceFormat?.Name?.Trim().TrimStart('.').ToLowerInvariant()
        ?? Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) == "xls";
}
