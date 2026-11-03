using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tesseract;

namespace WAT.DrawingInspector;

public static class OcrService
{
    /// <summary>
    /// Reads technical drawing text with more than one page segmentation strategy.
    /// Coordinates are mapped back to the original input image.
    /// </summary>
    public static List<OcrWord> ReadWords(string imagePath, bool selectedArea = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("OCR için çizim görüntüsü bulunamadı.", imagePath);

        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessdata))
            throw new DirectoryNotFoundException(
                $"OCR dil klasörü bulunamadı: {tessdata}. Paket içindeki tessdata klasörünü kontrol edin.");

        var turkishModel = Path.Combine(tessdata, "tur.traineddata");
        var englishModel = Path.Combine(tessdata, "eng.traineddata");
        if (!File.Exists(turkishModel) || !File.Exists(englishModel))
            throw new FileNotFoundException(
                $"Türkçe/İngilizce OCR modelleri bulunamadı. Beklenen klasör: {tessdata}");

        using var engine = new TesseractEngine(tessdata, "tur+eng", EngineMode.LstmOnly);
        engine.SetVariable("user_defined_dpi", "300");
        engine.SetVariable("preserve_interword_spaces", "1");

        using var original = Pix.LoadFromFile(imagePath);
        using var grayscale = original.Depth == 32
            ? original.ConvertRGBToGray(0.299f, 0.587f, 0.114f)
            : original.Depth == 8
                ? original.Clone()
                : original.ConvertTo8(0);

        // Small dimension lettering in PDF/raster drawings is commonly below the
        // preferred OCR character height. Upscale before recognition, but cap memory use.
        var longestSide = Math.Max(grayscale.Width, grayscale.Height);
        var scale = selectedArea
            ? Math.Clamp(2600.0 / Math.Max(1, longestSide), 1.5, 4.0)
            : longestSide <= 3000
                ? 2.0
                : longestSide <= 5200
                    ? 1.35
                    : 1.0;

        using var prepared = Math.Abs(scale - 1.0) < 0.001
            ? grayscale.Clone()
            : grayscale.Scale((float)scale, (float)scale);

        var results = new List<OcrWord>();
        var modes = selectedArea
            ? new[] { PageSegMode.SingleLine, PageSegMode.SparseText, PageSegMode.Auto }
            : new[] { PageSegMode.SparseText, PageSegMode.Auto };

        foreach (var mode in modes)
            results.AddRange(ReadPageWords(engine, prepared, mode, scale));

        // Keep the highest-confidence duplicate when different page-layout modes
        // recognise the same word in essentially the same place.
        return MergeOverlappingWords(results);
    }

    private static List<OcrWord> ReadPageWords(
        TesseractEngine engine,
        Pix image,
        PageSegMode mode,
        double scale)
    {
        var words = new List<OcrWord>();
        using var page = engine.Process(image, mode);
        using var iterator = page.GetIterator();
        iterator.Begin();

        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
            {
                words.Add(new OcrWord(
                    text.Trim(),
                    (int)Math.Round(bounds.X1 / scale),
                    (int)Math.Round(bounds.Y1 / scale),
                    Math.Max(1, (int)Math.Round((bounds.X2 - bounds.X1) / scale)),
                    Math.Max(1, (int)Math.Round((bounds.Y2 - bounds.Y1) / scale)),
                    iterator.GetConfidence(PageIteratorLevel.Word)));
            }
        }
        while (iterator.Next(PageIteratorLevel.Word));

        return words;
    }

    private static List<OcrWord> MergeOverlappingWords(IEnumerable<OcrWord> source)
    {
        var merged = new List<OcrWord>();
        foreach (var candidate in source
                     .Where(word => !string.IsNullOrWhiteSpace(word.Text) && word.Width > 0 && word.Height > 0)
                     .OrderByDescending(word => word.Confidence))
        {
            var duplicateIndex = merged.FindIndex(existing =>
                string.Equals(Normalize(existing.Text), Normalize(candidate.Text),
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(CenterX(existing) - CenterX(candidate)) <= Math.Max(5, Math.Max(existing.Width, candidate.Width) * 0.45) &&
                Math.Abs(CenterY(existing) - CenterY(candidate)) <= Math.Max(4, Math.Max(existing.Height, candidate.Height) * 0.65));

            if (duplicateIndex < 0)
                merged.Add(candidate);
            else if (candidate.Confidence > merged[duplicateIndex].Confidence)
                merged[duplicateIndex] = candidate;
        }

        return merged
            .OrderBy(word => word.Top)
            .ThenBy(word => word.Left)
            .ToList();
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static double CenterX(OcrWord word) => word.Left + word.Width / 2.0;
    private static double CenterY(OcrWord word) => word.Top + word.Height / 2.0;
}

public sealed record OcrWord(
    string Text,
    int Left,
    int Top,
    int Width,
    int Height,
    float Confidence);
