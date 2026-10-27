using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using YHPCReviewer.Models;

namespace YHPCReviewer.Diagnostics;

/// <summary>
/// İnternet bağlantısını katman katman sınar: ağ kartı → modem → internet → DNS → web.
/// Böylece sorunun nerede başladığını sade bir dille söyleyebiliriz.
/// </summary>
public static class NetworkChecks
{
    private static readonly string[] PublicIps = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
    private static readonly string[] DnsNames = ["www.microsoft.com", "www.google.com", "www.cloudflare.com"];

    public static async Task<List<CheckResult>> RunAsync(SystemInfo info, CancellationToken ct)
    {
        var results = new List<CheckResult>();

        var active = info.Adapters.Where(a => a.Status == "Bağlı" && a.Gateways.Count > 0).ToList();
        results.Add(CheckAdapters(info, active));
        if (active.Count == 0)
            return results; // Ağ kartı yoksa sonraki testler anlamsız.

        var gatewayOk = await CheckGatewayAsync(active, results);
        var (internetOk, avgLatency) = await CheckInternetAsync(results, gatewayOk);
        var dnsOk = await CheckDnsAsync(results, internetOk, ct);
        await CheckHttpAsync(results, internetOk, dnsOk, ct);

        if (internetOk && avgLatency > 0)
            results.Add(LatencyResult(avgLatency));

        var proxy = CheckProxy();
        if (proxy is not null) results.Add(proxy);

        return results;
    }

