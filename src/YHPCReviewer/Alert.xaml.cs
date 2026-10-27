using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using YHPCReviewer.UI;

namespace YHPCReviewer;

/// <summary>Uygulama içi uyarı/onay penceresi. Onay yalnızca gerçekten bir şeyi değiştiren işlemlerde istenir.</summary>
public partial class Alert : Window
{
    private Alert(Window owner, string title, string message, string ok, string? cancel, bool destructive)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = new TextBlock { Text = ok, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold };

        if (destructive)
            OkButton.SetResourceReference(BackgroundProperty, "Red");

        if (cancel is null)
        {
            Buttons.Children.Remove(CancelButton);
            OkButton.Margin = new Thickness(0);
        }

        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        Loaded += (_, _) =>
        {
            // Malzeme gibi belir: hafif büyüyerek ve netleşerek.
            Sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Quick) { EasingFunction = Motion.Settle });
            if (Motion.Reduced) return;
            var grow = new DoubleAnimation(0.94, 1, Motion.Standard) { EasingFunction = Motion.Settle };
            SheetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
            SheetScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        };
    }

    public static void Show(Window owner, string title, string message, string ok) =>
        new Alert(owner, title, message, ok, null, false).ShowDialog();

    public static bool Confirm(Window owner, string title, string message, string ok, bool destructive = false) =>
        new Alert(owner, title, message, ok, "Vazgeç", destructive).ShowDialog() == true;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
        base.OnKeyDown(e);
    }
}
