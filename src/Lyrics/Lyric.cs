using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class Lyric : FrameworkElement
{
    const double FontSize = 14, LineHeight = 18;
    const int MaxLines = 2;
    const double SweepEdge = 26;
    const double RowEdgeSlack = 0.5;
    const double PartedRowSlack = 4;
    const char FirstJoinedLetter = '֐', LastJoinedLetter = 'ࣿ';

    const int DotCount = 3;
    const double DotSize = 6, DotGap = 5, DotInset = 1;
    const double SpentDimming = 1.75;
    const double FadeShare = 0.3;
    const double BreathSwell = 0.24;
    const double BreathSeconds = 1.8;

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly double[] MaskOffsets = [0, 0, 1, 1];

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(Lyric), new FrameworkPropertyMetadata(0.0, (d, _) => ((Lyric)d).UpdateProgress()));

    public static readonly DependencyProperty UnsungProperty = DependencyProperty.Register(
        nameof(Unsung), typeof(double), typeof(Lyric),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Lyric)d).UpdateFill()));

    readonly string _words;
    readonly bool _wordless;
    readonly SolidColorBrush[] _dots = [];
    readonly ScaleTransform _breath = new(1, 1);
    FormattedText? _text;
    double[] _rowWidths = [];
    LinearGradientBrush[] _masks = [];
    Parting? _parting;
    FormattedText[] _letters = [];
    Point[] _letterOrigins = [];
    int[] _letterRows = [];
    double[] _letterCenters = [];
    int[] _letterSteps = [];
    bool _partMoving;
    bool _current, _active;
    Color? _paintAccent;
    SolidColorBrush? _glowBrush;
    const int ShadeSteps = 32;

    const double SparkRadius = 1.1, GlowOpacity = 0.35;

    // Эффект «живого текста». Off — строка рисуется как раньше.
    internal LyricFx TextEffect { get; set; }

    public int EffectStrength { get; set; } = 1;

    // Плавность перехода: во сколько раз медленнее (больше) или быстрее идёт появление эффекта.
    public double Tempo { get; set; } = 1;

    internal void SetPlaybackState(bool current, bool active)
    {
        if (_current == current && _active == active) return;
        _current = current;
        _active = active;
        InvalidateVisual();
    }

    bool EffectVisible => _hold || (TextEffect == LyricFx.Shimmer ? _current : _active);

    // Уровень подсветки уходящей строки: 1 — как есть, 0 — базовый цвет. Анимируется за время ухода в серый,
    // поэтому подсветка гаснет вместе с прозрачностью и не вспыхивает белым.
    public static readonly System.Windows.DependencyProperty HoldLevelProperty = System.Windows.DependencyProperty.Register(
        nameof(HoldLevel), typeof(double), typeof(Lyric),
        new System.Windows.FrameworkPropertyMetadata(1.0, System.Windows.FrameworkPropertyMetadataOptions.AffectsRender));

    public double HoldLevel => (double)GetValue(HoldLevelProperty);

    bool _hold;

    public void Hold(System.Windows.Duration time)
    {
        _hold = true;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, time);
        fade.Completed += (_, _) => Release();
        BeginAnimation(HoldLevelProperty, fade);
    }

    public void Release()
    {
        BeginAnimation(HoldLevelProperty, null);
        _hold = false;
        InvalidateVisual();
    }

    LyricFxFrame Fade(LyricFxFrame frame) => HoldLevel >= 1 ? frame
        : frame with { Shade = frame.Shade * HoldLevel, Glow = frame.Glow * HoldLevel };

    public Lyric(string words)
    {
        _words = words;
        _wordless = IsWordless(words);
        if (_wordless) _dots = Enumerable.Range(0, DotCount).Select(_ => new SolidColorBrush(Colors.White)).ToArray();
    }

    public static bool IsWordless(string words) => !words.Any(char.IsLetterOrDigit);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double Unsung
    {
        get => (double)GetValue(UnsungProperty);
        set => SetValue(UnsungProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 0 : available.Width;
        if (_wordless)
        {
            UpdateFill();
            return new Size(width, LineHeight);
        }

        var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _text = new FormattedText(_words, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(width, 1),
            MaxLineCount = MaxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            LineHeight = LineHeight,
        };

        int rows = Math.Clamp((int)Math.Round(_text.Height / LineHeight), 1, MaxLines);
        _rowWidths = new double[rows];
        _masks = new LinearGradientBrush[rows];
        if (_text.BuildHighlightGeometry(new Point()) is { } boxes)
        {
            Rect bounds = boxes.Bounds;
            foreach (Point point in boxes.GetFlattenedPathGeometry().Figures.SelectMany(figure => figure.Points()))
            {
                if (point.Y < bounds.Top + RowEdgeSlack) _rowWidths[0] = Math.Max(_rowWidths[0], point.X);
                if (point.Y > bounds.Bottom - RowEdgeSlack) _rowWidths[^1] = Math.Max(_rowWidths[^1], point.X);
            }
        }
        for (int i = 0; i < rows; i++)
        {
            _masks[i] = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, EndPoint = new Point(Math.Max(width, 1), 0) };
            foreach (double offset in MaskOffsets) _masks[i].GradientStops.Add(new GradientStop(Colors.Black, offset));
        }
        PrepareParting(face, width, rows);
        UpdateFill();
        return new Size(width, rows * LineHeight);
    }

    public bool Part(Point? pointer, double dt, bool animate)
    {
        // Перелив перерисовывается по прогрессу. Кадры нужны только для движущихся эффектов.
        bool living = animate && TextEffect is (LyricFx.Lift or LyricFx.Glow) && !_wordless && _active;
        if (_parting == null)
        {
            if (living) InvalidateVisual();
            return living;
        }

        bool moving = _parting.Advance(pointer, dt);
        if (moving || _partMoving || living) InvalidateVisual();
        _partMoving = moving;
        return moving || living;
    }

    internal void StopParting()
    {
        if (_parting?.Reset() == true) InvalidateVisual();
        _partMoving = false;
    }

    void PrepareParting(Typeface face, double width, int rows)
    {
        _parting = null;
        if (_words.Any(letter => letter is >= FirstJoinedLetter and <= LastJoinedLetter)) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var whole = new FormattedText(_words, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, Brushes.White, pixelsPerDip)
        {
            MaxTextWidth = Math.Max(width, 1),
            LineHeight = LineHeight,
        };
        if (whole.Height > rows * LineHeight + RowEdgeSlack) return;

        int[] starts = StringInfo.ParseCombiningCharacters(_words);
        var letters = new List<FormattedText>();
        var origins = new List<Point>();
        var rowsOf = new List<int>();
        var middles = new List<Point>();
        var centers = new List<double>();
        // Центры букв — в тех же единицах, что и фронт заливки (UpdateFill), то есть по всей ширине строк.
        var rowStarts = new double[rows];
        double advance = 0;
        for (int r = 0; r < rows; r++)
        {
            rowStarts[r] = advance;
            advance += _rowWidths[r] + SweepEdge;
        }
        double sweep = Math.Max(1e-6, advance);
        for (int i = 0; i < starts.Length; i++)
        {
            int start = starts[i], length = (i + 1 < starts.Length ? starts[i + 1] : _words.Length) - start;
            if (char.IsWhiteSpace(_words[start]) || whole.BuildHighlightGeometry(new Point(), start, length) is not { } shape) continue;

            Rect box = shape.Bounds;
            int row = Math.Clamp((int)((box.Top + box.Height / 2) / LineHeight), 0, rows - 1);
            letters.Add(new FormattedText(_words.Substring(start, length), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, FontSize, Brushes.White, pixelsPerDip) { LineHeight = LineHeight });
            origins.Add(new Point(box.Left, row * LineHeight));
            rowsOf.Add(row);
            middles.Add(new Point(box.Left + box.Width / 2, (row + 0.5) * LineHeight));
            centers.Add((rowStarts[row] + box.Left + box.Width / 2) / sweep);
        }
        _letters = [.. letters];
        _letterOrigins = [.. origins];
        _letterRows = [.. rowsOf];
        _letterCenters = [.. centers];
        _letterSteps = Enumerable.Repeat(-1, _letters.Length).ToArray();
        _parting = new Parting([.. middles]);
        _partMoving = false;
    }

    void UpdateProgress()
    {
        UpdateFill();
        if (TextEffect == LyricFx.Shimmer && EffectVisible) InvalidateVisual();
    }

    void UpdateFill()
    {
        if (_wordless)
        {
            UpdateDots();
            return;
        }

        double width = _masks.Length > 0 ? _masks[0].EndPoint.X : 0;
        Color unsung = Colors.Black.WithAlpha(Math.Clamp(Unsung, 0, 1));
        double at = Math.Clamp(Progress, 0, 1) * (_rowWidths.Sum() + SweepEdge * _rowWidths.Length);
        for (int i = 0; i < _masks.Length; i++)
        {
            double from = Math.Clamp(at, 0, _rowWidths[i] + SweepEdge) - SweepEdge;
            at -= _rowWidths[i] + SweepEdge;
            GradientStopCollection stops = _masks[i].GradientStops;
            stops[1].Offset = Math.Clamp(from / width, 0, 1);
            stops[2].Offset = Math.Clamp((from + SweepEdge) / width, 0, 1);
            stops[2].Color = stops[3].Color = unsung;
        }
    }

    void UpdateDots()
    {
        double spent = Math.Clamp(1 - SpentDimming * (1 - Unsung), 0, 1);
        double left = (1 - Math.Clamp(Progress, 0, 1)) * DotCount;
        for (int i = 0; i < DotCount; i++)
            _dots[i].Opacity = spent + (1 - spent) * Math.Clamp((left - i) / FadeShare, 0, 1);

        double breath = (1 - Math.Cos(Clock.Elapsed.TotalSeconds * 2 * Math.PI / BreathSeconds)) / 2;
        _breath.ScaleX = _breath.ScaleY = 1 + BreathSwell * (1 - spent) * breath;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_wordless) DrawDots(dc);
        else if (_text != null) DrawWords(dc, _text);
    }

    void DrawDots(DrawingContext dc)
    {
        for (int i = 0; i < DotCount; i++)
        {
            dc.PushTransform(new TranslateTransform(DotInset + DotSize / 2 + i * (DotSize + DotGap), LineHeight / 2));
            dc.PushTransform(_breath);
            dc.DrawEllipse(_dots[i], null, new Point(), DotSize / 2, DotSize / 2);
            dc.Pop();
            dc.Pop();
        }
    }

    void DrawWords(DrawingContext dc, FormattedText text)
    {
        if (TextEffect is LyricFx.Shimmer or LyricFx.Glow)
        {
            Color accent = Accent();
            if (_paintAccent != accent)
            {
                _paintAccent = accent;
                Array.Fill(_letterSteps, -1);
                _glowBrush = null;
            }
        }
        if (Unsung >= 1)
        {
            DrawRow(dc, text, null);
            return;
        }

        double slack = _parting == null ? 0 : PartedRowSlack;
        for (int i = 0; i < _masks.Length; i++)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, i * LineHeight - slack, ActualWidth, LineHeight + 2 * slack)));
            dc.PushOpacityMask(_masks[i]);
            DrawRow(dc, text, i);
            dc.Pop();
            dc.Pop();
        }
    }

    void DrawRow(DrawingContext dc, FormattedText text, int? row)
    {
        if (_parting == null)
        {
            DrawLine(dc, text);
            return;
        }

        for (int i = 0; i < _letters.Length; i++)
        {
            if (row != null && _letterRows[i] != row) continue;
            LyricFxFrame frame = Frame(i);
            Point at = _letterOrigins[i] + _parting[i] + new Vector(frame.X, frame.Y);
            if (frame.Glow > 0)
            {
                DrawGlow(dc, _letters[i], at, frame.Glow);
                _letterSteps[i] = -1;
            }
            // Кисть меняется только при смене заметного шага перелива, а не на каждом кадре.
            int step = (int)Math.Round(frame.Shade * ShadeSteps);
            if (step != _letterSteps[i])
            {
                _letterSteps[i] = step;
                _letters[i].SetForegroundBrush(LetterBrush(step / (double)ShadeSteps));
            }
            dc.DrawText(_letters[i], at);
            if (frame.Spark > 0) DrawSpark(dc, at + new Vector(_letters[i].Width / 2, -frame.SparkRise - 2), frame.Spark);
        }
    }

    // Строка, которую нельзя разбить на буквы (несколько строк или связанное письмо): эффект на всю строку.
    void DrawLine(DrawingContext dc, FormattedText text)
    {
        if (TextEffect == LyricFx.Off)
        {
            dc.DrawText(text, new Point());
            return;
        }

        LyricFxFrame frame = LyricFxPlan.Sample(TextEffect, EffectStrength, Clock.Elapsed.TotalSeconds,
            LyricFxPlan.Letter(Math.Clamp(Progress, 0, 1), 0.5, 1, Tempo, 0), EffectVisible);
        dc.PushTransform(new TranslateTransform(frame.X, frame.Y));
        text.SetForegroundBrush(LetterBrush(frame.Shade * HoldLevel));
        dc.DrawText(text, new Point());
        dc.Pop();
    }

    LyricFxFrame Frame(int index)
    {
        bool live = EffectVisible;
        if (TextEffect == LyricFx.Off || !live || index >= _letterCenters.Length) return default;
        LyricFxLetter letter = LyricFxPlan.Letter(Math.Clamp(Progress, 0, 1), _letterCenters[index], _letterCenters.Length, Tempo, index);
        return Fade(LyricFxPlan.Sample(TextEffect, EffectStrength, Clock.Elapsed.TotalSeconds, letter, live));
    }

    void DrawGlow(DrawingContext dc, FormattedText letter, Point at, double amount)
    {
        letter.SetForegroundBrush(_glowBrush ??= Frozen(Accent()));
        dc.PushOpacity(amount * GlowOpacity);
        dc.DrawText(letter, at);
        dc.Pop();
    }

    static void DrawSpark(DrawingContext dc, Point at, double alpha)
    {
        dc.PushOpacity(alpha);
        dc.DrawEllipse(Brushes.White, null, at, SparkRadius, SparkRadius);
        dc.Pop();
    }

    Brush LetterBrush(double shade) => LyricFxPaint.LetterBrush(this, shade);

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    Color Accent() => LyricFxPaint.Accent(this);
}
