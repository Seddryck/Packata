using Spectre.Console;

namespace Packata.Cli;

/// <summary>Buffers file output until rendering succeeds. Never replaces an existing file.</summary>
internal sealed class OutputDestination : IDisposable
{
    private readonly string? _path;
    private readonly StringWriter? _buffer;
    public TextWriter Writer { get; }
    public IAnsiConsole Console { get; }

    public OutputDestination(string? path)
    {
        _path = path;
        _buffer = path is null ? null : new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Writer = _buffer ?? System.Console.Out;
        Console = path is null ? AnsiConsole.Console : AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(Writer),
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
        });
        if (path is not null) Console.Profile.Capabilities.Unicode = AnsiConsole.Profile.Capabilities.Unicode;
    }

    public void Commit()
    {
        if (_path is not null) ContractDocumentWriter.Write(_path, _buffer!.ToString(), overwrite: false);
    }

    public void Dispose() => _buffer?.Dispose();
}
