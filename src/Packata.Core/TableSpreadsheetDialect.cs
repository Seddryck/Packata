using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents the table dialect as defined by the Data Package Table Dialect profile for the type 'spreadsheet'.
/// </summary>
public class TableSpreadsheetDialect : TableDialect
{
    /// <summary>
    /// Indicates whether the table has a header row.
    /// Default is true.
    /// </summary>
    /// <example>true</example>
    public bool Header { get; set; } = true;

    /// <summary>
    /// Specifies the row numbers to use as headers.
    /// Default is undefined.
    /// </summary>
    /// <example>[1]</example>
    public List<int>? HeaderRows { get; set; } = [1];

    /// <summary>
    /// A string that specifies the joining character for headers when there are multiple header rows.
    /// Default is " ".
    /// </summary>
    /// <example>" "</example>
    public string? HeaderJoin { get; set; } = " ";

    /// <summary>
    /// Specifies that any row beginning with this one-character string, without preceeding whitespace, causes the entire line to be ignored.
    /// Default is undefined.
    /// </summary>
    /// <example>"#"</example>
    public char? CommentChar { get; set; }

    /// <summary>
    /// Specifies the row numbers that are commented.
    /// Default is undefined.
    /// </summary>
    /// <example>[2, 5]</example>
    public List<int>? CommentRows { get; set; }

    private int? _sheetNumber = null;
    /// <summary>
    /// Specifies a sheet number of a table in the spreadsheet file.
    /// Default is '1'.
    /// </summary>
    /// <example>2</example>
    public int? SheetNumber
    {
        get => _sheetNumber ?? (SheetName is null ? 1 : null);
        set => _sheetNumber = value;
    }

    /// <summary>
    /// Specifies a sheet name of a table in the spreadsheet file.
    /// Default is undefined.
    /// </summary>
    /// <example>Data</example>
    public string? SheetName { get; set; }
}
