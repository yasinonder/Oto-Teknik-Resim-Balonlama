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
using System.Windows.Shapes;

namespace WAT.DrawingInspector;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BalloonItem> _balloons = new();
    private int _nextNumber = 1;
    private string? _imagePath;

    public MainWindow()
    {
        InitializeComponent();
        BalloonList.ItemsSource = _balloons;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Tüm dosyalar|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();

            DrawingImage.Source = bmp;
            DrawingImage.Width = bmp.PixelWidth;
            DrawingImage.Height = bmp.PixelHeight;
            DrawingSurface.Width = bmp.PixelWidth;
            DrawingSurface.Height = bmp.PixelHeight;
            OcrCanvas.Width = bmp.PixelWidth;
            OcrCanvas.Height = bmp.PixelHeight;
            BalloonCanvas.Width = bmp.PixelWidth;
            BalloonCanvas.Height = bmp.PixelHeight;
            OcrCanvas.Children.Clear();
            _imagePath = dlg.FileName;
            StatusText.Text = $"{Path.GetFileName(dlg.FileName)} açıldı ({bmp.PixelWidth} × {bmp.PixelHeight}). Görüntü koordinatları piksel bazındadır.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Dosya açılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Surface_Click(object sender, MouseButtonEventArgs e)
    {
        if (DrawingImage.Source is not BitmapSource) return;
        var p = e.GetPosition(DrawingSurface);
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
        Canvas.SetLeft(bubble, x - 15);
        Canvas.SetTop(bubble, y - 15);
        BalloonCanvas.Children.Add(bubble);
        StatusText.Text = $"Balon {item.Number} eklendi. Koordinatlar görüntü piksel koordinatıdır.";
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_imagePath))
        {
            MessageBox.Show("Önce bir görsel açın.");
            return;
        }

        try
        {
            StatusText.Text = "OCR metinleri ve konum kutuları algılanıyor...";
            var words = await Task.Run(() => OcrService.ReadWords(_imagePath));
            OcrCanvas.Children.Clear();

            foreach (var word in words)
            {
                var rect = new Rectangle
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
                words.Select(w => $"{w.Text} | X:{w.Left} Y:{w.Top} G:{w.Width} Y:{w.Height} | Güven:%{w.Confidence:F0}"));
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
        StatusText.Text = "Balonlar temizlendi.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV dosyası|*.csv", FileName = "balon-listesi.csv" };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder("No;X_Piksel;Y_Piksel\r\n");
        foreach (var b in _balloons)
        {
            sb.Append($"{b.Number};{b.X.ToString(CultureInfo.InvariantCulture)};{b.Y.ToString(CultureInfo.InvariantCulture)}\r\n");
        }
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        StatusText.Text = "CSV dışa aktarıldı.";
    }
}

public sealed class BalloonItem
{
    public int Number { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}