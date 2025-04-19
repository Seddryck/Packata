using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Validation;

namespace Packata.Core.Testing.Validation;
public class TableDelimitedDialectValidatorTests
{
    [Test]
    [TestCase('\\', '\"', false)]
    [TestCase('\\', '\"', true)]
    [TestCase('\\', null, true)]
    public void IsValid_EscapeAndQuote_ReturnsFalse(char? escape, char? quote, bool doubleQuote)
    {
        var validator = new TableDelimitedDialectValidator();
        var tableDialect = new TableDelimitedDialect() {EscapeChar = escape, QuoteChar = quote, DoubleQuote = doubleQuote};
        Assert.That(validator.IsValid(tableDialect), Is.False);
    }

    [Test]
    [TestCase(null, '\"', false)]
    [TestCase(null, '\"', true)]
    [TestCase('\\', null, false)]
    public void IsValid_EscapeAndQuote_ReturnsTrue(char? escape, char? quote, bool doubleQuote)
    {
        var validator = new TableDelimitedDialectValidator();
        var TableDialect = new TableDelimitedDialect() { EscapeChar = escape, QuoteChar = quote, DoubleQuote = doubleQuote };
        Assert.That(validator.IsValid(TableDialect), Is.True);
    }

    [Test]
    [TestCase(true)]
    [TestCase(false, 1,2)]
    public void IsValid_IsHeaderCoherent_ReturnsFalse(bool header, params int[] headerRows)
    {
        var validator = new TableDelimitedDialectValidator();
        var tableDialect = new TableDelimitedDialect() { Header = header, HeaderRows = [.. headerRows] };
        Assert.That(validator.IsValid(tableDialect), Is.False);
    }

    [Test]
    [TestCase(true, 1, 2)]
    [TestCase(false)]
    public void IsValid_IsHeaderCoherent_ReturnsTrue(bool header, params int[] headerRows)
    {
        var validator = new TableDelimitedDialectValidator();
        var tableDialect = new TableDelimitedDialect() { Header = header, HeaderRows = [.. headerRows] };
        Assert.That(validator.IsValid(tableDialect), Is.True);
    }
}
