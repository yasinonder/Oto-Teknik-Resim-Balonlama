using Tesseract;

namespace WAT.DrawingInspector;

public static class OcrService
{
    public static string Read(string imagePath)
    {
        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessdata))
            throw new DirectoryNotFoundException(
                "tessdata klasörü bulunamadı. tur.traineddata ve eng.traineddata dosyalarını tessdata içine ekleyin.");

        using var engine = new TesseractEngine(tessdata, "tur+eng", EngineMode.Default);
        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }
}