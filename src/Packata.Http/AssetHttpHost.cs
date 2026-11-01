using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Packata.Core.Reading;

namespace Packata.Http;

public sealed record AssetHttpOptions
{
    public IPAddress Address { get; init; } = IPAddress.Loopback;
    public int Port { get; init; }
    public AssetStreamOptions Streaming { get; init; } = new();
    public int MaxConcurrentRequests { get; init; } = 8;
    public int MaxRetainedTransfers { get; init; } = 4096;
    public TimeSpan TransferRetention { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Return a safe public capability diagnostic without opening data.</summary>
    public Func<DataEndpointReadRequest, string?>? SourceLimitation { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed record ServedFormat(string MediaType, IReadOnlyList<string> Aliases, bool Enabled, string? Limitation, object? Schema);
public sealed record ServedColumn(string Name, string? LogicalType, string? PhysicalType, bool Nullable, object Constraints);
public sealed record ServedAssetMetadata(string Id, string ContractId, string? ContractVersion,
    string AssetId, int BindingIndex, IReadOnlyList<ServedColumn> Columns, IReadOnlyList<ServedFormat> Formats,
    string SchemaUrl, string DataUrl);
public sealed record AssetHostReadiness(string Event, string BaseUrl, IReadOnlyList<ServedAssetMetadata> Assets,
    int CompletionRetentionSeconds);

/// <summary>Read-only Kestrel host. The caller owns the factory; requests own opened readers.
/// Dispose the host to stop accepting requests and cancel active streams.</summary>
public sealed class AssetHttpHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly SemaphoreSlim concurrency;
    private readonly CancellationTokenSource stopping = new();
    private readonly AssetHttpOptions options;
    private Uri? baseUri;
    private AssetHostReadiness? readiness;
    public Uri BaseUri => baseUri ?? throw new InvalidOperationException("Host not started.");
    public AssetHostReadiness Readiness => readiness ?? throw new InvalidOperationException("Host not started.");

    private AssetHttpHost(WebApplication application, AssetHttpOptions options)
    {
        this.application = application;
        this.options = options;
        concurrency = new(options.MaxConcurrentRequests);
    }

    public static async Task<AssetHttpHost> StartAsync(IEnumerable<ServedAsset> assets,
        IDataEndpointReaderFactory readers, IEnumerable<IAssetRepresentation> representations,
        AssetHttpOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readers);
        options ??= new();
        ValidateOptions(options);
        var selections = SelectAssets(assets);
        var encoders = SelectEncoders(representations);
        var descriptions = selections.ToDictionary(asset => asset.Id, asset => Describe(asset, encoders, options));
        var application = CreateApplication(options);
        var host = new AssetHttpHost(application, options);
        host.MapRoutes(selections, descriptions, encoders, readers);
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("The server did not publish its bound address.");
            var address = addresses.Addresses.Single();
            host.baseUri = new(address.EndsWith('/') ? address : address + "/");
            host.readiness = new("ready", host.BaseUri.AbsoluteUri, descriptions.Values.ToArray(), (int)options.TransferRetention.TotalSeconds);
            return host;
        }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static void ValidateOptions(AssetHttpOptions options)
    {
        options.Streaming.Validate();
        if (options.Port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRetainedTransfers);
        if (options.TransferRetention <= TimeSpan.Zero || options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static ServedAsset[] SelectAssets(IEnumerable<ServedAsset> assets)
    {
        var selections = assets.ToArray();
        if (selections.Length == 0 || selections.Select(asset => asset.Id).Distinct().Count() != selections.Length)
            throw new ArgumentException("Serve at least one unique asset selection.", nameof(assets));
        return selections;
    }

    private static IAssetRepresentation[] SelectEncoders(IEnumerable<IAssetRepresentation> representations)
    {
        var encoders = representations.ToArray();
        if (encoders.Length == 0 || encoders.Select(encoder => encoder.MediaType).Distinct(StringComparer.OrdinalIgnoreCase).Count() != encoders.Length)
            throw new ArgumentException("Register at least one unique representation.", nameof(representations));
        return encoders;
    }

    private static WebApplication CreateApplication(AssetHttpOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(options.Address, options.Port);
            server.Limits.MaxRequestBodySize = 0;
        });
        builder.Services.Configure<HostOptions>(configuration => configuration.ShutdownTimeout = options.ShutdownTimeout);
        return builder.Build();
    }

    private void MapRoutes(ServedAsset[] selections, Dictionary<string, ServedAssetMetadata> descriptions,
        IAssetRepresentation[] encoders, IDataEndpointReaderFactory readers)
    {
        var transfers = new TransferRegistry(options.MaxRetainedTransfers, options.TransferRetention);
        application.MapGet("/", () => new { protocol = "packata-http-v1", assets = "/assets", completion = "/transfers/{id}" });
        application.MapGet("/assets", () => descriptions.Values);
        application.MapGet("/assets/{id}", (string id) => descriptions.TryGetValue(id, out var metadata)
            ? Results.Json(metadata) : Results.NotFound());
        application.MapGet("/assets/{id}/schema", (string id) => descriptions.TryGetValue(id, out var metadata)
            ? Results.Json(new { metadata.Columns, metadata.Formats }) : Results.NotFound());
        application.MapGet("/transfers/{id}", (string id, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return transfers.Find(id) is { } status ? Results.Json(status) : Results.NotFound();
        });
        application.MapGet("/assets/{id}/data", (string id, HttpContext context) =>
            WriteDataAsync(id, context, selections, descriptions, encoders, readers, transfers));
    }

    private async Task WriteDataAsync(string id, HttpContext context, ServedAsset[] selections,
        Dictionary<string, ServedAssetMetadata> descriptions, IAssetRepresentation[] encoders,
        IDataEndpointReaderFactory readers, TransferRegistry transfers)
    {
        context.Response.Headers.Vary = "Accept";
        context.Response.Headers.CacheControl = "no-store";
        var asset = selections.FirstOrDefault(asset => asset.Id == id);
        if (asset is null) { context.Response.StatusCode = 404; return; }
        var formats = descriptions[id].Formats;
        var available = encoders.Where(encoder => formats.Single(format => format.MediaType == encoder.MediaType).Enabled);
        var encoder = AcceptNegotiation.Select(context.Request.Headers.Accept.ToString(), available);
        if (encoder is null)
        {
            context.Response.StatusCode = 406;
            await context.Response.WriteAsJsonAsync(new { code = "FORMAT_NOT_ACCEPTABLE", formats }, context.RequestAborted);
            return;
        }
        if (!await concurrency.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
        try
        {
            var transfer = transfers.Create(id);
            if (transfer is null) { context.Response.StatusCode = 429; return; }
            context.Response.ContentType = encoder.MediaType;
            context.Response.Headers["Packata-Transfer-Id"] = transfer.Id;
            context.Response.Headers["Packata-Completion"] = $"/transfers/{transfer.Id}";
            using var token = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping.Token,
                application.Lifetime.ApplicationStopping);
            await WriteTransferAsync(context, asset, encoder, readers, transfers, transfer, token.Token).ConfigureAwait(false);
        }
        finally { concurrency.Release(); }
    }

    private async Task WriteTransferAsync(HttpContext context, ServedAsset asset, IAssetRepresentation encoder,
        IDataEndpointReaderFactory readers, TransferRegistry transfers, TransferStatus transfer, CancellationToken token)
    {
        using var stream = new TransferStream(context.Response.Body);
        try
        {
            await encoder.WriteAsync(asset, readers, stream, options.Streaming, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            transfers.Finish(transfer.Id, "complete", stream.Bytes, stream.Digest());
        }
        catch (Exception exception)
        {
            var conversion = exception as AssetEncodingException;
            var code = conversion is null ? "ASSET_READ_FAILED" : "LOSSLESS_CONVERSION_FAILED";
            transfers.Finish(transfer.Id, "failed", stream.Bytes, null, code, conversion?.Field);
            options.Log?.Invoke($"Transfer {transfer.Id} failed ({exception.GetType().Name}).");
            await WriteFailureAsync(context, exception, code, conversion?.Field, transfer.Id).ConfigureAwait(false);
        }
    }

    private static async Task WriteFailureAsync(HttpContext context, Exception exception, string code, string? field, string transferId)
    {
        if (context.Response.HasStarted) { context.Abort(); return; }
        var status = exception is OperationCanceledException ? 503 : 422;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { type = "about:blank", title = "Asset data unavailable", status,
            code, field, transferId, message = "The source or encoder could not produce this representation. No data was returned." },
            options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, application.Lifetime.ApplicationStopping);
        try { await Task.Delay(Timeout.Infinite, token.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancellation is normal shutdown; the caller's await using then disposes the host.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(options.ShutdownTimeout);
        try { await application.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
            // A provider stuck in a synchronous read may outlive shutdown and still needs its semaphore.
            stopping.Dispose();
        }
    }

    private static ServedAssetMetadata Describe(ServedAsset asset, IAssetRepresentation[] encoders, AssetHttpOptions options)
    {
        var sourceLimitation = options.SourceLimitation?.Invoke(asset.Request);
        var formats = encoders.Select(encoder => DescribeFormat(encoder, asset, sourceLimitation)).ToArray();
        var columns = asset.Schema.Fields.Select(field => new ServedColumn(field.Name, field.LogicalType,
            field.PhysicalType, field.Nullable, field.Constraints.Where(constraint => constraint.Kind is
                "precision" or "scale" or "integerWidth" or "signed" or "timezone" or "defaultTimezone").ToArray())).ToArray();
        return new(asset.Id, asset.Contract.Identity.Id, asset.Contract.Identity.Version, asset.Asset.Id, asset.BindingIndex,
            columns, formats, $"/assets/{asset.Id}/schema", $"/assets/{asset.Id}/data");
    }

    private static ServedFormat DescribeFormat(IAssetRepresentation encoder, ServedAsset asset, string? sourceLimitation)
    {
        RepresentationSupport support;
        try { support = encoder.Describe(asset); }
        catch (NotSupportedException error) { support = new(false, error.Message); }
        return new(encoder.MediaType, encoder.Aliases, support.Supported && sourceLimitation is null,
            sourceLimitation ?? support.Limitation, support.Schema);
    }
}
