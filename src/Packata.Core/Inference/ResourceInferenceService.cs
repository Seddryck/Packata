using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.PathHandling;

namespace Packata.Core.Inference;
public class ResourceInferenceService : IResourceInferenceService
{
    private readonly IDialectInference[] _dialectStrategies;
    private readonly ICompressionInference[] _compressionStrategies;

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
                new MediaTypeBasedCompressionInference(),
                new ExtensionBasedCompressionInference(new ExtractExtensionFromPathsService())
            });

    protected ResourceInferenceService(IDialectInference[] dialectStrategies, ICompressionInference[] compressionStrategies)
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
