using NUnit.Framework;
using Packata.Core.Parsing;

namespace Packata.Core.Testing.Contracts;

public class ScalarSyntaxTests
{
    [TestCase("0", true, true)]
    [TestCase("+1", true, true)]
    [TestCase("-12", true, true)]
    [TestCase("001", true, true)]
    [TestCase("9223372036854775808", true, true)]
    [TestCase(".5", false, true)]
    [TestCase("+.5", false, true)]
    [TestCase("1.", false, true)]
    [TestCase("-1e+2", false, true)]
    [TestCase("1E-2", false, true)]
    [TestCase("1.25e3", false, true)]
    [TestCase("", false, false)]
    [TestCase("+", false, false)]
    [TestCase("-", false, false)]
    [TestCase(".", false, false)]
    [TestCase("+.", false, false)]
    [TestCase("1e", false, false)]
    [TestCase("1e+", false, false)]
    [TestCase("1e-", false, false)]
    [TestCase("--1", false, false)]
    [TestCase("1..2", false, false)]
    [TestCase(" 1", false, false)]
    [TestCase("1 ", false, false)]
    [TestCase("1\n", false, false)]
    [TestCase("١", false, false)]
    [TestCase("NaN", false, false)]
    [TestCase("Infinity", false, false)]
    [TestCase("1_000", false, false)]
    [TestCase("0x10", false, false)]
    public void Decimal_syntax_recognizes_complete_ASCII_literals_without_converting_codes(string text, bool integer, bool number)
    {
        Assert.That(ScalarSyntax.IsInteger(text), Is.EqualTo(integer));
        Assert.That(ScalarSyntax.IsNumber(text), Is.EqualTo(number));
    }

    [Test]
    public void Long_inputs_are_scanned_without_recursion_or_numeric_conversion()
    {
        var text = new string('9', 100_000);
        Assert.That(ScalarSyntax.IsInteger(text), Is.True);
        Assert.That(ScalarSyntax.IsNumber(text), Is.True);
        Assert.That(ScalarSyntax.IsNumber(text + "x"), Is.False);
    }
}
