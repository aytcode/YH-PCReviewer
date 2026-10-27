using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YHPCReviewer.UI;

/// <summary>
/// Windows 11'in Mica malzemesini pencereye uygular: kenar çubuğu masaüstü renginden hafifçe
/// etkilenen yarı saydam bir katman olur (macOS kenar çubuğu malzemesinin karşılığı).
/// Desteklenmeyen sistemlerde düz renge döner.
/// </summary>
public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;   // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic (iletişim pencereleri için)
    private const int DWMWCP_ROUND = 2;

    /// <summary>Pencere için Mica/Acrylic uygular; başarılıysa true döner.</summary>
    public static bool Apply(Window window, bool transient = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        SetDark(window);
        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        if (SystemParameters.HighContrast || Environment.OSVersion.Version.Build < 22621)
            return false;

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null) return false;

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) return false;

        var backdrop = transient ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0) return false;

        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
        return true;
    }

    /// <summary>Başlık çubuğunu uygulamanın temasına (açık/koyu) uydurur.</summary>
    public static void SetDark(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = ThemeManager.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
