using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.PathHandling;
using PocketCsvReader.Compression;

namespace Packata.ResourceReaders.Inference;
public class ResourceInferenceService : IResourceInferenceService
{
    private readonly IDialectInference[] _dialectStrategies;
    private readonly ICompressionInference[] _compressionStrategies;

    private static readonly IDictionary<string, string> _compressionMappings = GetCompressionMappings();
    private static IDictionary<string, string> GetCompressionMappings()
    {
        var factory = DecompressorFactory.Streaming();
        var keys = factory.GetSupportedKeys();
        var dict = new Dictionary<string, string>(keys.Length);
        foreach (var key in keys)
            dict.Add(key, factory.GetCompression(key));
        return dict;
    }

    public static ResourceInferenceService None => _none;
    private static ResourceInferenceService _none => new ([], []);

    public static ResourceInferenceService Instance => _instance;
    private static ResourceInferenceService _instance => new (
            new IDialectInference[]
            {
                new FormatBasedDialectInference(),
                new MediaTypeBasedDialectInference(),
                new ExtensionBasedDialectInference(new ExtractExtensionFromPathsService()),
            },
            new ICompressionInference[]
            {
                new MediaTypeBasedCompressionInference(_compressionMappings),
                new ExtensionBasedCompressionInference(new ExtractExtensionFromPathsService(), _compressionMappings)
            });

    protected internal ResourceInferenceService(IDialectInference[] dialectStrategies, ICompressionInference[] compressionStrategies)
        => (_dialectStrategies, _compressionStrategies) = (dialectStrategies, compressionStrategies);

    public void Enrich(Resource resource)
    {
        if (resource.Compression is null)
        {
            foreach (var strategy in _compressionStrategies)
            {
                if (strategy.TryInfer(resource, out var compression))
                {
                    resource.Compression = compression;
                    break;
                }
            }
        }

        if (resource.Dialect is null)
        {
            foreach (var strategy in _dialectStrategies)
            {
                if (strategy.TryInfer(resource, out var dialect))
                {
                    resource.Dialect = dialect;
                    break;
                }
            }
        }
    }
}
