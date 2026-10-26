using NUnit.Framework;

namespace Packata.Rules.Sql.Testing;

public class SqlPlaceholderTests
{
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static string Bind(string query, SqlDialect dialect = SqlDialect.Ansi)
        => SqlPlaceholderBinder.Bind(query, dialect, ["schema", "Order\"items"], ["select"], Quote);

    [Test]
    public void QuotesEachPhysicalSegmentIncludingReservedWordsAndClosingDelimiters()
        => Assert.That(Bind("SELECT {property} FROM {object}"), Is.EqualTo("SELECT \"select\" FROM \"schema\".\"Order\"\"items\""));

    [TestCase(SqlDialect.Ansi)]
    [TestCase(SqlDialect.Sqlite)]
    [TestCase(SqlDialect.PostgreSql)]
    [TestCase(SqlDialect.SqlServer)]
    [TestCase(SqlDialect.MySql)]
    [TestCase(SqlDialect.DuckDb)]
    public void LeavesLiteralsIdentifiersAndCommentsUnchanged(SqlDialect dialect)
    {
        const string query = "SELECT '{object}''{property}', \"{object}\" /* {object} */ FROM {object} -- {property}\nWHERE {property} > @minimum";
        Assert.That(Bind(query, dialect), Is.EqualTo("SELECT '{object}''{property}', \"{object}\" /* {object} */ FROM \"schema\".\"Order\"\"items\" -- {property}\nWHERE \"select\" > @minimum"));
    }

    [Test]
    public void PreservesDialectSpecificQuotedContentAndParameters()
    {
        Assert.That(Bind("SELECT $tag${object}$tag$, $$ {property} $$, $1 FROM {object}", SqlDialect.PostgreSql),
            Is.EqualTo("SELECT $tag${object}$tag$, $$ {property} $$, $1 FROM \"schema\".\"Order\"\"items\""));
        Assert.That(Bind("SELECT E'escaped\\\'{object}' FROM {object}", SqlDialect.PostgreSql),
            Is.EqualTo("SELECT E'escaped\\\'{object}' FROM \"schema\".\"Order\"\"items\""));
        Assert.That(Bind("SELECT [{object}]],{property}] FROM {object}", SqlDialect.SqlServer),
            Is.EqualTo("SELECT [{object}]],{property}] FROM \"schema\".\"Order\"\"items\""));
        Assert.That(Bind("SELECT `{object}` FROM {object} # {property}\n", SqlDialect.MySql),
            Is.EqualTo("SELECT `{object}` FROM \"schema\".\"Order\"\"items\" # {property}\n"));
    }

    [Test]
    public void HandlesNestedBlockCommentsWithoutReplacingTheirContents()
        => Assert.That(Bind("/* outer /* {object} */ {property} */ {object}", SqlDialect.PostgreSql),
            Is.EqualTo("/* outer /* {object} */ {property} */ \"schema\".\"Order\"\"items\""));

    [TestCase("SELECT 'unterminated")]
    [TestCase("SELECT /* unterminated")]
    public void RejectsMalformedQuotedContent(string query) => Assert.That(() => Bind(query), Throws.ArgumentException);

    [Test]
    public void MissingIdentifierBindingsAreDiagnosedOnlyForActivePlaceholders()
    {
        Assert.That(() => SqlPlaceholderBinder.Bind("SELECT {property}", SqlDialect.Ansi, ["table"], [], Quote), Throws.ArgumentException);
        Assert.That(SqlPlaceholderBinder.Bind("SELECT '{property}'", SqlDialect.Ansi, [], [], Quote), Is.EqualTo("SELECT '{property}'"));
    }
}
