using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class ConnectorOutputTests
{
    private static ConnectorDescription Connector(string id = "sample") => new(id, "0.46.0",
        ["alias", "another-alias"], true, false, false, false, null, [], ["net8.0", "net9.0", "net10.0"],
        [], "[0.46.0,1.0.0)", "System.Data.Odbc", true, "Legacy JSON prerequisites");

    private static string Render(IReadOnlyList<ConnectorDescription> connectors, bool verbose = false)
    {
        using var output = new StringWriter();
        ConnectorOutput.Write(connectors, output, verbose);
        return output.ToString();
    }

    [Test]
    public void Table_aligns_columns_and_shows_mixed_states_diagnostics_and_counts()
    {
        var output = Render([
            Connector("available"),
            Connector("installed") with { Installed = true, DownloadAvailable = false },
            Connector("active") with { Installed = true, Active = true },
            Connector("broken-connector-with-a-long-name") with
            {
                Installed = true, Active = true, Unhealthy = true, DiagnosticCodes = ["pack-assembly-missing"]
            }
        ]);
        var lines = output.Split(Environment.NewLine);
        var rows = lines.Where(line => line.StartsWith("CONNECTOR") || line.StartsWith("available ")
            || line.StartsWith("installed ") || line.StartsWith("active ") || line.StartsWith("broken-")).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(5));
            Assert.That(rows.Select(row => row.IndexOf("0.46.0", StringComparison.Ordinal)).Skip(1).Distinct(),
                Is.EquivalentTo(new[] { rows[0].IndexOf("VERSION", StringComparison.Ordinal) }));
            Assert.That(rows[1], Does.Contain("Not installed").And.EndWith("Available"));
            Assert.That(rows[2], Does.Contain("Installed").And.EndWith("Unavailable"));
            Assert.That(rows[3], Does.Contain("Active"));
            Assert.That(rows[4], Does.Contain("Unhealthy"));
            Assert.That(lines[Array.IndexOf(lines, rows[4]) + 1], Does.Contain("pack-assembly-missing"));
            Assert.That(output, Does.Contain("4 connectors · 3 installed · 2 active"));
            Assert.That(output, Does.Not.Contain("alias").And.Not.Contain("net8.0").And.Not.Contain("listed="));
            Assert.That(output, Does.Not.Contain('\u001b'));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Shared_runtime_and_caveats_appear_once(bool verbose)
    {
        var output = Render([Connector("first"), Connector("second")], verbose);
        Assert.Multiple(() =>
        {
            Assert.That(output.Split($"Runtime: {RuntimeInformation.RuntimeIdentifier}"), Has.Length.EqualTo(2));
            Assert.That(output.Split("Load status and server connectivity are not checked by this command."), Has.Length.EqualTo(2));
            Assert.That(output.Split("Some connectors require separately installed driver dependencies."), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void Verbose_view_preserves_discovery_and_compatibility_facts_without_implying_health_checks()
    {
        var output = Render([Connector() with { Listed = false }], verbose: true);
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("sample · 0.46.0"));
            Assert.That(output, Does.Contain("1 connector · 0 installed · 0 active"));
            Assert.That(output, Does.Match(@"Listed\s+No"));
            Assert.That(output, Does.Match(@"Installation\s+Not installed"));
            Assert.That(output, Does.Match(@"Activation\s+Inactive"));
            Assert.That(output, Does.Match(@"Health\s+No issue reported \(discovery only\)"));
            Assert.That(output, Does.Match(@"Load status\s+Not inspected"));
            Assert.That(output, Does.Match(@"Download\s+Available"));
            Assert.That(output, Does.Match(@"Aliases\s+alias, another-alias"));
            Assert.That(output, Does.Contain("[0.46.0,1.0.0)"));
            Assert.That(output, Does.Contain("net8.0, net9.0, net10.0"));
            Assert.That(output, Does.Match(@"Runtimes\s+Not specified"));
            Assert.That(output, Does.Match(@"Driver\s+System.Data.Odbc"));
            Assert.That(output, Does.Contain("A compatible external vendor driver must be installed separately."));
            Assert.That(output, Does.Not.Contain("Healthy"));
        });
    }

    [Test]
    public void Verbose_view_shows_unhealthy_active_installed_versions_and_diagnostics()
    {
        var output = Render([Connector() with
        {
            Installed = true, Active = true, Unhealthy = true, RuntimeIdentifiers = ["win-x64", "win-x86"],
            DiagnosticCodes = ["pack-assembly-missing", "pack-driver-missing"]
        }], verbose: true);
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Match(@"Installation\s+Installed"));
            Assert.That(output, Does.Match(@"Activation\s+Active"));
            Assert.That(output, Does.Match(@"Health\s+Unhealthy"));
            Assert.That(output, Does.Match(@"Runtimes\s+win-x64, win-x86"));
            Assert.That(output, Does.Contain("pack-assembly-missing").And.Contain("pack-driver-missing"));
        });
    }

    [TestCase("System.Data.Odbc", "external vendor driver")]
    [TestCase("System.Data.OleDb", "external vendor driver")]
    [TestCase("Microsoft.Data.Sqlite", "managed and native driver dependencies")]
    [TestCase(null, "managed and native driver dependencies")]
    public void Verbose_view_reports_driver_prerequisites(string? driver, string expected)
        => Assert.That(Render([Connector() with { Driver = driver }], verbose: true), Does.Contain(expected));

    [TestCase(false)]
    [TestCase(true)]
    public void Empty_results_have_zero_counts(bool verbose)
        => Assert.That(Render([], verbose), Does.Contain("0 connectors · 0 installed · 0 active"));
}
