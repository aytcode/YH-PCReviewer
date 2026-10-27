using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using Microsoft.Win32;
using YHPCReviewer.Models;

namespace YHPCReviewer.Diagnostics;

/// <summary>Disk, bellek, açılış, oyun hazırlığı ve güvenlik durumuyla ilgili salt-okunur kontroller.</summary>
public static class SystemChecks
{
    private const long GB = 1024L * 1024 * 1024;

    public static List<CheckResult> Run(SystemInfo info)
    {
        var results = new List<CheckResult>();
        results.AddRange(CheckDisks(info));
        AddIfNotNull(results, CheckDiskHealth());
        results.Add(CheckMemory(info));
        results.Add(CheckUptime(info));
        AddIfNotNull(results, CheckPendingReboot());
        AddIfNotNull(results, CheckWindowsUpdateAge());
        AddIfNotNull(results, CheckTempFolder());
        AddIfNotNull(results, CheckStartupItems());
        AddIfNotNull(results, CheckGpuDrivers());
        AddIfNotNull(results, CheckVisualCpp());
        AddIfNotNull(results, CheckPowerPlan());
        AddIfNotNull(results, CheckBattery(info));
        AddIfNotNull(results, CheckAntivirus());
        return results;
    }

    private static void AddIfNotNull(List<CheckResult> list, CheckResult? r)
    {
        if (r is not null) list.Add(r);
    }

    private static IEnumerable<CheckResult> CheckDisks(SystemInfo info)
    {
        foreach (var d in info.Disks)
        {
            var freeGb = d.FreeBytes / (double)GB;
            var freePct = 100 - d.UsedPercent;
            var severity = d.IsSystem switch
            {
                true when freeGb < 5 || freePct < 5 => Severity.Error,
                true when freeGb < 15 || freePct < 10 => Severity.Warning,
                false when freePct < 3 => Severity.Warning,
                _ => Severity.Ok
            };

            var what = d.IsSystem ? "Windows'un kurulu olduğu disk" : "Disk";
            yield return new CheckResult
            {
                Category = CheckCategory.Disk,
                Title = $"Disk alanı – {d.Display}",
                Severity = severity,
                Summary = severity switch
                {
                    Severity.Error => $"{what} neredeyse dolu ({freeGb:0.0} GB boş). Bu durum yavaşlığa, güncellemelerin ve oyunların başarısız olmasına yol açar.",
                    Severity.Warning => $"{what} dolmak üzere ({freeGb:0.0} GB boş, %{freePct:0}).",
                    _ => $"{(d.IsSystem ? "Windows'un kurulu olduğu diskte" : "Bu diskte")} yeterli boş alan var ({freeGb:0.0} GB boş)."
                },
                Details = $"{d.Format}, toplam {d.TotalBytes / (double)GB:0.0} GB, kullanılan %{d.UsedPercent:0.0}",
                Steps = severity == Severity.Ok ? [] :
                [
                    "Başlat'a \"Disk Temizleme\" yaz, diski seç ve \"Sistem dosyalarını temizle\" ile eski güncelleme dosyalarını da sil.",
                    "Ayarlar > Sistem > Depolama > Geçici dosyalar bölümünden gereksiz dosyaları kaldır.",
                    "Kullanmadığın büyük oyun ve programları Ayarlar > Uygulamalar'dan kaldır.",
                    "İndirilenler klasörünü ve Geri Dönüşüm Kutusu'nu kontrol et."
                ],
                Actions = severity == Severity.Ok ? [] :
                [
                    FixAction.Settings("Depolama ayarlarını aç", "ms-settings:storagesense"),
                    new FixAction { Label = "Disk Temizleme'yi aç", Kind = FixKind.Open, Target = "cleanmgr.exe", Glyph = "" },
                    FixAction.Settings("Yüklü uygulamalar", "ms-settings:appsfeatures")
                ]
            };
        }
    }

