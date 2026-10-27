using System.Reflection;
using YHPCReviewer.Models;

namespace YHPCReviewer.Diagnostics;

/// <summary>Tüm kontrolleri sırayla çalıştırır ve tek bir rapor üretir.</summary>
public static class DiagnosticEngine
{
    /// <summary>Arayüzdeki ilerleme listesinde gösterilen adımlar; <see cref="RunAsync"/> bu sırayla ilerler.</summary>
    public static readonly string[] StepNames =
    [
        "Sistem bilgileri",
        "İnternet ve ağ bağlantısı",
        "Disk, bellek ve güvenlik",
        "Uygulama hataları ve olay günlükleri",
        "Rapor hazırlanıyor"
    ];

    /// <param name="progress">Başlayan adımın sırası (StepNames'e göre).</param>
    public static async Task<DiagnosticReport> RunAsync(IProgress<int> progress, CancellationToken ct = default)
    {
        progress.Report(0);
        var info = await Task.Run(SystemInfoCollector.Collect, ct);

        progress.Report(1);
        var network = await NetworkChecks.RunAsync(info, ct);

        progress.Report(2);
        var system = await Task.Run(() => SystemChecks.Run(info), ct);

        progress.Report(3);
        var (events, appErrors) = await Task.Run(EventLogChecks.Run, ct);

        progress.Report(4);
        var all = network.Concat(system).Concat(events)
            .OrderByDescending(r => r.Severity)
            .ThenBy(r => r.Category)
            .ToList();

        progress.Report(StepNames.Length);
        return new DiagnosticReport
        {
            System = info,
            Results = all,
            AppErrors = appErrors,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"
        };
    }
}
