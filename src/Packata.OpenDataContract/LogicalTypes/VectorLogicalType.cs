namespace Packata.OpenDataContract;

public sealed class VectorLogicalType : ILogicalType
{
    private readonly Dictionary<string, object> _options;

    public VectorLogicalType(Dictionary<string, object>? options) => _options = options ?? [];

    public int? Dimensions => GetInt("dimensions");
    public string? ElementType => GetString("elementType");
    public string? DistanceMetric => GetString("distanceMetric");
    public bool? Normalized => GetBool("normalized");
    public string? EmbeddingModel => GetString("embeddingModel");
    public string? EmbeddingModelVersion => GetString("embeddingModelVersion");

    private string? GetString(string key) => _options.TryGetValue(key, out var value) ? value?.ToString() : null;
    private int? GetInt(string key) => _options.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    private bool? GetBool(string key) => _options.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;
}
