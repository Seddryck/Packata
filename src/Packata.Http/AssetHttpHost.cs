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
    /// <summary>Optional source capability policy. Return a safe public diagnostic, without opening data.</summary>
    public Func<DataEndpointReadRequest, string?>? SourceLimitation { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed record ServedFormat(string MediaType, IReadOnlyList<string> Aliases, bool Enabled, string? Limitation, object? Schema);
public sealed record ServedColumn(string Name, string? LogicalType, string? PhysicalType, bool Nullable,
    object Constraints);
public sealed record ServedAssetMetadata(string Id, string ContractId, string? ContractVersion,
    string AssetId, int BindingIndex, IReadOnlyList<ServedColumn> Columns, IReadOnlyList<ServedFormat> Formats,
    string SchemaUrl, string DataUrl);
public sealed record AssetHostReadiness(string Event, string BaseUrl, IReadOnlyList<ServedAssetMetadata> Assets,
    int CompletionRetentionSeconds);

/// <summary>Optional read-only Kestrel host. The caller retains ownership of the reader factory.
/// Each request owns its opened readers. Dispose the host to stop accepting requests and cancel active streams.</summary>
public sealed class AssetHttpHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly SemaphoreSlim concurrency;
    private readonly CancellationTokenSource stopping = new();
    private readonly AssetHttpOptions options;
    public Uri BaseUri { get; private set; } = null!;
    public AssetHostReadiness Readiness { get; private set; } = null!;

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
        options.Streaming.Validate();
        if (options.Port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRetainedTransfers);
        if (options.TransferRetention <= TimeSpan.Zero || options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        var selections = assets.ToArray();
        if (selections.Length == 0 || selections.Select(asset => asset.Id).Distinct().Count() != selections.Length)
            throw new ArgumentException("Serve at least one unique asset selection.", nameof(assets));
        var encoders = representations.ToArray();
        if (encoders.Length == 0 || encoders.Select(encoder => encoder.MediaType).Distinct(StringComparer.OrdinalIgnoreCase).Count() != encoders.Length)
            throw new ArgumentException("Register at least one unique representation.", nameof(representations));
        var descriptions = selections.ToDictionary(asset => asset.Id, asset => Describe(asset, encoders, options));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(options.Address, options.Port);
            server.Limits.MaxRequestBodySize = 0;
        });
        builder.Services.Configure<HostOptions>(configuration => configuration.ShutdownTimeout = options.ShutdownTimeout);
        var application = builder.Build();
        var host = new AssetHttpHost(application, options);
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
        application.MapGet("/assets/{id}/data", async (string id, HttpContext context) =>
        {
            context.Response.Headers.Vary = "Accept";
            context.Response.Headers.CacheControl = "no-store";
            var asset = selections.FirstOrDefault(asset => asset.Id == id);
            if (asset is null) { context.Response.StatusCode = 404; return; }
            var formats = descriptions[id].Formats;
            var available = encoders.Where(encoder => formats.Single(format => format.MediaType == encoder.MediaType).Enabled).ToArray();
            var encoder = AcceptNegotiation.Select(context.Request.Headers.Accept.ToString(), available);
            if (encoder is null)
            {
                context.Response.StatusCode = 406;
                await context.Response.WriteAsJsonAsync(new { code = "FORMAT_NOT_ACCEPTABLE", formats }, context.RequestAborted);
                return;
            }
            if (!await host.concurrency.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try
            {
                var transfer = transfers.Create(id);
                if (transfer is null) { context.Response.StatusCode = 429; return; }
                context.Response.ContentType = encoder.MediaType;
                context.Response.Headers["Packata-Transfer-Id"] = transfer.Id;
                context.Response.Headers["Packata-Completion"] = $"/transfers/{transfer.Id}";
                using var token = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.stopping.Token,
                    application.Lifetime.ApplicationStopping);
                using var stream = new TransferStream(context.Response.Body);
                try
                {
                    await encoder.WriteAsync(asset, readers, stream, options.Streaming, token.Token).ConfigureAwait(false);
                    await stream.FlushAsync(token.Token).ConfigureAwait(false);
                    token.Token.ThrowIfCancellationRequested();
                    transfers.Finish(transfer.Id, "complete", stream.Bytes, stream.Digest());
                }
                catch (Exception exception)
                {
                    var conversion = exception as AssetEncodingException;
                    var code = conversion is null ? "ASSET_READ_FAILED" : "LOSSLESS_CONVERSION_FAILED";
                    transfers.Finish(transfer.Id, "failed", stream.Bytes, null, code, conversion?.Field);
                    options.Log?.Invoke($"Transfer {transfer.Id} failed ({exception.GetType().Name}).");
                    if (context.Response.HasStarted) context.Abort();
                    else
                    {
                        context.Response.StatusCode = exception is OperationCanceledException ? 503 : 422;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(new { code, field = conversion?.Field, transferId = transfer.Id,
                            message = "The source or encoder could not produce this representation. No data was returned." },
                            context.RequestAborted).ConfigureAwait(false);
                    }
                }
            }
            finally { host.concurrency.Release(); }
        });
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            host.BaseUri = new(address.EndsWith('/') ? address : address + "/");
            host.Readiness = new("ready", host.BaseUri.AbsoluteUri, descriptions.Values.ToArray(), (int)options.TransferRetention.TotalSeconds);
            return host;
        }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(options.ShutdownTimeout);
        try { await application.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
            // A provider stuck in a synchronous read can outlive shutdown; its request still releases the semaphore.
            stopping.Dispose();
        }
    }

    private static ServedAssetMetadata Describe(ServedAsset asset, IAssetRepresentation[] encoders, AssetHttpOptions options)
    {
        var sourceLimitation = options.SourceLimitation?.Invoke(asset.Request);
        var formats = encoders.Select(encoder =>
        {
            RepresentationSupport support;
            try { support = encoder.Describe(asset); }
            catch (NotSupportedException error) { support = new(false, error.Message); }
            return new ServedFormat(encoder.MediaType, encoder.Aliases, support.Supported && sourceLimitation is null,
                sourceLimitation ?? support.Limitation, support.Schema);
        }).ToArray();
        var columns = asset.Asset.Schema!.Fields.Select(field => new ServedColumn(field.Name, field.LogicalType,
            field.PhysicalType, field.Nullable, field.Constraints.Where(constraint => constraint.Kind is
                "precision" or "scale" or "integerWidth" or "signed" or "timezone" or "defaultTimezone").ToArray())).ToArray();
        return new(asset.Id, asset.Contract.Identity.Id, asset.Contract.Identity.Version, asset.Asset.Id, asset.BindingIndex,
            columns, formats, $"/assets/{asset.Id}/schema", $"/assets/{asset.Id}/data");
    }
}
