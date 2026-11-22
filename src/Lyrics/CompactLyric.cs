using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class CompactLyric : FrameworkElement
{
    const double FontSize = 12;
    const string Ellipsis = "…";
    const char FirstJoinedLetter = '֐', LastJoinedLetter = 'ࣿ';

    const double LineBlur = 6, LineRise = 10;
    const double LineLeaveMs = 260, LineLeaveFadeMs = 200, LineLeaveBlurMs = 220;
    const double LineEnterMs = 380, LineEnterFadeMs = 300;

    const double LetterBlur = 4, LetterRise = 8, LetterDrop = 9, LetterShrink = 0.9;
    const double LetterLeaveMs = 150, LetterLeaveStepMs = 8, LetterLeaveSpreadMs = 100;
    const double LetterEnterMs = 340, LetterEnterWaitMs = 130, LetterEnterStepMs = 14, LetterEnterSpreadMs = 220;

    const double WordTurnMs = 300, WordStepMs = 35;

    const int MaxClusters = 2;
    // Как в открытом острове: граница заливки 26 px, неспетая часть — 0.6 непрозрачности.
    const double SweepEdge = 26;
    const byte UnsungMaskAlpha = 0x99;
    // Яркость подсветки белым: не полная, чтобы не резать глаз.
    const double GlowHighlight = 0.65;

    readonly record struct Pose(double Opacity, double Y, double ScaleX, double ScaleY, double Blur);

    sealed class TurnEase(bool rising) : EasingFunctionBase
    {
        protected override double EaseInCore(double time)
        {
            double angle = time * time * (3 - 2 * time) * Math.PI / 2;
            return rising ? Math.Sin(angle) : 1 - Math.Cos(angle);
        }

        protected override Freezable CreateInstanceCore() => new TurnEase(rising);
    }

    // Буква: свой визуал и свой трансформ эффекта, поэтому эффект двигает только её.
    sealed class Glyph
    {
        public required Brush Fill { get; init; }
        public SolidColorBrush? Solid { get; init; }
        public required TranslateTransform Effect { get; init; }
        public required double Center { get; init; }
        public required int Index { get; init; }
        public double Shade { get; set; }
        public double Glow { get; set; }
    }

    // Кусок анимации входа и выхода (слово, буква или строка). Буквы внутри него анимируются вместе.
    sealed class Piece(ScaleTransform size, TranslateTransform shift)
    {
        public List<Glyph> Glyphs { get; } = [];
        public int Index { get; init; }

        public void StopTransition()
        {
            foreach (Glyph glyph in Glyphs) glyph.Fill.BeginAnimation(Brush.OpacityProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            size.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            size.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        public void Glide(Pose from, Pose to, TimeSpan wait, TimeSpan run, IEasingFunction travel, IEasingFunction swell, IEasingFunction fade)
        {
            foreach (Glyph glyph in Glyphs)
                glyph.Fill.BeginAnimation(Brush.OpacityProperty, Delayed(from.Opacity, to.Opacity, wait, run, fade));
            shift.BeginAnimation(TranslateTransform.YProperty, Delayed(from.Y, to.Y, wait, run, travel));
            size.BeginAnimation(ScaleTransform.ScaleXProperty, Delayed(from.ScaleX, to.ScaleX, wait, run, swell));
            size.BeginAnimation(ScaleTransform.ScaleYProperty, Delayed(from.ScaleY, to.ScaleY, wait, run, swell));
        }
    }

    sealed class Cluster(ContainerVisual visual, Piece[] pieces)
    {
        BlurEffect? _blur;

        public Piece[] Pieces => pieces;

        public void StopTransition()
        {
            _blur?.BeginAnimation(BlurEffect.RadiusProperty, null);
            _blur = null;
            visual.Effect = null;
            foreach (Piece piece in pieces) piece.StopTransition();
        }

        public void Haze(double from, double to, TimeSpan wait, TimeSpan run, IEasingFunction fade)
        {
            BlurEffect blur = _blur = new BlurEffect { Radius = from };
            visual.Effect = null;
            var change = new DoubleAnimation(from, to, run) { BeginTime = wait, EasingFunction = fade };
            change.CurrentStateInvalidated += (clock, _) =>
            {
                if (ReferenceEquals(_blur, blur) && ((Clock)clock!).CurrentState == ClockState.Active) visual.Effect = blur;
            };
            change.Completed += (_, _) =>
            {
                if (ReferenceEquals(visual.Effect, blur)) visual.Effect = null;
            };
            blur.BeginAnimation(BlurEffect.RadiusProperty, change);
        }
    }

    static readonly Pose Rest = new(1, 0, 1, 1, 0);
    static readonly Pose LetterBelow = new(0, LetterDrop, LetterShrink, LetterShrink, LetterBlur);
    static readonly Pose LetterAbove = new(0, -LetterRise, LetterShrink, LetterShrink, LetterBlur);
    static readonly IEasingFunction Sink = new CubicEase { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction Settle = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Launch = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction Land = new QuinticEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction TurnSine = new TurnEase(true) { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction TurnCosine = new TurnEase(false) { EasingMode = EasingMode.EaseIn };

    readonly VisualCollection _visuals;
    Cluster[] _clusters = [];
    readonly List<TranslateTransform> _partShifts = [];
    readonly List<Point> _partMiddles = [];
    Parting? _parting;
    int _pieceCount, _glyphCount;
    Size _size;
    LyricChange _change;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    Color _sung = Colors.White;
    Color _accent = Colors.LightSteelBlue;
    double _progress, _shown;
    LyricFx _textEffect, _appliedEffect;
    bool _leaving;
    LinearGradientBrush? _mask;

    public CompactLyric()
    {
        _visuals = new VisualCollection(this);
        RenderTransform = new TransformGroup { Children = { Offset, Centring } };
    }

    public string Text { get; private set; } = "";
    internal bool IsEntered { get; private set; }

    public TranslateTransform Offset { get; } = new();

    public TranslateTransform Centring { get; } = new();

    public double IslandWidth { get; set; }

    // Эффект «живого текста» из настроек; Off — строка без движения.
    internal LyricFx TextEffect
    {
        get => _textEffect;
        set
        {
            if (_textEffect == value) return;
            _textEffect = value;
            UpdateMask();
        }
    }

    public int EffectStrength { get; set; } = 1;

    public double Tempo { get; set; } = 1;

    // Заливка слева направо: только для текста песни с таймингом, не для названия трека.
    public bool Sweeps { get; set; }

    // Доля спетой строки (0..1): меняет цвет букв без перестройки визуалов.
    public double Progress
    {
        get => _progress;
        set
        {
            value = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (value == _progress || value is > 0 and < 1 && Math.Abs(value - _progress) < 0.0005) return;
            _progress = value;
        }
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size available) => _size;

    internal void Show(string text, Brush brush, double maxWidth, LyricChange change)
    {
        IsEntered = false;
        Text = text;
        string shown = Fit(text, maxWidth);
        FormattedText whole = Format(shown, brush);
        _size = new Size(whole.WidthIncludingTrailingWhitespace, whole.Height);
        _change = shown.Any(letter => letter is >= FirstJoinedLetter and <= LastJoinedLetter) ? LyricChange.Smooth : change;

        _sung = brush is SolidColorBrush solid ? solid.Color : Colors.White;
        _accent = LyricFxPaint.Accent(this);
        _progress = 0;
        _shown = 0;
        _appliedEffect = LyricFx.Off;
        _mask = Sweeps ? NewMask() : null;
        _visuals.Clear();
        _partShifts.Clear();
        _partMiddles.Clear();
        (int Start, int Length)[] parts = [.. Split(shown, _change)];
        (int Start, int Length)[] letters = [.. Split(shown, LyricChange.Wave)];
        _pieceCount = parts.Length;
        _glyphCount = letters.Length;
        int clusterSize = Math.Max((parts.Length + MaxClusters - 1) / MaxClusters, 1);
        _clusters = [.. parts.Chunk(clusterSize).Select(cluster => CreateCluster(whole, shown, cluster, letters, brush))];
        UpdateMask();
        foreach (Glyph glyph in AllGlyphs()) ApplyTint(glyph);
        _parting = _change == LyricChange.Wave ? new Parting([.. _partMiddles]) : null;
        InvalidateMeasure();
    }

    internal bool Part(Point? pointer, double dt, bool animate = true)
    {
        // Прогресс приходит редкими тиками: сглаживаем его по кадрам, иначе буквы поднимаются ступеньками.
        // Назад (новая строка) не сглаживаем: сразу ставим прогресс.
        double shown = _progress < _shown ? _progress : _shown + (_progress - _shown) * (1 - Math.Exp(-dt * 30));
        if (Math.Abs(shown - _progress) < 0.00001) shown = _progress;
        bool settling = shown != _shown;
        if (settling) { _shown = shown; UpdateMask(); }
        LyricFx effect = IsEntered && Sweeps ? TextEffect : LyricFx.Off;
        if (effect != LyricFx.Shimmer && (!animate || _progress >= 1)) effect = LyricFx.Off;
        bool movingEffect = effect is LyricFx.Lift or LyricFx.Glow;
        // Перелив зависит от прогресса и сохраняет оттенок на паузе и после конца строки.
        if (movingEffect || effect == LyricFx.Shimmer && (settling || effect != _appliedEffect))
            RefreshEffects(Clock.Elapsed.TotalSeconds);
        else if (effect == LyricFx.Off && _appliedEffect != LyricFx.Off && !_leaving) ResetEffects();
        _appliedEffect = effect;

        if (_parting == null) return movingEffect || settling;
        bool moving = _parting.Advance(pointer, dt);
        for (int i = 0; i < _parting.Count; i++)
        {
            Vector offset = _parting[i];
            _partShifts[i].X = offset.X;
            _partShifts[i].Y = offset.Y;
        }
        return moving || movingEffect || settling;
    }

    internal void SetAccent(Color color)
    {
        if (_accent == color) return;
        _accent = color;
        if (IsEntered && _appliedEffect == LyricFx.Shimmer)
            foreach (Glyph glyph in AllGlyphs()) ApplyTint(glyph);
    }

    internal void SetCaptionColor(Color color)
    {
        if (Sweeps || _sung == color) return;
        _sung = color;
        foreach (Glyph glyph in AllGlyphs())
            if (glyph.Fill is SolidColorBrush solid) solid.Color = color;
    }

    internal void CompleteShimmer()
    {
        if (!IsEntered || !Sweeps || TextEffect != LyricFx.Shimmer || _shown == 1) return;
        SnapProgress(1);
    }

    internal void SnapProgress(double progress)
    {
        _progress = _shown = Math.Clamp(progress, 0, 1);
        UpdateMask();
        if (_textEffect == LyricFx.Shimmer)
        {
            RefreshEffects(Clock.Elapsed.TotalSeconds);
            _appliedEffect = LyricFx.Shimmer;
        }
        else if (_appliedEffect != LyricFx.Off)
        {
            ResetEffects();
            _appliedEffect = LyricFx.Off;
        }
    }

    internal void StopTransitions(bool visible)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = visible ? 1 : 0;
        Offset.BeginAnimation(TranslateTransform.XProperty, null);
        Offset.BeginAnimation(TranslateTransform.YProperty, null);
        Offset.Y = 0;
        if (Effect is BlurEffect blur) blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        Effect = null;
        foreach (Cluster cluster in _clusters) cluster.StopTransition();
        _parting?.Reset();
        foreach (TranslateTransform shift in _partShifts) shift.X = shift.Y = 0;
    }

    IEnumerable<Piece> AllPieces() => _clusters.SelectMany(cluster => cluster.Pieces);

    IEnumerable<Glyph> AllGlyphs() => AllPieces().SelectMany(piece => piece.Glyphs);

    void RefreshEffects(double time)
    {
        foreach (Glyph glyph in AllGlyphs())
        {
            LyricFxLetter letter = LyricFxPlan.Letter(_shown, glyph.Center, _glyphCount, Tempo, glyph.Index);
            LyricFxFrame frame = LyricFxPlan.Sample(TextEffect, EffectStrength, time, letter, IsEntered);
            glyph.Effect.X = frame.X;
            glyph.Effect.Y = frame.Y;
            glyph.Shade = frame.Shade;
            glyph.Glow = frame.Glow;
            ApplyTint(glyph);
        }
    }

    void ResetEffects()
    {
        foreach (Glyph glyph in AllGlyphs())
        {
            glyph.Effect.X = 0;
            glyph.Effect.Y = 0;
            glyph.Shade = 0;
            glyph.Glow = 0;
            ApplyTint(glyph);
        }
    }

    // Цвет буквы: спетая часть с оттенком эффекта, остальное — та же непрозрачность маски.
    void ApplyTint(Glyph glyph)
    {
        if (glyph.Solid == null) return;
        // Перелив переводит спетую букву в цвет полосы трека, свечение подсвечивает её белым.
        Color color = Tint(_sung, _accent, glyph.Shade);
        glyph.Solid.Color = Tint(color, Colors.White, glyph.Glow * GlowHighlight);
    }

    // Одна маска на всю строку, координаты абсолютные (как в открытом острове): спетая часть непрозрачна,
    // неспетая — 0.6, граница 26 px. Маска общая, поэтому при смене прогресса меняются только стопы.
    LinearGradientBrush NewMask()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(Math.Max(_size.Width, 1), 0),
        };
        for (int i = 0; i < 4; i++) brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        return brush;
    }

    void UpdateMask()
    {
        if (_mask == null) return;
        double width = Math.Max(_size.Width, 1);
        double front = Math.Clamp(_shown, 0, 1) * width;
        bool shimmer = TextEffect == LyricFx.Shimmer;
        // У перелива яркость хвоста следует заливке, без общего высветления неспетого текста.
        double edge = shimmer ? Math.Min(SweepEdge, width - front) : SweepEdge;
        GradientStopCollection stops = _mask.GradientStops;
        stops[0].Color = Colors.Black;
        stops[0].Offset = 0;
        stops[1].Color = Colors.Black;
        stops[1].Offset = Math.Clamp((front - edge) / width, 0, 1);
        stops[2].Offset = Math.Clamp(front / width, 0, 1);
        stops[3].Offset = 1;
        // Остальные эффекты постепенно повышают непрозрачность в конце строки.
        const double liftStart = 0.85;
        double t = shimmer && _shown < 1 ? 0 : Math.Clamp((_shown - liftStart) / (1 - liftStart), 0, 1);
        t = t * t * (3 - 2 * t);
        byte alpha = (byte)(UnsungMaskAlpha + (255 - UnsungMaskAlpha) * t);
        stops[2].Color = Color.FromArgb(alpha, 0, 0, 0);
        stops[3].Color = Color.FromArgb(alpha, 0, 0, 0);
    }

    static Color Tint(Color from, Color to, double t) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    internal void Enter(bool immediate = false)
    {
        IsEntered = true;
        _leaving = false;
        if (immediate)
        {
            StopTransitions(visible: true);
            return;
        }
        BeginAnimation(OpacityProperty, null);
        Offset.BeginAnimation(TranslateTransform.YProperty, null);
        Effect = null;
        Opacity = 1;

        switch (_change)
        {
            case LyricChange.Wave:
                Stagger(LetterBelow, Rest, LetterEnterWaitMs, StepWithin(LetterEnterStepMs, LetterEnterSpreadMs), LetterEnterMs, Land, Land, Settle);
                break;
            case LyricChange.Drum:
                Stagger(WordEdgeOn(1), Rest, 0, WordStepMs, WordTurnMs, TurnCosine, TurnSine, TurnSine);
                break;
            default:
                this.AnimateBlur(LineBlur, 0, Ms(LineEnterFadeMs), Settle);
                Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(LineRise, 0, Ms(LineEnterMs)) { EasingFunction = Settle });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(LineEnterFadeMs)));
                break;
        }
    }

    internal void Leave(bool snap)
    {
        IsEntered = false;
        _leaving = !snap;
        if (snap)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            return;
        }

        switch (_change)
        {
            case LyricChange.Wave:
                Stagger(Rest, LetterAbove, 0, StepWithin(LetterLeaveStepMs, LetterLeaveSpreadMs), LetterLeaveMs, Launch, Launch, Launch);
                break;
            case LyricChange.Drum:
                Stagger(Rest, WordEdgeOn(-1), 0, WordStepMs, WordTurnMs, TurnSine, TurnCosine, TurnCosine);
                break;
            default:
                this.AnimateBlur(0, LineBlur, Ms(LineLeaveBlurMs), keep: true);
                Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-LineRise, Ms(LineLeaveMs)) { EasingFunction = Sink });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(LineLeaveFadeMs)));
                break;
        }
    }

    Pose WordEdgeOn(int side) => new(0, side * _size.Height / 2, 1, 0, 0);

    void Stagger(Pose from, Pose to, double waitMs, double stepMs, double runMs,
        IEasingFunction travel, IEasingFunction swell, IEasingFunction fade)
    {
        TimeSpan run = TimeSpan.FromMilliseconds(runMs);
        int started = 0;
        foreach (Cluster cluster in _clusters)
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(waitMs + started * stepMs);
            TimeSpan spread = TimeSpan.FromMilliseconds((cluster.Pieces.Length - 1) * stepMs);
            foreach (Piece piece in cluster.Pieces)
                piece.Glide(from, to, TimeSpan.FromMilliseconds(waitMs + started++ * stepMs), run, travel, swell, fade);
            if (from.Blur != to.Blur) cluster.Haze(from.Blur, to.Blur, wait, run + spread, fade);
        }
    }

    double StepWithin(double longestMs, double spreadMs) => Math.Min(longestMs, spreadMs / Math.Max(_pieceCount, 1));

    static IEnumerable<(int Start, int Length)> Split(string text, LyricChange change)
    {
        if (change == LyricChange.Drum) return Regex.Matches(text, @"\S+").Select(word => (word.Index, word.Length));
        if (change != LyricChange.Wave) return text.Length > 0 ? [(0, text.Length)] : [];

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return starts
            .Select((start, i) => (start, (i + 1 < starts.Length ? starts[i + 1] : text.Length) - start))
            .Where(letter => !char.IsWhiteSpace(text[letter.start]));
    }

    Cluster CreateCluster(FormattedText whole, string text, (int Start, int Length)[] parts, (int Start, int Length)[] letters, Brush brush)
    {
        var visual = new ContainerVisual();
        _visuals.Add(visual);
        return new Cluster(visual, [.. parts.Select(part => CreatePiece(visual, whole, text, part, letters, brush))]);
    }

    // Кусок анимации с отдельным визуалом на каждую букву: эффект сдвигает только свою букву,
    // а вход и выход по-прежнему двигают весь кусок.
    Piece CreatePiece(ContainerVisual cluster, FormattedText whole, string text, (int Start, int Length) part, (int Start, int Length)[] letters, Brush brush)
    {
        Rect box = Box(whole, part.Start, part.Length);
        var size = new ScaleTransform(1, 1, box.Left + box.Width / 2, _size.Height / 2);
        var shift = new TranslateTransform();
        var offset = new TranslateTransform();
        int index = _partMiddles.Count;
        _partShifts.Add(offset);
        _partMiddles.Add(new Point(box.Left + box.Width / 2, _size.Height / 2));
        var unit = new ContainerVisual { Transform = new TransformGroup { Children = { size, shift, offset } } };
        cluster.Children.Add(unit);
        var piece = new Piece(size, shift) { Index = index };
        for (int i = 0; i < letters.Length; i++)
        {
            (int start, int length) = letters[i];
            if (start < part.Start || start >= part.Start + part.Length) continue;
            Rect letter = Box(whole, start, length);
            SolidColorBrush? solid = Sweeps ? new SolidColorBrush(_sung) : null;
            Brush fill = solid ?? brush.Clone();
            var effect = new TranslateTransform();
            var glyph = new DrawingVisual { Transform = effect };
            TextOptions.SetTextHintingMode(glyph, TextHintingMode.Animated);
            using (DrawingContext dc = glyph.RenderOpen())
            {
                if (_mask != null) dc.PushOpacityMask(_mask);
                dc.DrawText(Format(text.Substring(start, length), fill), new Point(letter.Left, 0));
                if (_mask != null) dc.Pop();
            }
            unit.Children.Add(glyph);
            piece.Glyphs.Add(new Glyph
            {
                Fill = fill,
                Solid = solid,
                Effect = effect,
                Index = i,
                Center = _size.Width > 0 ? (letter.Left + letter.Width / 2) / _size.Width : 0.5,
            });
        }
        return piece;
    }

    static Rect Box(FormattedText whole, int start, int length) => whole.BuildHighlightGeometry(new Point(), start, length)?.Bounds ?? new Rect();

    string Fit(string text, double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || Format(text, Brushes.White).WidthIncludingTrailingWhitespace <= maxWidth) return text;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int kept = starts.Length - 1;
        string cut;
        do cut = text[..starts[kept]].TrimEnd() + Ellipsis;
        while (Format(cut, Brushes.White).WidthIncludingTrailingWhitespace > maxWidth && kept-- > 0);
        return cut;
    }

    FormattedText Format(string text, Brush fill) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
        FontSize, fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
