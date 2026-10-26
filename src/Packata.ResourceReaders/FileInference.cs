using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class FileInference
{
    private const string IntegerType = "integer";
    private const string StringType = "string";
    private const string NumberType = "number";
    internal static DataContractInferenceResult Create(string path, FileDataContractInferenceOptions options,
        DataFormat format, IReadOnlyList<DataField> fields, IReadOnlyList<ContractInferenceDiagnostic> diagnostics)
    {
        var filename = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.AbsolutePath : path;
        var name = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrWhiteSpace(name)) name = "data";
        var id = options.AssetId ?? name;
        var assetName = options.AssetName ?? name;
        var endpoint = new DataEndpoint(options.EndpointId ?? $"{id}-source",
            options.EndpointName ?? $"{assetName} source", EndpointKind.File, null, new PathLocation([path]), format);
        var asset = new DataAsset(id, assetName, options.AssetPhysicalName ?? Path.GetFileName(filename),
            options.AssetDescription, AssetKind.File, new DataSchema(fields), [new EndpointBinding(endpoint.Id)]);
        return new(new DataContract(options.ContractIdentity ?? new ContractIdentity($"{id}-contract", assetName),
            options.ContractMetadata ?? new ContractMetadata($"{assetName} data contract"), [asset], [endpoint],
            new ContractGovernance()), diagnostics);
    }

    internal static (string? Type, string? Format) MapType(Type type) => Types.GetValueOrDefault(type);

    private static readonly IReadOnlyDictionary<Type, (string? Type, string? Format)> Types =
        new Dictionary<Type, (string?, string?)>
        {
            [typeof(bool)] = ("boolean", null),
            [typeof(byte)] = (IntegerType, "i16"), [typeof(sbyte)] = (IntegerType, "i16"),
            [typeof(short)] = (IntegerType, "i16"), [typeof(ushort)] = (IntegerType, "i32"),
            [typeof(int)] = (IntegerType, "i32"), [typeof(uint)] = (IntegerType, "i64"),
            [typeof(long)] = (IntegerType, "i64"),
            [typeof(float)] = (NumberType, "fp32"), [typeof(double)] = (NumberType, "fp64"),
            [typeof(decimal)] = (NumberType, null), [typeof(DateOnly)] = ("date", null),
            [typeof(TimeOnly)] = ("time", null), [typeof(TimeSpan)] = ("time", null),
            [typeof(DateTime)] = ("datetime", null), [typeof(DateTimeOffset)] = ("timestamp", null),
            [typeof(Guid)] = (StringType, "uuid"), [typeof(string)] = (StringType, null),
            [typeof(ReadOnlyMemory<char>)] = (StringType, null),
            [typeof(byte[])] = (StringType, "binary"), [typeof(ReadOnlyMemory<byte>)] = (StringType, "binary")
        };
}
