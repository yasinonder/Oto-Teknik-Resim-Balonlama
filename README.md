# WAT Teknik Resim Balonlama — WPF temel sürüm 0.7.0

C# / WPF masaüstü uygulamasıdır; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da WAT.DrawingInspector/WAT.DrawingInspector.csproj dosyasını açın ve NuGet paketlerini geri yükleyin.

## OCR dil verileri
Uygulama çalıştırma klasöründe tessdata dizini oluşturup Tesseract'ın tur.traineddata ve eng.traineddata dosyalarını ekleyin.

## Bu sürümde bulunanlar
- PDF dosyası açma, sayfa sayısı ve önceki/sonraki sayfa gezinme
- PDF sayfası üzerinde OCR ve manuel balon yerleştirme
- DXF okuma ve raster görüntüleme
- DXF LINE, CIRCLE, ARC, LWPOLYLINE, POLYLINE ve TEXT varlıklarını görüntüleme
- DXF BLOCK / INSERT çözümleme; blok taban noktasını, konumunu, döndürmesini ve X/Y ölçeklerini uygulama
- İç içe blok referanslarını güvenli derinlik sınırıyla açma
- DXF DIMENSION varlıklarını, BlockName ile bağlı ölçü bloğundaki ölçü çizgileri ve yazı üzerinden görüntüleme
- Blok dizilerinde (row/column) tekrar eden yerleşimleri görüntüleme; tek yönde en fazla 32 satır/sütun
- Geometriyi en-boy oranını koruyarak en fazla 5000 px kenar boyutunda rasterleştirme
- PNG/JPG/JPEG/BMP/TIFF görsel açma
- Görsel, PDF veya DXF üzerinde manuel balon yerleştirme
- Tesseract Türkçe + İngilizce OCR ve kelime konum kutuları
- Balon seçme, sürükleyerek taşıma ve seçili balonu tek tek silme
- Balon listesini CSV dışa aktarma; PDF sayfa numarasını dosyaya yazma
- `.watproj` SQLite projesine kaynak çizimi BLOB olarak gömme
- PDF sayfalarına göre balonları ve sonraki balon numaralarını SQLite tablolarında kalıcı saklama
- SQLite proje açma/kaydetme; taşınabilir tek proje dosyası

## Teknik sınırlar
- DXF 2B geometri aşamasındadır. DIMENSION görüntüsü, ölçü varlığının BlockName ile işaret ettiği blokta mevcut geometrinin rasterleştirilmesi yoluyla gösterilir; blok bulunmuyorsa ölçü varlığı desteklenmeyen olarak sayılır. Ölçü değerinin parametrelerden yeniden hesaplanması veya doğruluk onayı henüz yapılmaz. HATCH, SPLINE, MLEADER, 3B yüzey/katı, raster referansları, özel nesneler ve diğer desteklenmeyen varlıklar henüz çizime aktarılmaz; açılışta desteklenmeyen varlık sayısı bildirilir.
- Blokların temel 2B geometrileri dönüştürülür. Blok içindeki bazı gelişmiş varlıklar halen desteklenmeyebilir. XREF gibi dış dosya referanslarının yüklenmesi yoktur.
- DXF çizimi raster görüntüye dönüştürülür. Orijinal vektör nesneleri bu aşamada düzenlenebilir CAD modeli olarak tutulmaz ve DXF'e geri yazılmaz.
- DWG açma bu sürümde yoktur. ODA File Converter Windows için komut satırı sunar; ODA, üye olmayanların bu aracı ticari olmayan uygulamalar için kullanabileceğini belirtir. Şirket içi/ticari kullanım için ODA ile uygun lisans/izin teyit edilmeden bu araç uygulamaya gömülmez veya paketlenmez.
- PDF sayfaları PDFium üzerinden raster görüntüye dönüştürülür; PDF vektör nesneleri doğrudan analiz edilmez.
- OCR çıktısı ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler kullanıcı tarafından doğrulanmalıdır.

## Henüz tamamlanmayanlar
1. DIMENSION için alternatif fallback geometrileri; HATCH/SPLINE görüntülenmesi
2. DWG için ticari kullanım koşulları doğrulanmış dönüştürme/SDK entegrasyonu
3. Otomatik ölçü tanıma ve otomatik balonlama
4. Balonlara ölçü değeri, tolerans, karakteristik adı ve kontrol sonucu alanları ekleme
5. Excel ve PDF kontrol raporları
6. SQLite proje verisini rapor/ölçüm sonuçlarıyla genişletme
7. Tesseract dil verilerinin uygulama paketine lisans/dağıtım kontrolüyle eklenmesi
8. Self-contained x64 yayın ve taşınabilir ZIP