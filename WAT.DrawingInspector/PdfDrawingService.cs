namespace WAT.DrawingInspector;

public static class PdfDrawingService
{
    public static int GetPageCount(string pdfPath)
    {
        using var stream = File.OpenRead(pdfPath);
        return PDFtoImage.Conversion.GetPageCount(stream);
    }

    public static byte[] RenderPagePng(string pdfPath, int zeroBasedPageIndex)
    {
        if (zeroBasedPageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedPageIndex));

        using var input = File.OpenRead(pdfPath);
        using var output = new MemoryStream();

        var options = new PDFtoImage.RenderOptions(Dpi: 150, UseTiling: true);
        PDFtoImage.Conversion.SavePng(
            imageStream: output,
            pdfStream: input,
            page: new Index(zeroBasedPageIndex),
            options: options);

        return output.ToArray();
    }
}