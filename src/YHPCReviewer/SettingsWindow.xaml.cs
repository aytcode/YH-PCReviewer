using System.Windows;
using System.Windows.Controls;
using YHPCReviewer.AI;
using YHPCReviewer.UI;

namespace YHPCReviewer;

public partial class SettingsWindow : Window
{
    private readonly AiSettings _settings;

    public SettingsWindow(AiSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        EndpointBox.Text = settings.Endpoint;
        ModelBox.Text = settings.Model;
        KeyBox.Password = settings.GetApiKey();
        DisconnectButton.Visibility = settings.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.SetDark(this);
    }

    /// <summary>Formdaki (henüz kaydedilmemiş) değerlerle geçici bir ayar nesnesi.</summary>
    private AiSettings Draft()
    {
        var d = _settings.Clone();
        d.Endpoint = EndpointBox.Text.Trim();
        d.Model = ModelBox.Text.Trim();
        d.SetApiKey(KeyBox.Password);
        return d;
    }

    private void Field_Changed(object sender, RoutedEventArgs e) =>
        SetStatus(null, "Kaydetmeden önce bağlantıyı deneyebilirsin.");

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var draft = Draft();
        if (!AiSettings.IsValidUrl(draft.Endpoint))
        {
            SetStatus(false, "Geçerli bir adres gir (https://… ile başlamalı).");
            return;
        }

        SetBusy(true, "Bağlanılıyor…");
        try
        {
            SetStatus(true, await new AiClient(draft).TestAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            SetStatus(false, ex is TaskCanceledException ? "Sunucu yanıt vermedi (zaman aşımı)." : ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void Models_Click(object sender, RoutedEventArgs e)
    {
        var draft = Draft();
        if (!AiSettings.IsValidUrl(draft.Endpoint))
        {
            SetStatus(false, "Modelleri listelemek için önce API adresini gir.");
            return;
        }

        ModelsButton.IsEnabled = false;
        try
        {
            var models = await new AiClient(draft).ListModelsAsync(CancellationToken.None);
            if (models.Count == 0)
            {
                SetStatus(false, "Sunucu model listesi döndürmedi; model adını elle yazabilirsin.");
                return;
            }

            var menu = new ContextMenu { PlacementTarget = ModelsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var m in models)
            {
                var item = new MenuItem { Header = m, IsCheckable = true, IsChecked = m == ModelBox.Text };
                item.Click += (_, _) => ModelBox.Text = m;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            SetStatus(false, "Modeller alınamadı: " + ex.Message);
        }
        finally
        {
            ModelsButton.IsEnabled = true;
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (!Alert.Confirm(this, "AYT AI bağlantısı kaldırılsın mı?", "API adresi ve anahtarı bu bilgisayardan silinecek.", "Kaldır", destructive: true))
            return;
        _settings.Endpoint = "";
        _settings.Model = "";
        _settings.SetApiKey("");
        _settings.Enabled = false;
        _settings.Save();
        DialogResult = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = EndpointBox.Text.Trim();
        if (endpoint.Length > 0 && !AiSettings.IsValidUrl(endpoint))
        {
            SetStatus(false, "Geçerli bir adres gir (https://… ile başlamalı).");
            EndpointBox.Focus();
            return;
        }

        _settings.Enabled = endpoint.Length > 0;
        _settings.Endpoint = endpoint;
        _settings.Model = ModelBox.Text.Trim();
        _settings.SetApiKey(KeyBox.Password);
        _settings.Save();
        DialogResult = true;
    }

    private void SetBusy(bool busy, string? text)
    {
        TestButton.IsEnabled = !busy;
        TestSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            TestIcon.Visibility = Visibility.Collapsed;
            TestText.Text = text;
        }
    }

    /// <param name="ok">null: nötr, true: başarılı, false: hata.</param>
    private void SetStatus(bool? ok, string text)
    {
        TestText.Text = text;
        if (ok is null)
        {
            TestIcon.Visibility = Visibility.Collapsed;
            return;
        }
        TestIcon.Visibility = Visibility.Visible;
        TestIcon.Text = ok.Value ? "" : "";
        TestIcon.SetResourceReference(TextBlock.ForegroundProperty, ok.Value ? "Green" : "Red");
    }
}
