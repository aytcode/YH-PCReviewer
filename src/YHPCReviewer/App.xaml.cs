using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using YHPCReviewer.Diagnostics;
using YHPCReviewer.Reporting;

namespace YHPCReviewer;

public partial class App : Application
{
    /// <summary>"--demo": örnek verilerle açılır (ekran görüntüleri/tanıtım için).</summary>
    public static bool IsDemo { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        IsDemo = e.Args.Contains("--demo");
        DispatcherUnhandledException += OnUnhandled;

        // XAML'deki StringFormat'lar varsayılan olarak en-US kullanır; sistem dilini kullansın (31,7 GB).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // YHPCReviewer.exe --report rapor.md  → arayüz açmadan, gizlilik filtresi uygulanmış rapor yazar.
        var i = Array.IndexOf(e.Args, "--report");
        if (i >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var path = i + 1 < e.Args.Length ? e.Args[i + 1] : "YH-PCReviewer-Rapor.md";
            var report = await DiagnosticEngine.RunAsync(new Progress<int>());
            var text = Redactor.Apply(ReportBuilder.ToMarkdown(report), report.System, new PrivacyOptions());
            File.WriteAllText(path, text, new UTF8Encoding(true));
            Shutdown(report.ErrorCount > 0 ? 2 : 0);
            return;
        }

        base.OnStartup(e);
        UI.ThemeManager.Initialize();
        new MainWindow().Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}", "YH PC Reviewer",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
