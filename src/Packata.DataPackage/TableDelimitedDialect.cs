namespace Packata.DataPackage;

/// <summary>Represents a delimited Table Dialect.</summary>
public class TableDelimitedDialect : TableDialect
{
    /// <summary>Packata extension indicating whether every file repeats the header.</summary>
    /// <value>
    /// Packata extension indicating whether every file repeats the header.
    /// </value>
    public bool HeaderRepeat { get; set; } = true;
}
