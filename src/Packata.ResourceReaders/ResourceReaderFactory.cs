using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders;
public class ResourceReaderFactory
{
    private Dictionary<string, IResourceReaderFactory> Factories { get; } = [];
    private IResourceInferenceService _inferenceService = ResourceInferenceService.Instance;

    public ResourceReaderFactory()
    {
        Factories.Add("table", new TabularReaderFactory());
    }

    public ResourceReaderFactory(IResourceInferenceService inference)
        : this()
    { 
        _inferenceService = inference;
    }

    public void AddOrReplaceReader(string type, string subType, IResourceReaderBuilder builder)
    {
        if (!Factories.TryGetValue(type, out var factory))
            throw new ArgumentOutOfRangeException(nameof(type));
        factory.AddOrReplaceReader(subType, builder);
    }

    public void SetHeuristic(string type, Func<Resource, string> heuristic)
    {
        if (!Factories.TryGetValue(type, out var factory))
            throw new ArgumentOutOfRangeException(nameof(type));
        factory.SetHeuristic(heuristic);
    }

    public virtual IResourceReader Create(Resource resource)
    {
        _inferenceService.Enrich(resource);
        if (!Factories.TryGetValue(resource.Type ?? "table", out var factory))
            throw new ArgumentOutOfRangeException(nameof(resource));
        return factory.Create(resource);
    }
}
