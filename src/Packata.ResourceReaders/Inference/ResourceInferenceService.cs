using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.Storage;
using PocketCsvReader.Compression;

namespace Packata.ResourceReaders.Inference;
public class ResourceInferenceService : IResourceInferenceService
{
    private readonly IDialectInference[] _dialectStrategies;
    private readonly IFormatInference[] _formatStrategies;
    private readonly ICompressionInference[] _compressionStrategies;

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

    public static ResourceInferenceService None => _none;
    private static readonly ResourceInferenceService _none = new ([], [], []);

    public static ResourceInferenceService Instance => _instance;
    private static readonly ResourceInferenceService _instance = CreateInstance();

    private static ResourceInferenceService CreateInstance()
        => new (
            [
                new FormatBasedDialectInference(),
                new MediaTypeBasedDialectInference(),
                new ExtensionBasedDialectInference(new ExtractExtensionFromPathsService()),
            ],
            [
                new MediaTypeBasedFormatInference(),
                new ExtensionBasedFormatInference(new ExtractExtensionFromPathsService()),
            ],
            [
                new MediaTypeBasedCompressionInference(_compressionMappings),
                new ExtensionBasedCompressionInference(new ExtractExtensionFromPathsService(), _compressionMappings)
            ]);

    protected internal ResourceInferenceService(IDialectInference[] dialectStrategies, IFormatInference[] formatStrategies, ICompressionInference[] compressionStrategies)
        => (_dialectStrategies, _formatStrategies, _compressionStrategies) = (dialectStrategies, formatStrategies, compressionStrategies);

    public void Enrich(Resource resource)
    {
        if (string.IsNullOrEmpty(resource.Compression))
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

        if (string.IsNullOrEmpty(resource.Format))
        {
            foreach (var strategy in _formatStrategies)
            {
                if (strategy.TryInfer(resource, out var format))
                {
                    resource.Format = format;
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
