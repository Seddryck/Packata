using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Packata.Core.Serialization.Json;

namespace Packata.Core;
public class Schema
{
    public string? Profile { get; set; }
    public List<Field> Fields { get; set; } = [];
    public FieldsMatching FieldsMatch { get; set; } = FieldsMatching.Exact;

    /// <summary>
    /// Values that when encountered in the source, should be considered as `null`, 'not present', or 'blank' values.
    /// context: Many datasets arrive with missing data values, either because a value was not collected or it never existed.\nMissing values may be indicated simply by the value being empty in other cases a special value may have been used e.g. `-`, `NaN`, `0`, `-9999` etc.\nThe `missingValues` property provides a way to indicate that these values should be interpreted as equivalent to null.\n\n`missingValues` are strings rather than being the data type of the particular field. This allows for comparison prior to casting and for fields to have missing value which are not of their type, for example a `number` field to have missing values indicated by `-`.\n\nThe default value of `missingValue` for a non-string type field is the empty string `''`. For string type fields there is no default for `missingValue` (for string fields the empty string `''` is a valid value and need not indicate null).
    /// </summary>
    /// <example>
    /// "examples": [
    ///     "{\n  \"missingValues\": [\n    \"-\",\n    \"NaN\",\n    \"\"\n  ]\n}\n",
    ///     "{\n  \"missingValues\": []\n}\n"   
    /// ]   
    /// <example>
    public List<MissingValue>? MissingValues { get; set; } = null;
}
