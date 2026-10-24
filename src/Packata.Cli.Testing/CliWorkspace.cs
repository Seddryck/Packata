using System.Diagnostics;

namespace Packata.Cli.Testing;

internal sealed class CliWorkspace : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "packata-cli-" + Guid.NewGuid().ToString("N"));
    public CliWorkspace() => Directory.CreateDirectory(Path);
    public string Write(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public static async Task<CliResult> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return new(process.ExitCode, await output, await error);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);

    public const string PackageJson = """
        {"name":"sample","resources":[{"name":"customers","path":"customers.csv","format":"csv","dialect":{"header":true}}]}
        """;
    public const string PackageYaml = """
        name: sample
        resources:
          - name: customers
            path: customers.csv
            format: csv
            dialect:
              header: true
        """;
    public const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: customer-contract
        version: 1.0.0
        status: draft
        schema:
          - name: customers
            physicalType: file
        servers:
          - server: local
            type: local
            path: customers.csv
            format: csv
        """;
}

internal sealed record CliResult(int ExitCode, string Output, string Error);
