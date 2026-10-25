using System.Reflection;

namespace Packata.Cli;

internal static class ApplicationVersion
{
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var informationalVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return informationalVersion?.Split('+', 2)[0] ?? "0.0.0";
    }
}
