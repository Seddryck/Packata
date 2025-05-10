using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
/// <summary>
/// Interface for strategies that infer kind information from a resource.
/// </summary>
public interface IKindInference : IInferenceStrategy<string>
{ }
