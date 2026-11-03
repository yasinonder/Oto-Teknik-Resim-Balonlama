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
    float Confidence)
{
    public string DisplayText => OcrText;
    public string ConfidenceText => $"%{Confidence:0}";
}

public static class DimensionCandidateService
{
    private static readonly Regex SingleDimensionPattern = new(
        @"(?<![\p{L}\d])(?:Ø|ø|Φ|⌀|DIA|R|M)?\s*[+-]?\d{1,5}(?:[.,]\d{1,4})?(?:\s*(?:±|\+/-)\s*\d{1,4}(?:[.,]\d{1,4})?)?(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<OcrDimensionCandidate> FindCandidates(
        IEnumerable<OcrWord> words,
        float minimumConfidence = 55f)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (minimumConfidence is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));

        var candidates = new List<OcrDimensionCandidate>();
        foreach (var line in GroupIntoLines(words))
        {
            var text = string.Join(" ", line.OrderBy(word => word.Left)
                .Select(word => word.Text.Trim())
                .Where(value => value.Length > 0));
            var confidence = line.Average(word => word.Confidence);
            if (confidence < minimumConfidence || text.Length == 0)
                continue;

            var matches = SingleDimensionPattern.Matches(text);
            // Multiple numeric items in one OCR line are ambiguous (e.g. thread callouts
            // and coordinate pairs). Leave them for manual review instead of guessing.
            if (matches.Count != 1)
                continue;

            var match = matches[0];
            var matchedText = match.Value.Trim();
            var suggestedValue = matchedText
                .Replace("Ø", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("ø", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Φ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("⌀", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("DIA", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("R", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("M", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            var toleranceIndex = suggestedValue.IndexOf('±');
            if (toleranceIndex < 0)
                toleranceIndex = suggestedValue.IndexOf("+/-", StringComparison.Ordinal);
            if (toleranceIndex >= 0)
                suggestedValue = suggestedValue[..toleranceIndex].Trim();

            var left = line.Min(word => word.Left);
            var top = line.Min(word => word.Top);
            var right = line.Max(word => word.Left + word.Width);
            var bottom = line.Max(word => word.Top + word.Height);

            candidates.Add(new OcrDimensionCandidate(
                text, suggestedValue, left, top, Math.Max(1, right - left),
                Math.Max(1, bottom - top), confidence));
        }

        return candidates
            .OrderBy(candidate => candidate.Top)
            .ThenBy(candidate => candidate.Left)
            .ToArray();
    }

    private static List<List<OcrWord>> GroupIntoLines(IEnumerable<OcrWord> words)
    {
        var lines = new List<List<OcrWord>>();
        foreach (var word in words.Where(item => !string.IsNullOrWhiteSpace(item.Text) &&
                                               item.Width > 0 && item.Height > 0)
                                  .OrderBy(item => item.Top + item.Height / 2.0)
                                  .ThenBy(item => item.Left))
        {
            var centerY = word.Top + word.Height / 2.0;
            var target = lines.FirstOrDefault(line =>
            {
                var lineCenter = line.Average(item => item.Top + item.Height / 2.0);
                var lineHeight = line.Average(item => item.Height);
                var tolerance = Math.Max(4.0, Math.Max(lineHeight, word.Height) * 0.55);
                return Math.Abs(lineCenter - centerY) <= tolerance;
            });

            if (target is null)
                lines.Add(new List<OcrWord> { word });
            else
                target.Add(word);
        }

        return lines;
    }
}
