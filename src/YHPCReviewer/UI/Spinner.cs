using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YHPCReviewer.UI;

/// <summary>
/// Dönen yükleme göstergesi. Görünür olduğu sürece döner; Windows'ta animasyonlar kapalıysa
/// dönmek yerine yavaşça yanıp söner (baş döndürmeyen alternatif).
/// </summary>
public sealed class Spinner : FrameworkElement
{
    private readonly RotateTransform _rotate = new();

    public Spinner()
    {
        Width = Height = 16;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate;
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => Stop();
    }

    private void Update()
    {
        if (!IsVisible || !IsLoaded)
        {
            Stop();
            return;
        }

        if (Motion.Reduced)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
            return;
        }
        _rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void Stop()
    {
        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        BeginAnimation(OpacityProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var stroke = Math.Max(2, size / 8);
        var r = (size - stroke) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = TryFindResource("Track") as Brush ?? Brushes.LightGray;
        var accent = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
        dc.DrawEllipse(null, new Pen(track, stroke), c, r, r);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(accent, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
    }
}
