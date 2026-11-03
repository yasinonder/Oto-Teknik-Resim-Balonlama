using System.IO;
using Tesseract;

namespace WAT.DrawingInspector;

public static class OcrService
{
    public static List<OcrWord> ReadWords(string imagePath)
    {
        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessdata))
            throw new DirectoryNotFoundException(
                "tessdata klasörü bulunamadı. tools/Download-Tessdata.ps1 betiğini çalıştırın.");

        var turkishModel = Path.Combine(tessdata, "tur.traineddata");
        var englishModel = Path.Combine(tessdata, "eng.traineddata");
        if (!File.Exists(turkishModel) || !File.Exists(englishModel))
            throw new FileNotFoundException(
                "Türkçe ve İngilizce OCR modelleri eksik. tools/Download-Tessdata.ps1 betiğini çalıştırın.");

        // Official tessdata_fast models support the LSTM OCR engine only.
        using var engine = new TesseractEngine(tessdata, "tur+eng", EngineMode.LstmOnly);
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        using var iterator = page.GetIterator();

        var words = new List<OcrWord>();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
            {
                words.Add(new OcrWord(
                    text.Trim(),
                    bounds.X1,
                    bounds.Y1,
                    bounds.X2 - bounds.X1,
                    bounds.Y2 - bounds.Y1,
                    iterator.GetConfidence(PageIteratorLevel.Word)));
            }
        }
        while (iterator.Next(PageIteratorLevel.Word));

        return words;
    }
}

public sealed record OcrWord(
    string Text,
    int Left,
    int Top,
    int Width,
    int Height,
    float Confidence);