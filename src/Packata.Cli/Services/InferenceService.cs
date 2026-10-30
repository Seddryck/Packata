using Packata.Application;
using Packata.Core.Contracts;
using InferenceSelection = Packata.Application.ContractInferenceService.InferenceSelection;

namespace Packata.Cli;

/// <summary>Adapts command options and invocation-owned resource access to application inference.</summary>
internal static class InferenceService
{
    private static readonly IReadOnlyDictionary<string, InferenceOption> OptionNames = new Dictionary<string, InferenceOption>
    {
        ["--maximum-records"] = InferenceOption.MaximumRecords, ["--maximum-bytes"] = InferenceOption.MaximumBytes,
        ["--culture"] = InferenceOption.Culture, ["--header"] = InferenceOption.Header,
        ["--no-dialect-inference"] = InferenceOption.DialectInference,
        ["--sheet-name"] = InferenceOption.SheetName, ["--sheet-number"] = InferenceOption.SheetNumber
    };

    internal static async Task<InferenceSelection> InferFileAsync(ContractDocument document, DataAsset asset,
        DataEndpoint endpoint, InferSettings settings, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await ContractInferenceService.InferFileAsync(Input(document), asset, endpoint,
                Options(settings, arguments), cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedInferenceOptionsException error)
        {
            var names = OptionNames.Where(pair => error.Options.HasFlag(pair.Value)).Select(pair => pair.Key);
            throw new NotSupportedException($"Options {string.Join(", ", names)} do not apply to this resource format.");
        }
    }

    internal static async Task<InferenceSelection> InferDatabaseAsync(ContractDocument document, DataAsset asset,
        DataEndpoint endpoint, InferSettings settings, CancellationToken cancellationToken)
    {
        using var database = new CliDatabase(new CliConfiguration(), Path.GetDirectoryName(document.SourcePath) ?? "",
            document.Contract.Endpoints, settings.Connectors, settings.ConnectionEnvironment);
        try
        {
            var connection = await database.ResolveAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return await ContractInferenceService.InferDatabaseAsync(document.Contract, asset, endpoint, connection,
                database.HasOverride(endpoint.Id), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Packata.Database.DatabaseConnectionException error)
        {
            throw new CliException(error.Code + ": " + error.Message +
                " Use 'packata connector list', '--connector SCHEME=ID', or 'packata config list' to check acquisition settings. Downloads require a distribution catalog with archive URLs and hashes; configure one with 'packata config set connectors.catalog <path>'.");
        }
        catch (CliException) { throw; }
        catch { throw new CliException("Database inference failed. Check connectivity, permissions, connector compatibility and runtime credentials."); }
    }

    internal static InferenceInput Input(ContractDocument document) => new(document.Contract, new Streams(document));
    internal static ContractInferenceOptions Options(InferSettings settings, IReadOnlyList<string>? arguments = null) => new()
    {
        SuppliedOptions = OptionNames.Where(pair => arguments?.Any(argument => argument == pair.Key
            || argument.StartsWith(pair.Key + "=", StringComparison.Ordinal)) == true)
            .Aggregate(InferenceOption.None, (options, pair) => options | pair.Value),
        MaximumRecords = settings.MaximumRecords, MaximumBytes = settings.MaximumBytes,
        Header = settings.Header, Culture = settings.Culture, NoDialectInference = settings.NoDialectInference,
        SheetName = settings.SheetName, SheetNumber = settings.SheetNumber, StagingMaximumBytes = settings.StagingMaximumBytes
    };
    internal static DataAsset SelectAsset(DataContract contract, string assetId) => ContractInferenceService.SelectAsset(contract, assetId);
    internal static DataEndpoint SelectEndpoint(DataContract contract, DataAsset asset) => ContractInferenceService.SelectEndpoint(contract, asset);

    private sealed class Streams(ContractDocument document) : IInferenceStreamSource
    {
        public ValueTask<Stream> OpenAsync(DataAsset asset, DataEndpoint endpoint, long stagingMaximumBytes,
            long? workbookMaximumBytes, CancellationToken cancellationToken) =>
            document.OpenAssetStreamAsync(asset, endpoint, cancellationToken, stagingMaximumBytes, workbookMaximumBytes);
    }
}
