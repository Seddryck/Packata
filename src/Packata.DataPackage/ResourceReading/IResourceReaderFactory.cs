using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
public interface IResourceReaderFactory
{
    void AddOrReplaceReader(string profile, IResourceReaderBuilder builder);
    IResourceReader Create(Resource resource);
    void SetHeuristic(Func<Resource, string> heuristic);
}
