using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static DynamicIsland.Motion;

namespace DynamicIsland;

// Footer indicator and page switching.
// Arrow = the home page (white when it is shown); dots = the other pages (the shown one is white and grows).
// The weather card follows the finger when dragged sideways like one window; on release it either goes to the
// next page or springs back. A tap on the arrow or a dot switches to that page; the page shown is left alone.
public partial class MainWindow
{
    const double WeatherSwipeDistance = 70, WeatherSlideWidth = 540, WeatherDotIdle = 5, WeatherDotActive = 7;
    static readonly Color WeatherInactive = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    static readonly IEasingFunction WeatherEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction WeatherOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    readonly List<Ellipse> _weatherDots = [];
    readonly List<Border> _weatherDotHits = [];
    readonly List<SolidColorBrush> _weatherDotBrushes = [];
    readonly TranslateTransform _contentMove = new();
    readonly Spring _weatherPageSpring = new(0, 260, 30);
    Image? _weatherPageOverlay;
    TranslateTransform? _weatherPageCarry;
    int _weatherPageDirection;
    SolidColorBrush? _arrowBrush;
    readonly WeatherGesture _weatherGesture = new();
    double _weatherGestureHours, _weatherGestureDays, _weatherGestureOffset;
    bool _weatherListShown;

    // The page's place: null for the automatic location, otherwise the home city, a saved city or the selected city.
    WeatherPlace? PlaceFor(string id)
    {
        if (id == "auto") return null;
        if (_weatherCities.FirstOrDefault(city => WeatherWebData.Id(city) == id) is { } saved) return saved;
        if (WeatherLocation.Read(Settings.WeatherHome) is { } home && WeatherWebData.Id(home) == id) return home;
        return WeatherLocation.Read(Settings.WeatherPlace) is { } selected && WeatherWebData.Id(selected) == id ? selected : null;
    }

