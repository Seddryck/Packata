using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage.Serialization;
public interface ISerializerFactory
{
    IDataPackageSerializer Instantiate(string extension);
    IDataPackageSerializer Instantiate(SerializationFormat format);
}
