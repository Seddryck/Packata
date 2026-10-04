namespace Packata.DataPackage;

/// <summary>Represents a delimited Table Dialect.</summary>
public class TableDelimitedDialect : TableDialect
{
    /// <summary>Packata extension indicating whether every file repeats the header.</summary>
    public bool HeaderRepeat { get; set; } = true;
}
