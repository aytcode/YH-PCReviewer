using System.Windows;
using Microsoft.Win32;

namespace YHPCReviewer.UI;

/// <summary>Windows'un açık/koyu tema ayarını izler ve uygulama renklerini anında değiştirir.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        Apply(ReadSystemDark());
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            var dark = ReadSystemDark();
            if (dark == IsDark) return;
            Application.Current.Dispatcher.Invoke(() => Apply(dark));
        };
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };
        if (dicts.Count > 0) dicts[0] = theme;
        else dicts.Add(theme);
        ThemeChanged?.Invoke();
    }

    private static bool ReadSystemDark()
    {
        // Geliştirme/test için: YHRESCUE_THEME=light|dark sistem ayarını geçersiz kılar.
        switch (Environment.GetEnvironmentVariable("YHRESCUE_THEME")?.ToLowerInvariant())
        {
            case "light": return false;
            case "dark": return true;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
