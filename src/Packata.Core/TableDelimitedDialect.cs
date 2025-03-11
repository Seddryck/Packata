using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents the table dialect as defined by the Data Package Table Dialect profile for the type 'delimited'.
/// </summary>
public class TableDelimitedDialect : TableDialect
{
    /// <summary>
    /// A character sequence to use as the field separator.
    /// Default is ','.
    /// </summary>
    /// <example>','</example>
    public char Delimiter { get; set; } = ',';

    /// <summary>
    /// A character sequence that indicates the end of a line.
    /// Default is "\r\n".
    /// </summary>
    /// <example>"\n"</example>
    public string LineTerminator { get; set; } = "\r\n";

    /// <summary>
    /// A character sequence to use as the escape character.
    /// Default is undefined.
    /// </summary>
    /// <example>'\'</example>
    public char? EscapeChar { get; set; }

    /// <summary>
    /// A character sequence to use as the quoting character.
    /// Default is '"'.
    /// </summary>
    /// <example>'"'</example>
    public char? QuoteChar { get; set; } = '"';

    /// <summary>
    /// Specifies the handling of quotes inside fields.
    /// If Double Quote is set to true, two consecutive quotes must be interpreted as one.
    /// Default is 'true'.
    /// </summary>
    /// <example>true</example>
    public bool DoubleQuote { get; set; } = true;

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
    /// A string representing a sequence to be interpreted as a null value.
    /// Default is undefined.
    /// </summary>
    /// <example>"NULL"</example>
    public string? NullSequence { get; set; }

    /// <summary>
    /// Indicates whether to skip initial spaces in field values.
    /// Default is false.
    /// </summary>
    /// <example>false</example>
    public bool SkipInitialSpace { get; set; } = false;

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
}
