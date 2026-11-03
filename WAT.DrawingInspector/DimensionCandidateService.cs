using System;
using System.Collections.Generic;
using System.Globalization;
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
    string SuggestedTolerance = "",
    bool IsDimensionCandidate = false)
{
    public string DisplayText => OcrText;
    public string ConfidenceText => $"%{Confidence:0}";
    public string TypeText => IsDimensionCandidate ? "ÖLÇÜ ADAYI" : "OKUNAN METİN";
}

public static class DimensionCandidateService
{
    // One drawing callout: diameter/radius/thread prefix, nominal, optional symmetric
    // or bilateral tolerance, optional ISO fit class and unit.
    private static readonly Regex DimensionPattern = new(
        @"(?<![\p{L}\d])(?<prefix>Ø|ø|Φ|⌀|DIA|R|M)?\s*(?<nominal>[+-]?\d{1,5}(?:[.,]\d{1,4})?)(?:\s*(?:±|\+/-)\s*(?<tolerance>\d{1,4}(?:[.,]\d{1,4})?)|\s*(?<upper>\+\d{1,4}(?:[.,]\d{1,4})?)\s*/\s*(?<lower>-\d{1,4}(?:[.,]\d{1,4})?))?(?:\s*(?<fit>[A-HJ-NP-Z]{1,2}\d{1,2}))?(?:\s*(?:MM|CM|IN|INCH))?(?![\d.,])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HarmlessRemainder = new(
        @"[\s\p{P}\p{S}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<OcrDimensionCandidate> FindCandidates(
        IEnumerable<OcrWord> words,
        float minimumConfidence = 55f)
    {
        return ReadTextLines(words, minimumConfidence)
            .Where(item => item.IsDimensionCandidate)
            .ToArray();
    }

    /// <summary>
    /// Returns all legible OCR lines, not just lines that pass the dimension heuristic.
    /// This keeps recognised text visible to the operator when automatic classification fails.
    /// </summary>
    public static IReadOnlyList<OcrDimensionCandidate> ReadTextLines(
        IEnumerable<OcrWord> words,
        float minimumConfidence = 20f)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (minimumConfidence is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));

        var rows = new List<OcrDimensionCandidate>();
        foreach (var segment in GroupIntoTextSegments(words))
        {
            var ordered = segment.OrderBy(word => word.Left).ToArray();
            var text = string.Join(" ", ordered.Select(word => word.Text.Trim())
                .Where(value => value.Length > 0));
            if (text.Length == 0 || text.Length > 140)
                continue;

            var confidence = ordered.Average(word => word.Confidence);
            if (confidence < minimumConfidence)
                continue;

            var parsed = TryParseDimension(text, confidence);
            var left = ordered.Min(word => word.Left);
            var top = ordered.Min(word => word.Top);
            var right = ordered.Max(word => word.Left + word.Width);
            var bottom = ordered.Max(word => word.Top + word.Height);

            rows.Add(new OcrDimensionCandidate(
                text,
                parsed?.Nominal ?? string.Empty,
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top),
                confidence,
                parsed?.Tolerance ?? string.Empty,
                parsed is not null));
        }

        return rows
            .OrderByDescending(item => item.IsDimensionCandidate)
            .ThenBy(item => item.Top)
            .ThenBy(item => item.Left)
            .Take(500)
            .ToArray();
    }

    private static (string Nominal, string Tolerance)? TryParseDimension(string text, float confidence)
    {
        if (confidence < 35f)
            return null;

        var matches = DimensionPattern.Matches(text);
        if (matches.Count != 1)
            return null;

        var match = matches[0];
        var remainder = text.Remove(match.Index, match.Length);
        remainder = HarmlessRemainder.Replace(remainder, string.Empty);
        if (remainder.Length != 0)
            return null;

        var nominal = match.Groups["nominal"].Value.Trim();
        if (!IsPlausibleNumeric(nominal))
            return null;

        var tolerance = match.Groups["tolerance"].Value.Trim();
        if (match.Groups["upper"].Success && match.Groups["lower"].Success)
            tolerance = match.Groups["upper"].Value + "/" + match.Groups["lower"].Value;

        if (tolerance.Length > 0 &&
            !match.Groups["upper"].Success &&
            !IsPlausibleNumeric(tolerance))
            return null;

        // An isolated number is also a valid drawing dimension candidate.
        return (nominal, tolerance);
    }

    private static bool IsPlausibleNumeric(string value) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var number) &&
        number >= -100000m && number <= 100000m;

    private static List<List<OcrWord>> GroupIntoTextSegments(IEnumerable<OcrWord> words)
    {
        var lines = new List<List<OcrWord>>();
        foreach (var word in words.Where(item => !string.IsNullOrWhiteSpace(item.Text) &&
                                               item.Width > 0 && item.Height > 0)
                                  .OrderBy(item => item.Top + item.Height / 2.0)
                                  .ThenBy(item => item.Left))
        {
            var centerY = word.Top + word.Height / 2.0;
            List<OcrWord>? bestLine = null;
            var bestDistance = double.MaxValue;

            foreach (var line in lines)
            {
                var heights = line.Select(item => (double)item.Height).OrderBy(value => value).ToArray();
                var medianHeight = heights[heights.Length / 2];
                var centers = line.Select(item => item.Top + item.Height / 2.0).OrderBy(value => value).ToArray();
                var lineCenter = centers[centers.Length / 2];
                var distance = Math.Abs(lineCenter - centerY);
                if (distance <= Math.Max(3.0, Math.Max(medianHeight, word.Height) * 0.42) &&
                    distance < bestDistance)
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

        // Separate dimension callouts that happen to share one horizontal baseline.
        var segments = new List<List<OcrWord>>();
        foreach (var line in lines)
        {
            List<OcrWord>? current = null;
            var currentRight = 0;
            var heights = line.Select(word => (double)word.Height).OrderBy(value => value).ToArray();
            var medianHeight = heights[heights.Length / 2];
            var gapLimit = Math.Max(42.0, medianHeight * 3.2);

            foreach (var word in line.OrderBy(item => item.Left))
            {
                var gap = current is null ? 0 : word.Left - currentRight;
                if (current is null || gap > gapLimit)
                {
                    current = new List<OcrWord>();
                    segments.Add(current);
                }

                current.Add(word);
                currentRight = Math.Max(currentRight, word.Left + word.Width);
            }
        }

        return segments;
    }
}
