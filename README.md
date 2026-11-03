# WAT Teknik Resim Balonlama — WPF temel sürüm 0.1.0

Bu depo C# / WPF masaüstü uygulaması içindir; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da `WAT.DrawingInspector/WAT.DrawingInspector.csproj` dosyasını açın. NuGet paketlerinin geri yüklenmesine izin verin.

## OCR dil verileri
Uygulama klasöründe `tessdata` dizini oluşturup Tesseract'ın `tur.traineddata` ve `eng.traineddata` dosyalarını ekleyin.

## Bu temelde bulunanlar
- WPF masaüstü arayüzü
- PNG/JPG/JPEG/BMP/TIFF görsel açma
- Görsel üzerinde tıklayarak manuel balon yerleştirme
- Balon numarası ve piksel koordinatı listesi
- Tesseract ile Türkçe + İngilizce OCR altyapısı
- Balon listesini CSV dışa aktarma

## Geliştirme sırası / henüz tamamlanmayanlar
1. OCR kelime/karakter koordinat kutularını çizim koordinatlarına bağlama
2. PDF sayfa görüntüleme ve OCR
3. DXF içe aktarma; DWG için ODA File Converter tabanlı dönüştürme
4. Ölçü tespiti ve otomatik balonlama, balon sürükleme/düzenleme
5. Excel ve PDF kontrol raporları
6. SQLite proje, çizim ve kontrol verisi saklama
7. Self-contained x64 yayın ve taşınabilir ZIP

OCR çıktısı tek başına ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler teknik resim üzerinden yetkili kullanıcı tarafından doğrulanmalıdır.