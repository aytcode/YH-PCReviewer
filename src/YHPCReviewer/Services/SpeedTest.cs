using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace YHPCReviewer.Services;

/// <summary>
/// İndirme hızını Cloudflare'in herkese açık hız testi sunucusundan ölçer.
/// Yalnızca kullanıcı başlattığında çalışır; ilerlemeyi canlı bildirir.
/// </summary>
public static class SpeedTest
{
    private const string Url = "https://speed.cloudflare.com/__down?bytes=";
    private const long TestBytes = 50_000_000;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(12);

    /// <param name="progress">Anlık Mbps değeri.</param>
    /// <returns>Ortalama indirme hızı (Mbps).</returns>
    public static async Task<double> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync(Url + TestBytes, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long total = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        while (sw.Elapsed < MaxDuration)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;

            if (sw.Elapsed - lastReport > TimeSpan.FromMilliseconds(100))
            {
                lastReport = sw.Elapsed;
                progress.Report(Mbps(total, sw.Elapsed));
            }
        }

        var result = Mbps(total, sw.Elapsed);
        progress.Report(result);
        return result;
    }

    private static double Mbps(long bytes, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : bytes * 8 / elapsed.TotalSeconds / 1_000_000;
}
