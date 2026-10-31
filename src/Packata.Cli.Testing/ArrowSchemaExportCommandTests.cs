using System.Diagnostics;
using Apache.Arrow.Ipc;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class ArrowSchemaExportCommandTests
{
    private const string Source = """
        resources:
          - name: orders
            path: nonexistent-data-must-not-be-read.csv
            schema:
              fields:
                - name: id
                  type: integer
                  format: u64
        """;

    [Test]
    public async Task File_and_raw_stdout_are_identical_schema_messages_without_text_encoding()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source);
        var path = Path.Combine(workspace.Path, "orders.schema.ipc");
        var file = await CliWorkspace.Run("schema", "export", source, "--output-format", "arrow-ipc", "-o", path);
        Assert.That(file.ExitCode, Is.Zero, file.Error);
        Assert.That(file.Output, Is.Empty);
        var stdout = await BinaryRun("schema", "export", source, "--asset", "orders", "--output-format", "arrow-ipc");
        Assert.That(stdout.ExitCode, Is.Zero, stdout.Error);
        Assert.That(stdout.Bytes, Is.EqualTo(File.ReadAllBytes(path)));
        using var stream = new MemoryStream(stdout.Bytes);
        using var reader = new ArrowStreamReader(stream);
        Assert.That((await reader.GetSchema()).FieldsList.Single().Name, Is.EqualTo("id"));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        Assert.That(stdout.Error, Does.Contain("ARROW_CONSTRAINTS"));
        var repeat = await CliWorkspace.Run("schema", "export", source, "--output-format", "arrow-ipc", "-o", path);
        Assert.That(repeat.ExitCode, Is.EqualTo(3));
        var overwrite = await CliWorkspace.Run("schema", "export", source, "--output-format", "arrow-ipc", "-o", source);
        Assert.That(overwrite.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Source));
        Assert.That(Directory.GetFiles(workspace.Path, "*.tmp"), Is.Empty);
    }

    [TestCase("--arrow")]
    [TestCase("--output-format", "arrow-ipc", "--target", "arrow")]
    [TestCase("--output-format", "arrow-ipc", "--language", "python")]
    [TestCase("--output-format", "arrow-ipc", "--profile", "lance")]
    [TestCase("--output-format", "arrow-ipc", "--template", "missing.scriban")]
    public async Task Wrong_operation_and_removed_options_fail_before_source_access(params string[] options)
    {
        var result = await BinaryRun(["schema", "export", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Bytes, Is.Empty);
    }

    [Test]
    public async Task Mapping_or_asset_failure_never_creates_partial_file_or_stdout()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source.Replace("type: integer", "type: unknown"));
        var path = Path.Combine(workspace.Path, "absent.ipc");
        var file = await CliWorkspace.Run("schema", "export", source, "--output-format", "arrow-ipc", "-o", path);
        Assert.That(file.ExitCode, Is.EqualTo(1), file.Error);
        Assert.That(File.Exists(path), Is.False);
        var stdout = await BinaryRun("schema", "export", source, "--output-format", "arrow-ipc");
        Assert.That(stdout.ExitCode, Is.EqualTo(1));
        Assert.That(stdout.Bytes, Is.Empty);
        var missing = await BinaryRun("schema", "export", source, "--output-format", "arrow-ipc", "--asset", "missing");
        Assert.That(missing.ExitCode, Is.EqualTo(1));
        Assert.That(missing.Bytes, Is.Empty);
    }

    [Test]
    public void Cancelled_binary_publication_leaves_no_file()
    {
        using var workspace = new CliWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(workspace.Path, "absent.ipc");
        Assert.That(() => ContractDocumentWriter.WriteBytes(path, [1, 2, 3], cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    private static async Task<(int ExitCode, byte[] Bytes, string Error)> BinaryRun(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        await copy;
        return (process.ExitCode, output.ToArray(), await error);
    }
}
