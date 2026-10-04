namespace Packata.DataPackage;

/// <summary>Represents the Data Package v2 Table Dialect descriptor.</summary>
public class TableDialect
{
    public string Profile { get; set; } = "https://datapackage.org/profiles/1.0/tabledialect.json";

    /// <summary>Backward-compatible Packata discriminator; it is not required by Data Package v2.</summary>
    public string? Type { get; set; }

    public bool Header { get; set; } = true;
    public List<int>? HeaderRows { get; set; } = [1];
    public string? HeaderJoin { get; set; } = " ";
    public List<int>? CommentRows { get; set; }
    public string? CommentChar { get; set; }
    public string Delimiter { get; set; } = ",";
    public string LineTerminator { get; set; } = "\r\n";
    public string? QuoteChar { get; set; } = "\"";
    public bool DoubleQuote { get; set; } = true;
    public string? EscapeChar { get; set; }
    public string? NullSequence { get; set; }
    public bool SkipInitialSpace { get; set; }
    public string? Property { get; set; }
    public string? ItemType { get; set; }
    public List<string>? ItemKeys { get; set; }
    public int? SheetNumber { get; set; }
    public string? SheetName { get; set; }
    public string? Table { get; set; }
}
