using System.ComponentModel;
using System.Runtime.InteropServices;
using DubUrl.Application;
using DubUrl.Core.ProviderPacks;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ConnectorListSettings : PresentationSettings
{
    [CommandOption("--scheme <SCHEME>")]
    [Description("Filter connectors by database connection scheme.")]
    public string? Scheme { get; init; }

    [CommandOption("--verbose")]
    [Description("Show connector aliases, compatibility, discovery facts and prerequisites.")]
    public bool Verbose { get; init; }
}
internal sealed class ConnectorShowSettings : PresentationSettings
{
    [CommandArgument(0, "<ID>")]
    public required string Id { get; init; }
}
internal sealed class ConnectorInstallSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    public required string Id { get; init; }
}

internal sealed record ConnectorDescription(string Id, string Version, IReadOnlyList<string> Schemes,
    bool Listed, bool Installed, bool Active, bool Unhealthy, bool? Loaded,
    IReadOnlyList<string> DiagnosticCodes, IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> RuntimeIdentifiers, string? ApiRange, string? Driver, bool DownloadAvailable, string Prerequisites);

internal sealed class CliConnectors : IDisposable
{
    private readonly HttpClient _http;
    private readonly CliConfiguration _configuration;
    private readonly ProviderPackCatalog _catalog;
    private readonly IConnectorService _service;

    public CliConnectors(CliConfiguration configuration, HttpClient? http = null)
    {
        _configuration = configuration;
        _catalog = configuration.Catalog();
        _http = http ?? new HttpClient();
        _service = new ConnectorService(configuration.Store, _catalog, _http);
    }

    public IReadOnlyList<ConnectorDescription> List(string? scheme = null) => _service.List()
        .Where(info => scheme is null || info.Schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
        .Select(Describe).ToArray();

    public IReadOnlyList<ConnectorDescription> Show(string id)
    {
        var results = _service.List().Where(info => info.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(Describe).ToArray();
        return results.Length > 0 ? results : throw new CliException("connector-unknown: Use 'packata connector list' for canonical connector IDs.");
    }

    public async Task<ConnectorDescription> InstallAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = _catalog.Entries.SingleOrDefault(entry => entry.Provider.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("connector-unknown: Installation requires a canonical connector ID from the curated catalog.");
        var existing = _service.List().FirstOrDefault(info => info.Id.Equals(definition.Provider, StringComparison.OrdinalIgnoreCase)
            && info.Version == definition.Version && info.Active && !info.Unhealthy);
        if (existing is not null) return Describe(existing);
        if (_configuration.Offline)
            throw new CliException("connector-offline: A download is required. Set connectors.offline to false with 'packata config set' to allow it.");
        if (definition.DownloadUri is null || string.IsNullOrWhiteSpace(definition.Sha256))
            throw new CliException("connector-distribution-missing: The catalog contains metadata only. Configure a compatible published distribution catalog with 'packata config set connectors.catalog <path>'.");
        Console.Error.WriteLine($"Installing connector {definition.Provider} {definition.Version}.");
        try { return Describe(await _service.InstallAsync(definition.Provider, cancellationToken).ConfigureAwait(false)); }
        catch (ConnectorOperationException error) { throw new CliException($"{error.Code}: Connector installation failed. Check compatibility, catalog integrity, network access and store permissions."); }
    }

    private ConnectorDescription Describe(ConnectorInfo info)
    {
        var definition = _catalog.Entries.FirstOrDefault(entry => entry.Provider.Equals(info.Id, StringComparison.OrdinalIgnoreCase)
            && entry.Version == info.Version);
        return new(info.Id, info.Version, info.Schemes, info.Listed, info.Installed, info.Active, info.Unhealthy,
            null, info.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray(), definition?.TargetFrameworks ?? [],
            definition?.RuntimeIdentifiers ?? [], definition?.DubUrlApiRange, definition?.Driver,
            definition?.DownloadUri is not null && !string.IsNullOrWhiteSpace(definition.Sha256),
            "Loaded status is not inspected by discovery. Installation does not establish server connectivity. " +
            $"Current runtime: {RuntimeInformation.RuntimeIdentifier}. " +
            ConnectorOutput.DependencyNote(definition?.Driver));
    }

    public void Dispose() => _http.Dispose();

    public static void Write(IReadOnlyList<ConnectorDescription> connectors, string format, bool verbose = false)
    {
        if (format == "json") { CliOutput.Json(connectors); return; }
        ConnectorOutput.Write(connectors, Console.Out, verbose);
    }
}

internal sealed class ConnectorListCommand : Command<ConnectorListSettings>
{
    public override int Execute(CommandContext context, ConnectorListSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => { using var connectors = new CliConnectors(new CliConfiguration()); CliConnectors.Write(connectors.List(settings.Scheme), settings.OutputFormat, settings.Verbose); });
}
internal sealed class ConnectorShowCommand : Command<ConnectorShowSettings>
{
    public override int Execute(CommandContext context, ConnectorShowSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => { using var connectors = new CliConnectors(new CliConfiguration()); CliConnectors.Write(connectors.Show(settings.Id), settings.OutputFormat, verbose: true); });
}
internal sealed class ConnectorInstallCommand : AsyncCommand<ConnectorInstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectorInstallSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var connectors = new CliConnectors(new CliConfiguration());
            CliConnectors.Write([await connectors.InstallAsync(settings.Id, cancellationToken).ConfigureAwait(false)], "text", verbose: true);
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Connector installation was cancelled."); return 2; }
        catch (Exception error) { return CliOutput.Error(error); }
    }
}
