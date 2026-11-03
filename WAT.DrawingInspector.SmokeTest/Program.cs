using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClosedXML.Excel;
using System.Windows.Media.Imaging;
using IxMilia.Dxf;
using IxMilia.Dxf.Blocks;
using IxMilia.Dxf.Entities;
using WAT.DrawingInspector;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wat-dxf-smoke-{Guid.NewGuid():N}.dxf");
        var projectPath = path + ".watproj";
        var excelPath = path + ".xlsx";
        var pdfReportPath = path + ".report.pdf";
        var ocrFixturePath = path + ".ocr.png";
        try
        {
            var ocrWords = new[]
            {
                new OcrWord("Ø", 12, 100, 10, 10, 95),
                new OcrWord("10,5", 24, 100, 25, 10, 92),
                new OcrWord("±0,1", 53, 100, 24, 10, 90),
                new OcrWord("TOL", 12, 150, 16, 10, 97),
                new OcrWord("2X", 34, 150, 15, 10, 97),
                new OcrWord("20", 53, 150, 20, 10, 97),
                new OcrWord("45", 12, 200, 18, 10, 25)
            };
            var candidates = DimensionCandidateService.FindCandidates(ocrWords);
            if (candidates.Count != 1 ||
                candidates[0].SuggestedValue != "10,5" ||
                !candidates[0].OcrText.Contains("±0,1", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "OCR dimension candidate recognition must keep one unambiguous diameter/tolerance line and skip multi-value/low-confidence lines.");

            Console.WriteLine(
                $"OCR dimension candidate smoke test passed: one reviewed candidate '{candidates[0].OcrText}' at %{candidates[0].Confidence:0} confidence.");

            using (var visual = new DrawingVisual())
            {
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1500, 600));
                    var typeface = new Typeface(
                        new FontFamily("Arial"),
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal);

                    var dimensions = new[]
                    {
                        new FormattedText("10.5", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 110, Brushes.Black, 1.0),
                        new FormattedText("25.0", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 110, Brushes.Black, 1.0),
                        new FormattedText("R 5.0", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 110, Brushes.Black, 1.0)
                    };

                    drawing.DrawText(dimensions[0], new Point(100, 65));
                    drawing.DrawText(dimensions[1], new Point(100, 235));
                    drawing.DrawText(dimensions[2], new Point(650, 65));
                }

                var bitmap = new RenderTargetBitmap(1500, 600, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(ocrFixturePath);
                encoder.Save(output);
            }

            var realOcrWords = OcrService.ReadWords(ocrFixturePath);
            var realOcrCandidates = DimensionCandidateService.FindCandidates(realOcrWords);
            if (realOcrWords.Count == 0 || realOcrCandidates.Count == 0)
                throw new InvalidOperationException(
                    "Tesseract OCR integration did not read the generated technical drawing dimension fixture.");

            Console.WriteLine(
                $"Tesseract OCR integration smoke test passed: {realOcrWords.Count} words; " +
                $"{realOcrCandidates.Count} dimension candidate(s); first '{realOcrCandidates[0].OcrText}'.");

            var file = new DxfFile();
            // LWPOLYLINE requires DXF R14 or newer.
            file.Header.Version = DxfAcadVersion.R2010;
            file.Entities.Add(new DxfLine(
                new DxfPoint(0, 0, 0),
                new DxfPoint(100, 60, 0)));
            file.Entities.Add(new DxfCircle(
                new DxfPoint(40, 30, 0), 12));
            file.Entities.Add(new DxfArc(
                new DxfPoint(70, 30, 0), 10, 20, 160));
            file.Entities.Add(new DxfLwPolyline(new[]
            {
                new DxfLwPolylineVertex { X = 5, Y = 5 },
                new DxfLwPolylineVertex { X = 25, Y = 5 },
                new DxfLwPolylineVertex { X = 25, Y = 20 }
            }));
            file.Entities.Add(new DxfText(
                new DxfPoint(10, 40, 0), 3.0, "WAT TEST"));

            var block = new DxfBlock { Name = "WAT-SYMBOL" };
            block.BasePoint = new DxfPoint(0, 0, 0);
            block.Entities.Add(new DxfLine(
                new DxfPoint(0, 0, 0),
                new DxfPoint(20, 0, 0)));
            block.Entities.Add(new DxfCircle(
                new DxfPoint(10, 5, 0), 3));
            file.Blocks.Add(block);

            var insert = new DxfInsert
            {
                Name = "WAT-SYMBOL",
                Location = new DxfPoint(130, 20, 0),
                XScaleFactor = 1.5,
                YScaleFactor = 1.5,
                Rotation = 30
            };
            file.Entities.Add(insert);

            // A simplified aligned dimension represented by its anonymous dimension block.
            var dimensionBlock = new DxfBlock { Name = "*D1" };
            dimensionBlock.BasePoint = new DxfPoint(0, 0, 0);
            dimensionBlock.Entities.Add(new DxfLine(
                new DxfPoint(10, 18, 0), new DxfPoint(90, 18, 0)));
            dimensionBlock.Entities.Add(new DxfLine(
                new DxfPoint(10, 0, 0), new DxfPoint(10, 18, 0)));
            dimensionBlock.Entities.Add(new DxfLine(
                new DxfPoint(90, 0, 0), new DxfPoint(90, 18, 0)));
            dimensionBlock.Entities.Add(new DxfLine(
                new DxfPoint(10, 18, 0), new DxfPoint(16, 20, 0)));
            dimensionBlock.Entities.Add(new DxfText(
                new DxfPoint(45, 20, 0), 3.0, "80"));
            file.Blocks.Add(dimensionBlock);

            var dimension = new DxfAlignedDimension
            {
                BlockName = "*D1",
                DefinitionPoint1 = new DxfPoint(10, 0, 0),
                DefinitionPoint2 = new DxfPoint(90, 0, 0),
                DefinitionPoint3 = new DxfPoint(10, 18, 0),
                TextMidPoint = new DxfPoint(50, 20, 0),
                Text = "80"
            };
            file.Entities.Add(dimension);

            file.Save(path);

            var scene = DxfDrawingService.LoadScene(path);
            if (scene.SupportedEntityCount != 8 || scene.UnsupportedEntityCount != 0)
                throw new InvalidOperationException(
                    $"Expected 8 supported DXF entities including one dimension and 0 skipped, got {scene.SupportedEntityCount} supported / {scene.UnsupportedEntityCount} skipped.");

            var rendered = DxfDrawingService.RenderPng(scene);
            if (rendered.PngBytes.Length < 100)
                throw new InvalidOperationException("DXF renderer returned an empty PNG.");

            using var stream = new MemoryStream(rendered.PngBytes);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            if (frame.PixelWidth != rendered.PixelWidth ||
                frame.PixelHeight != rendered.PixelHeight)
                throw new InvalidOperationException("Rendered PNG dimensions do not match the render result.");

            Console.WriteLine(
                $"DXF smoke test passed: {rendered.SupportedEntityCount} entities including a transformed block and dimension; " +
                $"{rendered.PixelWidth}x{rendered.PixelHeight}px; {rendered.PngBytes.Length} bytes.");

            var expectedBalloons = new Dictionary<int, List<StoredBalloon>>
            {
                [0] = new()
                {
                    new StoredBalloon(1, 40.5, 80.25, "Delik çapı", "10.000", "+0.015", "-0.000", "10.008", "Uygun", "Mastar ile kontrol"),
                    new StoredBalloon(2, 120.0, 200.0, "Mil çapı", "25.000", "+0.020", "-0.020", "25.035", "Uygun Değil", "Üst limit aşıldı")
                },
                [1] = new()
                {
                    new StoredBalloon(1, 5.0, 6.0, "Yüzey mesafesi", "8.50", "+0.10", "-0.10", "", "Beklemede", "Ölçüm bekleniyor")
                }
            };
            var expectedSequences = new Dictionary<int, int> { [0] = 3, [1] = 2 };

            ProjectFileService.Save(
                projectPath,
                path,
                "wat-smoke.dxf",
                currentPageIndex: 1,
                expectedBalloons,
                expectedSequences);

            var loadedProject = ProjectFileService.Load(projectPath);
            var sourceMatches = File.ReadAllBytes(path).SequenceEqual(loadedProject.SourceBytes);
            var balloonsMatch =
                loadedProject.BalloonsByPage.Count == expectedBalloons.Count &&
                loadedProject.BalloonsByPage[0].SequenceEqual(expectedBalloons[0]) &&
                loadedProject.BalloonsByPage[1].SequenceEqual(expectedBalloons[1]);
            var sequencesMatch =
                loadedProject.NextNumbersByPage.Count == expectedSequences.Count &&
                loadedProject.NextNumbersByPage[0] == 3 &&
                loadedProject.NextNumbersByPage[1] == 2;

            if (loadedProject.SourceFileName != "wat-smoke.dxf" ||
                loadedProject.SourceExtension != ".dxf" ||
                loadedProject.CurrentPageIndex != 1 ||
                !sourceMatches || !balloonsMatch || !sequencesMatch)
                throw new InvalidOperationException("SQLite project save/load round-trip did not preserve source drawing and page annotations.");

            Console.WriteLine(
                $"SQLite project smoke test passed: embedded {loadedProject.SourceBytes.Length} source bytes; " +
                $"{loadedProject.BalloonsByPage.Sum(p => p.Value.Count)} balloons on " +
                $"{loadedProject.BalloonsByPage.Count} pages, including measurement/tolerance metadata.");

            InspectionReportService.ExportExcel(
                excelPath,
                loadedProject.SourceFileName,
                loadedProject.BalloonsByPage);

            using (var workbook = new XLWorkbook(excelPath))
            {
                var worksheet = workbook.Worksheet("Ölçüm Raporu");
                if (worksheet.Cell(6, 3).GetString() != "Delik çapı" ||
                    worksheet.Cell(6, 8).GetString() != "Uygun" ||
                    worksheet.Cell(7, 8).GetString() != "Uygun Değil" ||
                    worksheet.Cell(8, 3).GetString() != "Yüzey mesafesi" ||
                    worksheet.Cell(8, 8).GetString() != "Beklemede")
                    throw new InvalidOperationException("Excel report did not preserve balloon measurement results.");

                if (!File.Exists(excelPath) || new FileInfo(excelPath).Length < 1000)
                    throw new InvalidOperationException("Excel report file is missing or unexpectedly small.");
            }

            Console.WriteLine("Excel report smoke test passed: 3 balloon rows; Turkish characteristics and inspection results preserved.");

            PdfInspectionReportService.Export(
                pdfReportPath,
                loadedProject.SourceFileName,
                loadedProject.BalloonsByPage);

            if (!File.Exists(pdfReportPath) ||
                new FileInfo(pdfReportPath).Length < 1000 ||
                PdfDrawingService.GetPageCount(pdfReportPath) < 1)
                throw new InvalidOperationException("PDF report is missing, empty, or cannot be parsed.");

            var renderedReportPage = PdfDrawingService.RenderPagePng(pdfReportPath, 0);
            if (renderedReportPage.Length < 100)
                throw new InvalidOperationException("PDF report page could not be rendered.");

            Console.WriteLine("PDF report smoke test passed: generated PDF opened and first page rendered.");
            return 0;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(projectPath))
                File.Delete(projectPath);
            if (File.Exists(excelPath))
                File.Delete(excelPath);
            if (File.Exists(pdfReportPath))
                File.Delete(pdfReportPath);
            if (File.Exists(ocrFixturePath))
                File.Delete(ocrFixturePath);
        }
    }
}