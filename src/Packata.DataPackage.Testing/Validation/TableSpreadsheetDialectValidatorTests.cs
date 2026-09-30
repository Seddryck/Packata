using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Validation;

namespace Packata.Core.Testing.Validation;
public class TableSpreadsheetDialectValidatorTests
{
    [Test]
    [TestCase(1, null)]
    [TestCase(null, "data")]
    public void IsValid_SheetNumberName_ReturnsTrue(int? number, string? name)
    {
        var validator = new TableSpreadsheetDialectValidator();
        var spreadsheetDialect = new TableSpreadsheetDialect() { SheetNumber = number, SheetName = name };
        Assert.That(validator.IsValid(spreadsheetDialect), Is.True);
    }

    [Test]
    [TestCase(2, "data")]
    public void IsValid_SheetNumberName_ReturnsFalse(int? number, string? name)
    {
        var validator = new TableSpreadsheetDialectValidator();
        var spreadsheetDialect = new TableSpreadsheetDialect() { SheetNumber = number, SheetName = name };
        Assert.That(validator.IsValid(spreadsheetDialect), Is.False);
    }
}
