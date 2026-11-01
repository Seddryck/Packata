using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Packata.Http;

public static class AcceptNegotiation
{
    private sealed record Candidate(IAssetRepresentation Representation, double Quality, int Specificity);

    /// <summary>Most specific matching range determines quality. Quality, specificity and deterministic
    /// server preference then select a winner. Exact q=0 exclusions override accepting wildcards.</summary>
    public static IAssetRepresentation? Select(string? accept, IEnumerable<IAssetRepresentation> available)
    {
        var ranges = Parse(accept);
        if (ranges is null) return null;
        return available.Select(representation => Rank(representation, ranges)).Where(item => item.Quality > 0)
            .OrderByDescending(item => item.Quality).ThenByDescending(item => item.Specificity)
            .ThenBy(item => Preference(item.Representation.MediaType))
            .ThenBy(item => item.Representation.MediaType, StringComparer.Ordinal)
            .Select(item => item.Representation).FirstOrDefault();
    }

    private static List<MediaTypeWithQualityHeaderValue>? Parse(string? accept)
    {
        var ranges = new List<MediaTypeWithQualityHeaderValue>();
        if (string.IsNullOrWhiteSpace(accept)) accept = "*/*";
        foreach (var part in accept.Split(','))
        {
            if (!MediaTypeWithQualityHeaderValue.TryParse(part.Trim(), out var range) || !ValidRange(range)) return null;
            ranges.Add(range);
        }
        return ranges;
    }

    private static bool ValidRange(MediaTypeWithQualityHeaderValue range)
    {
        var media = range.MediaType?.Split('/');
        if (media is not { Length: 2 } || (media[0] == "*" && media[1] != "*")) return false;
        var weights = range.Parameters.Where(parameter => parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase)).ToArray();
        return weights.Length <= 1 && weights.All(weight => Regex.IsMatch(weight.Value ?? "",
            "^(0(?:\\.[0-9]{0,3})?|1(?:\\.0{0,3})?)$", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250)));
    }

    private static Candidate Rank(IAssetRepresentation representation, List<MediaTypeWithQualityHeaderValue> ranges)
    {
        var best = ranges.Select((range, index) =>
        {
            var specificity = new[] { representation.MediaType }.Concat(representation.Aliases)
                .Select(media => Match(range, media)).Max();
            return (Specificity: specificity, Quality: range.Quality ?? 1, Index: index);
        }).Where(item => item.Specificity >= 0).OrderByDescending(item => item.Specificity)
            .ThenBy(item => item.Index).FirstOrDefault((-1, 0, 0));
        return new(representation, best.Quality, best.Specificity);
    }

    private static bool SupportedParameter(NameValueHeaderValue parameter, string media)
        => parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase)
            || (parameter.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)
                && parameter.Value?.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase) == true
                && media == "application/x-ndjson");

    private static int Match(MediaTypeWithQualityHeaderValue range, string media)
    {
        if (range.Parameters.Any(parameter => !SupportedParameter(parameter, media))) return -1;
        var parts = (range.MediaType ?? "").Split('/');
        var target = media.Split('/');
        var parameters = range.Parameters.Count(parameter => !parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase));
        if (parts[0] == "*" && parts[1] == "*") return parameters;
        if (!parts[0].Equals(target[0], StringComparison.OrdinalIgnoreCase)) return -1;
        if (parts[1] == "*") return 10 + parameters;
        return parts[1].Equals(target[1], StringComparison.OrdinalIgnoreCase) ? 20 + parameters : -1;
    }

    private static int Preference(string media) => media switch
    {
        "application/x-ndjson" => 0, "application/avro" => 1, "application/vnd.apache.arrow.stream" => 2, _ => 3
    };
}
