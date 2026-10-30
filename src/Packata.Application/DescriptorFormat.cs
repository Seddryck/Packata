using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Packata.Application;

/// <summary>Shared standard selection for native parsing, validation and partially edited documents.</summary>
public static class DescriptorFormat
{
    public static string Identify(YamlMappingNode root, string requested = "auto") => requested switch
    {
        "odcs" or "datapackage" => requested,
        "auto" => ContractDocument.Child(root, "kind") is not null || ContractDocument.Child(root, "apiVersion") is not null
            ? "odcs" : "datapackage",
        _ => throw new ArgumentException("Unknown input format.", nameof(requested))
    };

    public static string Detect(string text, Uri? source = null)
    {
        try { return Identify(ContractSyntax.Parse(text).Root); }
        catch (Exception error) when (error is YamlException or FormatException or ArgumentException)
        { return Recover(text, source); }
    }

    /// <summary>A hint for broken or oversized buffers, never used to deserialize a valid mapping.</summary>
    public static string Recover(string text, Uri? source = null)
    {
        var path = string.Empty;
        if (source is not null) path = source.IsFile ? source.LocalPath : source.AbsolutePath;
        return path.Contains("odcs", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).StartsWith("contract", StringComparison.OrdinalIgnoreCase)
            || text.Contains("apiVersion", StringComparison.Ordinal) ? "odcs" : "datapackage";
    }
}