    /// <summary>Windows'un kendi disk sağlık değerlendirmesi (SMART tabanlı). Yönetici izni gerektirmez.</summary>
    private static CheckResult? CheckDiskHealth()
    {
        var disks = new List<(string name, int health, string media)>();
        SystemInfoCollector.TryWmi("SELECT FriendlyName, HealthStatus, MediaType FROM MSFT_PhysicalDisk", o =>
        {
            var media = Convert.ToInt32(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Disk" };
            disks.Add((o["FriendlyName"]?.ToString() ?? "Disk", Convert.ToInt32(o["HealthStatus"]), media));
        }, @"root\Microsoft\Windows\Storage");

        if (disks.Count == 0) return null;

        var bad = disks.Where(d => d.health == 2).ToList();
        var warn = disks.Where(d => d.health == 1).ToList();
        var details = string.Join("\n", disks.Select(d => $"{d.name} ({d.media}): {d.health switch { 0 => "Sağlıklı", 1 => "Uyarı", 2 => "Sağlıksız", _ => "Bilinmiyor" }}"));

        if (bad.Count == 0 && warn.Count == 0)
            return new CheckResult
            {
                Category = CheckCategory.Disk,
                Title = "Disk sağlığı",
                Severity = Severity.Ok,
                Summary = disks.Count == 1 ? "Diskin sağlıklı görünüyor." : $"{disks.Count} diskin hepsi sağlıklı görünüyor.",
                Details = details
            };

        var names = string.Join(", ", bad.Concat(warn).Select(d => d.name));
        return new CheckResult
        {
            Category = CheckCategory.Disk,
            Title = "Disk sağlığı",
            Severity = bad.Count > 0 ? Severity.Error : Severity.Warning,
            Summary = bad.Count > 0
                ? $"Windows, {names} diskinin arızalı olduğunu bildiriyor. Veri kaybı yaşamamak için hemen yedek al."
                : $"Windows, {names} diskinde sorun belirtileri görüyor. Önemli dosyalarını yedeklemen önerilir.",
            Details = details,
            Steps =
            [
                "Fotoğraf, belge gibi önemli dosyalarını harici bir diske ya da buluta hemen yedekle.",
                "Üreticinin aracıyla (Samsung Magician, WD Dashboard, Crucial Storage Executive) ya da CrystalDiskInfo ile SMART değerlerine bak.",
                "Disk garanti kapsamındaysa değişim için üreticiye başvur."
            ],
            Actions = [FixAction.Settings("Diskleri yönet", "ms-settings:disksandvolumes")]
        };
    }

    private static CheckResult CheckMemory(SystemInfo info)
    {
        var used = info.RamUsedPercent;
        var severity = used switch
        {
            > 92 => Severity.Warning,
            > 85 => Severity.Info,
            _ => Severity.Ok
        };
        var lowRam = info.RamTotalGb is > 0 and < 7.5;
        if (lowRam && severity == Severity.Ok) severity = Severity.Info;

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Bellek (RAM)",
            Severity = severity,
            Summary = (severity, lowRam) switch
            {
                (Severity.Warning, _) => $"Belleğin %{used:0}'ı dolu. Programlar yavaşlayabilir veya kapanabilir.",
                (_, true) => $"Bilgisayarında {info.RamTotalGb:0} GB bellek var; güncel oyunlar ve çok sekmeli tarayıcılar için az olabilir.",
                (Severity.Info, _) => $"Belleğin %{used:0}'ı kullanımda; şu an yoğun.",
                _ => $"Bellek kullanımı normal (%{used:0})."
            },
            Details = $"Toplam {info.RamTotalGb:0.0} GB, boş {info.RamAvailableGb:0.0} GB",
            Steps = severity == Severity.Ok ? [] :
            [
                "Ctrl+Shift+Esc ile Görev Yöneticisi'ni aç ve \"Bellek\" sütununa göre sırala.",
                "Kullanmadığın programları ve tarayıcı sekmelerini kapat.",
                "Sorun sürekli yaşanıyorsa bilgisayarına RAM eklemeyi düşünebilirsin."
            ],
            Actions = severity == Severity.Ok ? [] :
                [new FixAction { Label = "Görev Yöneticisi'ni aç", Kind = FixKind.Open, Target = "taskmgr.exe", Glyph = "" }]
        };
    }