    private static CheckResult CheckAdapters(SystemInfo info, List<AdapterInfo> active)
    {
        if (active.Count == 0)
        {
            var any = info.Adapters.Any(a => a.Status == "Bağlı");
            return new CheckResult
            {
                Category = CheckCategory.Network,
                Title = "Ağ bağlantısı",
                Severity = Severity.Error,
                Summary = any
                    ? "Ağ kartın açık ama modeme/yönlendiriciye bağlanamıyor (adres alınamadı)."
                    : "Bilgisayar hiçbir ağa bağlı değil. Wi-Fi kapalı ya da kablo takılı olmayabilir.",
                Details = string.Join("\n", info.Adapters.Select(a => $"{a.Description} [{a.Type}] – {a.Status}")),
                Steps =
                [
                    "Wi-Fi kullanıyorsan görev çubuğundaki ağ simgesine tıklayıp ağına yeniden bağlan.",
                    "Kablo kullanıyorsan kablonun hem bilgisayara hem modeme sıkıca takılı olduğunu kontrol et.",
                    "Uçak modunun kapalı olduğundan emin ol.",
                    "Modemi 30 saniye kapatıp tekrar aç.",
                    "Ayarlar > Ağ ve İnternet > Gelişmiş ağ ayarları > Ağ sıfırlama seçeneğini dene (son çare)."
                ],
                Actions =
                [
                    FixAction.Settings("Wi-Fi ayarlarını aç", "ms-settings:network-wifi"),
                    FixAction.Settings("Ağ ayarlarını aç", "ms-settings:network-status")
                ]
            };
        }

        var names = string.Join(", ", active.Select(a => $"{a.Type}: {a.Description}" + (a.SpeedMbps > 0 ? $" ({a.SpeedMbps} Mbps)" : "")));
        var wifiSlow = active.FirstOrDefault(a => a.Type == "Wi-Fi" && a.SpeedMbps is > 0 and < 50);
        return new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "Ağ kartı",
            Severity = wifiSlow is null ? Severity.Ok : Severity.Info,
            Summary = wifiSlow is null
                ? "Ağ kartın çalışıyor ve bir ağa bağlı."
                : $"Wi-Fi bağlantı hızın düşük ({wifiSlow.SpeedMbps} Mbps). Sinyal zayıf olabilir.",
            Details = names,
            Steps = wifiSlow is null ? [] :
            [
                "Modeme daha yakın bir yerde dene ya da arada duvar/metal eşya olmamasına dikkat et.",
                "Modemin 5 GHz ağı varsa ona bağlan.",
                "Oyun gibi işler için mümkünse kablolu bağlantı kullan."
            ]
        };
    }

    private static async Task<bool> CheckGatewayAsync(List<AdapterInfo> active, List<CheckResult> results)
    {
        var gateway = active.SelectMany(a => a.Gateways).First();
        var (ok, _) = await PingAsync(gateway, 3);
        results.Add(new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "Modem / yönlendirici",
            Severity = ok ? Severity.Ok : Severity.Warning,
            Summary = ok
                ? "Bilgisayar modemine ulaşabiliyor."
                : "Modem ping isteklerine cevap vermedi. Bazı modemler bunu bilerek engeller; internet çalışıyorsa sorun değil.",
            Details = $"Varsayılan ağ geçidi: {gateway}",
            Steps = ok ? [] :
            [
                "Modemin ışıklarını kontrol et; kırmızı/turuncu ışık varsa modemi yeniden başlat.",
                "Başka bir cihaz (telefon) aynı Wi-Fi ile internete girebiliyor mu bak."
            ]
        });
        return ok;
    }

    private static async Task<(bool ok, double avgMs)> CheckInternetAsync(List<CheckResult> results, bool gatewayOk)
    {
        var latencies = new List<long>();
        foreach (var ip in PublicIps)
        {
            var (ok, ms) = await PingAsync(ip, 2);
            if (ok) latencies.Add(ms);
        }

        var internetOk = latencies.Count > 0;
        results.Add(new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "İnternet erişimi",
            Severity = internetOk ? Severity.Ok : Severity.Error,
            Summary = internetOk
                ? "İnternete ulaşılabiliyor."
                : gatewayOk
                    ? "Modeme ulaşılıyor ama internete çıkılamıyor. Sorun büyük ihtimalle modemde ya da internet servis sağlayıcında."
                    : "İnternete ulaşılamıyor.",
            Details = $"Ping testi: {string.Join(", ", PublicIps)} → {latencies.Count}/{PublicIps.Length} yanıt",
            Steps = internetOk ? [] :
            [
                "Modemi fişten çekip 30 saniye bekle, sonra tekrar tak.",
                "Telefonun aynı ağda internete girebiliyor mu bak; giremiyorsa sorun bilgisayarda değil.",
                "İnternet servis sağlayıcının bölgende arıza olup olmadığını kontrol et.",
                "Faturanın ödendiğinden ve kotanın dolmadığından emin ol."
            ],
            Actions = internetOk ? [] : [FixAction.Settings("Ağ ayarlarını aç", "ms-settings:network-status")]
        });
        return (internetOk, latencies.Count > 0 ? latencies.Average() : 0);
    }

    private static async Task<bool> CheckDnsAsync(List<CheckResult> results, bool internetOk, CancellationToken ct)
    {
        var resolved = 0;
        var errors = new List<string>();
        foreach (var name in DnsNames)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));
                var addresses = await Dns.GetHostAddressesAsync(name, cts.Token);
                if (addresses.Length > 0) resolved++;
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.GetType().Name}");
            }
        }

        var ok = resolved > 0;
        results.Add(new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "DNS (site adlarını çözme)",
            Severity = ok ? Severity.Ok : Severity.Error,
            Summary = ok
                ? "Site adları (ör. google.com) doğru şekilde adrese çevrilebiliyor."
                : internetOk
                    ? "İnternet var ama site adları çözülemiyor. Bu bir DNS sorunu; siteler açılmıyor gibi görünür ama bağlantın aslında çalışıyor."
                    : "Site adları çözülemiyor (internet bağlantısı olmadığı için beklenen bir durum).",
            Details = errors.Count == 0 ? $"{resolved}/{DnsNames.Length} ad çözüldü" : string.Join("\n", errors),
            Steps = ok || !internetOk ? [] :
            [
                "Başlat menüsüne \"cmd\" yaz, açılan pencereye \"ipconfig /flushdns\" yazıp Enter'a bas.",
                "Ayarlar > Ağ ve İnternet > (bağlantın) > DNS sunucusu ataması > Düzenle: tercih edilen DNS'i 1.1.1.1, alternatifi 8.8.8.8 yap.",
                "Modemi yeniden başlat.",
                "Bir VPN veya reklam engelleyici kullanıyorsan geçici olarak kapatıp tekrar dene."
            ],
            Actions = ok || !internetOk ? [] :
            [
                new FixAction
                {
                    Label = "DNS önbelleğini temizle", Kind = FixKind.FlushDns, Glyph = "",
                    Confirm = "Windows'un hatırladığı site adresleri silinecek ve yeniden öğrenilecek. Bu güvenli bir işlemdir. Devam edilsin mi?"
                },
                FixAction.Settings("DNS ayarlarını aç", "ms-settings:network-status")
            ]
        });
        return ok;
    }

    private static async Task CheckHttpAsync(List<CheckResult> results, bool internetOk, bool dnsOk, CancellationToken ct)
    {
        if (!internetOk || !dnsOk) return;

        // Windows'un kendi "internet var mı" testinin kullandığı adres.
        const string url = "http://www.msftconnecttest.com/connecttest.txt";
        string? body = null;
        string? error = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            body = await http.GetStringAsync(url, ct);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var ok = body?.Trim() == "Microsoft Connect Test";
        results.Add(new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "Web sayfalarına erişim",
            Severity = ok ? Severity.Ok : Severity.Warning,
            Summary = ok
                ? "Web sayfaları normal şekilde yüklenebiliyor."
                : body is not null
                    ? "Web isteği beklenmedik bir sayfaya yönlendirildi. Otel/okul gibi giriş sayfası isteyen bir ağda olabilirsin."
                    : "İnternet var ama web isteği başarısız oldu. Güvenlik duvarı, antivirüs ya da proxy engelliyor olabilir.",
            Details = error ?? (ok ? url : $"Beklenmeyen yanıt: {Truncate(body, 120)}"),
            Steps = ok ? [] :
            [
                "Tarayıcıyı açıp herhangi bir siteye gir; bir giriş sayfası çıkarsa oturum aç.",
                "Antivirüs ya da güvenlik duvarı programını geçici olarak kapatıp tekrar dene.",
                "Ayarlar > Ağ ve İnternet > Proxy bölümünde elle girilmiş bir proxy varsa kapat."
            ],
            Actions = ok ? [] :
            [
                FixAction.Web("Tarayıcıda dene", "http://neverssl.com"),
                FixAction.Settings("Proxy ayarlarını aç", "ms-settings:network-proxy")
            ]
        });
    }

    private static CheckResult LatencyResult(double avgMs)
    {
        var severity = avgMs switch
        {
            > 200 => Severity.Warning,
            > 100 => Severity.Info,
            _ => Severity.Ok
        };
        return new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "Gecikme (ping)",
            Severity = severity,
            Summary = severity == Severity.Ok
                ? $"Ortalama gecikme {avgMs:0} ms; oyun ve görüntülü görüşme için iyi."
                : $"Ortalama gecikme {avgMs:0} ms; oyunlarda takılma ve görüntülü görüşmede donma yaşanabilir.",
            Steps = severity == Severity.Ok ? [] :
            [
                "Arka planda büyük indirme (Steam, Windows Update, torrent) varsa durdur.",
                "Mümkünse kablolu bağlantı kullan.",
                "Evde aynı anda çok cihaz yoğun internet kullanıyorsa tekrar dene."
            ]
        };
    }

    private static CheckResult? CheckProxy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        if (key?.GetValue("ProxyEnable") is not int enabled || enabled == 0) return null;
        var server = key.GetValue("ProxyServer") as string;
        return new CheckResult
        {
            Category = CheckCategory.Network,
            Title = "Proxy ayarı",
            Severity = Severity.Info,
            Summary = "Bilgisayarda elle girilmiş bir proxy açık. Bunu sen ya da şirketin ayarlamadıysa bağlantı sorunlarına yol açabilir.",
            Details = $"Proxy: {server}",
            Steps =
            [
                "Ayarlar > Ağ ve İnternet > Proxy bölümüne git.",
                "Tanımadığın bir proxy varsa \"Proxy sunucusu kullan\" seçeneğini kapat."
            ],
            Actions = [FixAction.Settings("Proxy ayarlarını aç", "ms-settings:network-proxy")]
        };
    }

    private static async Task<(bool ok, long ms)> PingAsync(string host, int attempts)
    {
        using var ping = new Ping();
        var times = new List<long>();
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 1500);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (PingException)
            {
            }
        }
        return times.Count > 0 ? (true, (long)times.Average()) : (false, 0);
    }

    private static string Truncate(string? s, int max) =>
        s is null ? "" : s.Length <= max ? s : s[..max] + "…";
}
