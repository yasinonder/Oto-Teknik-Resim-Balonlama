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
    private readonly ObservableCollection<OcrDimensionCandidate> _dimensionCandidates = new();
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
        DimensionCandidateList.ItemsSource = _dimensionCandidates;
        LoadMeasurementEditor(null);
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
                    .Select(FromStoredBalloon)
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
                        .Select(ToStoredBalloon)
                        .ToList();
                }

                foreach (var page in _pdfPageNextNumbers)
                    nextNumbersByPage[page.Key] = page.Value;
            }
            else
            {
                balloonsByPage[0] = _balloons
                    .Select(ToStoredBalloon)
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
        _dimensionCandidates.Clear();
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
            .Select(CloneBalloon)
            .ToList();
        _pdfPageNextNumbers[_pdfPageIndex] = _nextNumber;
    }

    private void RestorePdfAnnotations(int pageIndex)
    {
        _balloons.Clear();
        if (_pdfPageBalloons.TryGetValue(pageIndex, out var saved))
        {
            foreach (var item in saved)
                _balloons.Add(CloneBalloon(item));
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
        BalloonList.SelectedItem = item;
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
        LoadMeasurementEditor(BalloonList.SelectedItem as BalloonItem);
    }

    private void LoadMeasurementEditor(BalloonItem? item)
    {
        var enabled = item is not null;
        CharacteristicTextBox.IsEnabled = enabled;
        NominalValueTextBox.IsEnabled = enabled;
        LowerToleranceTextBox.IsEnabled = enabled;
        UpperToleranceTextBox.IsEnabled = enabled;
        MeasuredValueTextBox.IsEnabled = enabled;
        NotesTextBox.IsEnabled = enabled;

        CharacteristicTextBox.Text = item?.Characteristic ?? string.Empty;
        NominalValueTextBox.Text = item?.NominalValue ?? string.Empty;
        LowerToleranceTextBox.Text = item?.LowerTolerance ?? string.Empty;
        UpperToleranceTextBox.Text = item?.UpperTolerance ?? string.Empty;
        MeasuredValueTextBox.Text = item?.MeasuredValue ?? string.Empty;
        NotesTextBox.Text = item?.Notes ?? string.Empty;
        MeasurementResultText.Text = item is null
            ? "Ölçüm girişi için bir balon seçin."
            : $"Sonuç: {item.Result}";
        MeasurementResultText.Foreground = ResultBrush(item?.Result);
    }

    private void ApplyMeasurement_Click(object sender, RoutedEventArgs e)
    {
        if (BalloonList.SelectedItem is not BalloonItem item)
        {
            StatusText.Text = "Ölçüm bilgisi girmek için önce bir balon seçin.";
            return;
        }

        try
        {
            var nominal = ParseOptionalMeasurement(NominalValueTextBox.Text, "Nominal değer");
            var lower = ParseOptionalMeasurement(LowerToleranceTextBox.Text, "Alt tolerans");
            var upper = ParseOptionalMeasurement(UpperToleranceTextBox.Text, "Üst tolerans");
            var measured = ParseOptionalMeasurement(MeasuredValueTextBox.Text, "Ölçülen değer");

            if (lower.HasValue && lower.Value > 0m)
                throw new FormatException("Alt tolerans işaretli sapma olarak sıfır veya negatif girilmelidir (örn. -0,10).");
            if (upper.HasValue && upper.Value < 0m)
                throw new FormatException("Üst tolerans işaretli sapma olarak sıfır veya pozitif girilmelidir (örn. +0,10).");

            item.Characteristic = CharacteristicTextBox.Text.Trim();
            item.NominalValue = NominalValueTextBox.Text.Trim();
            item.LowerTolerance = LowerToleranceTextBox.Text.Trim();
            item.UpperTolerance = UpperToleranceTextBox.Text.Trim();
            item.MeasuredValue = MeasuredValueTextBox.Text.Trim();
            item.Notes = NotesTextBox.Text.Trim();
            item.Result = EvaluateMeasurement(nominal, lower, upper, measured);

            MeasurementResultText.Text = $"Sonuç: {item.Result}";
            MeasurementResultText.Foreground = ResultBrush(item.Result);
            BalloonList.Items.Refresh();

            if (_pdfPath is not null)
                SaveCurrentPdfAnnotations();

            StatusText.Text = $"Balon {item.Number} ölçüm bilgileri güncellendi: {item.Result}. Projeyi kaydetmeyi unutmayın.";
        }
        catch (FormatException ex)
        {
            MessageBox.Show(ex.Message, "Ölçüm bilgisi geçersiz", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static decimal? ParseOptionalMeasurement(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().Replace(" ", string.Empty);
        var commaIndex = normalized.LastIndexOf(',');
        var dotIndex = normalized.LastIndexOf('.');

        if (commaIndex >= 0 && dotIndex >= 0)
        {
            if (commaIndex > dotIndex)
                normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
            else
                normalized = normalized.Replace(",", string.Empty);
        }
        else if (commaIndex >= 0)
        {
            normalized = normalized.Replace(',', '.');
        }

        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (decimal.TryParse(normalized, styles, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new FormatException($"{fieldName} sayısal olmalıdır. Örnek: 12,345 veya 12.345");
    }

    private static string EvaluateMeasurement(decimal? nominal, decimal? lower, decimal? upper, decimal? measured)
    {
        if (!nominal.HasValue || !measured.HasValue)
            return "Beklemede";

        var lowerLimit = nominal.Value + (lower ?? 0m);
        var upperLimit = nominal.Value + (upper ?? 0m);
        return measured.Value >= lowerLimit && measured.Value <= upperLimit
            ? "Uygun"
            : "Uygun Değil";
    }

    private static Brush ResultBrush(string? result) => result switch
    {
        "Uygun" => Brushes.ForestGreen,
        "Uygun Değil" => Brushes.Firebrick,
        _ => Brushes.DarkGoldenrod
    };

    private static BalloonItem CloneBalloon(BalloonItem item) => new()
    {
        Number = item.Number,
        X = item.X,
        Y = item.Y,
        Characteristic = item.Characteristic,
        NominalValue = item.NominalValue,
        UpperTolerance = item.UpperTolerance,
        LowerTolerance = item.LowerTolerance,
        MeasuredValue = item.MeasuredValue,
        Result = item.Result,
        Notes = item.Notes
    };

    private static BalloonItem FromStoredBalloon(StoredBalloon item) => new()
    {
        Number = item.Number,
        X = item.X,
        Y = item.Y,
        Characteristic = item.Characteristic,
        NominalValue = item.NominalValue,
        UpperTolerance = item.UpperTolerance,
        LowerTolerance = item.LowerTolerance,
        MeasuredValue = item.MeasuredValue,
        Result = item.Result,
        Notes = item.Notes
    };

    private static StoredBalloon ToStoredBalloon(BalloonItem item) => new(
        item.Number,
        item.X,
        item.Y,
        item.Characteristic,
        item.NominalValue,
        item.UpperTolerance,
        item.LowerTolerance,
        item.MeasuredValue,
        item.Result,
        item.Notes);

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
        LoadMeasurementEditor(null);

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
            var candidates = DimensionCandidateService.FindCandidates(words);
            OcrCanvas.Children.Clear();
            _dimensionCandidates.Clear();
            foreach (var candidate in candidates)
                _dimensionCandidates.Add(candidate);

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

            foreach (var candidate in candidates)
            {
                var candidateRect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, candidate.Width),
                    Height = Math.Max(1, candidate.Height),
                    Stroke = Brushes.OrangeRed,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(28, 255, 140, 0))
                };
                Canvas.SetLeft(candidateRect, candidate.Left);
                Canvas.SetTop(candidateRect, candidate.Top);
                OcrCanvas.Children.Add(candidateRect);

                var candidateLabel = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(225, 150, 65, 0)),
                    Padding = new Thickness(3, 1, 3, 1),
                    Child = new TextBlock
                    {
                        Text = $"ADAY: {candidate.SuggestedValue} • %{candidate.Confidence:0}",
                        Foreground = Brushes.White,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    }
                };
                Canvas.SetLeft(candidateLabel, candidate.Left);
                Canvas.SetTop(candidateLabel, Math.Max(0, candidate.Top - 19));
                OcrCanvas.Children.Add(candidateLabel);
            }

            var candidateReport = candidates.Count == 0
                ? "Ölçü formatına benzeyen, tek değer içeren güvenilir OCR satırı bulunamadı."
                : string.Join(Environment.NewLine, candidates.Select(candidate =>
                    $"{candidate.OcrText} | Öneri: {candidate.SuggestedValue} | Güven:%{candidate.Confidence:0}"));

            var report = string.Join(Environment.NewLine,
                words.Select(w => $"{w.Text} | X:{w.Left} Y:{w.Top} W:{w.Width} H:{w.Height} | Güven:%{w.Confidence:F0}"));

            report += Environment.NewLine + Environment.NewLine +
                $"ÖLÇÜ ADAYLARI ({candidates.Count})" + Environment.NewLine + candidateReport;
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
            StatusText.Text = $"OCR tamamlandı: {words.Count} kelime, {candidates.Count} ölçü adayı. Adayları kontrol edip balon ekleyin.";
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

    private void AddSelectedDimensionCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (DimensionCandidateList.SelectedItem is not OcrDimensionCandidate candidate)
        {
            StatusText.Text = "Balon eklemek için önce OCR ölçü adaylarından birini seçin.";
            return;
        }

        if (BalloonCanvas.Width <= 30 || BalloonCanvas.Height <= 30)
        {
            StatusText.Text = "Önce geçerli bir çizim açın.";
            return;
        }

        var centerY = candidate.Top + candidate.Height / 2.0;
        var rightPosition = candidate.Left + candidate.Width + 34.0;
        var x = rightPosition <= BalloonCanvas.Width - 16
            ? rightPosition
            : Math.Max(16.0, candidate.Left - 34.0);
        var y = Math.Clamp(centerY, 16.0, BalloonCanvas.Height - 16.0);

        var item = new BalloonItem
        {
            Number = _nextNumber++,
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1),
            Characteristic = "OCR ölçü adayı - doğrula",
            Notes = $"OCR metni: {candidate.OcrText}; güven: %{candidate.Confidence:0}. " +
                "Nominal değer ve tolerans kullanıcı tarafından doğrulanmalıdır."
        };

        _balloons.Add(item);
        DrawBalloon(item);
        BalloonList.SelectedItem = item;
        _dimensionCandidates.Remove(candidate);
        if (_pdfPath is not null)
            SaveCurrentPdfAnnotations();

        StatusText.Text = $"OCR adayı balon {item.Number} olarak eklendi. OCR metni nominal değer olarak kabul edilmedi; lütfen doğrulayın.";
    }

    private void ClearOcr_Click(object sender, RoutedEventArgs e)
    {
        OcrCanvas.Children.Clear();
        _dimensionCandidates.Clear();
        StatusText.Text = "OCR konum kutuları ve ölçü adayları temizlendi.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _balloons.Clear();
        BalloonCanvas.Children.Clear();
        _balloonVisuals.Clear();
        BalloonList.SelectedItem = null;
        LoadMeasurementEditor(null);
        _nextNumber = 1;
        if (_pdfPath is not null)
            SaveCurrentPdfAnnotations();
        StatusText.Text = "Geçerli sayfanın balonları temizlendi.";
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourceDisplayName))
        {
            MessageBox.Show("Önce bir PDF, DXF veya görsel açın.", "PDF raporu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(_sourceDisplayName) + "-olcum-raporu.pdf";
        var dialog = new SaveFileDialog
        {
            Filter = "PDF raporu|*.pdf",
            FileName = defaultName,
            AddExtension = true,
            DefaultExt = ".pdf"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var balloonsByPage = new Dictionary<int, List<StoredBalloon>>();
            if (_pdfPath is not null)
            {
                SaveCurrentPdfAnnotations();
                foreach (var page in _pdfPageBalloons)
                    balloonsByPage[page.Key] = page.Value.Select(ToStoredBalloon).ToList();
            }
            else
            {
                balloonsByPage[0] = _balloons.Select(ToStoredBalloon).ToList();
            }

            PdfInspectionReportService.Export(
                dialog.FileName,
                _sourceDisplayName,
                balloonsByPage);

            StatusText.Text = $"PDF raporu oluşturuldu: {Path.GetFileName(dialog.FileName)}; " +
                $"{balloonsByPage.Sum(page => page.Value.Count)} balon kaydı dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PDF raporu oluşturulamadı",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "PDF raporu oluşturulamadı.";
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourceDisplayName))
        {
            MessageBox.Show("Önce bir PDF, DXF veya görsel açın.", "Excel raporu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(_sourceDisplayName) + "-olcum-raporu.xlsx";
        var dialog = new SaveFileDialog
        {
            Filter = "Excel çalışma kitabı|*.xlsx",
            FileName = defaultName,
            AddExtension = true,
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var balloonsByPage = new Dictionary<int, List<StoredBalloon>>();
            if (_pdfPath is not null)
            {
                SaveCurrentPdfAnnotations();
                foreach (var page in _pdfPageBalloons)
                    balloonsByPage[page.Key] = page.Value.Select(ToStoredBalloon).ToList();
            }
            else
            {
                balloonsByPage[0] = _balloons.Select(ToStoredBalloon).ToList();
            }

            InspectionReportService.ExportExcel(
                dialog.FileName,
                _sourceDisplayName,
                balloonsByPage);

            StatusText.Text = $"Excel raporu oluşturuldu: {Path.GetFileName(dialog.FileName)}; " +
                $"{balloonsByPage.Sum(page => page.Value.Count)} balon kaydı dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Excel raporu oluşturulamadı",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Excel raporu oluşturulamadı.";
        }
    }

    private static string FormatBalloonCsvRow(int? pageNumber, StoredBalloon balloon)
    {
        static string Cell(string? value)
        {
            var text = value ?? string.Empty;
            return text.Contains(';') || text.Contains('"') || text.Contains('\r') || text.Contains('\n')
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;
        }

        return string.Join(";",
            pageNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            balloon.Number.ToString(CultureInfo.InvariantCulture),
            balloon.X.ToString(CultureInfo.InvariantCulture),
            balloon.Y.ToString(CultureInfo.InvariantCulture),
            Cell(balloon.Characteristic),
            Cell(balloon.NominalValue),
            Cell(balloon.LowerTolerance),
            Cell(balloon.UpperTolerance),
            Cell(balloon.MeasuredValue),
            Cell(balloon.Result),
            Cell(balloon.Notes)) + "\r\n";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV dosyası|*.csv",
            FileName = "balon-listesi.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder("Sayfa;No;X_Piksel;Y_Piksel;Karakteristik;Nominal;Alt_Tolerans;Ust_Tolerans;Olculen;Sonuc;Notlar\r\n");
        if (_pdfPath is not null)
        {
            SaveCurrentPdfAnnotations();
            foreach (var page in _pdfPageBalloons.OrderBy(p => p.Key))
            {
                foreach (var balloon in page.Value.OrderBy(b => b.Number))
                {
                    sb.Append(FormatBalloonCsvRow(page.Key + 1, ToStoredBalloon(balloon)));
                }
            }
        }
        else
        {
            foreach (var balloon in _balloons)
            {
                sb.Append(FormatBalloonCsvRow(null, ToStoredBalloon(balloon)));
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
    private string _characteristic = string.Empty;
    private string _nominalValue = string.Empty;
    private string _upperTolerance = string.Empty;
    private string _lowerTolerance = string.Empty;
    private string _measuredValue = string.Empty;
    private string _result = "Beklemede";
    private string _notes = string.Empty;
    private string _dimensionText = string.Empty;
    private double _dimensionLeft = -1;
    private double _dimensionTop = -1;
    private double _dimensionWidth;
    private double _dimensionHeight;

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

    public string Characteristic
    {
        get => _characteristic;
        set => SetField(ref _characteristic, value ?? string.Empty);
    }

    public string NominalValue
    {
        get => _nominalValue;
        set => SetField(ref _nominalValue, value ?? string.Empty);
    }

    public string UpperTolerance
    {
        get => _upperTolerance;
        set => SetField(ref _upperTolerance, value ?? string.Empty);
    }

    public string LowerTolerance
    {
        get => _lowerTolerance;
        set => SetField(ref _lowerTolerance, value ?? string.Empty);
    }

    public string MeasuredValue
    {
        get => _measuredValue;
        set => SetField(ref _measuredValue, value ?? string.Empty);
    }

    public string Result
    {
        get => _result;
        set => SetField(ref _result, value ?? "Beklemede");
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value ?? string.Empty);
    }

    public string DimensionText
    {
        get => _dimensionText;
        set => SetField(ref _dimensionText, value ?? string.Empty);
    }

    public double DimensionLeft { get => _dimensionLeft; set => SetField(ref _dimensionLeft, value); }
    public double DimensionTop { get => _dimensionTop; set => SetField(ref _dimensionTop, value); }
    public double DimensionWidth { get => _dimensionWidth; set => SetField(ref _dimensionWidth, value); }
    public double DimensionHeight { get => _dimensionHeight; set => SetField(ref _dimensionHeight, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}