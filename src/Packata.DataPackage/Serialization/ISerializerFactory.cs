using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Serialization;
public interface ISerializerFactory
{
    IDataPackageSerializer Instantiate(string extension);
    IDataPackageSerializer Instantiate(SerializationFormat format);
}
