using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
/// <summary>
/// Interface for strategies that infer table dialect information from a resource.
/// </summary>
public interface IDialectInference : IInferenceStrategy<TableDialect>
{ }
