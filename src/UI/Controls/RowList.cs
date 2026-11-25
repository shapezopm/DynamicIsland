using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class RowList : StackPanel
{
    const double Radius = 12;
    const double HoverOpacity = 0.12, PressOpacity = 0.21;
    const double PressInsetX = 5, PressInsetY = 2;
    const double GlassBackInsetX = 7, GlassBackInsetY = 3;
    const double AppearSquish = 0.6;
    const double HiddenBelow = 0.05;

    public static readonly DependencyProperty IsBackActionProperty = DependencyProperty.RegisterAttached(
        "IsBackAction", typeof(bool), typeof(RowList), new PropertyMetadata(false));

    public static bool GetIsBackAction(DependencyObject row) => (bool)row.GetValue(IsBackActionProperty);
    public static void SetIsBackAction(DependencyObject row, bool value) => row.SetValue(IsBackActionProperty, value);

    readonly RectangleGeometry _shape = new() { RadiusX = Radius, RadiusY = Radius };
    readonly SolidColorBrush _fill = new(Colors.White) { Opacity = 0 };
    readonly LinearGradientBrush _glassFill = new(
        Color.FromArgb(32, 255, 255, 255), Color.FromArgb(14, 255, 255, 255), 90) { Opacity = 0 };

    readonly Spring _top = new(0), _bottom = new(0);
    readonly Spring _shown = new(0), _squish = new(0);
    readonly Spring _glassBack = new(0, 420, 38);
    readonly FrameLoop _loop;
    bool _lit, _pressed;

    public RowList()
    {
        _loop = new FrameLoop(Advance);
        AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, _) => UpdateHighlight()), true);
        AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => UpdateHighlight()), true);
        MouseEnter += (_, _) => UpdateHighlight();
        MouseLeave += (_, _) => UpdateHighlight();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) Reset();
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawGeometry(_fill, null, _shape);
        dc.DrawGeometry(_glassFill, null, _shape);
    }

    public void FollowPointer()
    {
        Mouse.Synchronize();
        UpdateHighlight();
    }

    void UpdateHighlight()
    {
        ButtonBase? row = InternalChildren.OfType<ButtonBase>().FirstOrDefault(button => button.IsMouseOver);
        if (row != null) MoveTo(row);

        bool lit = row != null, pressed = row is { IsPressed: true };
        if (lit != _lit) _shown.Tune(lit ? 420 : 90, lit ? 41 : 19);
        if (pressed != _pressed) _squish.Tune(pressed ? 700 : 380, pressed ? 44 : 16);
        _lit = lit;
        _pressed = pressed;
        _shown.Target = lit ? 1 : 0;
        _squish.Target = pressed ? 1 : 0;
        _loop.Start();
    }

    void MoveTo(ButtonBase row)
    {
        double top = VisualTreeHelper.GetOffset(row).Y, bottom = top + row.RenderSize.Height;
        _glassBack.Target = Settings.Glass && GetIsBackAction(row) ? 1 : 0;
        if (_shown.Value < HiddenBelow)
        {
            _top.Snap(top);
            _bottom.Snap(bottom);
            _glassBack.Snap(_glassBack.Target);
            _squish.Value = AppearSquish;
            return;
        }

        if (top != _top.Target)
        {
            bool down = top > _top.Target;
            (down ? _bottom : _top).Tune(560, 36);
            (down ? _top : _bottom).Tune(230, 26);
        }
        _top.Target = top;
        _bottom.Target = bottom;
    }

    void Reset()
    {
        _loop.Stop();
        _lit = _pressed = false;
        _shown.Snap(0);
        _squish.Snap(0);
        _glassBack.Snap(0);
        ApplyHighlight();
    }

    bool Advance(double dt)
    {
        bool moving = _top.Advance(dt);
        moving |= _bottom.Advance(dt);
        moving |= _shown.Advance(dt);
        moving |= _squish.Advance(dt);
        moving |= _glassBack.Advance(dt);
        ApplyHighlight();
        return moving;
    }

    void ApplyHighlight()
    {
        double glass = Settings.Glass ? Math.Clamp(_glassBack.Value, 0, 1) : 0;
        double x = PressInsetX * _squish.Value + GlassBackInsetX * glass;
        double y = PressInsetY * _squish.Value + GlassBackInsetY * glass;
        _shape.Rect = new Rect(x, _top.Value + y,
            Math.Max(ActualWidth - x * 2, 0), Math.Max(_bottom.Value - _top.Value - y * 2, 0));
        double shown = Math.Clamp(_shown.Value, 0, 1), pressed = Math.Clamp(_squish.Value, 0, 1);
        _fill.Opacity = shown * (1 - glass) * (HoverOpacity + (PressOpacity - HoverOpacity) * pressed);
        _glassFill.Opacity = shown * glass * (0.85 + 0.15 * pressed);
    }
}
