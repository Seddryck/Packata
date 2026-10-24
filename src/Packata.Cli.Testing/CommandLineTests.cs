using System.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class CommandLineTests
{
    [Test]
    public void Help_ReturnsSuccessAndUsesPackataCommandName()
    {
        var result = Run("--help");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("packata"));
            Assert.That(result.StandardOutput, Does.Contain("USAGE"));
            Assert.That(result.StandardError, Is.Empty);
        });
    }

    [Test]
    public void Version_ReturnsSuccessAndReportsAssemblyVersion()
    {
        var result = Run("--version");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain(ApplicationVersion.Value));
            Assert.That(result.StandardError, Is.Empty);
        });
    }

    [Test]
    public void InferHelp_DocumentsAssetSelectionAndPersistenceModes()
    {
        var result = Run("infer", "--help");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("datapackage.json").And.Contain("--asset"));
            Assert.That(result.StandardOutput, Does.Contain("--output").And.Contain("--write"));
            Assert.That(result.StandardOutput, Does.Contain("CSV or database"));
            Assert.That(result.StandardError, Is.Empty);
        });
    }

    [Test]
    public void DatabaseInferenceUpdate_PreservesEndpointAndWritesDatabaseSchemaFacets()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("database-contract.yaml", DatabaseOdcsYaml());
        var document = ContractDocument.Load(source);
        var originalAsset = document.Contract.Assets.Single();
        var originalEndpoint = document.Contract.Endpoints.Single();
        var inferredAsset = originalAsset with
        {
            Schema = new Packata.Core.Contracts.DataSchema(
            [
                new Packata.Core.Contracts.DataField(
                    "id", "integer", "INTEGER", Required: true),
                new Packata.Core.Contracts.DataField(
                    "name", "string", "VARCHAR", Required: false)
            ],
            ["id"])
        };
        var inferredEndpoint = originalEndpoint with
        {
            Location = new Packata.Core.Contracts.ConnectionLocation(
                "duckdb", Database: "./warehouse.duckdb")
        };

        var update = ContractDocumentUpdater.Update(
            document, originalAsset, originalEndpoint, inferredAsset, inferredEndpoint);
        var output = directory.Write("enriched-contract.yaml", update.Content);
        var reloaded = new Packata.OpenDataContract.DataContractFactory().LoadFromFile(output);
        var properties = reloaded.Schema.Single().Properties;

        Assert.Multiple(() =>
        {
            Assert.That(properties, Has.Count.EqualTo(2));
            Assert.That(properties[0].PrimaryKey, Is.True);
            Assert.That(properties[0].PrimaryKeyPosition, Is.EqualTo(1));
            Assert.That(properties[0].PhysicalType, Is.EqualTo("INTEGER"));
            Assert.That(properties[1].Required, Is.False);
            Assert.That(update.Content, Does.Contain("database: ./warehouse.duckdb"));
            Assert.That(update.Content, Does.Not.Contain("CSV dialect"));
        });
    }

    [Test]
    public void Infer_DataPackage_DisplaysResultWithoutChangingSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("datapackage.json", DataPackageJson());
        directory.Write("customers.csv", "Name,Age\nAlice,42\nBob,35\n");
        var original = File.ReadAllText(source);

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "customers");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Inferred asset:").And.Contain("customers"));
            Assert.That(result.StandardOutput, Does.Contain("Name").And.Contain("Age").And.Contain("integer"));
            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
            Assert.That(result.StandardError, Is.Empty);
        });
    }

    [Test]
    public void Infer_DataPackage_OutputWritesEnrichedCopyOnly()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("datapackage.json", DataPackageJsonWithExistingMetadata());
        directory.Write("customers.csv", "Name;Age\nAlice;42\nBob;35\n");
        var original = File.ReadAllText(source);

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "customers",
            "--output", "enriched.json");

        var enrichedPath = System.IO.Path.Combine(directory.Path, "enriched.json");
        var enriched = JObject.Parse(File.ReadAllText(enrichedPath));
        var reloaded = new Packata.DataPackage.DataPackageFactory().LoadFromFile(enrichedPath);
        var resource = (JObject)enriched["resources"]![0]!;
        var untouchedResource = (JObject)enriched["resources"]![1]!;
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
            Assert.That((string?)enriched["title"], Is.EqualTo("Customer contract"));
            Assert.That((string?)resource["schema"]!["fields"]![0]!["title"], Is.EqualTo("Customer name"));
            Assert.That(resource["schema"]!["fields"]!.Count(), Is.EqualTo(2));
            Assert.That((string?)resource["schema"]!["fields"]![1]!["type"], Is.EqualTo("integer"));
            Assert.That((string?)resource["dialect"]!["delimiter"], Is.EqualTo(";"));
            Assert.That((string?)untouchedResource["schema"]!["fields"]![0]!["title"], Is.EqualTo("Lookup code"));
            Assert.That(reloaded.Resources[0].Schema!.Fields, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Infer_DataPackageYaml_OutputWritesReloadableEnrichedCopy()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("datapackage.yaml", DataPackageYaml());
        directory.Write("customers.csv", "Name;Age\nAlice;42\nBob;35\n");
        var original = File.ReadAllText(source);

        var result = RunIn(directory.Path, "infer", "datapackage.yaml", "--asset", "customers",
            "--output", "enriched.yaml");

        var enrichedPath = System.IO.Path.Combine(directory.Path, "enriched.yaml");
        var resource = new Packata.DataPackage.DataPackageFactory().LoadFromFile(enrichedPath).Resources.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
            Assert.That(resource.Schema!.Fields, Has.Count.EqualTo(2));
            Assert.That(resource.Schema.Fields[1].Type, Is.EqualTo("integer"));
            Assert.That(resource.Dialect!.Delimiter, Is.EqualTo(";"));
        });
    }

    [Test]
    public void Infer_Odcs_WriteUpdatesSourceAndPreservesMetadata()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("contract.yaml", OdcsYaml());
        directory.Write("customers.csv", "Name,Age\nAlice,42\nBob,35\n");

        var result = RunIn(directory.Path, "infer", "contract.yaml", "--asset", "customers", "--write");
        var updated = File.ReadAllText(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(updated, Does.Contain("purpose: Customer contract"));
            Assert.That(updated, Does.Contain("properties:"));
            Assert.That(updated, Does.Contain("logicalType: integer"));
            Assert.That(updated, Does.Contain("property: delimiter"));
            Assert.That(new Packata.OpenDataContract.DataContractFactory().LoadFromFile(source).Schema[0].Properties,
                Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Infer_AmbiguousInputDisplaysConfidenceWarning()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("datapackage.json", DataPackageJson());
        directory.Write("customers.csv", "Name,Place\nAlice,Brussels\nBob,Paris\n");

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "customers");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("header.ambiguous").And.Contain("confidence"));
        });
    }

    [Test]
    public void Infer_RejectsUnknownAsset()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("datapackage.json", DataPackageJson());
        directory.Write("customers.csv", "Name\nAlice\n");

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "missing");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Asset 'missing' was not found"));
        });
    }

    [Test]
    public void Infer_RejectsExplicitUnsupportedFormatEvenWhenPathHasCsvExtension()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("datapackage.json", DataPackageJson().Replace("\"format\": \"csv\"", "\"format\": \"json\""));
        directory.Write("customers.csv", "Name\nAlice\n");

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "customers");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StandardOutput, Does.Contain("not bound to a supported CSV resource"));
        });
    }

    [Test]
    public void Infer_RejectsOutputAndWriteTogether()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("datapackage.json", DataPackageJson());

        var result = RunIn(directory.Path, "infer", "datapackage.json", "--asset", "customers",
            "--output", "copy.json", "--write");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StandardOutput + result.StandardError, Does.Contain("--output and --write cannot be combined"));
        });
    }

    private static ProcessResult Run(params string[] arguments)
        => RunCore(null, arguments);

    private static ProcessResult RunIn(string workingDirectory, params string[] arguments)
        => RunCore(workingDirectory, arguments);

    private static ProcessResult RunCore(string? workingDirectory, params string[] arguments)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null) startInfo.WorkingDirectory = workingDirectory;
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Packata CLI.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static string DataPackageJson() =>
        """
        {
          "name": "sample",
          "title": "Customer contract",
          "resources": [
            {
              "name": "customers",
              "title": "Customers",
              "path": "customers.csv",
              "format": "csv"
            }
          ]
        }
        """;

    private static string OdcsYaml() =>
        """
        apiVersion: v3.2.0
        kind: DataContract
        id: customer-contract
        version: 1.0.0
        status: draft
        description:
          purpose: Customer contract
        schema:
          - name: customers
            physicalType: file
        servers:
          - server: local
            type: local
            path: customers.csv
            format: csv
        """;

    private static string DataPackageJsonWithExistingMetadata() =>
        """
        {
          "name": "sample",
          "title": "Customer contract",
          "resources": [
            {
              "name": "customers",
              "title": "Customers",
              "path": "customers.csv",
              "format": "csv",
              "schema": {
                "fields": [
                  { "name": "Name", "type": "string", "title": "Customer name" }
                ]
              }
            },
            {
              "name": "lookup",
              "path": "lookup.csv",
              "format": "csv",
              "schema": {
                "fields": [
                  { "name": "Code", "type": "string", "title": "Lookup code" }
                ]
              }
            }
          ]
        }
        """;

    private static string DatabaseOdcsYaml() =>
        """
        apiVersion: v3.2.0
        kind: DataContract
        id: warehouse-contract
        version: 1.0.0
        status: draft
        schema:
          - name: customers
            physicalName: main.customers
            physicalType: table
        servers:
          - server: warehouse
            type: duckdb
            database: ./warehouse.duckdb
            schema: main
        """;

    private static string DataPackageYaml() =>
        """
        name: sample
        title: Customer contract
        resources:
          - name: customers
            title: Customers
            path: customers.csv
            format: csv
        """;

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"packata-cli-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
