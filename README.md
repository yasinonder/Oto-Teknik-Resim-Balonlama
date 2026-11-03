# WAT Teknik Resim Balonlama — WPF temel sürüm 0.4.0

C# / WPF masaüstü uygulamasıdır; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da WAT.DrawingInspector/WAT.DrawingInspector.csproj dosyasını açın ve NuGet paketlerini geri yükleyin.

## OCR dil verileri
Uygulama çalıştırma klasöründe tessdata dizini oluşturup Tesseract'ın tur.traineddata ve eng.traineddata dosyalarını ekleyin.

## Bu sürümde bulunanlar
- PDF dosyası açma ve sayfa sayısını okuma
- PDF sayfaları arasında gezinme; balonları geçerli uygulama oturumu içinde sayfa bazında tutma
- PDF sayfasını 150 DPI PNG görüntüsüne dönüştürme
- DXF dosyası okuma ve 2B geometrileri görüntüleme
- DXF LINE, CIRCLE, ARC, LWPOLYLINE, POLYLINE ve TEXT varlıklarını WPF görüntüsüne aktarma
- DXF çizimlerini en-boy oranını koruyarak en fazla 5000 px kenar boyutunda rasterleştirme
- DXF'den rasterleştirilen çizime OCR ve manuel balon uygulama
- PNG/JPG/JPEG/BMP/TIFF görsel açma
- Görsel üzerinde tıklayarak manuel balon yerleştirme
- Balon numarası ve piksel koordinatı listesi
- Tesseract Türkçe + İngilizce OCR
- OCR kelimelerini çizim üzerinde bounding-box kutularıyla işaretleme
- OCR kelime, koordinat ve güven skorunu gösterme
- PDF sayfası numaralı veya tek çizim için CSV balon dışa aktarma

## Teknik sınırlar
- DXF içe aktarma bu aşamada 2B LINE, CIRCLE, ARC, LWPOLYLINE, POLYLINE ve TEXT varlıklarını işler. INSERT/blok referansları, ölçülendirme varlıkları, HATCH, SPLINE, 3B ve diğer desteklenmeyen varlıklar henüz görüntüye aktarılmaz; açılış durumunda desteklenmeyen varlık sayısı bildirilir.
- DXF çizimi bu aşamada raster görüntüye dönüştürülür. Orijinal vektör nesneleri henüz düzenlenebilir CAD modeli olarak tutulmaz ve DXF'e geri yazılmaz.
- PDF sayfaları PDFium üzerinden raster görüntüye dönüştürülür; PDF vektör nesneleri doğrudan analiz edilmez.
- DWG açma bu sürümde yoktur. DWG için ODA File Converter gibi harici bir DXF dönüştürme aracı veya lisanslı bir DWG SDK gerekir.
- OCR metni ve güven skoru ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler kullanıcı tarafından doğrulanmalıdır.

## Henüz tamamlanmayanlar
1. DXF blok/INSERT çözümleme ve ölçülendirme varlıklarının görüntülenmesi
2. DWG dönüştürme entegrasyonu
3. Otomatik ölçü tanıma ve otomatik balonlama
4. Balon sürükleme, düzenleme, silme ve ölçüyle ilişkilendirme
5. Excel ve PDF kontrol raporları
6. SQLite proje ve kontrol verisi saklama
7. Tesseract dil verilerinin uygulama paketine lisans/dağıtım kontrolüyle eklenmesi
8. Self-contained x64 yayın ve taşınabilir ZIP