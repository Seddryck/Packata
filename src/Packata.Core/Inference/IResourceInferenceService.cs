using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
internal interface IResourceInferenceService
{
    void Enrich(Resource resource);
}
