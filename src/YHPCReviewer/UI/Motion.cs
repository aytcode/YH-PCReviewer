using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YHPCReviewer.UI;

/// <summary>
/// Uygulamanın hareket dili. Animasyonlar her zaman mevcut (ekrandaki) değerden başlar, böylece
/// yarıda kesilip yeniden başlatılabilir; kritik sönümlü yaya yakın bir eğri (üstel yavaşlama, taşma yok) kullanılır.
/// Windows'ta animasyonlar kapalıysa kısa bir saydamlık geçişine düşer.
/// </summary>
public static class Motion
{
    /// <summary>Ayarlar > Erişilebilirlik > Görsel efektler > Animasyon efektleri kapalıysa true.</summary>
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;

    // Kritik sönümlü (bounce = 0) yayın görsel karşılığı.
    public static readonly IEasingFunction Settle = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };

    public static readonly Duration Quick = new(TimeSpan.FromMilliseconds(200));
    public static readonly Duration Standard = new(TimeSpan.FromMilliseconds(320));
    public static readonly Duration Slow = new(TimeSpan.FromMilliseconds(900));

    /// <summary>Sayfa girişi: hafifçe aşağıdan yukarı kayarak belirir (azaltılmış harekette yalnızca belirir).</summary>
    public static void Enter(UIElement element, double offset = 10)
    {
        var fade = new DoubleAnimation(0, 1, Reduced ? Quick : Standard) { EasingFunction = Settle };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (Reduced) return;

        if (element.RenderTransform is not TranslateTransform t || t.IsFrozen)
            element.RenderTransform = t = new TranslateTransform();
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, Standard) { EasingFunction = Settle });
    }

    /// <summary>Bir sayı özelliğini mevcut değerinden hedefe yumuşakça taşır.</summary>
    public static void To(DependencyObject target, DependencyProperty property, double value, Duration? duration = null)
    {
        if (target is not IAnimatable a) return;
        if (Reduced)
        {
            a.BeginAnimation(property, null);
            target.SetValue(property, value);
            return;
        }
        a.BeginAnimation(property, new DoubleAnimation(value, duration ?? Standard) { EasingFunction = Settle },
            HandoffBehavior.SnapshotAndReplace);
    }
}
