using System.Windows;

namespace DynamicIsland;

// One press has one owner. Once moved, returning to the start cannot become a click.
internal sealed class WeatherGesture
{
    internal enum Surface { Page, Hours, Days }
    internal enum Motion { Pending, Close, City, Hours, Days, Keep }
    internal const double MoveThreshold = 6, ClickSeconds = 0.35;
    internal bool Active { get; private set; }
    internal Vector Delta { get; private set; }
    internal Motion Kind { get; private set; }
    Point _start;
    double _started, _farthest;
    Surface _surface;

    internal void Begin(Point point, double seconds, Surface surface)
    {
        _start = point; _started = seconds; _surface = surface;
        _farthest = 0; Delta = default; Kind = Motion.Pending; Active = true;
    }

    internal void Move(Point point)
    {
        if (!Active) return;
        Delta = point - _start;
        _farthest = Math.Max(_farthest, Delta.Length);
        if (Kind != Motion.Pending || _farthest < MoveThreshold) return;
        double x = Math.Abs(Delta.X), y = Math.Abs(Delta.Y);
        if (x > y * 1.2)
            Kind = _surface == Surface.Hours ? Motion.Hours : Motion.City;
        else if (y > x * 1.2)
            Kind = _surface == Surface.Days ? Motion.Days : Motion.Keep;
    }

    internal Motion End(Point point, double seconds)
    {
        if (!Active) return Motion.Keep;
        Move(point);
        Active = false;
        if (_farthest < MoveThreshold && seconds >= _started && seconds - _started < ClickSeconds)
            return Motion.Close;
        return Kind == Motion.Pending ? Motion.Keep : Kind;
    }

    internal void Cancel() { Active = false; Kind = Motion.Keep; }
}
