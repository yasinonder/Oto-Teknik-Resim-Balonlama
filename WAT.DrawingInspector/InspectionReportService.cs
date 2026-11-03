using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace WAT.DrawingInspector;

public static class InspectionReportService
{
    private static readonly string[] Headers =
    {
        "Sayfa",
        "Balon No",
        "Karakteristik",
        "Nominal Değer",
        "Alt Tolerans",
        "Üst Tolerans",
        "Ölçülen Değer",
        "Sonuç",
        "X (px)",
        "Y (px)",
        "Notlar"
    };

    public static void ExportExcel(
        string targetPath,
        string sourceDrawingName,
        IReadOnlyDictionary<int, List<StoredBalloon>> balloonsByPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(balloonsByPage);

        var fullPath = Path.GetFullPath(targetPath);
        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Excel rapor klasörü belirlenemedi.");
        Directory.CreateDirectory(folder);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Ölçüm Raporu");

        sheet.Range(1, 1, 1, Headers.Length).Merge();
        sheet.Cell(1, 1).Value = "WAT MOTOR A.Ş. | TEKNİK RESİM ÖLÇÜM RAPORU";
        sheet.Range(1, 1, 1, Headers.Length).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, Headers.Length).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, Headers.Length).Style.Font.FontColor = XLColor.White;
        sheet.Range(1, 1, 1, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#173746");
        sheet.Range(1, 1, 1, Headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 30;

        sheet.Cell(2, 1).Value = "Kaynak çizim";
        sheet.Cell(2, 2).Value = Path.GetFileName(sourceDrawingName ?? string.Empty);
        sheet.Range(2, 2, 2, Headers.Length).Merge();
        sheet.Cell(2, 1).Style.Font.Bold = true;

        sheet.Cell(3, 1).Value = "Rapor tarihi";
        sheet.Cell(3, 2).Value = DateTime.Now;
        sheet.Cell(3, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
        sheet.Cell(3, 1).Style.Font.Bold = true;

        const int headerRow = 5;
        for (var column = 0; column < Headers.Length; column++)
            sheet.Cell(headerRow, column + 1).Value = Headers[column];

        var header = sheet.Range(headerRow, 1, headerRow, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#365B6A");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        sheet.Row(headerRow).Height = 30;

        var row = headerRow + 1;
        foreach (var page in balloonsByPage.OrderBy(item => item.Key))
        {
            foreach (var balloon in page.Value.OrderBy(item => item.Number))
            {
                sheet.Cell(row, 1).Value = page.Key + 1;
                sheet.Cell(row, 2).Value = balloon.Number;
                sheet.Cell(row, 3).Value = balloon.Characteristic ?? string.Empty;
                sheet.Cell(row, 4).Value = balloon.NominalValue ?? string.Empty;
                sheet.Cell(row, 5).Value = balloon.LowerTolerance ?? string.Empty;
                sheet.Cell(row, 6).Value = balloon.UpperTolerance ?? string.Empty;
                sheet.Cell(row, 7).Value = balloon.MeasuredValue ?? string.Empty;
                sheet.Cell(row, 8).Value = string.IsNullOrWhiteSpace(balloon.Result)
                    ? "Beklemede"
                    : balloon.Result;
                sheet.Cell(row, 9).Value = balloon.X;
                sheet.Cell(row, 10).Value = balloon.Y;
                sheet.Cell(row, 11).Value = balloon.Notes ?? string.Empty;

                var resultCell = sheet.Cell(row, 8);
                switch (resultCell.GetString())
                {
                    case "Uygun":
                        resultCell.Style.Font.FontColor = XLColor.DarkGreen;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                        break;
                    case "Uygun Değil":
                        resultCell.Style.Font.FontColor = XLColor.DarkRed;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightPink;
                        break;
                    default:
                        resultCell.Style.Font.FontColor = XLColor.DarkGoldenrod;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                        break;
                }

                if (row % 2 == 0)
                    sheet.Range(row, 1, row, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F6F8");

                // Re-apply status color after the alternating row fill.
                switch (resultCell.GetString())
                {
                    case "Uygun":
                        resultCell.Style.Font.FontColor = XLColor.DarkGreen;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                        break;
                    case "Uygun Değil":
                        resultCell.Style.Font.FontColor = XLColor.DarkRed;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightPink;
                        break;
                    default:
                        resultCell.Style.Font.FontColor = XLColor.DarkGoldenrod;
                        resultCell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                        break;
                }

                row++;
            }
        }

        if (row == headerRow + 1)
        {
            sheet.Cell(row, 1).Value = "Bu çizim için henüz balon kaydı bulunmuyor.";
            sheet.Range(row, 1, row, Headers.Length).Merge();
            sheet.Cell(row, 1).Style.Font.Italic = true;
            row++;
        }

        var lastDataRow = row - 1;
        var reportRange = sheet.Range(headerRow, 1, lastDataRow, Headers.Length);
        reportRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        reportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        reportRange.SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);

        sheet.Column(1).Width = 9;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 24;
        sheet.Column(4).Width = 15;
        sheet.Column(5).Width = 14;
        sheet.Column(6).Width = 14;
        sheet.Column(7).Width = 15;
        sheet.Column(8).Width = 17;
        sheet.Column(9).Width = 12;
        sheet.Column(10).Width = 12;
        sheet.Column(11).Width = 42;
        sheet.Column(11).Style.Alignment.WrapText = true;

        workbook.Properties.Title = "WAT MOTOR A.Ş. Teknik Resim Ölçüm Raporu";
        workbook.Properties.Subject = "Balon bazlı ölçüm ve tolerans sonuçları";
        workbook.Properties.Author = "WAT MOTOR A.Ş.";
        workbook.SaveAs(fullPath);
    }
}