    private static CheckResult CheckUptime(SystemInfo info)
    {
        var days = info.Uptime.TotalDays;
        var long_ = days >= 7;
        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Son yeniden başlatma",
            Severity = long_ ? Severity.Info : Severity.Ok,
            Summary = long_
                ? $"Bilgisayar {days:0} gündür yeniden başlatılmamış. Birçok sorun basit bir yeniden başlatmayla düzelir."
                : $"Bilgisayar {info.UptimeDisplay} önce başlatılmış.",
            Details = "Not: \"Hızlı başlangıç\" açıksa Kapat düğmesi sayacı sıfırlamaz; Yeniden Başlat sıfırlar.",
            Steps = long_ ? ["Başlat > Güç > Yeniden başlat seçeneğini kullan (Kapat değil)."] : []
        };
    }

    private static CheckResult? CheckPendingReboot()
    {
        string[] keys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"
        ];
        if (!keys.Any(k => { using var key = Registry.LocalMachine.OpenSubKey(k); return key is not null; }))
            return null;

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Bekleyen güncelleme",
            Severity = Severity.Warning,
            Summary = "Bir Windows güncellemesi yeniden başlatma bekliyor. Yeniden başlatılana kadar bazı programlar ve oyunlar düzgün çalışmayabilir.",
            Steps = ["Çalışmalarını kaydet ve bilgisayarı yeniden başlat."],
            Actions = [FixAction.Settings("Windows Update'i aç", "ms-settings:windowsupdate")]
        };
    }

    /// <summary>En son yüklenen güncellemenin tarihi; uzun süre güncellenmeyen sistemler güvenlik ve uyumluluk sorunları yaşar.</summary>
    private static CheckResult? CheckWindowsUpdateAge()
    {
        DateTime? latest = null;
        SystemInfoCollector.TryWmi("SELECT InstalledOn FROM Win32_QuickFixEngineering", o =>
        {
            var raw = o["InstalledOn"]?.ToString();
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
                DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out d))
            {
                if (d <= DateTime.Now && (latest is null || d > latest)) latest = d;
            }
        });
        if (latest is null) return null;

        var days = (int)(DateTime.Now - latest.Value).TotalDays;
        var severity = days switch { > 90 => Severity.Warning, > 45 => Severity.Info, _ => Severity.Ok };
        return new CheckResult
        {
            Category = CheckCategory.Security,
            Title = "Windows güncellemeleri",
            Severity = severity,
            Summary = severity == Severity.Ok
                ? $"Windows güncel görünüyor (son güncelleme {days} gün önce)."
                : $"Son Windows güncellemesi {days} gün önce yüklenmiş. Eksik güncellemeler güvenlik açıklarına ve oyun/uygulama hatalarına yol açabilir.",
            Details = $"Son yüklenen güncelleme: {latest:dd.MM.yyyy}",
            Steps = severity == Severity.Ok ? [] :
            [
                "Ayarlar > Windows Update'e git ve \"Güncelleştirmeleri denetle\"ye tıkla.",
                "Güncellemeler takılıyorsa bilgisayarı yeniden başlatıp tekrar dene."
            ],
            Actions = severity == Severity.Ok ? [] : [FixAction.Settings("Windows Update'i aç", "ms-settings:windowsupdate")]
        };
    }

    private static CheckResult? CheckTempFolder()
    {
        var size = TempCleaner.MeasureBytes();
        var gb = size / (double)GB;
        if (gb < 2) return null;
        return new CheckResult
        {
            Category = CheckCategory.Disk,
            Title = "Geçici dosyalar",
            Severity = gb > 10 ? Severity.Warning : Severity.Info,
            Summary = $"Geçici dosyalar klasöründe {gb:0.0} GB dosya birikmiş. Bunlar güvenle silinebilir.",
            Steps =
            [
                "Aşağıdaki \"Geçici dosyaları temizle\" düğmesini kullan ya da Windows+R'ye basıp %temp% yaz.",
                "Kullanımda olan dosyalar silinmez, onlar atlanır."
            ],
            Actions =
            [
                new FixAction
                {
                    Label = "Geçici dosyaları temizle", Kind = FixKind.CleanTemp, Glyph = "",
                    Confirm = $"Geçici dosyalar klasöründeki bir günden eski dosyalar silinecek (yaklaşık {gb:0.0} GB). Kullanımdaki dosyalar atlanır. Devam edilsin mi?"
                }
            ]
        };
    }

    private static CheckResult? CheckStartupItems()
    {
        var count = 0;
        var names = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key is null) continue;
            foreach (var n in key.GetValueNames())
            {
                count++;
                names.Add(n);
            }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            try
            {
                var files = Directory.GetFiles(Environment.GetFolderPath(folder)).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)).ToArray();
                count += files.Length;
                names.AddRange(files.Select(Path.GetFileNameWithoutExtension)!);
            }
            catch { }
        }

        if (count < 12) return null;
        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Açılışta başlayan programlar",
            Severity = Severity.Info,
            Summary = $"Windows açılırken {count} program otomatik başlıyor. Bu, açılışı ve genel performansı yavaşlatabilir.",
            Details = string.Join(", ", names),
            Steps =
            [
                "Başlangıç uygulamaları sayfasını aç.",
                "Her açılışta gerekmeyen programları kapat (silinmezler, sadece otomatik açılmazlar)."
            ],
            Actions = [FixAction.Settings("Başlangıç uygulamalarını aç", "ms-settings:startupapps")]
        };
    }

    private static CheckResult? CheckGpuDrivers()
    {
        var old = new List<string>();
        var vendors = new HashSet<string>();
        SystemInfoCollector.TryWmi("SELECT Name, DriverDate FROM Win32_VideoController", o =>
        {
            if (o["DriverDate"] is not string raw) return;
            var name = o["Name"]?.ToString() ?? "";
            if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var date = ManagementDateTimeConverter.ToDateTime(raw);
                if (DateTime.Now - date > TimeSpan.FromDays(365))
                {
                    old.Add($"{name} – sürücü tarihi {date:dd.MM.yyyy}");
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) vendors.Add("nvidia");
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) vendors.Add("amd");
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) vendors.Add("intel");
                }
            }
            catch { }
        });

        if (old.Count == 0) return null;
        var actions = new List<FixAction>();
        if (vendors.Contains("nvidia")) actions.Add(FixAction.Web("NVIDIA sürücüsünü indir", "https://www.nvidia.com/tr-tr/drivers/"));
        if (vendors.Contains("amd")) actions.Add(FixAction.Web("AMD sürücüsünü indir", "https://www.amd.com/en/support/download/drivers.html"));
        if (vendors.Contains("intel")) actions.Add(FixAction.Web("Intel sürücüsünü indir", "https://www.intel.com/content/www/us/en/support/detect.html"));

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Ekran kartı sürücüsü",
            Severity = Severity.Warning,
            Summary = "Ekran kartı sürücün bir yıldan eski. Oyunların açılmaması, çökmesi veya siyah ekran gibi sorunların en sık sebeplerinden biri budur.",
            Details = string.Join("\n", old),
            Steps =
            [
                "Ekran kartı üreticisinin sitesinden güncel sürücüyü indirip kur.",
                "Dizüstü bilgisayarlarda üreticinin (ASUS, Lenovo, HP, Monster…) destek sayfasındaki sürücü de kullanılabilir.",
                "Kurulumdan sonra bilgisayarı yeniden başlat."
            ],
            Actions = actions
        };
    }

    /// <summary>Oyunların ve birçok programın çalışması için gereken Microsoft Visual C++ çalışma zamanları.</summary>
    private static CheckResult? CheckVisualCpp()
    {
        static bool Installed(string arch)
        {
            foreach (var path in new[]
                     {
                         $@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}",
                         $@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}"
                     })
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Installed") is int i && i == 1) return true;
            }
            return false;
        }

        var x64 = Installed("x64");
        var x86 = Installed("x86");
        var missing = new List<string>();
        if (!x64 && Environment.Is64BitOperatingSystem) missing.Add("x64");
        if (!x86) missing.Add("x86");

        if (missing.Count == 0)
            return new CheckResult
            {
                Category = CheckCategory.Applications,
                Title = "Oyun ve program bileşenleri",
                Severity = Severity.Ok,
                Summary = "Oyunların ihtiyaç duyduğu Microsoft Visual C++ bileşenleri yüklü."
            };

        var actions = new List<FixAction>();
        if (missing.Contains("x64")) actions.Add(FixAction.Web("Visual C++ x64 indir", "https://aka.ms/vs/17/release/vc_redist.x64.exe"));
        if (missing.Contains("x86")) actions.Add(FixAction.Web("Visual C++ x86 indir", "https://aka.ms/vs/17/release/vc_redist.x86.exe"));

        return new CheckResult
        {
            Category = CheckCategory.Applications,
            Title = "Oyun ve program bileşenleri",
            Severity = Severity.Warning,
            Summary = $"Microsoft Visual C++ çalışma zamanı eksik ({string.Join(", ", missing)}). \"MSVCP140.dll bulunamadı\" gibi hatalar ve açılmayan oyunlar genelde bundan kaynaklanır.",
            Steps =
            [
                "Aşağıdaki bağlantılardan eksik paketleri Microsoft'un sitesinden indir ve kur.",
                "Kurulumdan sonra oyunu ya da programı yeniden başlat."
            ],
            Actions = actions
        };
    }

    /// <summary>Güç tasarrufu planı oyunlarda düşük FPS'nin sık ve gözden kaçan bir nedenidir.</summary>
    private static CheckResult? CheckPowerPlan()
    {
        string output;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return null;
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
        }
        catch
        {
            return null;
        }

        const string powerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
        if (!output.Contains(powerSaver, StringComparison.OrdinalIgnoreCase)) return null;

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Güç planı",
            Severity = Severity.Info,
            Summary = "Bilgisayar \"Güç tasarrufu\" modunda. Pil ömrü uzar ama oyunlarda ve ağır programlarda performans belirgin şekilde düşer.",
            Steps = ["Ayarlar > Sistem > Güç ve pil > Güç modu'nu \"Dengeli\" ya da \"En iyi performans\" yap."],
            Actions = [FixAction.Settings("Güç ayarlarını aç", "ms-settings:powersleep")]
        };
    }

    /// <summary>Dizüstü bilgisayarlarda pilin tasarım kapasitesine göre ne kadar yıprandığı.</summary>
    private static CheckResult? CheckBattery(SystemInfo info)
    {
        double designed = 0, full = 0;
        SystemInfoCollector.TryWmi("SELECT DesignedCapacity FROM BatteryStaticData", o => designed += Convert.ToDouble(o["DesignedCapacity"]), @"root\wmi");
        SystemInfoCollector.TryWmi("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", o => full += Convert.ToDouble(o["FullChargedCapacity"]), @"root\wmi");
        if (designed <= 0 || full <= 0) return null;

        var health = Math.Min(100, 100 * full / designed);
        info.BatteryHealthPercent = health;
        var severity = health switch { < 50 => Severity.Warning, < 70 => Severity.Info, _ => Severity.Ok };
        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Pil sağlığı",
            Severity = severity,
            Summary = severity switch
            {
                Severity.Warning => $"Pilin ilk günkü kapasitesinin yalnızca %{health:0}'ını tutabiliyor. Şarjın çabuk bitmesi bu yüzden; pil değişimi düşünülebilir.",
                Severity.Info => $"Pil kapasitesi %{health:0}'a düşmüş; bu yaşlanmayla normaldir ama şarj süresi kısalmış olabilir.",
                _ => $"Pil sağlığı iyi (tasarım kapasitesinin %{health:0}'ı)."
            },
            Details = $"Tasarım kapasitesi {designed:0} mWh, tam şarj kapasitesi {full:0} mWh",
            Steps = severity == Severity.Ok ? [] :
            [
                "Bilgisayarı sürekli prizde kullanıyorsan üreticinin uygulamasında (ör. Control Center, MyASUS) şarj sınırını %80'e ayarla.",
                "Pil çok kısa dayanıyorsa yetkili serviste pil değişimi yaptırabilirsin."
            ],
            Actions = severity == Severity.Ok ? [] : [FixAction.Settings("Güç ve pil ayarları", "ms-settings:batterysaver")]
        };
    }

    private static CheckResult? CheckAntivirus()
    {
        var products = new List<(string name, bool enabled, bool upToDate)>();
        SystemInfoCollector.TryWmi("SELECT displayName, productState FROM AntiVirusProduct", o =>
        {
            var state = Convert.ToInt32(o["productState"]);
            products.Add((o["displayName"]?.ToString() ?? "?", (state & 0x1000) != 0, (state & 0x10) == 0));
        }, @"root\SecurityCenter2");

        if (products.Count == 0) return null;

        var active = products.Where(p => p.enabled).ToList();
        var details = string.Join("\n", products.Select(p => $"{p.name}: {(p.enabled ? "açık" : "kapalı")}, {(p.upToDate ? "güncel" : "güncel değil")}"));
        var openSecurity = new FixAction { Label = "Windows Güvenliği'ni aç", Kind = FixKind.Open, Target = "windowsdefender:", Glyph = "" };

        if (active.Count == 0)
            return new CheckResult
            {
                Category = CheckCategory.Security,
                Title = "Virüs koruması",
                Severity = Severity.Error,
                Summary = "Açık bir virüs koruması bulunamadı. Bilgisayarın korumasız.",
                Details = details,
                Steps =
                [
                    "Windows Güvenliği'ni aç.",
                    "\"Virüs ve tehdit koruması\" bölümünde gerçek zamanlı korumayı aç."
                ],
                Actions = [openSecurity]
            };

        if (active.Any(p => !p.upToDate))
            return new CheckResult
            {
                Category = CheckCategory.Security,
                Title = "Virüs koruması",
                Severity = Severity.Warning,
                Summary = "Virüs koruması açık ama tanımları güncel değil.",
                Details = details,
                Steps = ["Windows Güvenliği > Virüs ve tehdit koruması > Koruma güncelleştirmeleri > Güncelleştirmeleri denetle."],
                Actions = [openSecurity]
            };

        return new CheckResult
        {
            Category = CheckCategory.Security,
            Title = "Virüs koruması",
            Severity = active.Count > 1 ? Severity.Info : Severity.Ok,
            Summary = active.Count > 1
                ? "Aynı anda birden fazla antivirüs açık. Bu, yavaşlığa ve çakışmalara yol açabilir; birini kapatman önerilir."
                : $"Virüs koruması açık ve güncel ({active[0].name}).",
            Details = details
        };
    }
}
