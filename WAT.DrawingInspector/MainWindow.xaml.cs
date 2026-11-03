using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
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
    private readonly Dictionary<BalloonItem, Border> _balloonVisuals = new();

    private BalloonItem? _draggedBalloon;
    private Border? _draggedVisual;
    private Point _dragGrabOffset;

    private int _nextNumber = 1;
    private string? _imagePath;
    private string? _pdfPath;
    private string? _sourceDrawingPath;
    private string? _sourceDisplayName;
    private string? _currentProjectPath;
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

        await LoadDrawingFileAsync(dlg.FileName, Path.GetFileName(dlg.FileName));
    }

    private async Task LoadDrawingFileAsync(string filePath, string displayName)
    {
        SetBusy(true, "Dosya açılıyor...");
        try
        {
            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pageCount = await Task.Run(() => PdfDrawingService.GetPageCount(filePath));
                if (pageCount < 1)
                    throw new InvalidDataException("PDF dosyasında görüntülenebilir sayfa bulunamadı.");

                var pngBytes = await Task.Run(() => PdfDrawingService.RenderPagePng(filePath, 0));
                var bitmap = LoadBitmap(pngBytes);
                var tempPath = GetRenderedDrawingTempPath();
                File.WriteAllBytes(tempPath, pngBytes);

                ResetAnnotations();
                _pdfPath = filePath;
                _pdfPageCount = pageCount;
                _pdfPageIndex = 0;
                _imagePath = tempPath;
                ApplyBitmap(bitmap);
                UpdatePdfControls();
                StatusText.Text = $"{displayName} açıldı. Sayfa 1 / {pageCount}; PDF sayfası görüntüye dönüştürüldü.";
            }
            else if (string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "DXF geometrileri okunuyor...";
                var scene = await Task.Run(() => DxfDrawingService.LoadScene(filePath));
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
                var bitmap = LoadBitmapFromFile(filePath);

                ResetAnnotations();
                _pdfPath = null;
                _pdfPageCount = 0;
                _pdfPageIndex = 0;
                _imagePath = filePath;
                ApplyBitmap(bitmap);
                UpdatePdfControls();
                StatusText.Text = $"{displayName} açıldı ({bitmap.PixelWidth} × {bitmap.PixelHeight}). Görüntü koordinatları piksel bazındadır.";
            }

            _sourceDrawingPath = filePath;
            _sourceDisplayName = displayName;
            _currentProjectPath = null;
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

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WAT SQLite proje dosyası|*.watproj|SQLite veritabanı|*.db;*.sqlite;*.sqlite3|Tüm dosyalar|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        InspectionProjectData project;
        try
        {
            SetBusy(true, "SQLite proje dosyası okunuyor...");
            project = await Task.Run(() => ProjectFileService.Load(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proje açılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Proje açılamadı.";
            return;
        }
        finally
        {
            SetBusy(false);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(project.SourceExtension))
                throw new InvalidDataException("Proje dosyasında kaynak çizim uzantısı yok.");

            var folder = Path.Combine(Path.GetTempPath(), "WAT.DrawingInspector", "Projects");
            Directory.CreateDirectory(folder);
            var sourcePath = Path.Combine(folder, Guid.NewGuid().ToString("N") + project.SourceExtension);
            await Task.Run(() => File.WriteAllBytes(sourcePath, project.SourceBytes));

            await LoadDrawingFileAsync(sourcePath, project.SourceFileName);
            if (!string.Equals(_sourceDrawingPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                return;

            _currentProjectPath = dlg.FileName;

            foreach (var page in project.BalloonsByPage)
            {
                _pdfPageBalloons[page.Key] = page.Value
                    .Select(item => new BalloonItem { Number = item.Number, X = item.X, Y = item.Y })
                    .ToList();
            }

            foreach (var page in project.NextNumbersByPage)
                _pdfPageNextNumbers[page.Key] = page.Value;

            if (_pdfPath is null && project.CurrentPageIndex != 0)
                throw new InvalidDataException("Görsel/DXF projesi geçersiz bir sayfa numarası içeriyor.");

            if (_pdfPath is not null && project.CurrentPageIndex >= _pdfPageCount)
                throw new InvalidDataException("Proje dosyasındaki etkin PDF sayfası bu çizimde bulunmuyor.");

            RestorePdfAnnotations(0);
            if (_pdfPath is not null && project.CurrentPageIndex > 0)
                await NavigatePdfPageAsync(project.CurrentPageIndex);

            StatusText.Text = $"Proje açıldı: {project.SourceFileName}; {project.BalloonsByPage.Sum(p => p.Value.Count)} kayıtlı balon yüklendi.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proje içeriği açılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Proje içeriği açılamadı.";
        }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourceDrawingPath) || !File.Exists(_sourceDrawingPath))
        {
            MessageBox.Show("Önce PDF, DXF veya görsel açın.");
            return;
        }

        var defaultName = _currentProjectPath is not null
            ? Path.GetFileName(_currentProjectPath)
            : Path.GetFileNameWithoutExtension(_sourceDisplayName ?? _sourceDrawingPath) + ".watproj";

        var dlg = new SaveFileDialog
        {
            Filter = "WAT SQLite proje dosyası|*.watproj",
            FileName = defaultName,
            AddExtension = true,
            DefaultExt = ".watproj"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var balloonsByPage = new Dictionary<int, List<StoredBalloon>>();
            var nextNumbersByPage = new Dictionary<int, int>();

            if (_pdfPath is not null)
            {
                SaveCurrentPdfAnnotations();

                foreach (var page in _pdfPageBalloons)
                {
                    balloonsByPage[page.Key] = page.Value
                        .Select(item => new StoredBalloon(item.Number, item.X, item.Y))
                        .ToList();
                }

                foreach (var page in _pdfPageNextNumbers)
                    nextNumbersByPage[page.Key] = page.Value;
            }
            else
            {
                balloonsByPage[0] = _balloons
                    .Select(item => new StoredBalloon(item.Number, item.X, item.Y))
                    .ToList();
                nextNumbersByPage[0] = _nextNumber;
            }

            ProjectFileService.Save(
                dlg.FileName,
                _sourceDrawingPath,
                _sourceDisplayName ?? Path.GetFileName(_sourceDrawingPath),
                _pdfPath is null ? 0 : _pdfPageIndex,
                balloonsByPage,
                nextNumbersByPage);

            _currentProjectPath = dlg.FileName;
            StatusText.Text = $"Proje kaydedildi: {Path.GetFileName(dlg.FileName)}; çizim ve {balloonsByPage.Sum(p => p.Value.Count)} balon SQLite dosyasına gömüldü.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Proje kaydedilemedi", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Proje kaydedilemedi.";
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
        _balloonVisuals.Clear();
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
        _balloonVisuals.Clear();
        BalloonList.SelectedItem = null;
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
            Tag = item,
            Cursor = Cursors.SizeAll,
            CornerRadius = new CornerRadius(15),
            Background = Brushes.White,
            BorderBrush = Brushes.DarkRed,
            BorderThickness = new Thickness(2)
        };
        bubble.MouseLeftButtonDown += Balloon_MouseLeftButtonDown;
        bubble.MouseMove += Balloon_MouseMove;
        bubble.MouseLeftButtonUp += Balloon_MouseLeftButtonUp;
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
        _balloonVisuals[item] = bubble;
        UpdateBalloonSelectionVisuals();
    }

    private void Balloon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border bubble || bubble.Tag is not BalloonItem item)
            return;

        _draggedBalloon = item;
        _draggedVisual = bubble;
        _dragGrabOffset = e.GetPosition(bubble);
        BalloonList.SelectedItem = item;
        bubble.CaptureMouse();
        e.Handled = true;
    }

    private void Balloon_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedBalloon is null || _draggedVisual is null ||
            !_draggedVisual.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(BalloonCanvas);
        var left = Math.Clamp(point.X - _dragGrabOffset.X, 0,
            Math.Max(0, BalloonCanvas.Width - _draggedVisual.Width));
        var top = Math.Clamp(point.Y - _dragGrabOffset.Y, 0,
            Math.Max(0, BalloonCanvas.Height - _draggedVisual.Height));

        Canvas.SetLeft(_draggedVisual, left);
        Canvas.SetTop(_draggedVisual, top);
        _draggedBalloon.X = Math.Round(left + _draggedVisual.Width / 2, 1);
        _draggedBalloon.Y = Math.Round(top + _draggedVisual.Height / 2, 1);
        e.Handled = true;
    }

    private void Balloon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedVisual is null)
            return;

        _draggedVisual.ReleaseMouseCapture();
        _draggedVisual = null;
        _draggedBalloon = null;

        if (_pdfPath is not null)
            SaveCurrentPdfAnnotations();

        StatusText.Text = "Balon konumu güncellendi.";
        e.Handled = true;
    }

    private void BalloonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBalloonSelectionVisuals();
    }

    private void UpdateBalloonSelectionVisuals()
    {
        var selected = BalloonList.SelectedItem as BalloonItem;
        foreach (var pair in _balloonVisuals)
        {
            var isSelected = ReferenceEquals(pair.Key, selected);
            pair.Value.BorderBrush = isSelected ? Brushes.DodgerBlue : Brushes.DarkRed;
            pair.Value.Background = isSelected ? Brushes.AliceBlue : Brushes.White;
        }
    }

    private void DeleteSelectedBalloon_Click(object sender, RoutedEventArgs e)
    {
        if (BalloonList.SelectedItem is not BalloonItem selected)
        {
            StatusText.Text = "Silmek için önce balon listesinden bir balon seçin.";
            return;
        }

        if (_balloonVisuals.Remove(selected, out var visual))
            BalloonCanvas.Children.Remove(visual);

        _balloons.Remove(selected);
        BalloonList.SelectedItem = null;

        if (_pdfPath is not null)
            SaveCurrentPdfAnnotations();

        StatusText.Text = $"Balon {selected.Number} silindi.";
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
        _balloonVisuals.Clear();
        BalloonList.SelectedItem = null;
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

public sealed class BalloonItem : INotifyPropertyChanged
{
    private int _number;
    private double _x;
    private double _y;

    public int Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}