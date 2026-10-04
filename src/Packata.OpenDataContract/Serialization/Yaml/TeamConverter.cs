using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

internal class TeamConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Team);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is SequenceStart)
        {
            var members = rootDeserializer(typeof(List<TeamMember>)) as List<TeamMember> ?? [];
            return new Team { Members = members, UsesDeprecatedArrayStructure = true };
        }

        var source = rootDeserializer(typeof(TeamDocument)) as TeamDocument ?? new TeamDocument();
        return new Team
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Members = source.Members,
            AuthoritativeDefinitions = source.AuthoritativeDefinitions,
            Tags = source.Tags,
            CustomProperties = source.CustomProperties
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var team = value as Team ?? new Team();
        serializer(new TeamDocument
        {
            Id = team.Id,
            Name = team.Name,
            Description = team.Description,
            Members = team.Members,
            AuthoritativeDefinitions = team.AuthoritativeDefinitions,
            Tags = team.Tags,
            CustomProperties = team.CustomProperties
        });
    }
}
