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
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var contract = mapped.Value!;
            var selections = settings.Assets.Select(id => new ServedAsset(contract, id, settings.Binding!.Value)).ToArray();
            CliDatabase.ResolveOverrides(contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            using var database = selections.Any(asset => asset.Request.Endpoint.Kind == EndpointKind.Database)
                ? new CliDatabase(new CliConfiguration(), source.BaseUri.IsFile ? source.BaseUri.LocalPath : string.Empty,
                    contract.Endpoints, settings.Connectors, settings.ConnectionEnvironment) : null;
            var streams = source.Archive is null ? null
                : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
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
            Console.Out.WriteLine(JsonSerializer.Serialize(host.Readiness, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
            await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return ExitCodes.Success; }
        catch (Exception exception)
        {
            // Connection strings and provider exception messages can contain secrets.
            Console.Error.WriteLine($"CLI140: Unable to start the asset HTTP service ({exception.GetType().Name}). Check contract selection, binding, address and reader configuration.");
            return ExitCodes.Invocation;
        }
    }

    private static string? SourceLimitation(DataEndpoint endpoint)
    {
        if (endpoint.Location is InlineLocation or ConnectionLocation) return null;
        var format = new DataFormatResolver().Resolve(endpoint);
        return endpoint.Location is PathLocation && (format.Name is "csv" or "tsv" or "psv" or "xls" or "xlsx" or "parquet"
            || string.IsNullOrEmpty(format.Name) && endpoint.Format?.Options.ContainsKey("delimiter") == true)
            ? null : "This source has no reader registered by the CLI. Embedded hosts can register additional reader providers.";
    }
}
