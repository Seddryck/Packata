namespace Packata.DataPackage;

/// <summary>Represents a database Table Dialect.</summary>
public class TableDatabaseDialect : TableDialect
{
    /// <summary>Packata extension for a database schema or namespace.</summary>
    public string? Namespace { get; set; }
}
