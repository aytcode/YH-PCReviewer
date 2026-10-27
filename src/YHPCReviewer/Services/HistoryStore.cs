using System.IO;
using System.Text.Json;
using YHPCReviewer.Models;

namespace YHPCReviewer.Services;

/// <summary>Bir analizin özetini saklar; ayrıntılı rapor ve kişisel bilgiler kaydedilmez.</summary>
public sealed class HistoryEntry
{
    public DateTime Date { get; set; }
    public int Score { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public List<string> Issues { get; set; } = [];

    // Arayüz için hesaplanan alanlar (JSON'a yazılmaz).
    [System.Text.Json.Serialization.JsonIgnore] public List<string> NewIssues { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public List<string> ResolvedIssues { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public int? ScoreDelta { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Severity ScoreSeverity => Score switch { >= 85 => Severity.Ok, >= 60 => Severity.Warning, _ => Severity.Error };

    [System.Text.Json.Serialization.JsonIgnore]
    public string DateDisplay => Date.ToString("d MMMM yyyy, HH:mm");

    [System.Text.Json.Serialization.JsonIgnore]
    public string ChangeSummary
    {
        get
        {
            if (ScoreDelta is null) return "İlk analiz";
            var parts = new List<string>();
            if (ResolvedIssues.Count > 0) parts.Add($"{ResolvedIssues.Count} sorun çözüldü");
            if (NewIssues.Count > 0) parts.Add($"{NewIssues.Count} yeni sorun");
            return parts.Count == 0 ? "Değişiklik yok" : string.Join(" · ", parts);
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DeltaDisplay => ScoreDelta switch
    {
        null or 0 => "",
        > 0 => $"+{ScoreDelta}",
        _ => ScoreDelta.ToString()!
    };
}

public static class HistoryStore
{
    private const int MaxEntries = 50;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YHPCReviewer", "history.json");

    /// <summary>En yeni başta olacak şekilde geçmişi ve önceki analize göre farkları döndürür.</summary>
    public static List<HistoryEntry> Load()
    {
        List<HistoryEntry> list;
        try
        {
            list = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            list = [];
        }

        list = list.OrderByDescending(e => e.Date).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (i + 1 >= list.Count) break;
            var prev = list[i + 1];
            list[i].ScoreDelta = list[i].Score - prev.Score;
            list[i].NewIssues = list[i].Issues.Except(prev.Issues).ToList();
            list[i].ResolvedIssues = prev.Issues.Except(list[i].Issues).ToList();
        }
        return list;
    }

    public static List<HistoryEntry> Add(DiagnosticReport report)
    {
        var list = Load();
        list.Insert(0, new HistoryEntry
        {
            Date = report.CreatedAt,
            Score = report.HealthScore,
            Errors = report.ErrorCount,
            Warnings = report.WarningCount,
            Issues = report.Problems.Select(p => p.Title).ToList()
        });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list.Take(MaxEntries).ToList()));
        }
        catch
        {
            // Geçmiş yazılamazsa analiz yine de gösterilir.
        }
        return Load();
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }
}
