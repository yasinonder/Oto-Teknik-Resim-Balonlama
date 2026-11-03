# WAT Teknik Resim Balonlama — WPF temel sürüm 0.2.0

C# / WPF masaüstü uygulamasıdır; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da `WAT.DrawingInspector/WAT.DrawingInspector.csproj` dosyasını açın ve NuGet paketlerini geri yükleyin.

## OCR dil verileri
Uygulama çalıştırma klasöründe `tessdata` dizini oluşturup Tesseract'ın `tur.traineddata` ve `eng.traineddata` dosyalarını ekleyin.

## Bu sürümde bulunanlar
- WPF masaüstü arayüzü
- PNG/JPG/JPEG/BMP/TIFF görsel açma
- Görsel üzerinde tıklayarak manuel balon yerleştirme
- Balon numarası ve piksel koordinatı listesi
- Tesseract Türkçe + İngilizce OCR
- OCR kelimeleri için Tesseract bounding-box koordinatlarının alınması
- Algılanan kelimelerin çizim üzerinde yeşil kutularla işaretlenmesi
- OCR kelime, koordinat ve güven skorunun sonuç penceresinde gösterilmesi
- Balon listesini CSV dışa aktarma

## Henüz tamamlanmayanlar
1. OCR kutularını düzenleme ve algılanan metni ölçü/karakter sınıflarına ayırma
2. PDF sayfa görüntüleme ve OCR
3. DXF içe aktarma; DWG için ODA File Converter tabanlı dönüştürme
4. Teknik ölçülerin otomatik tanınması ve otomatik balonlama
5. Balon sürükleme/düzenleme ve balon-metinsel ölçü ilişkilendirmesi
6. Excel ve PDF kontrol raporları
7. SQLite proje, çizim ve kontrol verisi saklama
8. Self-contained x64 yayın ve taşınabilir ZIP

OCR metni ve güven skoru, ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler yetkili kullanıcı tarafından çizim üzerinden doğrulanmalıdır.