using Didot.Core;

namespace Packata.Application.Generation;

public interface ISchemaTemplateRenderer
{
    Task<string> RenderAsync(TemplateInput template, string engine, IReadOnlyDictionary<string, object?> context,
        CancellationToken cancellationToken);
}

public sealed class DidotTemplateRenderer : ISchemaTemplateRenderer
{
    public async Task<string> RenderAsync(TemplateInput template, string engine,
        IReadOnlyDictionary<string, object?> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var renderer = TemplateEngineFactory.Default.Create(engine);
        // An ordinary serializable envelope keeps the same model root across Didot
        // engines, independently of their default WrapAsModel configuration.
        var content = await Task.Run(() => renderer.Render(template.Text, new { model = context }), cancellationToken).ConfigureAwait(false);
        // Didot's synchronous render cannot be interrupted. Never publish a cancelled result.
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }
}
