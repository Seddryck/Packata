﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class Field
{
    /// <summary>
    /// A name for this field.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The type keyword, which identifies the class inheriting from `Field` to deserialize",
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// The format keyword, which provides a hint for the field's formatting.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// "A human-readable title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// "A text description. Markdown is encouraged.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// An example value for the field.
    /// </summary>
    public List<string> Examples { get; set; } = [];

    /// <summary>
    /// The RDF type for this field.
    /// </summary>
    public string? RdfType { get; set; }
}

/// <summary>
/// The field contains strings, that is, sequences of characters.
/// </summary>
public class StringField : Field
{ }

public abstract class NumericField : Field
{
    /// <summary>
    /// A boolean field with a default of `true`. If `true` the physical contents of this field must follow the formatting constraints already set out. If `false` the contents of this field may contain leading and/or trailing non-numeric characters (which implementors MUST therefore strip). The purpose of `bareNumber` is to allow publishers to publish numeric data that contains trailing characters such as percentages e.g. `95%` or leading characters such as currencies e.g. `€95` or `EUR 95`. Note that it is entirely up to implementors what, if anything, they do with stripped text.
    /// </summary>
    public bool BareNumber { get; set; } = true;

    /// <summary>
    /// A string whose value is used to group digits within the number. This property does not have a default value. A common value is `,` e.g. '100,000'.
    /// </summary>
    public char? GroupChar { get; set; }
}

/// <summary>
/// The field contains numbers of any kind including decimals.
/// </summary>
/// <remarks>
/// The lexical formatting follows that of decimal in [XMLSchema](https://www.w3.org/TR/xmlschema-2/#decimal): a non-empty finite-length sequence of decimal digits separated by a period as a decimal indicator. An optional leading sign is allowed. If the sign is omitted, '+' is assumed. Leading and trailing zeroes are optional. If the fractional part is zero, the period and following zero(es) can be omitted. For example: '-1.23', '12678967.543233', '+100000.00', '210'.\n\nThe following special string values are permitted (case does not need to be respected):\n  - NaN: not a number\n  - INF: positive infinity\n  - -INF: negative infinity\n\nA number `MAY` also have a trailing:\n  - exponent: this `MUST` consist of an E followed by an optional + or - sign followed by one or more decimal digits (0-9)\n  - percentage: the percentage sign: `%`. In conversion percentages should be divided by 100.\n\nIf both exponent and percentages are present the percentage `MUST` follow the exponent e.g. '53E10%' (equals 5.3).
/// </remarks>
public class NumberField : NumericField
{
    /// <summary>
    /// A string whose value is used to represent a decimal point within the number. The default value is `.`.
    /// </summary>
    public char? DecimalChar { get; set; }
}

/// <summary>
/// The field contains integers - that is whole numbers.
/// Integer values are indicated in the standard way for any valid integer.
/// </summary>
public class IntegerField : NumericField
{ }

public abstract class TemporalField : Field
{ }

/// <summary>
/// The field contains temporal date values.
/// </summary>
public class DateField : TemporalField
{ }

/// <summary>
/// The field contains temporal time values.
/// </summary>
public class TimeField : TemporalField
{ }

/// <summary>
/// The field contains temporal datetime values.
/// </summary>
public class DateTimeField : TemporalField
{ }

/// <summary>
/// A calendar year, being an integer with 4 digits. Equivalent to [gYear in XML Schema](https://www.w3.org/TR/xmlschema-2/#gYear)
/// </summary>
public class YearField : TemporalField
{ }

/// <summary>
/// A calendar year month, being an integer with 1 or 2 digits. Equivalent to [gYearMonth in XML Schema](https://www.w3.org/TR/xmlschema-2/#gYearMonth)
/// </summary>
public class YearMonthField : TemporalField
{ }


/// <summary>
/// The field contains boolean (true/false) data.
/// </summary>
public class BooleanField : Field
{ }

/// <summary>
/// The field contains data which can be parsed as a valid JSON object.
/// </summary>
public class ObjectField : Field
{ }
