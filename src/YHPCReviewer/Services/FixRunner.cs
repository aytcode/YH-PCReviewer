using System.Diagnostics;
using YHPCReviewer.Diagnostics;
using YHPCReviewer.Models;

namespace YHPCReviewer.Services;

/// <summary>Hızlı düzeltmeleri çalıştırır. Hiçbiri yönetici izni istemez; değişiklik yapanlar önce onay ister.</summary>
public static class FixRunner
{
    /// <summary>İşlemi çalıştırır ve kullanıcıya gösterilecek kısa bir sonuç metni döndürür.</summary>
    public static async Task<string> RunAsync(FixAction action)
    {
        switch (action.Kind)
        {
            case FixKind.Open:
                Process.Start(new ProcessStartInfo(action.Target!) { UseShellExecute = true });
                return $"{action.Label}: açıldı.";

            case FixKind.FlushDns:
                var ok = await Task.Run(() =>
                {
                    using var p = Process.Start(new ProcessStartInfo("ipconfig.exe", "/flushdns")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    });
                    p?.WaitForExit(10_000);
                    return p?.ExitCode == 0;
                });
                return ok ? "DNS önbelleği temizlendi. Siteleri yeniden açmayı dene." : "DNS önbelleği temizlenemedi.";

            case FixKind.CleanTemp:
                var freed = await Task.Run(TempCleaner.Clean);
                return $"Geçici dosyalar temizlendi: {freed / 1024d / 1024 / 1024:0.00} GB yer açıldı.";

            default:
                return "";
        }
    }
}
