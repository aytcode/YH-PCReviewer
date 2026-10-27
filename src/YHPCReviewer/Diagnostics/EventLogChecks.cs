using System.Diagnostics.Eventing.Reader;
using YHPCReviewer.Models;

namespace YHPCReviewer.Diagnostics;

/// <summary>
/// Windows olay günlüklerinden uygulama çökmelerini, beklenmedik kapanmaları,
/// mavi ekranları ve disk hatalarını okur. Yönetici izni gerektirmez.
/// </summary>
public static class EventLogChecks
{
    private const int AppDays = 14;
    private const int SystemDays = 30;

    public static (List<CheckResult> results, List<AppErrorInfo> appErrors) Run()
    {
        var results = new List<CheckResult>();
        var appErrors = ReadAppErrors();
        results.Add(SummarizeAppErrors(appErrors));

        AddIfNotNull(results, CheckUnexpectedShutdowns());
        AddIfNotNull(results, CheckBlueScreens());
        AddIfNotNull(results, CheckDiskErrors());
        return (results, appErrors);
    }

    private static void AddIfNotNull(List<CheckResult> list, CheckResult? r)
    {
        if (r is not null) list.Add(r);
    }

    private static List<AppErrorInfo> ReadAppErrors()
    {
        var map = new Dictionary<string, AppErrorInfo>(StringComparer.OrdinalIgnoreCase);
        var query = $"*[System[(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)] and System[TimeCreated[timediff(@SystemTime) <= {Ms(AppDays)}]]]";

        foreach (var e in Read("Application", query, 2000))
        {
            var props = e.Properties;
            if (props.Count == 0) continue;
            var app = props[0].Value?.ToString();
            if (string.IsNullOrWhiteSpace(app)) continue;

            var isHang = e.Id == 1002;
            var module = !isHang && props.Count > 3 ? props[3].Value?.ToString() : null;
            var kind = isHang ? "Yanıt vermiyor" : "Çökme";
            var key = $"{app}|{kind}";
            var time = e.TimeCreated ?? DateTime.MinValue;

            if (!map.TryGetValue(key, out var item))
            {
                item = new AppErrorInfo { Application = app, Module = module, Kind = kind };
                map[key] = item;
            }
            item.Count++;
            if (time > item.LastSeen) item.LastSeen = time;
        }

        return map.Values.OrderByDescending(a => a.Count).ThenByDescending(a => a.LastSeen).ToList();
    }

