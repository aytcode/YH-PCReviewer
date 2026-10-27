using System.IO;

namespace YHPCReviewer.Diagnostics;

/// <summary>Kullanıcının %TEMP% klasörünü ölçer ve (yalnızca onay sonrası) eski dosyaları siler.</summary>
public static class TempCleaner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static long MeasureBytes()
    {
        long size = 0;
        try
        {
            foreach (var f in new DirectoryInfo(Path.GetTempPath()).EnumerateFiles("*", Options))
            {
                try { size += f.Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    /// <summary>Bir günden eski dosyaları siler; kullanımda olanları atlar. Silinen bayt sayısını döndürür.</summary>
    public static long Clean()
    {
        var cutoff = DateTime.Now.AddDays(-1);
        long freed = 0;
        var root = new DirectoryInfo(Path.GetTempPath());

        foreach (var f in root.EnumerateFiles("*", Options))
        {
            try
            {
                if (f.LastWriteTime > cutoff) continue;
                var len = f.Length;
                f.Delete();
                freed += len;
            }
            catch
            {
                // Kullanımda ya da erişilemiyor: atla.
            }
        }

        // Boşalan alt klasörleri derinden yüzeye doğru kaldır.
        foreach (var d in root.EnumerateDirectories("*", Options).OrderByDescending(d => d.FullName.Length))
        {
            try
            {
                if (!d.EnumerateFileSystemInfos().Any()) d.Delete();
            }
            catch { }
        }
        return freed;
    }
}
