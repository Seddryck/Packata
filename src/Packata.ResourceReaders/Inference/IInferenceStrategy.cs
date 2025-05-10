using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public interface IInferenceStrategy<T> : IInferenceStrategy
{
    bool TryInfer(Resource resource, [NotNullWhen(true)] out T? value);
}

public interface IInferenceStrategy
{ }
