using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

internal class ContextConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(DataContractContext);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar)
        {
            var instructions = parser.Consume<Scalar>().Value;
            return new DataContractContext { Instructions = instructions, UsesStringShorthand = true };
        }

        var source = rootDeserializer(typeof(ContextDocument)) as ContextDocument ?? new ContextDocument();
        return new DataContractContext
        {
            Instructions = source.Instructions,
            VerifiedStatements = source.VerifiedStatements,
            Constraints = source.Constraints
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var context = value as DataContractContext ?? new DataContractContext();
        if (context.UsesStringShorthand && context.VerifiedStatements.Count == 0 && context.Constraints.Count == 0)
        {
            emitter.Emit(new Scalar(context.Instructions ?? string.Empty));
            return;
        }

        serializer(new ContextDocument
        {
            Instructions = context.Instructions,
            VerifiedStatements = context.VerifiedStatements,
            Constraints = context.Constraints
        });
    }
}
