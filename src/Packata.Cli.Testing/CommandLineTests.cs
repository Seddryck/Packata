using System.Diagnostics;
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

    private static ProcessResult Run(params string[] arguments)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
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
}
