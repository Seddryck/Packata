using Packata.Core.Contracts;

namespace Packata.Application;

/// <summary>Host-owned acquisition with explicit staging and workbook limits.</summary>
public interface IInferenceStreamSource
{
    ValueTask<Stream> OpenAsync(DataAsset asset, DataEndpoint endpoint, long stagingMaximumBytes,
        long? workbookMaximumBytes, CancellationToken cancellationToken);
}

public sealed record InferenceInput(DataContract Contract, IInferenceStreamSource Streams)
{
    internal ValueTask<Stream> OpenAssetStreamAsync(DataAsset asset, DataEndpoint endpoint,
        CancellationToken cancellationToken, long stagingMaximumBytes = 256L * 1024 * 1024, long? workbookMaximumBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Streams.OpenAsync(asset, endpoint, stagingMaximumBytes, workbookMaximumBytes, cancellationToken);
    }
}

public sealed record ContractInferenceOptions
{
    public InferenceOptions SuppliedOptions { get; init; }
    public int MaximumRecords { get; init; } = 1000;
    public long? MaximumBytes { get; init; }
    public string Culture { get; init; } = string.Empty;
    public string Header { get; init; } = "auto";
    public bool NoDialectInference { get; init; }
    public string? SheetName { get; init; }
    public int? SheetNumber { get; init; }
    public long StagingMaximumBytes { get; init; } = 256L * 1024 * 1024;

    public void Validate()
    {
        ValidateSuppliedOptions(SuppliedOptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StagingMaximumBytes);
        ValidateSampling();
        ValidateWorksheet();
    }

    private static void ValidateSuppliedOptions(InferenceOptions suppliedOptions)
    {
        if ((suppliedOptions & ~InferenceCapabilities.AllOptions) != 0)
            throw new ArgumentOutOfRangeException(nameof(suppliedOptions), "SuppliedOptions contains unknown inference options.");
    }

    private static void ValidateMaximumBytes(long? maximumBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "MaximumBytes must be positive when specified.");
    }

    private void ValidateSampling()
    {
        ValidateMaximumBytes(MaximumBytes);
        if (Header.ToLowerInvariant() is not ("auto" or "present" or "absent"))
            throw new ArgumentException("Header mode must be auto, present or absent.");
        if (!string.IsNullOrWhiteSpace(Culture)) _ = System.Globalization.CultureInfo.GetCultureInfo(Culture);
    }

    private void ValidateWorksheet()
    {
        if (SheetNumber <= 0 || (SheetNumber is not null && SheetName is not null) || (SheetName is not null && string.IsNullOrWhiteSpace(SheetName)))
            throw new ArgumentException("Select a nonempty worksheet name or a positive worksheet number.");
    }
}

public enum DescriptorDocumentKind { DataPackageJson, DataPackageYaml, OpenDataContractYaml }
public sealed record EditableContract(string OriginalText, DescriptorDocumentKind Kind);
public sealed record QualityReportSource(string Source, string OriginalText, string RunnerVersion, string Process);
