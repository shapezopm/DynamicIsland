using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class TrackShapeBar : FrameworkElement
{
    internal const int BarCount = 60;
    const double UnplayedOpacity = 0.42;
    const float Silence = 0.035f, TypicalLevel = 0.61f, MinimumRange = 0.04f;
    readonly float[] _profile = new float[BarCount];
    readonly GeometryGroup _shape = new();
    readonly RectangleGeometry[] _bars = new RectangleGeometry[BarCount];
    readonly RectangleGeometry _progressClip = new();
    readonly SolidColorBrush _lit = new(Colors.White), _dim = new(Colors.White) { Opacity = UnplayedOpacity };
    double _progress;

    public TrackShapeBar()
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new RectangleGeometry { RadiusX = 0.4, RadiusY = 0.4 };
            _shape.Children.Add(_bars[i]);
        }
    }

    internal void Tint(Color color)
    {
        if (color == _lit.Color) return;
        _lit.Color = _dim.Color = color;
    }

    internal void SetProgress(double fraction)
    {
        double progress = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        if (progress == _progress || progress is > 0 and < 1 && Math.Abs(progress - _progress) * ActualWidth < 0.05)
            return;
        _progress = progress;
        ClipProgress();
    }

    internal void Reset()
    {
        Array.Clear(_profile);
        SetProgress(0);
        UpdateBars();
    }

    internal void SetProfile(ReadOnlySpan<float> levels)
    {
        if (levels.Length != BarCount) return;
        levels.CopyTo(_profile);
        UpdateBars();
    }

    internal static float[] ShapeLevels(ReadOnlySpan<float> levels)
    {
        var output = new float[BarCount];
        if (levels.Length != BarCount) return output;
        Span<float> audible = stackalloc float[BarCount];
        int count = 0;
        foreach (float level in levels)
            if (level > Silence) audible[count++] = level;
        if (count == 0) return output;

        audible = audible[..count];
        audible.Sort();
        float median = (audible[(count - 1) / 2] + audible[count / 2]) / 2;
        float quietRange = Math.Max(MinimumRange, median - audible[(count - 1) / 10]);
        float loudRange = Math.Max(MinimumRange, audible[(count - 1) * 9 / 10] - median);

        for (int i = 0; i < levels.Length; i++)
        {
            float level = levels[i];
            if (level <= Silence) continue;

            // Use the track's typical loudness as the baseline and preserve small local dips.
            float difference = level - median;
            float contrast = difference < 0
                ? 0.38f * Math.Max(-1.4f, difference / quietRange)
                : 0.27f * Math.Min(1.4f, difference / loudRange);
            float left = levels[Math.Max(0, i - 1)];
            float right = levels[Math.Min(levels.Length - 1, i + 1)];
            float valley = Math.Clamp((Math.Min(left, right) - level) * 2.6f, 0, 0.16f);
            output[i] = Math.Clamp(TypicalLevel + contrast - valley, 0.05f, 1f);
        }
        return output;
    }

    internal static float[] ApproximateLevels(TrackSnapshot track)
    {
        byte[] key = Encoding.UTF8.GetBytes(string.Join('\u001f', track.Title, track.Artist, track.Album));
        var random = new Random(BinaryPrimitives.ReadInt32LittleEndian(SHA256.HashData(key)));
        var levels = new float[BarCount];
        const int section = 5;
        float from = random.NextSingle(), to = random.NextSingle();
        for (int i = 0; i < levels.Length; i++)
        {
            float position = (i % section) / (float)section;
            float envelope = from + (to - from) * position;
            levels[i] = 0.22f + 0.40f * envelope + 0.24f * random.NextSingle();
            if (i % section == section - 1)
            {
                from = to;
                to = random.NextSingle();
            }
        }
        return levels;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateBars();
        ClipProgress();
    }

    void UpdateBars()
    {
        var area = new Rect(RenderSize);
        for (int i = 0; i < BarCount; i++)
            _bars[i].Rect = BarBounds(area, i, BarCount, _profile[i]);
    }

    void ClipProgress() =>
        _progressClip.Rect = new Rect(0, 0, ActualWidth * _progress, ActualHeight);

    protected override void OnRender(DrawingContext dc) => DrawProgress(dc, _shape, _progressClip, _lit, _dim);

    internal static void Draw(DrawingContext dc, Rect area, ReadOnlySpan<float> levels, double progress, Brush lit, Brush dim)
    {
        if (area.Width <= 0 || area.Height <= 0 || levels.IsEmpty) return;
        var shape = new GeometryGroup();
        for (int i = 0; i < levels.Length; i++)
            shape.Children.Add(new RectangleGeometry(BarBounds(area, i, levels.Length, levels[i]), 0.4, 0.4));
        var clip = new RectangleGeometry(new Rect(area.X, area.Y, area.Width * progress, area.Height));
        DrawProgress(dc, Shades.Frozen(shape), Shades.Frozen(clip), lit, dim);
    }

    static Rect BarBounds(Rect area, int index, int count, double level)
    {
        double step = area.Width / count;
        double width = step * 0.57;
        double minimum = Math.Min(2, area.Height);
        double height = minimum + (Math.Min(26, area.Height) - minimum) * level;
        double x = area.X + (index + 0.5) * step;
        return new Rect(x - width / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    static void DrawProgress(DrawingContext dc, Geometry shape, RectangleGeometry clip, Brush lit, Brush dim)
    {
        dc.DrawGeometry(dim, null, shape);
        dc.PushClip(clip);
        dc.DrawGeometry(lit, null, shape);
        dc.Pop();
    }
}
