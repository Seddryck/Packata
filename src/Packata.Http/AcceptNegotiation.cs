using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Packata.Http;

public static class AcceptNegotiation
{
    /// <summary>Most specific matching range determines quality per representation; quality then
    /// specificity then server preference (NDJSON, Avro, Arrow, lexical media type) selects a winner.
    /// Exact q=0 excludes a representation even under an accepting wildcard.</summary>
    public static IAssetRepresentation? Select(string? accept, IEnumerable<IAssetRepresentation> available)
    {
        var ranges = new List<MediaTypeWithQualityHeaderValue>();
        if (string.IsNullOrWhiteSpace(accept)) accept = "*/*";
        foreach (var part in accept.Split(','))
        {
            if (!MediaTypeWithQualityHeaderValue.TryParse(part.Trim(), out var range)) return null;
            var weights = range.Parameters.Where(parameter => parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (weights.Length > 1 || weights.Any(weight => !Regex.IsMatch(weight.Value ?? "", "^(0(?:\\.[0-9]{0,3})?|1(?:\\.0{0,3})?)$"))) return null;
            if (range.Quality is { } quality && (quality < 0 || quality > 1 || !double.IsFinite(quality))) return null;
            var media = range.MediaType?.Split('/');
            if (media is not { Length: 2 } || (media[0] == "*" && media[1] != "*")) return null;
            ranges.Add(range);
        }
        return available.Select(representation =>
        {
            var best = ranges.Select((range, index) =>
            {
                var specificity = new[] { representation.MediaType }.Concat(representation.Aliases)
                    .Select(media => Match(range, media)).Max();
                return (Specificity: specificity, Quality: range.Quality ?? 1, Index: index);
            }).Where(item => item.Specificity >= 0)
                .OrderByDescending(item => item.Specificity).ThenBy(item => item.Index).FirstOrDefault((-1, 0, 0));
            return (Representation: representation, best.Quality, best.Specificity);
        }).Where(item => item.Quality > 0)
            .OrderByDescending(item => item.Quality).ThenByDescending(item => item.Specificity)
            .ThenBy(item => Preference(item.Representation.MediaType))
            .ThenBy(item => item.Representation.MediaType, StringComparer.Ordinal)
            .Select(item => item.Representation).FirstOrDefault();
    }

    private static int Match(MediaTypeWithQualityHeaderValue range, string media)
    {
        // Encoders expose no media parameters other than UTF-8 for row JSON.
        if (range.Parameters.Any(parameter => !parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase)
            && !(parameter.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)
                && parameter.Value?.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase) == true
                && media == "application/x-ndjson"))) return -1;
        var parts = range.MediaType!.Split('/');
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
