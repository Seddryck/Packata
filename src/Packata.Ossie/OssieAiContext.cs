namespace Packata.Ossie;

public sealed record OssieAiContext
{
    public OssieAiContext(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public OssieAiContext(OssieAiContextDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Details = details;
    }

    public string? Text { get; }

    public OssieAiContextDetails? Details { get; }
}
