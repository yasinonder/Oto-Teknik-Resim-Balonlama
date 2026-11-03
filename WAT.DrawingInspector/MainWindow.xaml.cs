using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WAT.DrawingInspector;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BalloonItem> _balloons = new();
    private readonly Dictionary<int, List<BalloonItem>> _pdfPageBalloons = new();
    private readonly Dictionary<int, int> _pdfPageNextNumbers = new();

    private int _nextNumber = 1;
    private string? _imagePath;
    private string? _pdfPath;
    private int _pdfPageIndex;
    private int _pdfPageCount;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        BalloonList.ItemsSource = _balloons;
        UpdatePdfControls();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Teknik resim ve görseller|*.pdf;*.dxf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|PDF dosyaları|*.pdf|DXF dosyaları|*.dxf|Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Tüm dosyalar|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        SetBusy(true, "Dosya açılıyor...");
        try
        {
            var extension = Path.GetExtension(dlg.FileName);
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pageCount = await Task.Run(() => PdfDrawingService.GetPageCount(dlg.FileName));
                if (pageCount < 1)
                    throw new InvalidDataException("PDF dosyasında görüntülenebilir sayfa bulunamadı.");

                var pngBytes = await Task.Run(() => PdfDrawingService.RenderPagePng(dlg.FileName, 0));
                var bitmap = LoadBitmap(pngBytes);
                var tempPath = GetRenderedDrawingTempPath();
                File.WriteAllBytes(tempPath, pngBytes);

                ResetAnnotations();
                _pdfPath = dlg.FileName;
                _pdfPageCount = pageCount;
                _pdfPageIndex = 0;
                _imagePath = tempPath;
                ApplyBitmap(bitmap);
                UpdatePdfControls();
                StatusText.Text = $"{Path.GetFileName(dlg.FileName)} açıldı. Sayfa 1 / {pageCount}; PDF sayfası görüntüye dönüştürüldü.";
            }
            else if (string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "DXF geometrileri okunuyor...";
                var scene = await Task.Run(() => DxfDrawingService.LoadScene(dlg.FileName));
                var rendered = DxfDrawingService.RenderPng(scene);
                var bitmap = LoadBitmap(rendered.PngBytes);
                var tempPath = GetRenderedDrawingTempPath();
                File.WriteAllBytes(tempPath, rendered.PngBytes);

                ResetAnnotations();
                _pdfPath = null;
                _pdfPageCount = 0;
                _pdfPageIndex = 0;
                _imagePath = tempPath;
                ApplyBitmap(bitmap);
                UpdatePdfControls();
                StatusText.Text = $"DXF açıldı: {rendered.SupportedEntityCount} desteklenen geometrik varlık; {rendered.UnsupportedEntityCount} desteklenmeyen varlık atlandı. Görüntü {rendered.PixelWidth} × {rendered.PixelHeight} px.";
            }
            else
            {
                var bitmap = LoadBitmapFromFile(dlg.FileName);

                ResetAnnotations();
                _pdfPath = null;
                _pdfPageCount = 0;
                _pdfPageIndex = 0;
                _imagePath = dlg.FileName;
                ApplyBitmap(bitmap);
                UpdatePdfControls();
                StatusText.Text = $"{Path.GetFileName(dlg.FileName)} açıldı ({bitmap.PixelWidth} × {bitmap.PixelHeight}). Görüntü koordinatları piksel bazındadır.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Dosya açılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Dosya açılamadı.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PdfPrevious_Click(object sender, RoutedEventArgs e)
    {
        await NavigatePdfPageAsync(_pdfPageIndex - 1);
    }

    private async void PdfNext_Click(object sender, RoutedEventArgs e)
    {
        await NavigatePdfPageAsync(_pdfPageIndex + 1);
    }

    private async Task NavigatePdfPageAsync(int targetPage)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_pdfPath) ||
            targetPage < 0 || targetPage >= _pdfPageCount)
            return;

        SetBusy(true, $"PDF sayfası {targetPage + 1} yükleniyor...");
        try
        {
            var pdfPath = _pdfPath;
            var pngBytes = await Task.Run(() => PdfDrawingService.RenderPagePng(pdfPath, targetPage));
            var bitmap = LoadBitmap(pngBytes);
            File.WriteAllBytes(GetRenderedDrawingTempPath(), pngBytes);

            SaveCurrentPdfAnnotations();
            _pdfPageIndex = targetPage;
            _imagePath = GetRenderedDrawingTempPath();
            ApplyBitmap(bitmap);
            RestorePdfAnnotations(targetPage);
            UpdatePdfControls();
            StatusText.Text = $"PDF sayfa {targetPage + 1} / {_pdfPageCount} yüklendi. Bu sayfanın balonları korunuyor.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PDF sayfası açılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "PDF sayfası yüklenemedi.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyBitmap(BitmapSource bitmap)
    {
        DrawingImage.Source = bitmap;
        DrawingImage.Width = bitmap.PixelWidth;
        DrawingImage.Height = bitmap.PixelHeight;
        DrawingSurface.Width = bitmap.PixelWidth;
        DrawingSurface.Height = bitmap.PixelHeight;
        OcrCanvas.Width = bitmap.PixelWidth;
        OcrCanvas.Height = bitmap.PixelHeight;
        BalloonCanvas.Width = bitmap.PixelWidth;
        BalloonCanvas.Height = bitmap.PixelHeight;
        OcrCanvas.Children.Clear();
        BalloonCanvas.Children.Clear();
    }

    private static BitmapImage LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage LoadBitmapFromFile(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string GetRenderedDrawingTempPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WAT.DrawingInspector");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "current-rendered-drawing.png");
    }

    private void ResetAnnotations()
    {
        _balloons.Clear();
        _pdfPageBalloons.Clear();
        _pdfPageNextNumbers.Clear();
        _nextNumber = 1;
        OcrCanvas.Children.Clear();
        BalloonCanvas.Children.Clear();
    }

    private void SaveCurrentPdfAnnotations()
    {
        if (_pdfPath is null) return;

        _pdfPageBalloons[_pdfPageIndex] = _balloons
            .Select(b => new BalloonItem { Number = b.Number, X = b.X, Y = b.Y })
            .ToList();
        _pdfPageNextNumbers[_pdfPageIndex] = _nextNumber;
    }

    private void RestorePdfAnnotations(int pageIndex)
    {
        _balloons.Clear();
        if (_pdfPageBalloons.TryGetValue(pageIndex, out var saved))
        {
            foreach (var item in saved)
                _balloons.Add(new BalloonItem { Number = item.Number, X = item.X, Y = item.Y });
        }

        _nextNumber = _pdfPageNextNumbers.TryGetValue(pageIndex, out var next)
            ? next
            : (_balloons.Count == 0 ? 1 : _balloons.Max(b => b.Number) + 1);

        foreach (var item in _balloons)
            DrawBalloon(item);
    }

    private void Surface_Click(object sender, MouseButtonEventArgs e)
    {
        if (DrawingImage.Source is not BitmapSource bitmap) return;

        var p = e.GetPosition(DrawingSurface);
        if (p.X < 0 || p.Y < 0 || p.X >= bitmap.PixelWidth || p.Y >= bitmap.PixelHeight)
            return;

        AddBalloon(p.X, p.Y);
    }

    private void AddBalloon(double x, double y)
    {
        var item = new BalloonItem
        {
            Number = _nextNumber++,
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1)
        };

        _balloons.Add(item);
        DrawBalloon(item);
        StatusText.Text = $"Balon {item.Number} eklendi. Koordinatlar görüntü piksel koordinatıdır.";
    }

    private void DrawBalloon(BalloonItem item)
    {
        var bubble = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Brushes.White,
            BorderBrush = Brushes.DarkRed,
            BorderThickness = new Thickness(2)
        };
        bubble.Child = new TextBlock
        {
            Text = item.Number.ToString(CultureInfo.InvariantCulture),
            Foreground = Brushes.DarkRed,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(bubble, item.X - 15);
        Canvas.SetTop(bubble, item.Y - 15);
        BalloonCanvas.Children.Add(bubble);
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_imagePath))
        {
            MessageBox.Show("Önce bir görsel, PDF veya DXF açın.");
            return;
        }

        SetBusy(true, "OCR metinleri ve konum kutuları algılanıyor...");
        try
        {
            var imagePath = _imagePath;
            var words = await Task.Run(() => OcrService.ReadWords(imagePath));
            OcrCanvas.Children.Clear();

            foreach (var word in words)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, word.Width),
                    Height = Math.Max(1, word.Height),
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(24, 50, 205, 50))
                };
                Canvas.SetLeft(rect, word.Left);
                Canvas.SetTop(rect, word.Top);
                OcrCanvas.Children.Add(rect);

                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 20, 80, 50)),
                    Padding = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock
                    {
                        Text = word.Text,
                        Foreground = Brushes.White,
                        FontSize = 10,
                        MaxWidth = 220,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };
                Canvas.SetLeft(label, word.Left);
                Canvas.SetTop(label, Math.Max(0, word.Top - 16));
                OcrCanvas.Children.Add(label);
            }

            var report = string.Join(Environment.NewLine,
                words.Select(w => $"{w.Text} | X:{w.Left} Y:{w.Top} W:{w.Width} H:{w.Height} | Güven:%{w.Confidence:F0}"));
            var resultWindow = new Window
            {
                Title = $"OCR Sonuçları — {words.Count} kelime",
                Width = 760,
                Height = 560,
                Owner = this,
                Content = new TextBox
                {
                    Text = report,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(10)
                }
            };
            resultWindow.ShowDialog();
            StatusText.Text = $"OCR tamamlandı: {words.Count} kelime için çizim üzerinde konum kutuları gösteriliyor.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "OCR hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "OCR başarısız.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        if (message is not null)
            StatusText.Text = message;

        OpenButton.IsEnabled = !busy;
        OcrButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_imagePath);
        UpdatePdfControls();
    }

    private void UpdatePdfControls()
    {
        if (_pdfPath is null)
        {
            PdfPageText.Text = "PDF yok";
            PdfPrevButton.IsEnabled = false;
            PdfNextButton.IsEnabled = false;
            return;
        }

        PdfPageText.Text = $"Sayfa {_pdfPageIndex + 1} / {_pdfPageCount}";
        PdfPrevButton.IsEnabled = !_isBusy && _pdfPageIndex > 0;
        PdfNextButton.IsEnabled = !_isBusy && _pdfPageIndex < _pdfPageCount - 1;
    }

    private void ClearOcr_Click(object sender, RoutedEventArgs e)
    {
        OcrCanvas.Children.Clear();
        StatusText.Text = "OCR konum kutuları temizlendi.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _balloons.Clear();
        BalloonCanvas.Children.Clear();
        _nextNumber = 1;
        if (_pdfPath is not null)
            SaveCurrentPdfAnnotations();
        StatusText.Text = "Geçerli sayfanın balonları temizlendi.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV dosyası|*.csv",
            FileName = "balon-listesi.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder("Sayfa;No;X_Piksel;Y_Piksel\r\n");
        if (_pdfPath is not null)
        {
            SaveCurrentPdfAnnotations();
            foreach (var page in _pdfPageBalloons.OrderBy(p => p.Key))
            {
                foreach (var balloon in page.Value.OrderBy(b => b.Number))
                {
                    sb.Append($"{page.Key + 1};{balloon.Number};{balloon.X.ToString(CultureInfo.InvariantCulture)};{balloon.Y.ToString(CultureInfo.InvariantCulture)}\r\n");
                }
            }
        }
        else
        {
            foreach (var balloon in _balloons)
            {
                sb.Append($";{balloon.Number};{balloon.X.ToString(CultureInfo.InvariantCulture)};{balloon.Y.ToString(CultureInfo.InvariantCulture)}\r\n");
            }
        }

        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        StatusText.Text = "Balon listesi CSV olarak dışa aktarıldı.";
    }
}

public sealed class BalloonItem
{
    public int Number { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}