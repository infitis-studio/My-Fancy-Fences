using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace My_Fancy_Fences;

public sealed class LetterSpacedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LetterSpacingProperty = DependencyProperty.Register(
        nameof(LetterSpacing), typeof(double), typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines), typeof(int), typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(LetterSpacedTextBlock),
        new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double LetterSpacing { get => (double)GetValue(LetterSpacingProperty); set => SetValue(LetterSpacingProperty, value); }
    public int MaxLines { get => (int)GetValue(MaxLinesProperty); set => SetValue(MaxLinesProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width);
        var lines = BuildLines(maxWidth);
        var width = lines.Count == 0 ? 0 : lines.Max(MeasureLine);
        var lineHeight = CreateFormattedText("Ag").Height;
        return new Size(
            double.IsInfinity(maxWidth) ? width : Math.Min(width, maxWidth),
            lineHeight * Math.Max(1, lines.Count));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var lines = BuildLines(Math.Max(0, ActualWidth));
        var lineHeight = CreateFormattedText("Ag").Height;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineWidth = MeasureLine(line);
            var x = TextAlignment switch
            {
                TextAlignment.Center => Math.Max(0, (ActualWidth - lineWidth) / 2),
                TextAlignment.Right => Math.Max(0, ActualWidth - lineWidth),
                _ => 0
            };
            var y = lineIndex * lineHeight;

            foreach (var character in line)
            {
                var glyph = CreateFormattedText(character.ToString());
                drawingContext.DrawText(glyph, new Point(x, y));
                x += glyph.WidthIncludingTrailingWhitespace + LetterSpacing;
            }
        }
    }

    private List<string> BuildLines(double maxWidth)
    {
        var text = Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        if (double.IsInfinity(maxWidth) || MaxLines <= 1)
            return [FitWithEllipsis(text, maxWidth)];

        var lines = new List<string>();
        var current = string.Empty;
        var index = 0;
        var lineStart = 0;
        var lastLineStart = 0;
        while (index < text.Length && lines.Count < Math.Max(1, MaxLines))
        {
            var candidate = current + text[index];
            if (current.Length == 0 || MeasureLine(candidate) <= maxWidth)
            {
                current = candidate;
                index++;
                continue;
            }

            // Prefer breaking at the last space, so a word that fits on the next line moves there whole
            // instead of leaving a few of its characters at the end of this one.
            var breakAt = current.LastIndexOf(' ');
            if (breakAt > 0)
            {
                index -= current.Length - breakAt - 1;
                current = current[..breakAt];
            }

            lines.Add(current.TrimEnd());
            current = string.Empty;
            while (index < text.Length && text[index] == ' ')
                index++;
            lastLineStart = lineStart;
            lineStart = index;
        }

        if (lines.Count < MaxLines && current.Length > 0)
            lines.Add(current.TrimEnd());

        if (index < text.Length && lines.Count > 0)
            lines[^1] = FitWithEllipsis(text[lastLineStart..].Trim(), maxWidth);

        return lines;
    }

    private string FitWithEllipsis(string text, double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || MeasureLine(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var result = text;
        while (result.Length > 0 && MeasureLine(result + ellipsis) > maxWidth)
            result = result[..^1];
        return result + ellipsis;
    }

    private double MeasureLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;

        var width = line.Sum(character => CreateFormattedText(character.ToString()).WidthIncludingTrailingWhitespace);
        return Math.Max(0, width + Math.Max(0, line.Length - 1) * LetterSpacing);
    }

    private FormattedText CreateFormattedText(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
        FontSize,
        Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
