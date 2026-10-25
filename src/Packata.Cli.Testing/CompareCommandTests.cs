using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

public class CompareCommandTests
{
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            properties:
              - name: amount
                logicalType: integer
                required: true
                nullable: false
        """;
    private const string Package = """
        {"name":"sales","resources":[{"name":"orders","path":"must-not-open.csv","schema":{"fields":[{"name":"amount","type":"integer","constraints":{"required":true}}]}}]}
        """;

    [TestCase(Odcs)]
    [TestCase(Package)]
    public async Task IdenticalDescriptors_ProduceCompletedCompatibleJsonAndText(string content)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", content);
        var result = await CliWorkspace.Run("contract", "compare", source, source, "--output", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That((int?)json["schemaVersion"], Is.EqualTo(1));
            Assert.That((bool?)json["completed"], Is.True);
            Assert.That((bool?)json["isCompatible"], Is.True);
            Assert.That((string?)json["outcome"], Is.EqualTo("compatible"));
            Assert.That((string?)json["direction"], Is.EqualTo("baseline-to-candidate"));
            Assert.That((string?)json["policy"], Is.EqualTo(CompatibilityResult.ConsumerPolicy));
            Assert.That(json["findings"], Is.Empty);
            Assert.That(result.Error, Is.Empty);
        });
        var text = await CliWorkspace.Run("contract", "compare", source, source);
        Assert.That(text.Output, Does.Contain("Baseline (previous)").And.Contain("Candidate (current)").And.Contain("compatible").And.Contain("Totals"));
        Assert.That(text.Output.Contains('\u001b'), Is.False);
    }

    [TestCase(Odcs)]
    [TestCase(Package)]
    public async Task DirectionAndJson_AgreeWithBackendAndRemainDeterministic(string content)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", content);
        var candidate = workspace.Write("candidate", content.Replace("integer", "number"));
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        var again = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        var reverse = await CliWorkspace.Run("contract", "compare", candidate, baseline, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That(reverse.ExitCode, Is.Zero, reverse.Error + reverse.Output);
        Assert.That(again.Output, Is.EqualTo(result.Output));
        var library = ContractCompatibility.Compare(
            (await ContractSource.LoadAsync(baseline, "auto", default)).CompatibilityInput(),
            (await ContractSource.LoadAsync(candidate, "auto", default)).CompatibilityInput());
        var findings = JObject.Parse(result.Output)["findings"]!;
        Assert.That(findings.Select(x => (string?)x["path"]), Is.EqualTo(library.Findings.Select(x => x.Path.Value)));
        Assert.That((string?)findings.Single()["classification"], Is.EqualTo("breaking"));
        Assert.That((string?)findings.Single()["change"]!["kind"], Is.EqualTo("PropertyTypeChanged"));
        Assert.That(findings.Single()["sourceLocation"]!.Type, Is.EqualTo(JTokenType.Null));
        // The factual command still succeeds and does not carry a compatibility outcome.
        var diff = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output", "json");
        Assert.That(diff.ExitCode, Is.Zero);
        Assert.That(JObject.Parse(diff.Output)["outcome"], Is.Null);
    }

    [TestCase(Odcs, "required: true", "required: false")]
    [TestCase(Package, "\"required\":true", "\"required\":false")]
    [TestCase(Odcs, "nullable: false", "nullable: true")]
    public async Task WeakenedPresenceOrNullability_IsBreaking(string content, string before, string after)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", content);
        var candidate = workspace.Write("candidate", content.Replace(before, after));
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("breaking"));
        var reverse = await CliWorkspace.Run("contract", "compare", candidate, baseline, "--output", "json");
        Assert.That(reverse.ExitCode, Is.Zero, reverse.Error + reverse.Output);
    }

    [TestCase("odcs")]
    [TestCase("datapackage")]
    public async Task ConstraintBounds_AreClassifiedForBothStandards(string standard)
    {
        using var workspace = new CliWorkspace();
        var content = standard == "odcs" ? Odcs + "\n        logicalTypeOptions:\n          maximum: 10\n"
            : Package.Replace("\"required\":true", "\"required\":true,\"maximum\":10");
        var baseline = workspace.Write("baseline", content);
        var candidate = workspace.Write("candidate", content.Replace("10", "20"));
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        var json = JObject.Parse(result.Output);
        Assert.That((string?)json["outcome"], Is.EqualTo("breaking"));
        Assert.That(json["findings"]!.Select(x => (string?)x["rule"]), Does.Contain("constraint:maximum"));
    }

    [Test]
    public async Task DataPackageYaml_UsesTheSameConstraintAndNullabilitySemantics()
    {
        using var workspace = new CliWorkspace();
        const string content = """
            name: sales
            resources:
              - name: orders
                path: must-not-open.csv
                schema:
                  fields:
                    - name: amount
                      type: integer
                      constraints:
                        required: true
                        maximum: 10
            """;
        var baseline = workspace.Write("baseline.yaml", content);
        var candidate = workspace.Write("candidate.yaml", content.Replace("10", "20"));
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("breaking"));
    }

    [Test]
    public async Task BoundsDroppedByNativeMapping_AreUnknownInsteadOfFalseCompatibility()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("source", Odcs + "\n        logicalTypeOptions:\n          minimum: 999999999999999999999999999999999999999999\n");
        var result = await CliWorkspace.Run("contract", "compare", source, source, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("unknown"));
        Assert.That(JObject.Parse(result.Output)["diagnostics"]!.Select(x => (string?)x["code"]), Does.Contain("CMP020"));
    }

    [TestCase("breaking", "breaking", 4)]
    [TestCase("breaking", "none", 0)]
    [TestCase("breaking", "unknown", 0)]
    [TestCase("potentiallyBreaking", "potentially-breaking", 4)]
    [TestCase("potentiallyBreaking", "breaking", 0)]
    [TestCase("unknown", "unknown", 4)]
    [TestCase("unknown", "none", 0)]
    public async Task Gates_ChangeExitStatusWithoutChangingSemanticOutcome(string outcome, string failOn, int exitCode)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", Odcs);
        var changed = outcome switch
        {
            "breaking" => Odcs.Replace("integer", "number"),
            "potentiallyBreaking" => Odcs + "\n        physicalName: total\n",
            _ => Odcs + "\n        futureGuarantee: true\n"
        };
        var candidate = workspace.Write("candidate", changed);
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--fail-on", failOn, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Error + result.Output);
        var json = JObject.Parse(result.Output);
        Assert.That((string?)json["outcome"], Is.EqualTo(outcome));
        Assert.That((bool?)json["isCompatible"], Is.False);
        Assert.That((bool?)json["gatePassed"], Is.EqualTo(exitCode == 0));
    }

    [TestCase(Odcs, "name: amount", "name: renamed")]
    [TestCase(Package, "\"name\":\"amount\"", "\"name\":\"renamed\"")]
    [TestCase(Odcs, "name: orders", "name: renamed")]
    [TestCase(Package, "\"name\":\"orders\"", "\"name\":\"renamed\"")]
    public async Task Renames_AreAddRemovePairsWithNoInference(string content, string before, string after)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", content);
        var candidate = workspace.Write("candidate", content.Replace(before, after));
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        var changes = JObject.Parse(result.Output)["findings"]!.Where(x => x["change"]?.Type == JTokenType.Object).ToArray();
        Assert.That(changes.Select(x => (string?)x["change"]!["operation"]), Does.Contain("added").And.Contain("removed"));
    }

    [TestCase("baseline")]
    [TestCase("candidate")]
    public async Task InvalidContracts_EmitAttributedJsonFailureEnvelopes(string input)
    {
        using var workspace = new CliWorkspace();
        var valid = workspace.Write("valid", Odcs);
        var invalid = workspace.Write("invalid", "kind: Other\nschema: []\n");
        var result = await CliWorkspace.Run("contract", "compare", input == "baseline" ? invalid : valid,
            input == "candidate" ? invalid : valid, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        var json = JObject.Parse(result.Output);
        Assert.That((bool?)json["completed"], Is.False);
        Assert.That((string?)json["outcome"], Is.EqualTo("failed"));
        Assert.That(json["diagnostics"]!.All(x => (string?)x["input"] == input), Is.True);
        Assert.That(result.Error, Is.Empty);
    }

    [TestCase("odcs")]
    [TestCase("datapackage")]
    public async Task UnmappedMembersAndMissingSchemas_CannotBecomeFalseCompatibility(string standard)
    {
        using var workspace = new CliWorkspace();
        var content = standard == "odcs" ? Odcs + "\n        ignoredByParser: true\n"
            : Package.Replace("\"name\":\"amount\",", "\"name\":\"amount\",\"ignoredByParser\":true,");
        var source = workspace.Write("source", content);
        var result = await CliWorkspace.Run("contract", "compare", source, source, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        var json = JObject.Parse(result.Output);
        Assert.That((bool?)json["completed"], Is.True);
        Assert.That((string?)json["outcome"], Is.EqualTo("unknown"));
        Assert.That(json["diagnostics"]!.Select(x => (string?)x["code"]), Does.Contain("CMP020"));
        Assert.That(json["diagnostics"]!.Select(x => (string?)x["sourcePath"]), Has.Some.EndsWith("ignoredByParser"));
        if (standard == "datapackage")
        {
            source = workspace.Write("no-schema", CliWorkspace.PackageJson);
            result = await CliWorkspace.Run("contract", "compare", source, source, "--output", "json");
            Assert.That(result.ExitCode, Is.EqualTo(4));
            Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("unknown"));
        }
    }

    [Test]
    public async Task FieldAdditionsAndRemovals_KeepTheirDirectionAndClassification()
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", Odcs);
        var candidate = workspace.Write("candidate", Odcs + "\n      - name: extra\n        logicalType: string\n");
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        Assert.That((int?)JObject.Parse(result.Output)["totals"]!["nonBreaking"], Is.EqualTo(1));
        result = await CliWorkspace.Run("contract", "compare", candidate, baseline, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4));
        Assert.That((int?)JObject.Parse(result.Output)["totals"]!["breaking"], Is.EqualTo(1));
    }

    [TestCase("malformed", "{\"password\":\"DO-NOT-EMIT\",broken", 1)]
    [TestCase("missing", null, 3)]
    [TestCase("bad.zip", "not a zip", 1)]
    [TestCase("unsupported", "[1,2]", 1)]
    public async Task LoadFailures_EmitSafeJsonRatherThanEmptySuccess(string name, string? content, int exitCode)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("valid", Odcs);
        var candidate = content is null ? Path.Combine(workspace.Path, name) : workspace.Write(name, content);
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Output + result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["completed"], Is.False);
        Assert.That(result.Output + result.Error, Does.Not.Contain("DO-NOT-EMIT"));
    }

    [Test]
    public async Task UnsupportedStandardsAndCanonicalSnapshotFailures_HaveAnalysisFailureExit()
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("odcs", Odcs);
        var candidate = workspace.Write("package", Package);
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(5));
        Assert.That((bool?)JObject.Parse(result.Output)["completed"], Is.False);
        var unsupported = workspace.Write("unsupported", Package.Replace("\"name\":\"sales\",", "\"name\":\"sales\",\"sources\":[{\"title\":\"Origin\",\"path\":\"https://example.test\"}],"));
        result = await CliWorkspace.Run("contract", "compare", unsupported, unsupported, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(5), result.Error + result.Output);
        Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("failed"));
    }

    [TestCase("--policy", "other")]
    [TestCase("--fail-on", "unknown,none")]
    [TestCase("--baseline-format", "xml")]
    [TestCase("--candidate-format", "xml")]
    public async Task InvalidOptions_EmitInvocationFailureJson(string option, string value)
    {
        var result = await CliWorkspace.Run("contract", "compare", "a", "b", option, value, "--output", "json");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That((string?)JObject.Parse(result.Output)["outcome"], Is.EqualTo("failed"));
        Assert.That(result.Error, Does.Contain("CLI002"));
    }

    [Test]
    public async Task HelpAndMissingArguments_DescribeCommandAndFailExplicitly()
    {
        var help = await CliWorkspace.Run("contract", "compare", "--help");
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("BASELINE").And.Contain("CANDIDATE").And.Contain("--policy").And.Contain("--fail-on"));
        var missing = await CliWorkspace.Run("contract", "compare", "a", "--output=json");
        Assert.That(missing.ExitCode, Is.EqualTo(2));
        Assert.That((bool?)JObject.Parse(missing.Output)["completed"], Is.False);
    }

    [TestCase("text")]
    [TestCase("json")]
    public async Task QuietSuppressesOutputAndRetainsDiagnostics(string output)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("source", Odcs + "\n        futureGuarantee: true\n");
        var result = await CliWorkspace.Run("contract", "compare", source, source, "--quiet", "--output", output);
        Assert.That(result.ExitCode, Is.EqualTo(4));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("CMP020"));
    }

    [TestCase("text")]
    [TestCase("json")]
    public async Task CredentialsInTypedChanges_AreRedacted(string output)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", Odcs);
        var candidate = workspace.Write("candidate", Odcs + "\n        description: 'pwd=DO-NOT-EMIT'\n        customProperties:\n          - property: settings\n            value:\n              password: DO-NOT-EMIT\n");
        var result = await CliWorkspace.Run("contract", "compare", baseline, candidate, "--output", output);
        Assert.That(result.Output + result.Error, Does.Not.Contain("DO-NOT-EMIT"));
        if (output == "json") Assert.That(result.Output, Does.Contain("[redacted]"));
    }

    [Test]
    public async Task CancelledComparison_ProducesFailureJsonAndCancellationExit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            var exit = await new CompareCommand().ExecuteAsync(null!, new CompareSettings { Baseline = "missing", Candidate = "missing", Output = "json" }, cancellation.Token);
            Assert.That(exit, Is.EqualTo(130));
            Assert.That((string?)JObject.Parse(output.ToString())["outcome"], Is.EqualTo("failed"));
        }
        finally { Console.SetOut(previous); }
    }

    [Test]
    public async Task HttpZipAndUnavailableEndpoints_OnlyLoadDescriptors()
    {
        using var workspace = new CliWorkspace();
        var zipPath = Path.Combine(workspace.Path, "package.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("nested/datapackage.json").Open());
            writer.Write(Package.Replace("must-not-open.csv", "http://127.0.0.1:1/must-not-open.csv"));
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serve = Serve();
        var result = await CliWorkspace.RunWithEnvironment(new Dictionary<string, string?>
        {
            ["PACKATA_CONFIG_HOME"] = workspace.Write("bad-config-dir", "not a directory"),
            ["DUBURL_PROVIDER_STORE"] = workspace.Write("bad-provider-dir", "not a directory")
        }, "contract", "compare", zipPath, $"http://127.0.0.1:{port}/package.zip", "--output", "json");
        await serve;
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);

        async Task Serve()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            var bytes = File.ReadAllBytes(zipPath);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), timeout.Token);
            await stream.WriteAsync(bytes, timeout.Token);
        }
    }
}
