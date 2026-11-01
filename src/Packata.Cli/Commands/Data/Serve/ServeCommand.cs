using System.Net;
using System.Text.Json;
using Packata.Core.Contracts;
using Packata.Http;
using Packata.Http.Arrow;
using Packata.ResourceReaders;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ServeCommand : AsyncCommand<ServeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ServeSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken,
                retainArchive: true).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source, cancellationToken);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var contract = mapped.RequireValue();
            var binding = settings.Binding ?? throw new ArgumentException("An explicit source binding is required.");
            var selections = settings.Assets.Select(id => new ServedAsset(contract, id, binding)).ToArray();
            CliDatabase.ResolveOverrides(contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            var root = source.BaseUri.IsFile ? source.BaseUri.LocalPath : string.Empty;
            using var database = selections.Any(asset => asset.Request.Endpoint.Kind == EndpointKind.Database)
                ? new CliDatabase(new CliConfiguration(), root,
                    contract.Endpoints, settings.Connectors, settings.ConnectionEnvironment) : null;
            var streams = source.Archive is null ? null
                : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor
                    ?? throw new InvalidOperationException("Missing archive descriptor."));
            var readers = database?.CreateReaders(source.BaseUri)
                ?? PreviewService.CreateFactory(source.BaseUri, streams: streams, stagingMaximumBytes: settings.StagingMaximumBytes);
            var representations = settings.Formats.Split(',', StringSplitOptions.TrimEntries).Select(format => format switch
            {
                "ndjson" => (IAssetRepresentation)new NdjsonRepresentation(),
                "avro" => new AvroRepresentation(),
                "arrow" => new ArrowRepresentation(),
                _ => throw new ArgumentException("Unsupported representation.")
            }).ToArray();
            await using var host = await AssetHttpHost.StartAsync(selections, readers, representations, new()
            {
                Address = IPAddress.Parse(settings.Address), Port = settings.Port,
                Streaming = new() { BatchSize = settings.BatchSize, MaxBufferedBytes = settings.MaximumBufferBytes },
                SourceLimitation = request => SourceLimitation(request.Endpoint),
                Log = Console.Error.WriteLine
            }, cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(host.Readiness, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                .ConfigureAwait(false);
            await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
            await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return ExitCodes.Success; }
        catch (Exception exception)
        {
            // Connection strings and provider exception messages can contain secrets.
            await Console.Error.WriteLineAsync($"CLI140: Unable to start the asset HTTP service ({exception.GetType().Name}). Check contract selection, binding, address and reader configuration.")
                .ConfigureAwait(false);
            return ExitCodes.Invocation;
        }
    }

    private static string? SourceLimitation(DataEndpoint endpoint)
    {
        if (endpoint.Location is InlineLocation or ConnectionLocation) return null;
        var format = new DataFormatResolver().Resolve(endpoint);
        return endpoint.Location is PathLocation && (format.Name is "csv" or "tsv" or "psv" or "xls" or "xlsx" or "parquet"
            || (string.IsNullOrEmpty(format.Name) && endpoint.Format?.Options.ContainsKey("delimiter") == true))
            ? null : "This source has no reader registered by the CLI. Embedded hosts can register additional reader providers.";
    }
}
