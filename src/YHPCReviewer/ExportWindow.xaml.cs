using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using YHPCReviewer.Models;
using YHPCReviewer.Reporting;

namespace YHPCReviewer;

/// <summary>
/// Raporun paylaşılacak hâlini gösterir. Kişisel bilgiler varsayılan olarak gizlenir;
/// kullanıcı seçenekleri değiştirebilir ve metni elle düzenleyebilir.
/// </summary>
public partial class ExportWindow : Window
{
    public enum Mode { Export, SendToAi }

    private readonly DiagnosticReport _report;
    private readonly string _fullText;
    private bool _loaded;

    /// <summary>SendToAi modunda kullanıcının onayladığı metin.</summary>
    public string FinalText { get; private set; } = "";

    public ExportWindow(DiagnosticReport report, Mode mode)
    {
        InitializeComponent();
        _report = report;
        _fullText = ReportBuilder.ToMarkdown(report);

        if (mode == Mode.SendToAi)
        {
            Title = "AYT AI'ye gönderilecek rapor";
            Heading.Text = "Gönderilecek metni onayla";
            Intro.Text = "AYT AI'ye yalnızca sağda gördüğün metin gönderilir. Gizlemek istediğin bilgileri seç ya da metni düzenle.";
            ExportButtons.Visibility = Visibility.Collapsed;
            SendButtons.Visibility = Visibility.Visible;
        }

        _loaded = true;
        Regenerate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UI.WindowEffects.SetDark(this);
    }

    private PrivacyOptions Options => new()
    {
        HideUserName = OptUser.IsChecked == true,
        HideComputerName = OptComputer.IsChecked == true,
        HideIpAddresses = OptIp.IsChecked == true,
        HideMacAddresses = OptMac.IsChecked == true,
        HideHardwareIds = OptBoard.IsChecked == true
    };

    private void Regenerate()
    {
        if (!_loaded) return;
        Preview.Text = Redactor.Apply(_fullText, _report.System, Options);
        Status.Text = "";
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => Regenerate();

    private void Reset_Click(object sender, RoutedEventArgs e) => Regenerate();

    private void SaveTxt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Raporu kaydet",
            FileName = $"YH-PCReviewer-Rapor-{_report.CreatedAt:yyyy-MM-dd-HHmm}",
            Filter = "Markdown (*.md)|*.md|Metin dosyası (*.txt)|*.txt",
            DefaultExt = ".md"
        };
        if (dlg.ShowDialog(this) != true) return;

        File.WriteAllText(dlg.FileName, Preview.Text, new UTF8Encoding(true));
        Status.Text = $"Kaydedildi: {Path.GetFileName(dlg.FileName)}";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Preview.Text);
            Status.Text = "Panoya kopyalandı. YazılımHub'da yeni bir yardım konusu açıp Ctrl+V ile yapıştırabilirsin.";
        }
        catch (Exception ex)
        {
            Status.Text = "Kopyalanamadı: " + ex.Message;
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        FinalText = Preview.Text;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
