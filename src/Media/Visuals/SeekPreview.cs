using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

enum SeekPreviewKind { TrackShape, Lines, Wave, Magnifier, Lift, Ripple }

// Анимированная иллюстрация режима полосы трека или эффекта под курсором для плиток настроек.
// Кадры идут только пока плитка видна: подписка на Rendering снимается при скрытии.
sealed class SeekPreview : FrameworkElement
{
    const double Thickness = 6, Padding = 14, KnobRadius = 4;
    // Волна — те же размеры, что у реальной полосы (WaveBar): длина 32, штрих 3, фаза 4.5 рад/с.
    const double WavelengthPx = 32, WaveAmplitude = 2.6, StrokePx = 3;
    const double KnobWidth = 3, KnobHeight = 14, KnobCorner = 1.5, KnobGap = 2;
    const double WaveDimOpacity = 0.45;
    const double ProgressSpeed = 0.7, SweepSpeed = 0.9, WavePhaseSpeed = 4.5, RippleSpeed = 5;
    // Пружина «волна ↔ линия»: тот же критически затухающий отклик, что в WaveBar (рост ~0.43 с, спад ~0.35 с).
    const double RiseStiffness = 182, RiseDamping = 27, FallStiffness = 275, FallDamping = 33;
    const double PlayFrames = 2, PauseFrames = 2, PlayCycle = PlayFrames + PauseFrames;
    static readonly Brush Lit = Brushes.White;
    static readonly Brush Dim = Shades.White(0.25);
    static readonly Brush TipBack = Shades.Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x24, 0x24, 0x24)));
    static readonly Pen LitPen = Shades.Frozen(new Pen(Brushes.White, StrokePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen DimPen = Shades.Frozen(new Pen(Dim, StrokePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen WaveDimPen = Shades.Frozen(new Pen(Shades.White(WaveDimOpacity), StrokePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly float[] TrackExample =
        [0.16f, 0.44f, 0.59f, 0.35f, 0.81f, 0.69f, 0.42f, 0.12f, 0.22f, 0.39f, 0.71f, 0.52f, 0.73f, 0.46f, 0.58f, 0.77f, 0.49f, 0.33f];

    double _time;
    TimeSpan? _lastFrame;
    readonly Spring _energy = new(0, RiseStiffness, RiseDamping);

    public SeekPreview()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Subscribe();
            else Unsubscribe();
        };
        Unloaded += (_, _) => Unsubscribe();
    }

    public SeekPreviewKind Kind { get; set; }

    void Subscribe()
    {
        _lastFrame = null;
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
    }

    void Unsubscribe()
    {
        CompositionTarget.Rendering -= OnFrame;
        _lastFrame = null;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastFrame is TimeSpan last ? Math.Clamp((now - last).TotalSeconds, 0, 0.05) : 0;
        _lastFrame = now;
        _time += dt;
        // Демо-цикл для плитки «Волна»: 2 с играет (волна растёт), 2 с пауза (выравнивается).
        bool playing = _time % PlayCycle < PlayFrames;
        _energy.Target = playing ? 1 : 0;
        if (playing) _energy.Tune(RiseStiffness, RiseDamping);
        else _energy.Tune(FallStiffness, FallDamping);
        _energy.Advance(dt);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double left = Padding, right = w - Padding, middle = h / 2;
        // Прогресс плавно ходит туда-обратно; курсор «ездит» по полосе для режимов под курсором.
        double fraction = 0.5 + 0.35 * Math.Sin(_time * ProgressSpeed);
        double progress = left + (right - left) * fraction;
        double pointer = left + (right - left) * (0.5 + 0.36 * Math.Sin(_time * SweepSpeed + 1.3));
        switch (Kind)
        {
            case SeekPreviewKind.TrackShape:
                TrackShapeBar.Draw(dc, new Rect(left, 0, Math.Max(0, right - left), h), TrackExample, fraction, Lit, Dim);
                break;
            case SeekPreviewKind.Lines: Plain(dc, left, right, middle, progress); Ticks(dc, left, right, middle); break;
            case SeekPreviewKind.Wave: WavePreview(dc, left, right, middle, progress, h); break;
            case SeekPreviewKind.Magnifier: Bulge(dc, left, right, middle, pointer); break;
            case SeekPreviewKind.Lift: Lift(dc, left, right, middle, pointer); break;
            default: Ripple(dc, left, right, middle, pointer); break;
        }
    }

    static double Gauss(double d, double spread) => Math.Exp(-d * d / (2 * spread * spread));

    static void Plain(DrawingContext dc, double left, double right, double middle, double knob)
    {
        dc.DrawGeometry(Dim, null, Band(left, right, middle, _ => Thickness));
        dc.DrawGeometry(Lit, null, Band(left, knob, middle, _ => Thickness));
        dc.DrawEllipse(Lit, null, new Point(knob, middle), KnobRadius, KnobRadius);
    }

    static void Ticks(DrawingContext dc, double left, double right, double middle)
    {
        for (int i = 1; i < 6; i++)
            dc.DrawEllipse(Dim, null, new Point(left + (right - left) * i / 6, middle - Thickness / 2 - 5), 1.2, 1.2);
    }

    void WavePreview(DrawingContext dc, double left, double right, double middle, double knob, double height)
    {
        // Непройденная часть — ровная приглушённая линия.
        double restStart = knob + KnobWidth / 2 + KnobGap;
        if (restStart < right) dc.DrawLine(WaveDimPen, new Point(restStart, middle), new Point(right, middle));

        // Пройденная часть — волна, обрезанная перед маркером.
        double waveEnd = knob - KnobWidth / 2 - KnobGap;
        if (waveEnd > left)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, waveEnd, height)));
            dc.DrawGeometry(null, LitPen, WaveShape(left, right, middle));
            dc.Pop();
        }

        // Маркер — вертикальная скруглённая капсула.
        dc.DrawRoundedRectangle(Lit, null,
            new Rect(knob - KnobWidth / 2, middle - KnobHeight / 2, KnobWidth, KnobHeight), KnobCorner, KnobCorner);
    }

    double WaveY(double x, double middle) =>
        middle + WaveAmplitude * _energy.Value * Math.Sin(x / WavelengthPx * Math.Tau + _time * WavePhaseSpeed);

    Geometry WaveShape(double left, double right, double middle)
    {
        var shape = new StreamGeometry();
        using (StreamGeometryContext outline = shape.Open())
        {
            outline.BeginFigure(new Point(left, WaveY(left, middle)), false, false);
            for (double x = left + 1; x <= right; x += 1)
                outline.LineTo(new Point(x, WaveY(x, middle)), true, false);
        }
        shape.Freeze();
        return shape;
    }

    static void Bulge(DrawingContext dc, double left, double right, double middle, double pointer)
    {
        Func<double, double> bulge = x => Thickness + 7 * Gauss(x - pointer, 14);
        dc.DrawGeometry(Dim, null, Band(left, right, middle, bulge));
        dc.DrawGeometry(Lit, null, Band(left, pointer, middle, bulge));
        dc.DrawEllipse(Lit, null, new Point(pointer, middle), KnobRadius, KnobRadius);
    }

    static void Lift(DrawingContext dc, double left, double right, double middle, double pointer)
    {
        Func<double, double> rise = x => Thickness + 9 * Gauss(x - pointer, 5);
        dc.DrawGeometry(Dim, null, Band(left, right, middle, rise));
        dc.DrawGeometry(Lit, null, Band(left, pointer, middle, rise));
        dc.DrawRoundedRectangle(TipBack, null, new Rect(pointer - 16, middle - Thickness / 2 - 18, 32, 11), 4, 4);
    }

    void Ripple(DrawingContext dc, double left, double right, double middle, double pointer)
    {
        Func<double, double> swell = x =>
        {
            double d = x - pointer;
            return Thickness + 6 * Gauss(d, 18) * (0.5 + 0.5 * Math.Cos(d / 4.5 - _time * RippleSpeed));
        };
        dc.DrawGeometry(Dim, null, Band(left, right, middle, swell));
        dc.DrawGeometry(Lit, null, Band(left, pointer, middle, swell));
        dc.DrawEllipse(Lit, null, new Point(pointer, middle), KnobRadius, KnobRadius);
    }

    // Полоса переменной толщины между x0 и x1, центрированная по middle.
    static Geometry Band(double x0, double x1, double middle, Func<double, double> thickness)
    {
        var band = new StreamGeometry();
        using (StreamGeometryContext outline = band.Open())
        {
            outline.BeginFigure(new Point(x0, middle - thickness(x0) / 2), true, true);
            for (double x = x0 + 1; x <= x1; x += 1)
                outline.LineTo(new Point(x, middle - thickness(x) / 2), true, false);
            for (double x = x1; x >= x0; x -= 1)
                outline.LineTo(new Point(x, middle + thickness(x) / 2), true, false);
        }
        band.Freeze();
        return band;
    }
}
