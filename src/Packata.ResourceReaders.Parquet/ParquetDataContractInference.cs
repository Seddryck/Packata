using System.Buffers.Binary;
using Packata.Core.Contracts;
using Parquet.Schema;
using DataField = Packata.Core.Contracts.DataField;

namespace Packata.ResourceReaders.Parquet;

/// <summary>Options for metadata-only Parquet schema inference.</summary>
public sealed record ParquetDataContractInferenceOptions : FileDataContractInferenceOptions
{
    /// <summary>Maximum serialized footer size before Parquet.Net decodes metadata.</summary>
    /// <value>
    /// Maximum serialized footer size before Parquet.Net decodes metadata.
    /// </value>
    public int MaximumFooterBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumFields { get; init; } = 10000;
    public int MaximumDepth { get; init; } = 32;
}

/// <summary>Extracts the declared Parquet schema without opening row groups or reading column data.</summary>
public sealed class ParquetDataContractInference
{
    private readonly ParquetDataContractInferenceOptions _options;

    public ParquetDataContractInference(ParquetDataContractInferenceOptions? options = null)
    {
        _options = options ?? new();
        if (_options.MaximumFooterBytes <= 0 || _options.MaximumFields <= 0 || _options.MaximumDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Parquet metadata limits must be positive.");
    }

    public async Task<DataContractInferenceResult> InferAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await using var stream = File.OpenRead(sourcePath);
        return await InferAsync(stream, sourcePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requires a readable seekable stream. Leaves it open and restores its position, including on failure.</summary>
    public async Task<DataContractInferenceResult> InferAsync(Stream stream, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek)
            throw new NotSupportedException("Parquet inference requires a readable seekable stream; non-seekable input is not buffered.");
        var position = stream.Position;
        try
        {
            await ValidateFooterAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            await using var reader = await global::Parquet.ParquetReader.CreateAsync(stream,
                leaveStreamOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var diagnostics = new List<ContractInferenceDiagnostic>();
            var count = 0;
            var fields = reader.Schema.Fields.Select(field => Map(field, sourcePath, diagnostics, 0, ref count)).ToArray();
            return FileInference.Create(sourcePath, _options,
                new DataFormat("parquet", "application/vnd.apache.parquet"), fields, diagnostics);
        }
        finally { stream.Position = position; }
    }

    private async Task ValidateFooterAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.Length < 12) throw new InvalidDataException("Parquet file is truncated.");
        stream.Seek(-8, SeekOrigin.End);
        var trailer = new byte[8];
        await stream.ReadExactlyAsync(trailer, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(trailer);
        if (!trailer.AsSpan(4).SequenceEqual("PAR1"u8) || length > stream.Length - 12)
            throw new InvalidDataException("Parquet footer is invalid or encrypted metadata is unsupported.");
        if (length > _options.MaximumFooterBytes)
            throw new InvalidDataException("Parquet footer exceeds MaximumFooterBytes.");
    }

    private DataField Map(Field field, string parent, List<ContractInferenceDiagnostic> diagnostics, int depth, ref int count)
    {
        if (++count > _options.MaximumFields || depth >= _options.MaximumDepth)
            throw new InvalidDataException("Parquet schema exceeds the field or nesting limit.");
        var path = $"{parent}/{field.Name}";
        var physical = field.SchemaElement?.Type.ToString();
        var children = new List<DataField>();
        if (field is StructField structure)
        {
            foreach (var child in structure.Fields) children.Add(Map(child, path, diagnostics, depth + 1, ref count));
            return new(field.Name, "object", physical, Required: !field.IsNullable, Nullable: field.IsNullable, Children: children);
        }
        if (field is ListField list)
        {
            return new(field.Name, "array", physical, Required: !field.IsNullable, Nullable: field.IsNullable,
                Items: Map(list.Item, path, diagnostics, depth + 1, ref count));
        }

        if (field is not global::Parquet.Schema.DataField data || data.IsArray)
        {
            diagnostics.Add(new("PARQUET001", InferenceSeverity.Error, path,
                "Map or legacy repeated field cannot be represented faithfully; inference must not be persisted."));
            return new(field.Name, null, physical);
        }
        return MapScalar(data, path, diagnostics, physical);
    }

    private static DataField MapScalar(global::Parquet.Schema.DataField data, string path,
        List<ContractInferenceDiagnostic> diagnostics, string? physical)
    {
        var (type, format) = FileInference.MapType(data.ClrType);
        if (data is DecimalDataField decimalField)
            physical = $"{physical} DECIMAL({decimalField.Precision},{decimalField.Scale})";
        if (data is DateTimeDataField temporal)
        {
            type = temporal.IsAdjustedToUTC ? "timestamp" : "datetime";
            if (temporal.DateTimeFormat == DateTimeFormat.Date) type = "date";
            physical = $"{physical} {temporal.DateTimeFormat} utc={temporal.IsAdjustedToUTC} unit={temporal.Unit}";
        }
        if (data is TimeDataField time) physical = $"{physical} TIME({time.Precision})";
        if (type is null)
        {
            diagnostics.Add(new("PARQUET002", InferenceSeverity.Error, path,
                $"Parquet type '{data.ClrType.Name}' has no lossless canonical mapping."));
        }

        return new(data.Name, type, physical, format, Required: !data.IsNullable, Nullable: data.IsNullable);
    }
}
