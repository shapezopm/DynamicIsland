using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DynamicIsland;

sealed class TextEffectPreview : FrameworkElement
{
    static readonly string[] Lines = ["Свет живёт", "внутри меня"];
    const double FontSize = 10.5, LineHeight = 14, FrameSeconds = 1.0 / 60;
    const double SweepSeconds = 1.2, FadeSeconds = 0.32, PauseSeconds = 0.18;
    const double LineSeconds = SweepSeconds + FadeSeconds + PauseSeconds;
    const int Strength = 2;

    readonly FrameLoop _loop;
    double _time;

    public TextEffectPreview()
    {
        _loop = new FrameLoop(Advance, FrameSeconds);
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            _time = 0;
            if (IsVisible && Kind != LyricFx.Off)
            {
                InvalidateVisual();
                _loop.Start();
            }
            else _loop.Stop();
        };
    }

    public LyricFx Kind { get; set; }

    bool Advance(double dt)
    {
        _time = (_time + dt) % (Lines.Length * LineSeconds);
        InvalidateVisual();
        return IsVisible && Kind != LyricFx.Off;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Brush plain = Kind == LyricFx.Off ? (Brush)FindResource("Dim") : Brushes.White;
        for (int line = 0; line < Lines.Length; line++)
        {
            double top = (h - Lines.Length * LineHeight) / 2 + line * LineHeight;
            double elapsed = _time - line * LineSeconds;
            bool active = elapsed >= 0 && elapsed < SweepSeconds + FadeSeconds;
            var letters = Lines[line].Select(letter => new FormattedText(letter.ToString(), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, face, FontSize, plain, dpi)).ToArray();
            double total = letters.Sum(letter => letter.WidthIncludingTrailingWhitespace);
            double left = (w - total) / 2;
            // Одна строка за раз: проход слева направо, затухание, затем переход к следующей.
            double front = Math.Min(elapsed / SweepSeconds, 1);
            double fade = 1 - Math.Clamp((elapsed - SweepSeconds) / FadeSeconds, 0, 1);
            for (int i = 0; i < letters.Length; i++)
            {
                LyricFxFrame frame = LyricFxPlan.Sample(Kind, Strength, _time,
                    LyricFxPlan.Letter(front, (i + 0.5) / letters.Length, letters.Length, 1, i), active);
                Point at = new(left + frame.X, top + frame.Y);
                FormattedText letter = letters[i];
                double shade = Math.Max(frame.Shade, frame.Glow * 0.65) * fade;
                if (shade > 0) letter.SetForegroundBrush(LyricFxPaint.LetterBrush(this, shade));
                dc.DrawText(letter, at);
                left += letter.WidthIncludingTrailingWhitespace;
            }
        }
    }
}
