using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
public interface IResourceReaderBuilder
{
    void Configure(Resource resource);
    IResourceReader Build();
}
