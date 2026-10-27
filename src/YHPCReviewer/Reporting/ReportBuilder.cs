using System.Text;
using YHPCReviewer.Models;

namespace YHPCReviewer.Reporting;

/// <summary>Raporu Markdown olarak yazar. Markdown hem düz metin olarak okunabilir hem de YazılımHub'da biçimli görünür.</summary>
public static class ReportBuilder
{
    public static string ToMarkdown(DiagnosticReport r)
    {
        var sb = new StringBuilder();
        var s = r.System;

        sb.AppendLine("# YH PC Reviewer Teşhis Raporu");
        sb.AppendLine();
        sb.AppendLine($"- **Tarih:** {r.CreatedAt:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"- **Sağlık puanı:** {r.HealthScore}/100 ({r.ErrorCount} sorun, {r.WarningCount} uyarı)");
        sb.AppendLine($"- **YH PC Reviewer sürümü:** {r.AppVersion}");
        sb.AppendLine();

        sb.AppendLine("## Özet");
        sb.AppendLine();
        var problems = r.Results.Where(x => x.Severity is Severity.Error or Severity.Warning).ToList();
        if (problems.Count == 0)
            sb.AppendLine("Önemli bir sorun bulunamadı.");
        else
            foreach (var p in problems)
                sb.AppendLine($"- {Icon(p.Severity)} **{p.Title}:** {p.Summary}");
        sb.AppendLine();

        sb.AppendLine("## Sistem");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Bilgisayar adı | {s.ComputerName} |");
        sb.AppendLine($"| Kullanıcı | {s.UserName} |");
        sb.AppendLine($"| İşletim sistemi | {s.OsName} {s.OsBuild} ({s.Architecture}) |");
        sb.AppendLine($"| İşlemci | {s.Cpu} ({s.CpuCores} çekirdek / {s.CpuThreads} iş parçacığı) |");
        sb.AppendLine($"| Bellek | {s.RamTotalGb:0.0} GB (kullanım %{s.RamUsedPercent:0}) |");
        foreach (var g in s.Gpus) sb.AppendLine($"| Ekran kartı | {g} |");
        if (!string.IsNullOrWhiteSpace(s.Motherboard)) sb.AppendLine($"| Anakart | {s.Motherboard} |");
        sb.AppendLine($"| Açık kalma süresi | {s.UptimeDisplay} |");
        sb.AppendLine();

        sb.AppendLine("### Diskler");
        sb.AppendLine();
        foreach (var d in s.Disks)
            sb.AppendLine($"- {d.Display}{(d.IsSystem ? " – sistem diski" : "")}: {Gb(d.FreeBytes)} boş / {Gb(d.TotalBytes)} (%{d.UsedPercent:0} dolu)");
        sb.AppendLine();

        sb.AppendLine("### Ağ bağdaştırıcıları");
        sb.AppendLine();
        foreach (var a in s.Adapters.Where(a => a.Status == "Bağlı"))
        {
            sb.AppendLine($"- **{a.Description}** ({a.Type}, {a.Status}{(a.SpeedMbps > 0 ? $", {a.SpeedMbps} Mbps" : "")})");
            if (a.Mac is not null) sb.AppendLine($"  - MAC: {a.Mac}");
            if (a.Addresses.Count > 0) sb.AppendLine($"  - IP: {string.Join(", ", a.Addresses)}");
            if (a.Gateways.Count > 0) sb.AppendLine($"  - Ağ geçidi: {string.Join(", ", a.Gateways)}");
            if (a.DnsServers.Count > 0) sb.AppendLine($"  - DNS: {string.Join(", ", a.DnsServers)}");
        }
        sb.AppendLine();

        sb.AppendLine("## Bulgular ve çözüm önerileri");
        sb.AppendLine();
        foreach (var c in r.Results)
        {
            sb.AppendLine($"### {Icon(c.Severity)} {c.Title} — {c.SeverityLabel}");
            sb.AppendLine();
            sb.AppendLine(c.Summary);
            sb.AppendLine();
            if (c.HasSteps)
            {
                sb.AppendLine("**Ne yapmalıyım?**");
                sb.AppendLine();
                for (var i = 0; i < c.Steps.Count; i++)
                    sb.AppendLine($"{i + 1}. {c.Steps[i]}");
                sb.AppendLine();
            }
            if (c.HasDetails)
            {
                sb.AppendLine("<details><summary>Teknik ayrıntı</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(c.Details);
                sb.AppendLine("```");
                sb.AppendLine("</details>");
                sb.AppendLine();
            }
        }

        if (r.AppErrors.Count > 0)
        {
            sb.AppendLine("## Uygulama hataları (son 14 gün)");
            sb.AppendLine();
            sb.AppendLine("| Uygulama | Tür | Sayı | Son görülme | Modül |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var e in r.AppErrors.Take(25))
                sb.AppendLine($"| {e.Application} | {e.Kind} | {e.Count} | {e.LastSeen:dd.MM.yyyy HH:mm} | {e.Module} |");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("_Bu rapor YH PC Reviewer ile oluşturuldu. YH PC Reviewer bilgisayarda hiçbir ayarı değiştirmez, yalnızca okur._");
        return sb.ToString();
    }

    private static string Icon(Severity s) => s switch
    {
        Severity.Error => "🔴",
        Severity.Warning => "🟠",
        Severity.Info => "🔵",
        _ => "🟢"
    };

    private static string Gb(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";
}
