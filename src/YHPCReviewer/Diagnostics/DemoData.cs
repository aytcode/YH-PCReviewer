using YHPCReviewer.Models;
using YHPCReviewer.Services;

namespace YHPCReviewer.Diagnostics;

/// <summary>
/// "--demo" ile açıldığında kullanılan örnek rapor. Ekran görüntüleri ve tanıtım için;
/// gerçek bilgisayardan hiçbir bilgi okunmaz, geçmişe yazılmaz.
/// </summary>
public static class DemoData
{
    private const long GB = 1024L * 1024 * 1024;

    public static DiagnosticReport Create() => new()
    {
        AppVersion = "demo",
        System = new SystemInfo
        {
            ComputerName = "DEMO-PC",
            UserName = "demo",
            OsName = "Windows 11 Pro",
            OsBuild = "24H2 (Derleme 26100.2894)",
            Architecture = "X64",
            Cpu = "AMD Ryzen 7 7800X3D 8-Core Processor",
            CpuCores = 8,
            CpuThreads = 16,
            RamTotalGb = 31.9,
            RamAvailableGb = 18.4,
            Gpus = ["NVIDIA GeForce RTX 4070 (sürücü 31.0.15.3623)"],
            Motherboard = "ASUS TUF GAMING B650-PLUS",
            Uptime = TimeSpan.FromHours(53),
            Disks =
            [
                new DiskInfo { Name = "C:", Format = "NTFS", TotalBytes = 1000 * GB, FreeBytes = 38 * GB, IsSystem = true },
                new DiskInfo { Name = "D:", Label = "Oyunlar", Format = "NTFS", TotalBytes = 2000 * GB, FreeBytes = 912 * GB }
            ],
            Adapters =
            [
                new AdapterInfo
                {
                    Name = "Ethernet", Description = "Realtek Gaming 2.5GbE Family Controller", Type = "Kablolu (Ethernet)",
                    Status = "Bağlı", SpeedMbps = 1000, Addresses = ["192.168.1.24"], Gateways = ["192.168.1.1"], DnsServers = ["192.168.1.1"]
                }
            ]
        },
        AppErrors =
        [
            new AppErrorInfo { Application = "eldenring.exe", Module = "nvwgf2umx.dll", Count = 5, LastSeen = DateTime.Now.AddHours(-20) },
            new AppErrorInfo { Application = "Discord.exe", Module = "ntdll.dll", Count = 3, LastSeen = DateTime.Now.AddDays(-2) },
            new AppErrorInfo { Application = "steamwebhelper.exe", Module = "libcef.dll", Count = 1, LastSeen = DateTime.Now.AddDays(-6) }
        ],
        Results =
        [
            new CheckResult
            {
                Category = CheckCategory.Disk, Title = "Disk alanı – C:", Severity = Severity.Warning,
                Summary = "Windows'un kurulu olduğu disk dolmak üzere (38,0 GB boş, %4).",
                Details = "NTFS, toplam 1000,0 GB, kullanılan %96,2",
                Steps =
                [
                    "Başlat'a \"Disk Temizleme\" yaz, diski seç ve \"Sistem dosyalarını temizle\" ile eski güncelleme dosyalarını da sil.",
                    "Kullanmadığın büyük oyun ve programları Ayarlar > Uygulamalar'dan kaldır.",
                    "İndirilenler klasörünü ve Geri Dönüşüm Kutusu'nu kontrol et."
                ],
                Actions = [FixAction.Settings("Depolama ayarlarını aç", "ms-settings:storagesense")]
            },
            new CheckResult
            {
                Category = CheckCategory.System, Title = "Ekran kartı sürücüsü", Severity = Severity.Warning,
                Summary = "Ekran kartı sürücün bir yıldan eski. Oyunların açılmaması, çökmesi veya siyah ekran gibi sorunların en sık sebeplerinden biri budur.",
                Details = "NVIDIA GeForce RTX 4070 – sürücü tarihi 14.08.2024",
                Steps =
                [
                    "Ekran kartı üreticisinin sitesinden güncel sürücüyü indirip kur.",
                    "Kurulumdan sonra bilgisayarı yeniden başlat."
                ],
                Actions = [FixAction.Web("NVIDIA sürücüsünü indir", "https://www.nvidia.com/tr-tr/drivers/")]
            },
            new CheckResult
            {
                Category = CheckCategory.Applications, Title = "Uygulama çökmeleri", Severity = Severity.Warning,
                Summary = "Son 14 günde 3 farklı programda toplam 9 çökme kaydı var. En sık: eldenring.exe, Discord.exe, steamwebhelper.exe.",
                Details = "eldenring.exe – Çökme ×5 (modül: nvwgf2umx.dll)\nDiscord.exe – Çökme ×3 (modül: ntdll.dll)\nsteamwebhelper.exe – Çökme ×1 (modül: libcef.dll)",
                Steps =
                [
                    "Çökmeler ekran kartı sürücüsünden kaynaklanıyor gibi görünüyor: ekran kartı sürücünü güncelle.",
                    "Sorun yaşayan programı/oyunu en son sürümüne güncelle.",
                    "Oyunlarda: Steam'de oyuna sağ tıkla > Özellikler > Yüklü Dosyalar > \"Oyun dosyalarının bütünlüğünü doğrula\"."
                ]
            },
            new CheckResult
            {
                Category = CheckCategory.Disk, Title = "Geçici dosyalar", Severity = Severity.Info,
                Summary = "Geçici dosyalar klasöründe 6,2 GB dosya birikmiş. Bunlar güvenle silinebilir.",
                Steps = ["Aşağıdaki \"Geçici dosyaları temizle\" düğmesini kullan."],
                Actions = [new FixAction { Label = "Geçici dosyaları temizle", Kind = FixKind.Open, Target = "ms-settings:storagesense", Glyph = "" }]
            },
            new CheckResult
            {
                Category = CheckCategory.System, Title = "Açılışta başlayan programlar", Severity = Severity.Info,
                Summary = "Windows açılırken 14 program otomatik başlıyor. Bu, açılışı ve genel performansı yavaşlatabilir.",
                Actions = [FixAction.Settings("Başlangıç uygulamalarını aç", "ms-settings:startupapps")]
            },
            Ok(CheckCategory.Network, "Ağ kartı", "Ağ kartın çalışıyor ve bir ağa bağlı."),
            Ok(CheckCategory.Network, "Modem / yönlendirici", "Bilgisayar modemine ulaşabiliyor."),
            Ok(CheckCategory.Network, "İnternet erişimi", "İnternete ulaşılabiliyor."),
            Ok(CheckCategory.Network, "DNS (site adlarını çözme)", "Site adları (ör. google.com) doğru şekilde adrese çevrilebiliyor."),
            Ok(CheckCategory.Network, "Web sayfalarına erişim", "Web sayfaları normal şekilde yüklenebiliyor."),
            Ok(CheckCategory.Network, "Gecikme (ping)", "Ortalama gecikme 9 ms; oyun ve görüntülü görüşme için iyi."),
            Ok(CheckCategory.Disk, "Disk sağlığı", "2 diskin hepsi sağlıklı görünüyor."),
            Ok(CheckCategory.System, "Bellek (RAM)", "Bellek kullanımı normal (%42)."),
            Ok(CheckCategory.System, "Son yeniden başlatma", "Bilgisayar 2 gün 5 saat önce başlatılmış."),
            Ok(CheckCategory.Applications, "Oyun ve program bileşenleri", "Oyunların ihtiyaç duyduğu Microsoft Visual C++ bileşenleri yüklü."),
            Ok(CheckCategory.Security, "Windows güncellemeleri", "Windows güncel görünüyor (son güncelleme 9 gün önce)."),
            Ok(CheckCategory.Security, "Virüs koruması", "Virüs koruması açık ve güncel (Windows Defender).")
        ]
    };

    public static List<HistoryEntry> History() =>
    [
        new() { Date = DateTime.Now, Score = 82, Errors = 0, Warnings = 3, ScoreDelta = 6,
                Issues = ["Disk alanı – C:", "Ekran kartı sürücüsü", "Uygulama çökmeleri"], ResolvedIssues = ["Bekleyen güncelleme"] },
        new() { Date = DateTime.Now.AddDays(-5), Score = 76, Errors = 0, Warnings = 4,
                Issues = ["Disk alanı – C:", "Ekran kartı sürücüsü", "Uygulama çökmeleri", "Bekleyen güncelleme"] }
    ];

    private static CheckResult Ok(CheckCategory c, string title, string summary) =>
        new() { Category = c, Title = title, Severity = Severity.Ok, Summary = summary };
}
