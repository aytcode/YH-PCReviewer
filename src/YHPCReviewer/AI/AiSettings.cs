using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YHPCReviewer.AI;

/// <summary>
/// AYT AI bağlantı ayarları. %APPDATA%\YHPCReviewer\settings.json içinde saklanır;
/// API anahtarı Windows DPAPI ile yalnızca bu kullanıcı hesabının çözebileceği şekilde şifrelenir.
/// </summary>
public sealed class AiSettings
{
    public bool Enabled { get; set; }

    /// <summary>OpenAI uyumlu API adresi. "https://…/v1" ya da tam "…/chat/completions" adresi olabilir.</summary>
    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "";

    public string? ProtectedApiKey { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YHPCReviewer", "settings.json");

    /// <summary>Sonunda /chat/completions olmayan temel adres (ör. https://api.ornek.com/v1).</summary>
    public string BaseUrl
    {
        get
        {
            var e = Endpoint.Trim().TrimEnd('/');
            const string suffix = "/chat/completions";
            return e.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? e[..^suffix.Length] : e;
        }
    }

    public string ChatUrl => BaseUrl + "/chat/completions";
    public string ModelsUrl => BaseUrl + "/models";

    public string GetApiKey()
    {
        if (string.IsNullOrEmpty(ProtectedApiKey)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }

    public void SetApiKey(string key)
    {
        ProtectedApiKey = string.IsNullOrEmpty(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme is "https" or "http";

    public bool IsConfigured => IsValidUrl(Endpoint);

    public AiSettings Clone() => new()
    {
        Enabled = Enabled,
        Endpoint = Endpoint,
        Model = Model,
        ProtectedApiKey = ProtectedApiKey
    };

    public static AiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(FilePath)) ?? new AiSettings();
        }
        catch
        {
        }
        return new AiSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
