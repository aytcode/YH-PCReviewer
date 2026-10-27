using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace YHPCReviewer.UI;

/// <summary>
/// AYT AI yanıtlarındaki basit Markdown'ı (başlık, madde, numaralı liste, **kalın**, *italik*, `kod`)
/// seçilebilir, salt-okunur metin olarak gösterir. Akış sırasında metin değiştikçe yeniden çizilir.
/// </summary>
public sealed partial class MarkdownView : RichTextBox
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata("", (d, _) => ((MarkdownView)d).Render()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    public MarkdownView()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FontSize = 14;
    }

    private void Render()
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize,
            LineHeight = FontSize * 1.5,
            Foreground = Foreground
        };
        List? list = null;
        var ordered = false;

        foreach (var raw in (Markdown ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                list = null;
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                list = null;
                var level = heading.Groups[1].Value.Length;
                var p = new Paragraph { Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold, FontSize = level <= 2 ? FontSize + 3 : FontSize + 1 };
                AddInlines(p.Inlines, heading.Groups[2].Value);
                doc.Blocks.Add(p);
                continue;
            }

            var bullet = BulletRegex().Match(line);
            var number = NumberRegex().Match(line);
            if (bullet.Success || number.Success)
            {
                var isOrdered = number.Success;
                if (list is null || ordered != isOrdered)
                {
                    ordered = isOrdered;
                    list = new List
                    {
                        MarkerStyle = isOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 2, 0, 6),
                        Padding = new Thickness(20, 0, 0, 0)
                    };
                    doc.Blocks.Add(list);
                }
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                AddInlines(p.Inlines, isOrdered ? number.Groups[1].Value : bullet.Groups[1].Value);
                list.ListItems.Add(new ListItem(p));
                continue;
            }

            list = null;
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            AddInlines(para.Inlines, line);
            doc.Blocks.Add(para);
        }

        Document = doc;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var pos = 0;
        foreach (Match m in InlineRegex().Matches(text))
        {
            if (m.Index > pos) inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success) inlines.Add(new Bold(new Run(m.Groups["b"].Value)));
            else if (m.Groups["i"].Success) inlines.Add(new Italic(new Run(m.Groups["i"].Value)));
            else if (m.Groups["c"].Success)
                inlines.Add(new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas") });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) inlines.Add(new Run(text[pos..]));
    }

    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*•]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*\d+[.)]\s+(.*)$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\*\*(?<b>[^*]+)\*\*|(?<![*\w])\*(?<i>[^*\s][^*]*)\*(?!\*)|`(?<c>[^`]+)`")]
    private static partial Regex InlineRegex();
}
