using System.Text.RegularExpressions;
using YHPCReviewer.Models;

namespace YHPCReviewer.Reporting;

public sealed class PrivacyOptions
{
    public bool HideComputerName { get; set; } = true;
    public bool HideUserName { get; set; } = true;
    public bool HideIpAddresses { get; set; } = true;
    public bool HideMacAddresses { get; set; } = true;
    public bool HideHardwareIds { get; set; }
}

/// <summary>
/// Rapor metninden kişisel/tanımlayıcı bilgileri çıkarır. Paylaşmadan önce kullanıcı
/// sonucu önizlemede görür ve elle de düzenleyebilir.
/// </summary>
public static partial class Redactor
{
    // Testlerde kullandığımız herkese açık adresler kişisel bilgi değildir.
    private static readonly HashSet<string> PublicTestIps = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    public static string Apply(string text, SystemInfo info, PrivacyOptions o)
    {
        if (o.HideMacAddresses)
            text = MacRegex().Replace(text, "[MAC]");

        if (o.HideIpAddresses)
        {
            // Önce IPv6: "::ffff:192.168.1.1" gibi karma adresler tek parça gizlensin.
            text = Ipv6Regex().Replace(text, "[IPv6]");
            text = Ipv4Regex().Replace(text, m => PublicTestIps.Contains(m.Value) ? m.Value : "[IP]");
        }

        if (o.HideUserName && info.UserName.Length >= 2)
            text = ReplaceWord(text, info.UserName, "[KULLANICI]");

        if (o.HideComputerName && info.ComputerName.Length >= 2)
            text = ReplaceWord(text, info.ComputerName, "[BİLGİSAYAR]");

        if (o.HideHardwareIds && !string.IsNullOrWhiteSpace(info.Motherboard))
            text = text.Replace(info.Motherboard, "[ANAKART]");

        return text;
    }

    private static string ReplaceWord(string text, string word, string replacement) =>
        Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}_])", replacement, RegexOptions.IgnoreCase);

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex Ipv4Regex();

    // "::" içeren ya da en az 4 iki nokta üst üste barındıran onaltılık dizi; saat (16:55) gibi metinlere dokunmaz.
    [GeneratedRegex(@"(?<![\w:.])(?=[0-9A-Fa-f:]*::|(?:[0-9A-Fa-f]{0,4}:){4})[0-9A-Fa-f:]{2,}(?:\d{1,3}(?:\.\d{1,3}){3})?(?:%\d+)?(?![\w:.])")]
    private static partial Regex Ipv6Regex();
}
