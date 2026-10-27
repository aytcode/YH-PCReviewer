using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using YHPCReviewer.AI;
using YHPCReviewer.Diagnostics;
using YHPCReviewer.Models;
using YHPCReviewer.Services;

namespace YHPCReviewer;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record SpecTile(string Glyph, string Label, string Value, string Sub, double? Percent = null, bool Inverse = false)
{
    public bool HasMeter => Percent is not null;
    public double MeterValue => Percent ?? 0;

    /// <summary>Doluluk çubukları için "yüksek = kötü"; pil sağlığı için tersi.</summary>
    public double RiskPercent => Inverse ? 100 - MeterValue : MeterValue;
}

public enum StepState { Pending, Running, Done }

public sealed class StepItem(string name) : Observable
{
    private StepState _state;
    public string Name { get; } = name;
    public StepState State { get => _state; set { if (Set(ref _state, value)) { Raise(nameof(IsRunning)); Raise(nameof(IsDone)); Raise(nameof(IsPending)); } } }
    public bool IsRunning => State == StepState.Running;
    public bool IsDone => State == StepState.Done;
    public bool IsPending => State == StepState.Pending;
}

public sealed class ChatMessage : Observable
{
    private string _text = "";
    private bool _isStreaming;
    private bool _isError;

    public required bool IsUser { get; init; }
    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsStreaming { get => _isStreaming; set => Set(ref _isStreaming, value); }
    public bool IsError { get => _isError; set => Set(ref _isError, value); }

    /// <summary>Modele gönderilen içerik (ilk mesajda raporun tamamı); ekranda ise kısa hâli görünür.</summary>
    public string? PayloadOverride { get; init; }
}

public sealed class MainViewModel : Observable
{
    private DiagnosticReport? _report;
    private bool _isAnalyzing;
    private List<HistoryEntry> _history = App.IsDemo ? [] : HistoryStore.Load();
    private double _speedMbps;
    private bool _isSpeedTesting;
    private string _speedStatus = "Henüz ölçülmedi";
    private bool _isAiBusy;
    private AiSettings _ai = AiSettings.Load();
    private CancellationTokenSource? _aiCts;

    public DiagnosticReport? Report
    {
        get => _report;
        private set
        {
            if (!Set(ref _report, value)) return;
            Raise(nameof(HasReport));
            Raise(nameof(ProblemResults));
            Raise(nameof(InfoResults));
            Raise(nameof(OkResults));
            Raise(nameof(OkSummary));
            Raise(nameof(Headline));
            Raise(nameof(ProblemCount));
            Raise(nameof(ScoreDeltaText));
            Raise(nameof(SpecTiles));
        }
    }

    public bool HasReport => Report is not null;