    private static CheckResult SummarizeAppErrors(List<AppErrorInfo> errors)
    {
        if (errors.Count == 0)
            return new CheckResult
            {
                Category = CheckCategory.Applications,
                Title = "Uygulama çökmeleri",
                Severity = Severity.Ok,
                Summary = $"Son {AppDays} günde kayıtlı bir uygulama çökmesi yok."
            };

        var total = errors.Sum(e => e.Count);
        var top = errors.Take(5).ToList();
        var topNames = string.Join(", ", top.Select(e => e.Application));

        // Çöken modüle göre tipik nedenleri tahmin et.
        var modules = errors.Select(e => e.Module ?? "").ToList();
        var steps = new List<string>
        {
            "Sorun yaşayan programı/oyunu en son sürümüne güncelle.",
            "Oyunlarda: Steam'de oyuna sağ tıkla > Özellikler > Yüklü Dosyalar > \"Oyun dosyalarının bütünlüğünü doğrula\" (Epic'te \"Doğrula\")."
        };
        if (modules.Any(m => m.StartsWith("nvwgf", StringComparison.OrdinalIgnoreCase) || m.StartsWith("atidxx", StringComparison.OrdinalIgnoreCase) || m.StartsWith("amdxx", StringComparison.OrdinalIgnoreCase) || m.StartsWith("igd", StringComparison.OrdinalIgnoreCase) || m.Contains("d3d", StringComparison.OrdinalIgnoreCase) || m.Contains("dxgi", StringComparison.OrdinalIgnoreCase)))
            steps.Insert(0, "Çökmeler ekran kartı sürücüsünden kaynaklanıyor gibi görünüyor: ekran kartı sürücünü güncelle.");
        if (modules.Any(m => m.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase) || m.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase) || m.StartsWith("ucrtbase", StringComparison.OrdinalIgnoreCase)))
            steps.Add("Microsoft'un sitesinden en güncel \"Visual C++ Redistributable\" paketini (x64 ve x86) kurup yeniden dene.");
        if (modules.Any(m => m.Equals("KERNELBASE.dll", StringComparison.OrdinalIgnoreCase) || m.Equals("ntdll.dll", StringComparison.OrdinalIgnoreCase)))
            steps.Add("Programı sağ tıklayıp \"Yönetici olarak çalıştır\" ile açmayı dene; antivirüsün programı engellemediğinden emin ol.");
        steps.Add("Sorun sürerse programı kaldırıp yeniden kur.");

        return new CheckResult
        {
            Category = CheckCategory.Applications,
            Title = "Uygulama çökmeleri",
            Severity = total >= 5 ? Severity.Warning : Severity.Info,
            Summary = $"Son {AppDays} günde {errors.Count} farklı programda toplam {total} çökme/donma kaydı var. En sık: {topNames}.",
            Details = string.Join("\n", top.Select(e => $"{e.Application} – {e.Kind} ×{e.Count}" + (string.IsNullOrEmpty(e.Module) ? "" : $" (modül: {e.Module})"))),
            Steps = steps,
            Actions = [FixAction.Settings("Yüklü uygulamaları aç", "ms-settings:appsfeatures")]
        };
    }

    private static CheckResult? CheckUnexpectedShutdowns()
    {
        var query = $"*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='EventLog'] and EventID=6008)) and TimeCreated[timediff(@SystemTime) <= {Ms(SystemDays)}]]]";
        var events = Read("System", query, 200).ToList();
        // Kernel-Power 41 ve EventLog 6008 genelde aynı olay için birlikte yazılır.
        var count = Math.Max(events.Count(e => e.Id == 41), events.Count(e => e.Id == 6008));
        if (count == 0) return null;

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Beklenmedik kapanmalar",
            Severity = count >= 3 ? Severity.Error : Severity.Warning,
            Summary = $"Son {SystemDays} günde bilgisayar {count} kez düzgün kapanmadan kapanmış ya da yeniden başlamış (elektrik kesintisi, donma veya güç düğmesine basılı tutma).",
            Details = string.Join("\n", events.Take(10).Select(e => $"{e.TimeCreated:dd.MM.yyyy HH:mm} – {e.ProviderName} {e.Id}")),
            Steps =
            [
                "Bu olaylar oyun oynarken oluyorsa aşırı ısınma veya yetersiz güç kaynağı (PSU) olası nedenlerdir; kasanın tozunu temizle ve fanların döndüğünü kontrol et.",
                "Dizüstü bilgisayarda havalandırma deliklerinin kapalı olmadığından emin ol.",
                "Ekran kartı sürücüsünü ve BIOS'u güncelle.",
                "Sorun sürerse bilgisayarı bir teknik servise kontrol ettir."
            ]
        };
    }

    private static CheckResult? CheckBlueScreens()
    {
        var query = $"*[System[(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) and TimeCreated[timediff(@SystemTime) <= {Ms(SystemDays)}]]]";
        var events = Read("System", query, 100).ToList();
        if (events.Count == 0) return null;

        var codes = events
            .Select(e => e.Properties.Count > 0 ? e.Properties[0].Value?.ToString() : null)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Split(' ')[0])
            .Distinct()
            .ToList();

        return new CheckResult
        {
            Category = CheckCategory.System,
            Title = "Mavi ekran hataları",
            Severity = Severity.Error,
            Summary = $"Son {SystemDays} günde {events.Count} mavi ekran (sistem çökmesi) yaşanmış.",
            Details = codes.Count > 0 ? "Hata kodları: " + string.Join(", ", codes) : null,
            Steps =
            [
                "Windows Update'ten tüm güncellemeleri yükle ve yeniden başlat.",
                "Ekran kartı, yonga seti ve ağ kartı sürücülerini üreticinin sitesinden güncelle.",
                "Son zamanlarda yeni bir donanım ya da sürücü yüklediysen onu geri almayı dene.",
                "Başlat'a \"Windows Bellek Tanılama\" yaz ve RAM testini çalıştır.",
                "Hata kodunu (yukarıda) internette arat veya bu raporu YazılımHub'da paylaşarak yardım iste."
            ],
            Actions =
            [
                FixAction.Settings("Windows Update'i aç", "ms-settings:windowsupdate"),
                new FixAction { Label = "Bellek Tanılama'yı aç", Kind = FixKind.Open, Target = "mdsched.exe", Glyph = "" }
            ]
        };
    }

    private static CheckResult? CheckDiskErrors()
    {
        var query = $"*[System[((Provider[@Name='disk'] and (EventID=7 or EventID=51 or EventID=153)) or (Provider[@Name='Ntfs' or @Name='Microsoft-Windows-Ntfs'] and EventID=55)) and TimeCreated[timediff(@SystemTime) <= {Ms(SystemDays)}]]]";
        var events = Read("System", query, 200).ToList();
        if (events.Count == 0) return null;

        var badBlocks = events.Count(e => e.Id == 7);
        return new CheckResult
        {
            Category = CheckCategory.Disk,
            Title = "Disk hataları",
            Severity = badBlocks > 0 || events.Count >= 5 ? Severity.Error : Severity.Warning,
            Summary = badBlocks > 0
                ? "Diskte bozuk alan (bad sector) kayıtları var. Disk arızalanıyor olabilir; önemli dosyalarını hemen yedekle."
                : $"Son {SystemDays} günde {events.Count} disk okuma/yazma hatası kaydedilmiş.",
            Details = string.Join("\n", events.GroupBy(e => $"{e.ProviderName} {e.Id}").Select(g => $"{g.Key} ×{g.Count()}")),
            Steps =
            [
                "Önemli dosyalarını harici bir diske veya buluta yedekle.",
                "Dosya Gezgini'nde diske sağ tıkla > Özellikler > Araçlar > Denetle ile hata denetimi yap.",
                "Harici disk ise kablosunu ve USB girişini değiştirip dene.",
                "Üreticinin disk sağlık aracıyla (Samsung Magician, WD Dashboard, CrystalDiskInfo) SMART durumuna bak."
            ]
        };
    }

    private static IEnumerable<EventRecord> Read(string log, string xpath, int max)
    {
        var list = new List<EventRecord>();
        try
        {
            var q = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            for (var i = 0; i < max; i++)
            {
                var e = reader.ReadEvent();
                if (e is null) break;
                list.Add(e);
            }
        }
        catch
        {
            // Günlük okunamıyorsa (politika, bozuk günlük) bu kontrol atlanır.
        }
        return list;
    }

    private static long Ms(int days) => days * 24L * 60 * 60 * 1000;
}
