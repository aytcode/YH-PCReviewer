namespace YHPCReviewer.Models;

public enum Severity
{
    Ok,
    Info,
    Warning,
    Error
}

public enum CheckCategory
{
    Network,
    Disk,
    System,
    Applications,
    Security
}

/// <summary>Tek bir teşhis adımının sonucu. Kullanıcıya sade dille gösterilir.</summary>
public sealed class CheckResult
{
    public required CheckCategory Category { get; init; }
    public required string Title { get; init; }
    public required Severity Severity { get; init; }

    /// <summary>Teknik bilgisi olmayan birinin anlayacağı tek-iki cümlelik açıklama.</summary>
    public required string Summary { get; init; }

    /// <summary>Destek veren kişiler için teknik ayrıntı (isteğe bağlı).</summary>
    public string? Details { get; init; }

    /// <summary>Sırayla denenecek çözüm adımları.</summary>
    public List<string> Steps { get; init; } = [];

    public string CategoryLabel => Category switch
    {
        CheckCategory.Network => "Ağ & İnternet",
        CheckCategory.Disk => "Disk",
        CheckCategory.System => "Sistem",
        CheckCategory.Applications => "Uygulamalar",
        CheckCategory.Security => "Güvenlik",
        _ => Category.ToString()
    };

    public string SeverityLabel => Severity switch
    {
        Severity.Ok => "Sorun yok",
        Severity.Info => "Bilgi",
        Severity.Warning => "Uyarı",
        Severity.Error => "Sorun",
        _ => Severity.ToString()
    };

    /// <summary>Kullanıcının tek tıkla deneyebileceği güvenli düzeltmeler.</summary>
    public List<FixAction> Actions { get; init; } = [];

    public bool HasSteps => Steps.Count > 0;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool HasActions => Actions.Count > 0;
    public bool IsProblem => Severity is Severity.Error or Severity.Warning;

    /// <summary>Segoe Fluent Icons simgesi (durum rozetinde gösterilir).</summary>
    public string StatusGlyph => Severity switch
    {
        Severity.Error => "",
        Severity.Warning => "",
        Severity.Info => "",
        _ => ""
    };
}

public enum FixKind
{
    /// <summary>Bir Windows ayar sayfasını ya da web adresini açar; hiçbir şeyi değiştirmez.</summary>
    Open,
    /// <summary>DNS önbelleğini temizler (ipconfig /flushdns).</summary>
    FlushDns,
    /// <summary>Kullanıcının geçici dosyalar klasöründeki eski dosyaları siler.</summary>
    CleanTemp
}

public sealed class FixAction
{
    public required string Label { get; init; }
    public required FixKind Kind { get; init; }
    public string? Target { get; init; }
    public string Glyph { get; init; } = "";

    /// <summary>Doluysa işlem çalışmadan önce kullanıcıya bu metinle onay sorulur.</summary>
    public string? Confirm { get; init; }

    public static FixAction Settings(string label, string uri) =>
        new() { Label = label, Kind = FixKind.Open, Target = uri, Glyph = "" };

    public static FixAction Web(string label, string url) =>
        new() { Label = label, Kind = FixKind.Open, Target = url, Glyph = "" };
}

public sealed class ChainNode
{
    public required string Label { get; init; }
    public required string Glyph { get; init; }

    /// <summary>null: bu halka test edilmedi (önceki halka başarısız olduğu için).</summary>
    public Severity? Severity { get; init; }
    public bool IsLast { get; init; }

    public string StatusText => Severity switch
    {
        null => "Test edilmedi",
        Models.Severity.Error => "Sorun",
        Models.Severity.Warning => "Yanıt yok",
        _ => "Çalışıyor"
    };
}

public sealed class DiskInfo
{
    public required string Name { get; init; }
    public string? Label { get; init; }
    public string? Format { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public bool IsSystem { get; init; }

    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - FreeBytes) / TotalBytes;
    public string Display => string.IsNullOrWhiteSpace(Label) ? Name : $"{Name} ({Label})";
    public string TotalGb => $"{TotalBytes / 1024d / 1024 / 1024:0.0} GB";
    public string FreeGb => $"{FreeBytes / 1024d / 1024 / 1024:0.0} GB";
}

public sealed class AdapterInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public long SpeedMbps { get; init; }
    public List<string> Addresses { get; init; } = [];
    public List<string> Gateways { get; init; } = [];
    public List<string> DnsServers { get; init; } = [];
    public string? Mac { get; init; }

    public string DnsDisplay => DnsServers.Count == 0 ? "DNS: –" : "DNS: " + string.Join(", ", DnsServers);
}