    /// <summary>"Bu bilgisayar" bölümündeki donanım kutucukları.</summary>
    public List<SpecTile> SpecTiles
    {
        get
        {
            if (Report is null) return [];
            var s = Report.System;
            var tiles = new List<SpecTile>
            {
                new("", "İşlemci", s.Cpu, $"{s.CpuCores} çekirdek · {s.CpuThreads} iş parçacığı"),
                new("", "Bellek", $"{s.RamTotalGb:0.#} GB", $"%{s.RamUsedPercent:0} kullanımda", s.RamUsedPercent)
            };
            tiles.AddRange(s.Gpus.Select(g =>
            {
                var i = g.IndexOf(" (sürücü", StringComparison.Ordinal);
                return new SpecTile("", "Ekran kartı", i > 0 ? g[..i] : g, i > 0 ? g[(i + 2)..^1] : "");
            }));
            tiles.AddRange(s.Disks.Select(d => new SpecTile("", d.IsSystem ? $"Disk {d.Name} · sistem" : $"Disk {d.Name}",
                $"{d.FreeGb} boş", $"{d.TotalGb} içinde %{d.UsedPercent:0} dolu", d.UsedPercent)));
            if (s.BatteryHealthPercent is double b)
                tiles.Add(new SpecTile("", "Pil sağlığı", $"%{b:0}", "tasarım kapasitesine göre", b, Inverse: true));
            tiles.Add(new SpecTile("", "Açık kalma süresi", s.UptimeDisplay, $"{s.Motherboard}"));
            return tiles;
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set { if (Set(ref _isAnalyzing, value)) Raise(nameof(ShowEmpty)); }
    }

    public bool ShowEmpty => !IsAnalyzing && !HasReport;

    public ObservableCollection<StepItem> Steps { get; } =
        new(DiagnosticEngine.StepNames.Select(n => new StepItem(n)));

    public int ProblemCount => Report?.Problems.Count ?? 0;

    // --- Bulgular: önem sırasına göre üç bölüm ---

    public List<CheckResult> ProblemResults => Report?.Results.Where(r => r.IsProblem).ToList() ?? [];
    public List<CheckResult> InfoResults => Report?.Results.Where(r => r.Severity == Severity.Info).ToList() ?? [];
    public List<CheckResult> OkResults => Report?.Results.Where(r => r.Severity == Severity.Ok).ToList() ?? [];

    /// <summary>Genel bakıştaki büyük başlık: durumu tek cümlede söyler.</summary>
    public string Headline => Report is null ? "" : ProblemCount switch
    {
        0 => "Bilgisayarın iyi durumda.",
        1 => "Bir konu dikkatini bekliyor.",
        _ => $"{ProblemCount} konu dikkatini bekliyor."
    };

    public string OkSummary => $"{OkResults.Count} kontrol sorunsuz geçti";

    // --- Geçmiş ---

    public List<HistoryEntry> History { get => _history; private set { if (Set(ref _history, value)) Raise(nameof(ScoreDeltaText)); } }

    /// <summary>"Son analize göre +6 puan · 1 sorun çözüldü" gibi kısa özet.</summary>
    public string? ScoreDeltaText
    {
        get
        {
            if (Report is null || History.Count < 2 || History[0].ScoreDelta is not int d) return null;
            var since = History[1].Date.ToString("d MMMM");
            var pts = d switch { > 0 => $"+{d} puan", < 0 => $"{d} puan", _ => "puan değişmedi" };
            return $"{since} tarihli analize göre {pts} · {History[0].ChangeSummary.ToLowerInvariant()}";
        }
    }

    public void ClearHistory()
    {
        HistoryStore.Clear();
        History = [];
    }

    // --- Analiz ---

    public async Task AnalyzeAsync()
    {
        if (IsAnalyzing) return;
        IsAnalyzing = true;
        foreach (var s in Steps) s.State = StepState.Pending;

        try
        {
            var progress = new Progress<int>(i =>
            {
                for (var k = 0; k < Steps.Count; k++)
                    Steps[k].State = k < i ? StepState.Done : k == i ? StepState.Running : StepState.Pending;
            });
            DiagnosticReport report;
            if (App.IsDemo)
            {
                // Tanıtım modu: adımları canlandır, gerçek sistemi okuma, geçmişe yazma.
                for (var i = 0; i <= Steps.Count; i++)
                {
                    ((IProgress<int>)progress).Report(i);
                    await Task.Delay(450);
                }
                report = DemoData.Create();
                History = DemoData.History();
            }
            else
            {
                report = await DiagnosticEngine.RunAsync(progress);
                History = HistoryStore.Add(report);
            }
            Report = report;
            Raise(nameof(ScoreDeltaText));
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>Hızlı düzeltme sonrası ilgili bulgular değiştiği için raporu yeniden üretmeden ekranı tazeler.</summary>
    public void Refresh()
    {
        var r = _report;
        _report = null;
        Raise(nameof(Report));
        Report = r;
    }

    // --- Hız testi ---

    public double SpeedMbps { get => _speedMbps; private set { if (Set(ref _speedMbps, value)) Raise(nameof(SpeedDisplay)); } }

    /// <summary>Henüz ölçülmediyse yanıltıcı "0" yerine tire.</summary>
    public string SpeedDisplay => SpeedMbps > 0 || IsSpeedTesting ? SpeedMbps.ToString("0") : "–";
    public bool IsSpeedTesting { get => _isSpeedTesting; private set { if (Set(ref _isSpeedTesting, value)) Raise(nameof(SpeedDisplay)); } }
    public string SpeedStatus { get => _speedStatus; private set => Set(ref _speedStatus, value); }

    public async Task RunSpeedTestAsync()
    {
        if (IsSpeedTesting) return;
        IsSpeedTesting = true;
        SpeedMbps = 0;
        SpeedStatus = "Ölçülüyor…";
        try
        {
            var result = await SpeedTest.RunAsync(new Progress<double>(v => SpeedMbps = v), CancellationToken.None);
            SpeedStatus = result switch
            {
                >= 100 => "Çok hızlı — 4K yayın, büyük oyun indirmeleri ve görüntülü görüşme için fazlasıyla yeterli.",
                >= 25 => "İyi — HD/4K yayın ve çevrim içi oyun için yeterli.",
                >= 8 => "Orta — HD video izlenebilir ama büyük indirmeler uzun sürer.",
                _ => "Yavaş — video takılabilir; modemi yeniden başlatmayı ya da kablolu bağlantıyı dene."
            };
        }
        catch (Exception ex)
        {
            SpeedStatus = "Hız ölçülemedi: " + ex.Message;
        }
        finally
        {
            IsSpeedTesting = false;
        }
    }

    // --- AYT AI ---

    public AiSettings AiSettings
    {
        get => _ai;
        set { _ai = value; Raise(); Raise(nameof(AiConfigured)); }
    }

    public bool AiConfigured => _ai.IsConfigured;
    public ObservableCollection<ChatMessage> Chat { get; } = [];
    public bool IsAiBusy { get => _isAiBusy; private set => Set(ref _isAiBusy, value); }

    /// <summary>
    /// Sohbete bir mesaj ekler ve yanıtı akış hâlinde yazar. İlk mesajda gizlilik filtresinden
    /// geçmiş ve kullanıcının onayladığı rapor gönderilir.
    /// </summary>
    public async Task SendToAiAsync(string display, string? payload = null)
    {
        if (IsAiBusy) return;
        Chat.Add(new ChatMessage { IsUser = true, Text = display, PayloadOverride = payload });
        var answer = new ChatMessage { IsUser = false, IsStreaming = true };
        Chat.Add(answer);
        IsAiBusy = true;
        _aiCts = new CancellationTokenSource();

        var turns = Chat.Where(m => m != answer && !m.IsError)
            .Select(m => new ChatTurn(m.IsUser ? "user" : "assistant", m.PayloadOverride ?? m.Text))
            .ToList();

        var sb = new StringBuilder();
        var lastFlush = DateTime.MinValue;
        try
        {
            await new AiClient(_ai).ChatAsync(turns, delta =>
            {
                sb.Append(delta);
                // Markdown'ı her parçada yeniden çizmek pahalı: ~20 kare/sn yeter.
                if ((DateTime.UtcNow - lastFlush).TotalMilliseconds > 50)
                {
                    answer.Text = sb.ToString();
                    lastFlush = DateTime.UtcNow;
                }
            }, _aiCts.Token);
            answer.Text = sb.ToString();
        }
        catch (OperationCanceledException)
        {
            answer.Text = sb.Length > 0 ? sb + "\n\n*(durduruldu)*" : "*(durduruldu)*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Text = "**AYT AI'ye ulaşılamadı.** " + ex.Message;
        }
        finally
        {
            answer.IsStreaming = false;
            IsAiBusy = false;
        }
    }

    public void StopAi() => _aiCts?.Cancel();

    public void ClearChat()
    {
        StopAi();
        Chat.Clear();
    }

    public bool HasChat => Chat.Count > 0;
}
