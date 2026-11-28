namespace Packata.Ossie;

public sealed record OssieDialectExpression
{
    public required string Dialect { get; init; }

    public required string Expression { get; init; }
}
