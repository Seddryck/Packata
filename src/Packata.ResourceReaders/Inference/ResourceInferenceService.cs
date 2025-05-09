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
    private readonly IReadOnlyCollection<IInferenceStrategy> _strategies;

    protected internal ResourceInferenceService(IEnumerable<IInferenceStrategy> strategies)
        => _strategies = strategies.ToArray();

    private static void InferIfUnset<T>(
        Resource resource,
        Func<Resource, T?> getter,
        Action<Resource, T> setter,
        IEnumerable<IInferenceStrategy<T>> strategies,
        Func<T?, bool> isUnset)
    {
        if (isUnset(getter(resource)))
        {
            foreach (var strategy in strategies)
            {
                if (strategy.TryInfer(resource, out var value))
                {
                    setter(resource, value);
                    break;
                }
            }
        }
    }

    public void Enrich(Resource resource)
    {
        InferIfUnset(
            resource,
            r => r.Compression,
            (r, v) => r.Compression = v,
            _strategies.OfType<ICompressionInference>(),
            string.IsNullOrEmpty
        );

        InferIfUnset(
            resource,
            r => r.Format,
            (r, v) => r.Format = v,
            _strategies.OfType<IFormatInference>(),
            string.IsNullOrEmpty
        );

        InferIfUnset<TableDelimitedDialect>(
            resource,
            r => r.Dialect as TableDelimitedDialect,
            (r, v) => r.Dialect = v,
            _strategies.OfType<IDialectInference>(),
            v => v == null
        );

        InferIfUnset(
            resource,
            r => r.Kind,
            (r, v) => r.Kind = v,
            _strategies.OfType<IKindInference>(),
            v => v == null
        );
    }
}
