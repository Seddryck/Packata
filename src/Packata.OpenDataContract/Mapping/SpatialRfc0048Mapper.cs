using System.Collections;
using System.Globalization;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static class SpatialRfc0048Mapper
{
    private static readonly HashSet<string> Algorithms = ["spherical", "vincenty", "thomas", "andoyer", "karney"];
    private static readonly HashSet<string> OptionNames = ["subType", "crs", "dimensions", "encoding", "algorithm", "bbox", "orientation", "epoch"];

    internal static SpatialDescriptor? Map(string? type, IReadOnlyDictionary<string, object>? options, bool enabled,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (type is not ("geometry" or "geography")) return null;
        if (!enabled)
        {
            diagnostics.Add(new("SPATIAL_PROFILE", MappingSeverity.Error, path,
                "geometry and geography require the opt-in RFC-0048 profile. Official ODCS v3.2.0 does not define these logical types."));
            return null;
        }

        options ??= new Dictionary<string, object>();
        foreach (var key in options.Keys.Where(key => !OptionNames.Contains(key)))
            Error($"Unsupported spatial option '{key}' was retained in source metadata.", path, diagnostics);
        var crs = Text(options, "crs", path, diagnostics);
        var algorithm = Text(options, "algorithm", path, diagnostics);
        var orientation = Text(options, "orientation", path, diagnostics);
        var dimensions = Integer(options, "dimensions", path, diagnostics);
        var subtype = EnumOption<SpatialSubtype>(options, "subType", path, diagnostics);
        var encoding = EnumOption<SpatialEncoding>(options, "encoding", path, diagnostics);
        var epoch = Number(options, "epoch", path, diagnostics);
        var bbox = BoundingBox(options, path, diagnostics);
        var spatial = new SpatialDescriptor(type == "geometry" ? SpatialModel.Geometry : SpatialModel.Geography,
            subtype, crs, dimensions, encoding, Algorithm: algorithm, Orientation: orientation, Epoch: epoch)
        { BoundingBox = bbox };
        CheckCoordinates(spatial, path, diagnostics);
        CheckInterpretation(spatial, path, diagnostics);
        return spatial;
    }

    private static void CheckCoordinates(SpatialDescriptor spatial, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (spatial.Model == SpatialModel.Geometry && string.IsNullOrWhiteSpace(spatial.Crs))
            Error("Geometry requires an explicit CRS.", path, diagnostics);
        if (spatial.Dimensions is not (null or 2 or 3 or 4)) Error("Spatial dimensions must be 2, 3, or 4.", path, diagnostics);
        if (spatial.BoundingBox is { } bbox && bbox.Count != 2 * (spatial.Dimensions ?? 2))
            Error("Spatial bbox must contain two bounds per coordinate dimension.", path, diagnostics);
    }

    private static void CheckInterpretation(SpatialDescriptor spatial, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (spatial.Algorithm is { } algorithm && (!Algorithms.Contains(algorithm) || spatial.Model != SpatialModel.Geography))
            Error("A spatial edge algorithm requires geography and a supported RFC-0048 algorithm.", path, diagnostics);
        if (spatial.Orientation is not (null or "counterclockwise")) Error("Spatial orientation must be counterclockwise.", path, diagnostics);
        if (spatial.Encoding == SpatialEncoding.TopoJson) Error("RFC-0048 does not define TopoJSON encoding.", path, diagnostics);
    }

    private static string? Text(IReadOnlyDictionary<string, object> options, string key, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (!options.TryGetValue(key, out var value)) return null;
        if (value is string text && !string.IsNullOrWhiteSpace(text)) return text;
        Error($"Spatial option '{key}' must be a non-empty string.", path, diagnostics);
        return null;
    }

    private static T? EnumOption<T>(IReadOnlyDictionary<string, object> options, string key, string path,
        ICollection<MappingDiagnostic> diagnostics)
        where T : struct, Enum
    {
        var text = Text(options, key, path, diagnostics);
        if (text is null) return null;
        if (Enum.TryParse<T>(text, true, out var parsed) && Enum.IsDefined(parsed)
            && !char.IsDigit(text[0]) && text[0] != '-') return parsed;
        Error($"Spatial option '{key}' has an unsupported value.", path, diagnostics);
        return null;
    }

    private static int? Integer(IReadOnlyDictionary<string, object> options, string key, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (!options.TryGetValue(key, out var value)) return null;
        if (value is not (string or bool) && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer;
        Error($"Spatial option '{key}' must be an integer.", path, diagnostics);
        return null;
    }

    private static double? Number(IReadOnlyDictionary<string, object> options, string key, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (!options.TryGetValue(key, out var value)) return null;
        if (value is not (null or string or bool) && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)) return number;
        Error($"Spatial option '{key}' must be a finite number.", path, diagnostics);
        return null;
    }

    private static IReadOnlyList<double>? BoundingBox(IReadOnlyDictionary<string, object> options, string path,
        ICollection<MappingDiagnostic> diagnostics)
    {
        if (!options.TryGetValue("bbox", out var value)) return null;
        if (value is not IEnumerable sequence || value is string)
        {
            Error("Spatial bbox must be an array of finite numbers.", path, diagnostics);
            return null;
        }
        var result = new List<double>();
        foreach (var item in sequence)
        {
            if (item is null or string or bool || !double.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number))
            {
                Error("Spatial bbox must be an array of finite numbers.", path, diagnostics);
                return null;
            }
            result.Add(number);
        }
        return result;
    }

    private static void Error(string message, string path, ICollection<MappingDiagnostic> diagnostics)
        => diagnostics.Add(new("SPATIAL_OPTIONS", MappingSeverity.Error, path + "/logicalTypeOptions", message));
}