public sealed class AppErrorInfo
{
    public required string Application { get; init; }
    public string? Module { get; init; }
    public int Count { get; set; }
    public DateTime LastSeen { get; set; }
    public string Kind { get; init; } = "Çökme";
}

public sealed class SystemInfo
{
    public string ComputerName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsName { get; set; } = "";
    public string OsBuild { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public double RamTotalGb { get; set; }
    public double RamAvailableGb { get; set; }
    public List<string> Gpus { get; set; } = [];
    public string Motherboard { get; set; } = "";
    public TimeSpan Uptime { get; set; }
    public List<DiskInfo> Disks { get; set; } = [];
    public List<AdapterInfo> Adapters { get; set; } = [];

    /// <summary>Dizüstü bilgisayarlarda pilin tasarım kapasitesine oranı; masaüstünde null.</summary>
    public double? BatteryHealthPercent { get; set; }
    public bool HasBattery => BatteryHealthPercent is not null;

    public double RamUsedPercent => RamTotalGb <= 0 ? 0 : 100.0 * (RamTotalGb - RamAvailableGb) / RamTotalGb;

    public string UptimeDisplay =>
        Uptime.TotalDays >= 1 ? $"{(int)Uptime.TotalDays} gün {Uptime.Hours} saat" :
        Uptime.TotalHours >= 1 ? $"{(int)Uptime.TotalHours} saat {Uptime.Minutes} dk" : $"{Uptime.Minutes} dakika";
}

public sealed class DiagnosticReport
{
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public required SystemInfo System { get; init; }
    public List<CheckResult> Results { get; init; } = [];
    public List<AppErrorInfo> AppErrors { get; init; } = [];
    public string AppVersion { get; init; } = "";

    /// <summary>0-100 arası basit sağlık puanı.</summary>
    public int HealthScore
    {
        get
        {
            var score = 100;
            foreach (var r in Results)
            {
                score -= r.Severity switch
                {
                    Severity.Error => 15,
                    Severity.Warning => 6,
                    _ => 0
                };
            }
            return Math.Clamp(score, 0, 100);
        }
    }

    public string CreatedAtDisplay => CreatedAt.ToString("d MMMM HH:mm");

    public int ErrorCount => Results.Count(r => r.Severity == Severity.Error);
    public int WarningCount => Results.Count(r => r.Severity == Severity.Warning);

    public Severity ScoreSeverity => HealthScore switch
    {
        >= 85 => Severity.Ok,
        >= 60 => Severity.Warning,
        _ => Severity.Error
    };

    public string ScoreLabel => ScoreSeverity switch
    {
        Severity.Ok => "Bilgisayarın genel olarak sağlıklı görünüyor.",
        Severity.Warning => "Dikkat edilmesi gereken birkaç konu var.",
        _ => "Çözülmesi gereken önemli sorunlar var."
    };

    public List<CheckResult> Problems => Results.Where(r => r.Severity is Severity.Error or Severity.Warning).ToList();
    public List<CheckResult> NetworkResults => Results.Where(r => r.Category == CheckCategory.Network).ToList();
    public List<CheckResult> AppResults => Results.Where(r => r.Category == CheckCategory.Applications).ToList();

    /// <summary>Bilgisayardan web'e bağlantı zinciri; sorunun hangi halkada başladığını görselleştirir.</summary>
    public List<ChainNode> NetworkChain
    {
        get
        {
            (string label, string glyph, string[] titles)[] links =
            [
                ("Bilgisayar", "", ["Ağ kartı", "Ağ bağlantısı"]),
                ("Modem", "", ["Modem / yönlendirici"]),
                ("İnternet", "", ["İnternet erişimi"]),
                ("DNS", "", ["DNS (site adlarını çözme)"]),
                ("Web", "", ["Web sayfalarına erişim"])
            ];
            return links.Select((l, i) =>
            {
                var r = Results.FirstOrDefault(x => x.Category == CheckCategory.Network && l.titles.Contains(x.Title));
                return new ChainNode
                {
                    Label = l.label,
                    Glyph = l.glyph,
                    Severity = r?.Severity,
                    IsLast = i == links.Length - 1
                };
            }).ToList();
        }
    }
    public List<AdapterInfo> ConnectedAdapters => System.Adapters.Where(a => a.Status == "Bağlı").ToList();
}
