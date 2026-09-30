using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
public interface IResourceReader
{
    IDataReader ToDataReader(Resource resource);
}
