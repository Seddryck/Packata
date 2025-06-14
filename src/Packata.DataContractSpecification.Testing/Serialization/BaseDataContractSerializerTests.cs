using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.DataContractSpecification.Serialization;

namespace Packata.DataContractSpecification.Testing.Serialization;

public abstract class BaseDataContractSerializerTests
{
    protected abstract IDataContractSerializer GetSerializer();

    protected abstract string GetFormat();
}
