using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Mapping;
using PocketCsvReader.Compression;

namespace Packata.ResourceReaders.Inference;
internal class ResourceInferenceServiceBuilder
{
    private readonly Dictionary<Type, IInferenceStrategy> _strategies = new();
    private SchemeMapperBuilder? _schemeMapperBuilder;

    public ResourceInferenceServiceBuilder AddStrategy<T>(IInferenceStrategy<T> strategy)
    {
        var concreteType = strategy.GetType(); // Use concrete type to deduplicate
        _strategies[concreteType] = (IInferenceStrategy)strategy;
        return this;
    }

    public ResourceInferenceService Build()
        => new (_strategies.Values);

    private static readonly IDictionary<string, string> _compressionMappings = GetCompressionMappings();
    private static Dictionary<string, string> GetCompressionMappings()
    {
        var factory = DecompressorFactory.Streaming();
        var keys = factory.GetSupportedKeys();
        var dict = new Dictionary<string, string>(keys.Length);
        foreach (var key in keys)
            dict.Add(key, factory.GetCompression(key));
        return dict;
    }

    private Func<string, bool> GetDatabaseSchemes()
    {
        static SchemeMapperBuilder create()
        {
            var builder = new DubUrl.Mapping.SchemeMapperBuilder();
            builder.Build();
            return builder!;
        }
        _schemeMapperBuilder ??= create();
        return (scheme) => _schemeMapperBuilder.CanHandle(scheme);
    }

    public static ResourceInferenceService None => _none;
    private static readonly ResourceInferenceService _none = new([]);

    public static ResourceInferenceService Default => _default;
    private static readonly ResourceInferenceService _default = CreateDefault();

    private static ResourceInferenceService CreateDefault()
    {
        var builder = new ResourceInferenceServiceBuilder();
        builder.AddStrategy(new FormatBasedDialectInference());
        builder.AddStrategy(new MediaTypeBasedDialectInference());
        builder.AddStrategy(new ExtensionBasedDialectInference(new ExtractExtensionFromPathsService()));
        builder.AddStrategy(new MediaTypeBasedFormatInference());
        builder.AddStrategy(new ExtensionBasedFormatInference(new ExtractExtensionFromPathsService()));
        builder.AddStrategy(new MediaTypeBasedCompressionInference(_compressionMappings));
        builder.AddStrategy(new ExtensionBasedCompressionInference(new ExtractExtensionFromPathsService(), _compressionMappings));
        builder.AddStrategy(new SchemeBasedKindInference((string value) => new string[] { "http", "https" }.Any(x => StringComparer.InvariantCultureIgnoreCase.Compare(x, value) == 0), builder.GetDatabaseSchemes()));
        return builder.Build();
    }
}