    // The states follow the page that is really shown (_weatherSelectedId), including after a swipe.
    void RefreshWeatherDots()
    {
        bool homeFirst = HomeFirst;
        var extras = _weatherPages.Skip(homeFirst ? 1 : 0).ToList();
        if (_weatherDots.Count != extras.Count)
        {
            WeatherDots.Children.Clear();
            _weatherDots.Clear();
            _weatherDotHits.Clear();
            _weatherDotBrushes.Clear();
            for (int i = 0; i < extras.Count; i++)
            {
                var brush = new SolidColorBrush(WeatherInactive);
                var dot = new Ellipse
                {
                    Width = WeatherDotActive, Height = WeatherDotActive, Fill = brush, IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(WeatherDotIdle / WeatherDotActive, WeatherDotIdle / WeatherDotActive),
                };
                var hit = new WeatherActionBorder { Width = 13, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = dot };
                WireWeatherAction(hit, "Перейти к городу", () => GoToWeatherPage((string)hit.Tag!));
                Claim(hit);
                hit.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    GoToWeatherPage((string)hit.Tag!);
                };
                _weatherDots.Add(dot);
                _weatherDotBrushes.Add(brush);
                _weatherDotHits.Add(hit);
                WeatherDots.Children.Add(hit);
            }
        }
        for (int i = 0; i < _weatherDots.Count; i++)
        {
            bool on = extras[i].Id == _weatherSelectedId;
            _weatherDotHits[i].Tag = extras[i].Id;
            System.Windows.Automation.AutomationProperties.SetName(_weatherDotHits[i], $"{extras[i].Name}{(on ? ", выбран" : "")}");
            AnimateWeatherColor(_weatherDotBrushes[i], on ? Colors.White : WeatherInactive);
            double scale = on ? 1 : WeatherDotIdle / WeatherDotActive;
            var transform = (ScaleTransform)_weatherDots[i].RenderTransform;
            if (SystemParameters.ClientAreaAnimation)
            {
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, Ms(180)));
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, Ms(180)));
            }
            else
            {
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transform.ScaleX = transform.ScaleY = scale;
            }
        }
        _arrowBrush ??= new SolidColorBrush(WeatherInactive);
        WeatherAutoIcon.Fill = _arrowBrush;
        bool homeActive = homeFirst && _weatherPages.Count > 0 && _weatherPages[0].Id == _weatherSelectedId;
        AnimateWeatherColor(_arrowBrush, homeActive ? Colors.White : WeatherInactive);
        WeatherDots.Visibility = extras.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WeatherCityName.Visibility = _weatherPages.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void AnimateWeatherColor(SolidColorBrush brush, Color to)
    {
        if (SystemParameters.ClientAreaAnimation)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Ms(160)) { EasingFunction = WeatherEase });
        else
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
        }
    }

    // Arrow and dots: the page that is already shown is left alone, any other page is switched to.
    void GoToWeatherPage(string id)
    {
        if (id == _weatherSelectedId) return;
        int current = _weatherPages.FindIndex(page => page.Id == _weatherSelectedId);
        int next = _weatherPages.FindIndex(page => page.Id == id);
        if (next < 0) return;
        SwitchWeatherPage(next, next > current ? 1 : -1);
    }

    // The card leaves to the side of the step and the new place comes in from the other side, under the same spring.
    // The page as it is now becomes a picture: it is carried away on the spring from where the finger left it,
    // while the new page comes in on the same spring from the other side. One movement, no pause for loading.
    void SwitchWeatherPage(int next, int direction)
    {
        string id = _weatherPages[next].Id;
        double from = _contentMove.X;
        StopWeatherPageTransition();
        _contentMove.BeginAnimation(TranslateTransform.XProperty, null);
        _contentMove.X = 0;
        if (!SystemParameters.ClientAreaAnimation)
        {
            _weatherPageSpring.Snap(0);
            Safely(() => SelectWeatherCityAsync(id));
            return;
        }
        var picture = CapturePage();
        var carry = new TranslateTransform(from, 0);
        var overlay = new Image
        {
            Source = picture, Width = 522, Height = 452, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = carry,
        };
        WeatherView.Children.Insert(WeatherView.Children.IndexOf(WeatherContent) + 1, overlay);
        _weatherPageOverlay = overlay;
        _weatherPageCarry = carry;
        _weatherPageDirection = direction;
        _weatherPageSpring.Snap(from + direction * WeatherSlideWidth);
        _weatherPageSpring.Target = 0;
        ApplyWeatherPageTransition();
        StartShapeLoop();
        Safely(() => SelectWeatherCityAsync(id));
    }

    void ApplyWeatherPageTransition()
    {
        if (_weatherPageOverlay is null || _weatherPageCarry is null)
        {
            if (!_weatherGesture.Active) _contentMove.X = _weatherPageSpring.Value;
            return;
        }
        _contentMove.X = _weatherPageSpring.Value;
        _weatherPageCarry.X = _weatherPageSpring.Value - _weatherPageDirection * WeatherSlideWidth;
        if (Math.Abs(_weatherPageSpring.Value) < 0.005 && Math.Abs(_weatherPageSpring.Velocity) < 0.05)
            StopWeatherPageTransition();
    }

    void StopWeatherPageTransition()
    {
        if (_weatherPageOverlay is { } overlay) WeatherView.Children.Remove(overlay);
        _weatherPageOverlay = null;
        _weatherPageCarry = null;
        _weatherPageSpring.Snap(_contentMove.X);
    }

    BitmapSource CapturePage()
    {
        var bitmap = new RenderTargetBitmap(522, 452, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(WeatherContent);
        bitmap.Freeze();
        return bitmap;
    }

    // The card springs back to its place when the drag was too short.
    void BackWeatherContent()
    {
        if (SystemParameters.ClientAreaAnimation)
        {
            _weatherPageSpring.Snap(_contentMove.X);
            _weatherPageSpring.Target = 0;
            StartShapeLoop();
        }
        else
        {
            _contentMove.BeginAnimation(TranslateTransform.XProperty, null);
            _contentMove.X = 0;
        }
    }

    void WireWeatherSwipe()
    {
        WeatherContent.RenderTransform = _contentMove;
        BuildWeatherScreens();
        Claim(WeatherHomeHit);
        WireWeatherAction(WeatherHomeHit, "Текущее место", () => { if (HomeFirst) GoToWeatherPage(_weatherPages[0].Id); });
        WeatherHomeHit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (HomeFirst) GoToWeatherPage(_weatherPages[0].Id);
        };
        Root.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) { CancelWeatherGesture(); return; }
            if (!CanStartWeatherGesture(e.OriginalSource)) return;
            BeginWeatherGesture(e.OriginalSource, e.GetPosition(WeatherView), _clock.Elapsed.TotalSeconds);
            if (!Root.CaptureMouse()) { CancelWeatherGesture(); return; }
            e.Handled = true;
        };
        Root.PreviewMouseMove += (_, e) =>
        {
            if (!_weatherGesture.Active) return;
            MoveWeatherGesture(e.GetPosition(WeatherView));
            e.Handled = true;
        };
        Root.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || !_weatherGesture.Active) return;
            EndWeatherGesture(e.GetPosition(WeatherView), _clock.Elapsed.TotalSeconds);
            e.Handled = true;
        };
        Root.LostMouseCapture += (_, _) =>
        {
            if (_weatherGesture.Active && !Root.IsMouseCaptured) CancelWeatherGesture();
        };
        Deactivated += (_, _) => CancelWeatherGesture();
    }

    bool CanStartWeatherGesture(object source)
    {
        if (_panel != Panel.Weather || WeatherSubpageOpen || _weatherPan is not null || _weatherGesture.Active) return false;
        // Buttons, dots and text editors keep their own normal press/release behavior.
        if (WithinWeatherElement(source, WeatherFooter) || WithinWeatherElement(source, WeatherNoticeAction)) return false;
        for (DependencyObject? node = source as DependencyObject; node is not null; node = WeatherParent(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.ScrollBar) return false;
        return true;
    }

    void BeginWeatherGesture(object source, Point point, double seconds)
    {
        var surface = WithinWeatherElement(source, WeatherStrip) ? WeatherGesture.Surface.Hours
            : WithinWeatherElement(source, WeatherDaysView) ? WeatherGesture.Surface.Days : WeatherGesture.Surface.Page;
        _weatherGesture.Begin(point, seconds, surface);
        _weatherGestureHours = _weatherScroll.Target;
        _weatherGestureDays = _weatherDaysScroll.Target;
        _collapseTimeout.Cancel();
        // Take over an interrupted transition at its current visual position.
        _weatherGestureOffset = _contentMove.X;
        StopWeatherPageTransition();
        _contentMove.BeginAnimation(TranslateTransform.XProperty, null);
        _contentMove.X = _weatherGestureOffset;
    }

    void MoveWeatherGesture(Point point)
    {
        _weatherGesture.Move(point);
        var delta = _weatherGesture.Delta;
        switch (_weatherGesture.Kind)
        {
            case WeatherGesture.Motion.City:
                _contentMove.X = Math.Clamp(_weatherGestureOffset + delta.X, -WeatherSlideWidth, WeatherSlideWidth);
                break;
            case WeatherGesture.Motion.Hours:
                _weatherScroll.Target = Math.Clamp(_weatherGestureHours - delta.X, 0, _weatherOverflow);
                StartShapeLoop();
                break;
            case WeatherGesture.Motion.Days:
                _weatherDaysScroll.Target = Math.Clamp(_weatherGestureDays - delta.Y, 0, _weatherDaysOverflow);
                StartShapeLoop();
                break;
        }
    }

    void EndWeatherGesture(Point point, double seconds)
    {
        MoveWeatherGesture(point);
        var result = _weatherGesture.End(point, seconds);
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();
        if (result == WeatherGesture.Motion.Close)
        {
            BackWeatherContent();
            ToggleOpen();
            UpdateTargets();
        }
        else if (result == WeatherGesture.Motion.City)
        {
            double dx = _weatherGesture.Delta.X;
            int direction = dx < 0 ? 1 : -1;
            int count = _weatherPages.Count;
            int index = _weatherPages.FindIndex(page => page.Id == _weatherSelectedId);
            if (Math.Abs(dx) >= WeatherSwipeDistance && count > 1 && index >= 0)
                SwitchWeatherPage(((index + direction) % count + count) % count, direction);
            else BackWeatherContent();
        }
        else
        {
            if (result == WeatherGesture.Motion.Days)
            {
                _weatherDaysScroll.Target = Math.Clamp(Math.Round(_weatherDaysScroll.Target / DayRowHeight) * DayRowHeight, 0, _weatherDaysOverflow);
                StartShapeLoop();
            }
            BackWeatherContent();
        }
    }

    void CancelWeatherGesture()
    {
        if (!_weatherGesture.Active) return;
        _weatherGesture.Cancel();
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();
        BackWeatherContent();
    }

    static DependencyObject? WeatherParent(DependencyObject node) => node is Visual
        ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    static bool WithinWeatherElement(object? source, FrameworkElement? root)
    {
        if (root is null || source is not DependencyObject node) return false;
        DependencyObject? current = node;
        while (current is not null)
        {
            if (ReferenceEquals(current, root)) return true;
            current = WeatherParent(current);
        }
        return false;
    }
}
