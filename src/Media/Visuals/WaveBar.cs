using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

// "Волна": плавная синусоида вдоль трека. Пройденная часть яркая, в позиции прогресса — кружок.
// Под курсором волна реагирует по выбранному режиму «Под курсором»: Лупа — вспухает вокруг курсора,
// Строка — острый подъём в точке курсора, Волна — круги расходятся от курсора.
public sealed class WaveBar : FrameworkElement
{
    // Размеры сняты с видео (45 fps) и пересчитаны на бар 260 px: длина волны ≈ 1/8 бара.
    const double WavelengthPx = 32;
    const double FullAmplitudePx = 4;
    const double StrokePx = 3.0;
    const double KnobWidth = 3, KnobHeight = 14, KnobCorner = 1.5, KnobGap = 2;
    const double PhaseSpeed = 4.5, RippleSpeed = 7.0;
    const double LiftRate = 4.0, FollowRate = 18.0;
    const double SampleStep = 2;
    const double UnplayedOpacity = 0.45;

    Color _tint = Colors.White;
    Pen _litPen = null!, _dimPen = null!;
    Brush _knob = Brushes.White;

    public WaveBar() => Retint();

    // Цвет полосы: пройденная часть, маркер и (приглушённо) непройденная линия.
    public Color Tint
    {
        get => _tint;
        set
        {
            if (value == _tint) return;
            _tint = value;
            Retint();
            InvalidateVisual();
        }
    }

    void Retint()
    {
        Brush lit = Shades.Frozen(new SolidColorBrush(_tint));
        Brush dim = Shades.Frozen(new SolidColorBrush(Color.FromArgb((byte)(255 * UnplayedOpacity), _tint.R, _tint.G, _tint.B)));
        _litPen = Shades.Frozen(new Pen(lit, StrokePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        _dimPen = Shades.Frozen(new Pen(dim, StrokePx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        _knob = lit;
    }

    double _fill, _thickness = 10, _phase, _ripple, _lift, _pointerShown;
    double _energy = 1;
    SeekHover _hover = SeekHover.Magnifier;

    // 0 — ровная линия (пауза), 1 — полная волна (играет). Меняется плавно извне.
    public double Energy
    {
        get => _energy;
        set => _energy = Math.Clamp(value, 0, 1);
    }

    public double Fill
    {
        get => _fill;
        set
        {
            if (value == _fill) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            if (value == _thickness) return;
            _thickness = value;
            InvalidateVisual();
        }
    }

    internal void Advance(double dt, double? pointer, SeekHover hover)
    {
        _hover = hover;
        // Фаза замедляется вместе с амплитудой: при паузе волна гаснет на месте, а не «уезжает».
        _phase = (_phase + dt * PhaseSpeed * _energy) % Math.Tau;
        _ripple = (_ripple + dt * RippleSpeed) % Math.Tau;
        _lift = Approach(_lift, pointer.HasValue ? 1 : 0, dt * LiftRate);
        if (pointer is double x) _pointerShown += (x - _pointerShown) * Math.Min(1, dt * FollowRate);
        else if (_pointerShown == 0) _pointerShown = ActualWidth / 2;
        InvalidateVisual();
    }

    static double Approach(double value, double target, double step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    static double Gauss(double d, double spread) => Math.Exp(-d * d / (2 * spread * spread));

    // 0..1: множитель амплитуды к концу трека (ставит MainWindow.Seek.cs каждый кадр). Меняет только волну.
    public double Taper { get; set; } = 1;

    double Amplitude => FullAmplitudePx * _energy * Taper;

    // Вклад курсора в амплитуду в точке x, уже с учётом плавного появления (_lift).
    double PointerBoost(double x)
    {
        double d = x - _pointerShown;
        double shape = _hover switch
        {
            SeekHover.Lift => 2.2 * Gauss(d, 7),
            SeekHover.Wave => 0.9 * Gauss(d, 46) * Math.Cos(d * 0.22 - _ripple),
            _ => 1.4 * Gauss(d, 24),
        };
        return _lift * shape;
    }

    double WaveY(double x, double middle) =>
        middle + Amplitude * (1 + PointerBoost(x)) * Math.Sin(x / WavelengthPx * Math.Tau + _phase);

    Geometry WaveShape(double from, double to, double middle)
    {
        var shape = new StreamGeometry();
        using (StreamGeometryContext outline = shape.Open())
        {
            outline.BeginFigure(new Point(from, WaveY(from, middle)), false, false);
            for (double x = from + SampleStep; x < to; x += SampleStep)
                outline.LineTo(new Point(x, WaveY(x, middle)), true, false);
            outline.LineTo(new Point(to, WaveY(to, middle)), true, false);
        }
        shape.Freeze();
        return shape;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double middle = height / 2;
        double fill = Math.Clamp(_fill, 0, width);
        double knobLeft = fill - KnobWidth / 2;
        // Концы штриха круглые и радиусом StrokePx/2 выходят за границу, поэтому концы волны
        // и линии сдвинуты внутрь на этот радиус, иначе кружки на концах обрезаются.
        double inset = StrokePx / 2;
        double waveEnd = Math.Max(0, knobLeft - KnobGap);

        // Непройденная часть — ровная тусклая линия, без волны.
        double restStart = fill + KnobWidth / 2 + KnobGap;
        if (restStart < width - inset)
            dc.DrawLine(_dimPen, new Point(restStart, middle), new Point(width - inset, middle));

        // Пройденная часть — волна от левого края до маркера, оба конца закруглены штрихом.
        if (waveEnd > inset)
            dc.DrawGeometry(null, _litPen, WaveShape(inset, waveEnd, middle));

        // Маркер позиции — вертикальная скруглённая капсула по центру.
        dc.DrawRoundedRectangle(_knob, null,
            new Rect(knobLeft, middle - KnobHeight / 2, KnobWidth, KnobHeight), KnobCorner, KnobCorner);
    }
}
