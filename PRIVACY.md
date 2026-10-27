# Gizlilik

YH PC Reviewer bilgisayarında çalışır ve topladığı bilgileri **yalnızca senin ekranında** gösterir.
Sen bir işlem başlatmadıkça hiçbir bilgi bilgisayarından çıkmaz.

## Neyi okur?

| Bilgi | Kaynak | Neden |
|---|---|---|
| Windows sürümü, işlemci, bellek, ekran kartı, anakart modeli | Kayıt defteri, WMI | Sistem özeti |
| Disk boyutu, boş alan, disk sağlık durumu | Windows depolama API'leri | Disk kontrolleri |
| Ağ kartları, IP, ağ geçidi, DNS sunucuları | .NET ağ API'leri | Bağlantı testleri |
| Son 14 günün uygulama çökmeleri, son 30 günün beklenmedik kapanma/mavi ekran/disk hatası kayıtları | Windows olay günlükleri | Çökme ve donanım sorunlarını bulmak |
| Açılışta başlayan programların adları | Kayıt defteri, Başlangıç klasörü | Yavaşlık önerisi |
| Geçici dosyalar klasörünün boyutu | Dosya sistemi | Temizlik önerisi |
| Antivirüs durumu, son güncelleme tarihi, güç planı, pil kapasitesi | Windows Güvenlik Merkezi, WMI, powercfg | İlgili kontroller |

**Okumadıkları:** belgelerin ve dosyaların içeriği, şifreler, tarayıcı geçmişi, Wi-Fi ağ adı ve şifresi, e-postalar, seri numaraları.

## Ne saklar?

- `%LOCALAPPDATA%\YHPCReviewer\history.json`: her analizin **özeti** (tarih, puan, sorun başlıkları). Kişisel bilgi içermez; “Geçmişi temizle” ile silinir.
- `%APPDATA%\YHPCReviewer\settings.json`: yalnızca AYT AI'yi bağladıysan adres, model ve **Windows DPAPI ile şifrelenmiş** API anahtarı.

## Ne gönderir?

- **Rapor paylaşma:** Rapor yalnızca sen “Dosyaya kaydet” ya da “Kopyala” dediğinde dışarı çıkar. Kullanıcı adı, bilgisayar adı, IP ve MAC adresleri varsayılan olarak gizlenir; göndermeden önce metni görür ve düzenleyebilirsin.
- **AYT AI:** Yalnızca sen bir soru sorduğunda, senin girdiğin adrese gönderilir. İlk soruda rapor gönderilmeden önce gizlenmiş hâli gösterilir ve onayın istenir.
- **Ağ testleri:** README'deki “Bağlandığı adresler” tablosundaki sunuculara yalnızca bağlantıyı ölçmek için ping/DNS/HTTP isteği yapılır; bu isteklerle hiçbir kişisel veri gönderilmez.

Telemetri, kullanım istatistiği, reklam ya da otomatik güncelleme **yoktur**.
