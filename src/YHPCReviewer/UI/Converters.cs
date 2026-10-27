using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using YHPCReviewer.Models;

namespace YHPCReviewer.UI;

/// <summary>
/// Önem derecesini tema rengine çevirir. Parametre "soft" ise rengin %15 saydam hâlini
/// (simge arka planı için) döndürür. null (test edilmedi) gri olur.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Severity.Error => "Red",
            Severity.Warning => "Orange",
            Severity.Info => "Teal",
            Severity.Ok => "Green",
            _ => "TertiaryLabel"
        };
        var brush = Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;
        if (parameter as string != "soft") return brush;
        var c = brush.Color;
        return new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Doluluk yüzdesine göre çubuk rengi: mavi → turuncu → kırmızı.</summary>
public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var p = value is double d ? d : 0;
        if (parameter as string == "inverse") p = 100 - p; // pil sağlığı gibi "yüksek = iyi" değerler
        return Application.Current.TryFindResource(p switch
        {
            >= 90 => "Red",
            >= 75 => "Orange",
            _ => parameter as string == "inverse" ? "Green" : "Accent"
        }) ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Yüzdeyi (0-100) çubuğun genişliğine çevirir: values[0] = yüzde, values[1] = toplam genişlik.</summary>
public sealed class PercentWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double p || values[1] is not double w) return 0d;
        return Math.Max(0, w * Math.Clamp(p, 0, 100) / 100);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null/boş metin ise gizler.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var has = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InvertBool : IValueConverter
{
    public static readonly InvertBool Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class IsEmptyString : IValueConverter
{
    public static readonly IsEmptyString Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => string.IsNullOrEmpty(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Bağlanan tüm değerler true ise görünür.</summary>
public sealed class AllTrueToVisibility : IMultiValueConverter
{
    public static readonly AllTrueToVisibility Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sıfırdan başlayan sırayı adım numarasına çevirir.</summary>
public sealed class PlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i ? (i + 1).ToString(culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Liste boşsa gizler; parametre "empty" ise yalnızca boşken gösterir.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var any = value switch
        {
            int i => i > 0,
            ICollection c => c.Count > 0,
            _ => false
        };
        if (parameter as string == "empty") any = !any;
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
