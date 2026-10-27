using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using YHPCReviewer.AI;
using YHPCReviewer.Models;
using YHPCReviewer.Services;
using YHPCReviewer.UI;

namespace YHPCReviewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private FrameworkElement? _currentPage;

    public MainWindow()
    {
        InitializeComponent();
        // Run öğelerindeki tarih biçimleri de sistem dilini kullansın ("27 Ekim").
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        DataContext = _vm;
        _currentPage = Page_overview;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        VersionText.Text = $"Sürüm {version} · Kaynak kodu görülebilir";

        ThemeManager.ThemeChanged += OnThemeChanged;
        _toastTimer.Tick += (_, _) => HideToast();
        _vm.Chat.CollectionChanged += Chat_CollectionChanged;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasReport))
                AnalyzeLabel.Text = _vm.HasReport ? "Yeniden analiz et" : "Sorunları analiz et";
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!WindowEffects.Apply(this))
            SetResourceReference(BackgroundProperty, "SidebarSolidBg");
    }

    private void OnThemeChanged()
    {
        WindowEffects.SetDark(this);
        // Renk dönüştürücüleri temaya göre yeniden çalışsın.
        DataContext = null;
        DataContext = _vm;
    }

    // ---------- Gezinti ----------

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string tag }) return;
        if (tag == "settings")
        {
            // Ayarlar bir sayfa değil, pencere: seçimi önceki sayfaya geri al.
            var previous = e.RemovedItems.OfType<ListBoxItem>().FirstOrDefault();
            Nav.SelectedItem = previous;
            Dispatcher.BeginInvoke(() => OpenSettings(), DispatcherPriority.Input);
            return;
        }
        ShowPage(tag);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Birleşik başlık çubuğunda büyütülmüş pencere ekran kenarından taşar; çerçeve kalınlığı kadar içeri al.
        var max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, max ? "Önceki boyutuna getir" : "Ekranı kapla");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void Navigate(string tag)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == tag) { item.IsSelected = true; return; }
    }

    private void ShowPage(string tag)
    {
        if (FindName("Page_" + tag) is not FrameworkElement page || page == _currentPage) return;
        if (_currentPage is not null) _currentPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        _currentPage = page;
        if (page is ScrollViewer sv) sv.ScrollToTop();
        Motion.Enter(page);
        if (tag == "ai") Dispatcher.BeginInvoke(() => Composer.Focus(), DispatcherPriority.Input);
    }

    private void GoToFindings_Click(object sender, RoutedEventArgs e) => Navigate("findings");

    // ---------- Analiz ----------

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task<bool> AnalyzeAsync()
    {
        Navigate("overview");
        try
        {
            await _vm.AnalyzeAsync();
            Motion.Enter(OverviewResult, 14);
            return true;
        }
        catch (Exception ex)
        {
            Alert.Show(this, "Analiz tamamlanamadı", ex.Message, "Tamam");
            return false;
        }
    }

    // ---------- Hızlı düzeltmeler ----------

    private async void FixAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FixAction action } button) return;
        if (action.Confirm is not null &&
            !Alert.Confirm(this, action.Label, action.Confirm, "Devam et"))
            return;

        button.IsEnabled = false;
        try
        {
            ShowToast(await FixRunner.RunAsync(action));
        }
        catch (Exception ex)
        {
            Alert.Show(this, action.Label, "İşlem yapılamadı: " + ex.Message, "Tamam");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ---------- Rapor ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Report is null) return;
        new ExportWindow(_vm.Report, ExportWindow.Mode.Export) { Owner = this }.ShowDialog();
    }

    private async void SpeedTest_Click(object sender, RoutedEventArgs e) => await _vm.RunSpeedTestAsync();

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Alert.Confirm(this, "Geçmiş silinsin mi?", "Kaydedilen tüm analiz özetleri bu bilgisayardan silinecek. Bu işlem geri alınamaz.", "Sil", destructive: true))
            _vm.ClearHistory();
    }

    // ---------- AYT AI ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private bool OpenSettings()
    {
        var dlg = new SettingsWindow(_vm.AiSettings) { Owner = this };
        var saved = dlg.ShowDialog() == true;
        if (saved) _vm.AiSettings = AiSettings.Load();
        return saved;
    }

    private async void AskAi_Click(object sender, RoutedEventArgs e)
    {
        Navigate("ai");
        if (_vm.Chat.Count == 0) await AskAsync("Raporumu yorumla: en önemli sorunlar neler ve nasıl çözerim?");
    }

    private async void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string q }) await AskAsync(q);
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendComposerAsync();

    private async void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendComposerAsync();
        }
    }

    private async Task SendComposerAsync()
    {
        var text = Composer.Text.Trim();
        if (text.Length == 0 || _vm.IsAiBusy) return;
        Composer.Text = "";
        await AskAsync(text);
    }

    /// <summary>
    /// Sohbetin ilk sorusunda rapor da gönderilir: rapor yoksa önce analiz yapılır, gönderilecek
    /// metin gizlilik önizlemesinde kullanıcıya gösterilir ve onay alınır.
    /// </summary>
    private async Task AskAsync(string question)
    {
        if (!_vm.AiConfigured && !OpenSettings()) return;
        if (!_vm.AiConfigured) return;

        if (_vm.Chat.Count > 0)
        {
            await _vm.SendToAiAsync(question);
            return;
        }

        if (_vm.Report is null)
        {
            ShowToast("Önce bilgisayarın analiz ediliyor…");
            if (!await AnalyzeAsync()) return;
            Navigate("ai");
        }

        var preview = new ExportWindow(_vm.Report!, ExportWindow.Mode.SendToAi) { Owner = this };
        if (preview.ShowDialog() != true || string.IsNullOrWhiteSpace(preview.FinalText)) return;

        var payload = $"{question}\n\n---\nYH PC Reviewer teşhis raporu:\n\n{preview.FinalText}";
        await _vm.SendToAiAsync(question, payload);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _vm.StopAi();

    private void ClearChat_Click(object sender, RoutedEventArgs e) => _vm.ClearChat();

    private void Chat_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ChatMessage m in e.NewItems)
                m.PropertyChanged += ChatMessage_PropertyChanged;
        ScrollChatToEnd();
    }

    private void ChatMessage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessage.Text)) ScrollChatToEnd();
    }

    private void ScrollChatToEnd()
    {
        // Kullanıcı yukarı kaydırıp okuyorsa onu aşağı çekme.
        var nearBottom = ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 80;
        if (nearBottom) Dispatcher.BeginInvoke(ChatScroll.ScrollToEnd, DispatcherPriority.Background);
    }

    // ---------- Bildirim ----------

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        _toastTimer.Stop();
        Motion.To(Toast, OpacityProperty, 1, Motion.Quick);
        if (!Motion.Reduced)
        {
            if (Toast.RenderTransform is not System.Windows.Media.TranslateTransform t || t.IsFrozen)
                Toast.RenderTransform = t = new System.Windows.Media.TranslateTransform();
            t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, Motion.Standard) { EasingFunction = Motion.Settle });
        }
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Motion.To(Toast, OpacityProperty, 0, Motion.Standard);
    }
}
