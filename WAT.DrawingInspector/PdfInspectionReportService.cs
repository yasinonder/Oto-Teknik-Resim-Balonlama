using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace WAT.DrawingInspector;

public static class PdfInspectionReportService
{
    private static readonly string[] Headers =
    {
        "Sayfa", "Balon No", "Karakteristik", "Nominal",
        "Alt Tol.", "Üst Tol.", "Ölçülen", "Sonuç",
        "X (px)", "Y (px)", "Notlar"
    };

    private static readonly double[] ColumnWidthsCm =
    {
        1.2, 1.4, 4.4, 2.0, 1.8, 1.8, 2.0, 2.2, 1.5, 1.5, 5.2
    };

    public static void Export(
        string targetPath,
        string sourceDrawingName,
        IReadOnlyDictionary<int, List<StoredBalloon>> balloonsByPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(balloonsByPage);

        var fullPath = Path.GetFullPath(targetPath);
        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("PDF rapor klasörü belirlenemedi.");
        Directory.CreateDirectory(folder);

        var document = new Document();
        document.Info.Title = "WAT MOTOR A.Ş. Teknik Resim Ölçüm Raporu";
        document.Info.Subject = "Balon bazlı ölçüm ve tolerans sonuçları";
        document.Info.Author = "WAT MOTOR A.Ş.";
        document.Styles["Normal"].Font.Name = "Arial";
        document.Styles["Normal"].Font.Size = 8;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(0.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(0.8);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.0);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.0);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.5);

        var title = section.AddParagraph("WAT MOTOR A.Ş.");
        title.Format.Font.Name = "Arial";
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Colors.DarkSlateGray;
        title.Format.SpaceAfter = Unit.FromCentimeter(0.05);

        var subtitle = section.AddParagraph("TEKNİK RESİM BALONLAMA VE ÖLÇÜM RAPORU");
        subtitle.Format.Font.Name = "Arial";
        subtitle.Format.Font.Size = 11;
        subtitle.Format.Font.Bold = true;
        subtitle.Format.SpaceAfter = Unit.FromCentimeter(0.25);

        var sourceLine = section.AddParagraph("Kaynak çizim: " + Path.GetFileName(sourceDrawingName ?? string.Empty));
        sourceLine.Format.Font.Size = 8;
        sourceLine.Format.SpaceAfter = Unit.FromCentimeter(0.05);

        var dateLine = section.AddParagraph("Rapor tarihi: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR")));
        dateLine.Format.Font.Size = 8;
        dateLine.Format.SpaceAfter = Unit.FromCentimeter(0.3);

        var table = section.AddTable();
        table.Borders.Visible = true;
        table.Borders.Width = 0.4;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = Unit.FromCentimeter(0.08);
        table.RightPadding = Unit.FromCentimeter(0.08);
        table.TopPadding = Unit.FromCentimeter(0.06);
        table.BottomPadding = Unit.FromCentimeter(0.06);
        table.Format.Font.Name = "Arial";
        table.Format.Font.Size = 7;

        foreach (var width in ColumnWidthsCm)
            table.AddColumn(Unit.FromCentimeter(width));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Colors.DarkSlateGray;
        header.Format.Font.Bold = true;
        header.Format.Font.Color = Colors.White;
        header.Format.Font.Size = 7;
        header.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < Headers.Length; i++)
        {
            header.Cells[i].AddParagraph(Headers[i]);
            header.Cells[i].Format.Alignment = ParagraphAlignment.Center;
        }

        var rowIndex = 0;
        foreach (var page in balloonsByPage.OrderBy(item => item.Key))
        {
            foreach (var balloon in page.Value.OrderBy(item => item.Number))
            {
                var row = table.AddRow();
                row.VerticalAlignment = VerticalAlignment.Center;
                if (rowIndex % 2 == 1)
                    row.Shading.Color = Colors.LightGray;

                var values = new[]
                {
                    (page.Key + 1).ToString(CultureInfo.InvariantCulture),
                    balloon.Number.ToString(CultureInfo.InvariantCulture),
                    balloon.Characteristic ?? string.Empty,
                    balloon.NominalValue ?? string.Empty,
                    balloon.LowerTolerance ?? string.Empty,
                    balloon.UpperTolerance ?? string.Empty,
                    balloon.MeasuredValue ?? string.Empty,
                    string.IsNullOrWhiteSpace(balloon.Result) ? "Beklemede" : balloon.Result,
                    balloon.X.ToString("0.0", CultureInfo.InvariantCulture),
                    balloon.Y.ToString("0.0", CultureInfo.InvariantCulture),
                    balloon.Notes ?? string.Empty
                };

                for (var i = 0; i < values.Length; i++)
                {
                    row.Cells[i].AddParagraph(values[i]);
                    row.Cells[i].Format.Font.Size = 6.5;
                    row.Cells[i].Format.Alignment = (i == 2 || i == 10)
                        ? ParagraphAlignment.Left
                        : ParagraphAlignment.Center;
                }

                switch (values[7])
                {
                    case "Uygun":
                        row.Cells[7].Shading.Color = Colors.LightGreen;
                        row.Cells[7].Format.Font.Color = Colors.DarkGreen;
                        break;
                    case "Uygun Değil":
                        row.Cells[7].Shading.Color = Colors.LightPink;
                        row.Cells[7].Format.Font.Color = Colors.DarkRed;
                        break;
                    default:
                        row.Cells[7].Shading.Color = Colors.LightYellow;
                        row.Cells[7].Format.Font.Color = Colors.DarkGoldenrod;
                        break;
                }

                rowIndex++;
            }
        }

        if (rowIndex == 0)
        {
            var emptyRow = table.AddRow();
            emptyRow.Cells[0].AddParagraph("Bu çizim için henüz balon kaydı bulunmuyor.");
        }

        var footer = section.Footers.Primary.AddParagraph(
            "WAT MOTOR A.Ş. | Teknik Resim Ölçüm Raporu");
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Colors.Gray;
        footer.Format.Alignment = ParagraphAlignment.Center;

        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(fullPath);
    }
}
