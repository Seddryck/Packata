using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Packata.Core.ResourceReading;
public class ResourceReaderFactory
{
    private Dictionary<string, IResourceReaderFactory> Factories { get; } = new();

    public ResourceReaderFactory()
    {
        Factories.Add("table", new TableReaderFactory());
    }

    public void AddOrReplaceReader(string type, string subType, IResourceReaderBuilder builder)
    {
        if (!Factories.TryGetValue(type, out var factory))
            throw new ArgumentOutOfRangeException();
        factory.AddOrReplaceReader(subType, builder);
    }

    public void SetHeuristic(string type, Func<Resource, string> heuristic)
    {
        if (!Factories.TryGetValue(type, out var factory))
            throw new ArgumentOutOfRangeException();
        factory.SetHeuristic(heuristic);
    }

    public IResourceReader Create(Resource resource)
    {
        if (!Factories.TryGetValue(resource.Type ?? "table", out var factory))
            throw new ArgumentOutOfRangeException();
        return factory.Create(resource);
    }
}
