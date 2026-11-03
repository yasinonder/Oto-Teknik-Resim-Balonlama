# WAT Teknik Resim Balonlama — WPF temel sürüm 0.3.0

C# / WPF masaüstü uygulamasıdır; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da WAT.DrawingInspector/WAT.DrawingInspector.csproj dosyasını açın ve NuGet paketlerini geri yükleyin.

## OCR dil verileri
Uygulama çalıştırma klasöründe tessdata dizini oluşturup Tesseract'ın tur.traineddata ve eng.traineddata dosyalarını ekleyin.

## Bu sürümde bulunanlar
- WPF masaüstü arayüzü
- PDF dosyası açma ve sayfa sayısını okuma
- PDF sayfaları arasında önceki/sonraki gezinme
- PDF sayfasını 150 DPI PNG olarak çizim alanına aktarma
- PDF'nin her sayfasında balonların ayrı tutulması; sayfalar arasında gezinince mevcut sayfa balonlarının korunması
- PDF üzerindeki balonları sayfa numarasıyla CSV dışa aktarma
- PNG/JPG/JPEG/BMP/TIFF görsel açma
- Görsel üzerinde tıklayarak manuel balon yerleştirme
- Balon numarası ve piksel koordinatı listesi
- Tesseract Türkçe + İngilizce OCR
- OCR kelimeleri için bounding-box koordinatlarını alma
- Algılanan kelimeleri çizim üzerinde yeşil kutularla işaretleme
- OCR kelime, koordinat ve güven skorunu sonuç penceresinde gösterme
- Balon listesini CSV dışa aktarma

## Teknik notlar
- PDF sayfaları PDFium üzerinden raster görüntüye dönüştürülür; bu sürüm henüz PDF içindeki vektör çizgi/ölçü nesnelerini doğrudan okumaz.
- PDF sayfaları 150 DPI çözünürlükte, tek tek işlenir. Çok büyük paftalarda bellek ihtiyacını düşürmek için bütün PDF aynı anda görüntüye çevrilmez.
- PDFtoImage paketi MIT lisanslıdır. Bağımlılıklarının lisans ve dağıtım bildirimleri yayımlanacak paketle birlikte ayrıca denetlenmelidir.
- OCR metni ve güven skoru ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler yetkili kullanıcı tarafından çizim üzerinden doğrulanmalıdır.

## Henüz tamamlanmayanlar
1. OCR kutularını düzenleme ve algılanan metni ölçü/karakter sınıflarına ayırma
2. DXF içe aktarma; DWG için ODA File Converter tabanlı dönüştürme
3. Teknik ölçülerin otomatik tanınması ve otomatik balonlama
4. Balon sürükleme/düzenleme ve balon-metinsel ölçü ilişkilendirmesi
5. Excel ve PDF kontrol raporları
6. SQLite proje, çizim ve kontrol verisi saklama
7. Self-contained x64 yayın ve taşınabilir ZIP