namespace Packata.Ossie;

public sealed record OssieExpression
{
    public string? Sql { get; init; }

    public IReadOnlyList<OssieDialectExpression>? Dialects { get; init; }
}
