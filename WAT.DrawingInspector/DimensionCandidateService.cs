using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WAT.DrawingInspector;

public sealed record OcrDimensionCandidate(
    string OcrText,
    string SuggestedValue,
    int Left,
    int Top,
    int Width,
    int Height,
    float Confidence,
    string SuggestedTolerance = "")
{
    public string DisplayText => OcrText;
    public string ConfidenceText => $"%{Confidence:0}";
}

public static class DimensionCandidateService
{
    // Match a single dimension expression; ordinary note text is deliberately rejected.
    private static readonly Regex DimensionPattern = new(
        @"(?<![\\p{L}\\d])(?<prefix>Ø|ø|Φ|⌀|DIA|R|M)?\\s*(?<nominal>[+-]?\\d{1,5}(?:[.,]\\d{1,4})?)(?:\\s*(?:±|\\+/-)\\s*(?<tolerance>\\d{1,4}(?:[.,]\\d{1,4})?))?(?:\\s*(?:MM|CM|IN|INCH))?(?![\\d.,])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HarmlessRemainder = new(
        @"[\\s\\p{P}\\p{S}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<OcrDimensionCandidate> FindCandidates(
        IEnumerable<OcrWord> words,
        float minimumConfidence = 65f)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (minimumConfidence is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));

        var candidates = new List<OcrDimensionCandidate>();
        foreach (var line in GroupIntoLines(words))
        {
            var ordered = line.OrderBy(word => word.Left).ToArray();
            var text = string.Join(" ", ordered.Select(word => word.Text.Trim())
                .Where(value => value.Length > 0));
            if (text.Length == 0 || text.Length > 90)
                continue;

            var confidence = ordered.Average(word => word.Confidence);
            if (confidence < minimumConfidence)
                continue;

            var matches = DimensionPattern.Matches(text);
            // Never infer one value from a note containing several dimensions/numbers.
            if (matches.Count != 1)
                continue;

            var match = matches[0];
            var remainder = text.Remove(match.Index, match.Length);
            remainder = HarmlessRemainder.Replace(remainder, string.Empty);
            // Text such as "MAX 30" or "DIN EN 50347" is not a dimension callout.
            if (remainder.Length != 0)
                continue;

            var nominal = match.Groups["nominal"].Value.Trim();
            var tolerance = match.Groups["tolerance"].Value.Trim();
            if (!IsPlausibleNumeric(nominal) ||
                (tolerance.Length > 0 && !IsPlausibleNumeric(tolerance)))
                continue;

            var left = ordered.Min(word => word.Left);
            var top = ordered.Min(word => word.Top);
            var right = ordered.Max(word => word.Left + word.Width);
            var bottom = ordered.Max(word => word.Top + word.Height);

            candidates.Add(new OcrDimensionCandidate(
                text,
                nominal,
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top),
                confidence,
                tolerance));
        }

        return candidates
            .OrderBy(candidate => candidate.Top)
            .ThenBy(candidate => candidate.Left)
            .ToArray();
    }

    private static bool IsPlausibleNumeric(string value) =>
        decimal.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var number) &&
        number >= -100000m && number <= 100000m;

    private static List<List<OcrWord>> GroupIntoLines(IEnumerable<OcrWord> words)
    {
        var lines = new List<List<OcrWord>>();
        foreach (var word in words.Where(item => !string.IsNullOrWhiteSpace(item.Text) &&
                                               item.Width > 0 && item.Height > 0)
                                  .OrderBy(item => item.Top + item.Height / 2.0)
                                  .ThenBy(item => item.Left))
        {
            List<OcrWord>? bestLine = null;
            var bestDistance = double.MaxValue;

            foreach (var line in lines)
            {
                var centers = line.Select(item => item.Top + item.Height / 2.0)
                    .OrderBy(value => value).ToArray();
                var center = centers[centers.Length / 2];
                var heights = line.Select(item => (double)item.Height)
                    .OrderBy(value => value).ToArray();
                var medianHeight = heights[heights.Length / 2];
                var currentCenter = word.Top + word.Height / 2.0;
                var overlap = Math.Max(0,
                    Math.Min(word.Top + word.Height, line.Max(item => item.Top + item.Height)) -
                    Math.Max(word.Top, line.Min(item => item.Top)));
                var overlapRatio = overlap / Math.Max(1.0, Math.Min(word.Height, medianHeight));
                var distance = Math.Abs(center - currentCenter);
                var aligned = overlapRatio >= 0.25 ||
                              distance <= Math.Max(2.5, Math.Min(word.Height, medianHeight) * 0.28);

                if (aligned && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLine = line;
                }
            }

            if (bestLine is null)
                lines.Add(new List<OcrWord> { word });
            else
                bestLine.Add(word);
        }

        return lines;
    }
}
