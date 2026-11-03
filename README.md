# Teknik Resim Balonlama — WPF temel sürüm 0.7.0

C# / WPF masaüstü uygulamasıdır; Python/Tkinter değildir.

## Geliştirme ortamı
- Visual Studio 2022
- .NET 8 SDK
- Windows 10/11 x64

Visual Studio'da WAT.DrawingInspector/WAT.DrawingInspector.csproj dosyasını açın ve NuGet paketlerini geri yükleyin.

## OCR dil verileri
Kaynak kodu ilk kez hazırlarken PowerShell'de şu komutu çalıştırın:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Download-Tessdata.ps1
```

Script, resmi Tesseract `tessdata_fast` deposundan sabitlenmiş sürümdeki `tur.traineddata` ve `eng.traineddata` dosyalarını `WAT.DrawingInspector/tessdata` klasörüne indirir ve SHA-256 özetlerini gösterir. Uygulama LSTM OCR motorunu kullanır. Dil dosyaları projede yerel olarak bulunduğunda derleme ve publish çıktısına otomatik kopyalanır; bir kez indirildikten sonra uygulamanın OCR çalışması için internet bağlantısı gerekmez. Dosyalar henüz kaynak depoya gömülü değildir; çevrimdışı kurulum için bu iki dosya önceden temin edilip aynı klasöre konmalıdır.

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
- Tesseract Türkçe + İngilizce OCR; genel notları otomatik balon adayı yapmayan, sıkı satır hizalama ve güven filtresi kullanan ölçü adayı çıkarımı
- OCR ekranında yalnızca ölçü adaylarını gösterme; açıklama, standart notu ve diğer genel yazıları otomatik olarak işaretlememe
- Kullanıcının çizim üzerinde fareyle alan seçerek yalnızca seçilen bölgeyi OCR ile okuması
- Seçilen ölçüyü mevcut balona bağlama veya ölçünün yanına yeni balon oluşturma; orijinal ölçü metnini, nominal/tolerans önerisini ve çizimdeki alan koordinatlarını saklama
- Bağlı ölçü alanını mavi kutuyla çizimde gösterme; bağlantıyı SQLite proje dosyasına kaydetme
- Balon seçme, sürükleyerek taşıma ve seçili balonu tek tek silme
- Balon listesini CSV dışa aktarma; sayfa, koordinat ve ölçüm alanlarını yazma
- Seçili balon için karakteristik adı, çizimden okunan orijinal ölçü, nominal değer, alt/üst tolerans sapması, ölçülen değer ve not girişi
- Program içinde tüm PDF sayfalarındaki karakteristikleri toplu düzenlemek için düzenlenebilir ölçüm raporu tablosu; nominal/tolerans/ölçülen değer değiştikçe Uygun / Uygun Değil / Beklemede sonucunu yeniden hesaplama
- Çizim görünümünde ekrana sığdırma, gerçek boyut, yakınlaştırma/uzaklaştırma ve Ctrl + fare tekerleği desteği
- İşaretli tolerans sapmalarına göre otomatik Uygun / Uygun Değil / Beklemede değerlendirmesi
- `.watproj` SQLite projesine kaynak çizimi BLOB olarak gömme
- PDF sayfalarına göre balonları, ölçüm verilerini ve sonraki balon numaralarını kalıcı saklama
- SQLite proje açma/kaydetme; tek dosyalı proje
- Excel `.xlsx` ölçüm raporu: sayfa/balon bilgileri, ölçüler, toleranslar, sonuç renklendirmesi ve filtre
- PDF ölçüm raporu: A4 yatay tablo, sayfa/balon bilgileri, toleranslar ve sonuç renklendirmesi
- Üç farklı ölçüm sonucu için SQLite, Excel ve PDF çıktılarını sınayan Windows smoke testleri

## Teknik sınırlar
- DXF 2B geometri aşamasındadır. DIMENSION görüntüsü, ölçü varlığının BlockName ile işaret ettiği blokta mevcut geometrinin rasterleştirilmesi yoluyla gösterilir; blok bulunmuyorsa ölçü varlığı desteklenmeyen olarak sayılır. Ölçü değerinin parametrelerden yeniden hesaplanması veya doğruluk onayı henüz yapılmaz. HATCH, SPLINE, MLEADER, 3B yüzey/katı, raster referansları, özel nesneler ve diğer desteklenmeyen varlıklar henüz çizime aktarılmaz; açılışta desteklenmeyen varlık sayısı bildirilir.
- Blokların temel 2B geometrileri dönüştürülür. Blok içindeki bazı gelişmiş varlıklar halen desteklenmeyebilir. XREF gibi dış dosya referanslarının yüklenmesi yoktur.
- DXF çizimi raster görüntüye dönüştürülür. Orijinal vektör nesneleri bu aşamada düzenlenebilir CAD modeli olarak tutulmaz ve DXF'e geri yazılmaz.
- DWG desteği bu projenin dağıtım kapsamına alınmamıştır. Ticari lisans veya ayrıca onay gerektirebilecek DWG dönüştürücü/SDK paketlenmeyecektir. Bu nedenle taşınabilir ZIP; PDF, DXF ve PNG/JPG/JPEG/BMP/TIFF dosyalarını destekleyecek şekilde hazırlanacaktır.
- PDF sayfaları PDFium üzerinden raster görüntüye dönüştürülür; PDF vektör nesneleri doğrudan analiz edilmez.
- OCR ölçü adayları sezgisel metin eşleştirmesidir: aynı satırda tek bir ölçü ifadesi, uygun metin kalıbı ve güven eşiği aranır; genel not metinleri ve çoklu sayısal ifadeler elenir. OCR tahmini kesin ölçüm değildir. Balona bağlanan ham ölçü metni saklanır; nominal ve tolerans alanları öneri olarak doldurulur ve kullanıcı tarafından doğrulanmalıdır. Seçim alanı OCR doğruluğunu iyileştirebilir ancak mühendis onayının yerini almaz.
- OCR çıktısı ölçüm doğruluğu veya teknik uygunluk onayı değildir. Kritik ölçüler kullanıcı tarafından doğrulanmalıdır.

## Henüz tamamlanmayanlar
1. DIMENSION için alternatif fallback geometrileri; HATCH/SPLINE görüntülenmesi
2. DWG için ticari kullanım koşulları doğrulanmış dönüştürme/SDK entegrasyonu
3. Otomatik ölçü tanıma ve otomatik balonlama
4. Gerçek üretim çizimlerinde farklı yazı tipleri, dikey/yatay ölçüler, birleşik ve tek taraflı toleranslar ile GD&T okuma doğruluğunun geniş testlerle iyileştirilmesi
5. DXF için HATCH/SPLINE ve daha kapsamlı teknik çizim varlık desteği
6. Gerçek Windows istemci testleri ve ölçü yakalama/rapor düzenleme iş akışının saha doğrulaması
