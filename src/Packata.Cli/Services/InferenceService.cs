using Packata.Application;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using InferenceSelection = Packata.Application.ContractInferenceService.InferenceSelection;

namespace Packata.Cli;

/// <summary>Adapts command options and invocation-owned resource access to application inference.</summary>
internal static class InferenceService
{
    internal static Task<InferenceSelection> InferFileAsync(ContractDocument document, DataAsset asset,
        DataEndpoint endpoint, InferSettings settings, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var format = new DataFormatResolver().Resolve(endpoint).Name;
        if (format is "xls" or "xlsx") RejectOptions(arguments, ["--culture", "--no-dialect-inference"]);
        else
        {
            RejectOptions(arguments, ["--sheet-name", "--sheet-number"]);
            if (format is "parquet" or "pqt")
                RejectOptions(arguments, ["--maximum-records", "--maximum-bytes", "--culture", "--header", "--no-dialect-inference"]);
        }
        return ContractInferenceService.InferFileAsync(Input(document), asset, endpoint, Options(settings), cancellationToken);
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
    internal static ContractInferenceOptions Options(InferSettings settings) => new()
    {
        MaximumRecords = settings.MaximumRecords, MaximumBytes = settings.MaximumBytes,
        Header = settings.Header, Culture = settings.Culture, NoDialectInference = settings.NoDialectInference,
        SheetName = settings.SheetName, SheetNumber = settings.SheetNumber, StagingMaximumBytes = settings.StagingMaximumBytes
    };
    internal static DataAsset SelectAsset(DataContract contract, string assetId) => ContractInferenceService.SelectAsset(contract, assetId);
    internal static DataEndpoint SelectEndpoint(DataContract contract, DataAsset asset) => ContractInferenceService.SelectEndpoint(contract, asset);
    private static void RejectOptions(IReadOnlyList<string> arguments, IReadOnlyList<string> unsupported)
    {
        var invalid = unsupported.Where(option => arguments.Any(argument => argument == option
            || argument.StartsWith(option + "=", StringComparison.Ordinal))).ToArray();
        if (invalid.Length > 0) throw new NotSupportedException($"Options {string.Join(", ", invalid)} do not apply to this resource format.");
    }

    private sealed class Streams(ContractDocument document) : IInferenceStreamSource
    {
        public ValueTask<Stream> OpenAsync(DataAsset asset, DataEndpoint endpoint, long stagingMaximumBytes,
            long? workbookMaximumBytes, CancellationToken cancellationToken) =>
            document.OpenAssetStreamAsync(asset, endpoint, cancellationToken, stagingMaximumBytes, workbookMaximumBytes);
    }
}
