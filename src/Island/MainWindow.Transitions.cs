using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double PageSlideDistance = 56;
    const double FadeOutScale = 0.9, FadeInScale = 0.86;
    const double FadeInGrowMs = 380, RootMenuExtraMs = 50;
    static readonly IEasingFunction SlideEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
    static readonly TimeSpan RowStagger = TimeSpan.FromMilliseconds(28);
    static readonly TimeSpan FadeInDelay = TimeSpan.FromMilliseconds(70);
    static readonly double RootMenuMorphSeconds = FadeInDelay.TotalSeconds + (FadeInGrowMs + RootMenuExtraMs) / 1000;
    readonly record struct RowSlide(UIElement Row, Transform? Original, Transform Display, TranslateTransform Shift);
    readonly Dictionary<FrameworkElement, List<RowSlide>> _rowSlides = new();
    int _viewTransitionVersion;
    double _rootMenuMorphElapsed = RootMenuMorphSeconds;

    static TransformGroup CreateViewTransforms() => new() { Children = { new ScaleTransform(1, 1), new ScaleTransform(1, 1), new TranslateTransform() } };

    static ScaleTransform FadeScaleOf(FrameworkElement view) => (ScaleTransform)((TransformGroup)view.RenderTransform).Children[0];

    static ScaleTransform FitScaleOf(FrameworkElement view) => (ScaleTransform)((TransformGroup)view.RenderTransform).Children[1];

    static TranslateTransform OffsetOf(FrameworkElement view) => (TranslateTransform)((TransformGroup)view.RenderTransform).Children[2];

    static bool IsHidden(FrameworkElement view) => view.Visibility != Visibility.Visible || view.Opacity < BarelyVisible;
    static UIElementCollection? RowsOf(FrameworkElement view) => view is Grid { Children: [StackPanel list] } ? list.Children : null;

    bool IsCurrent(FrameworkElement view) => view == Shared ? MediaSpots.ContainsKey(_view) : _views[_view] == view;

    void SwitchView(View previous, View target, double extraMs)
    {
        _viewTransitionVersion++;
        FrameworkElement outgoing = _views[previous], incoming = _views[target];
        foreach (FrameworkElement view in _views.Values)
        {
            if (view != incoming && view != outgoing && view.Visibility == Visibility.Visible)
                CollapseView(view);
        }
        if (previous == View.Weather || target == View.Weather)
        {
            if (outgoing.Visibility == Visibility.Visible) FadeOutWeather(outgoing);
            FadeInWeather(incoming);
            return;
        }
        int direction = MenuDirection(previous, target);
        if (direction != 0)
        {
            if (outgoing.Visibility == Visibility.Visible) SlideOut(outgoing, direction);
            SlideIn(incoming, direction);
            return;
        }
        if (outgoing.Visibility == Visibility.Visible) FadeOut(outgoing, extraMs);
        FadeIn(incoming, extraMs);
    }

    void FadeOutWeather(FrameworkElement view)
    {
        view.IsHitTestVisible = false;
        view.Effect = null;
        if (!SystemParameters.ClientAreaAnimation || view.Opacity <= 0)
        {
            CollapseView(view);
            return;
        }
        ScaleTransform scale = FadeScaleOf(view);
        scale.AnimateScale(WeatherAnimation(scale.ScaleX, 0.985, 170));
        HideWhenFaded(view, Ms(170), WeatherEase);
    }

    void FadeInWeather(FrameworkElement view)
    {
        bool fresh = view.Visibility != Visibility.Visible;
        ScaleTransform scale = FadeScaleOf(view);
        TranslateTransform offset = OffsetOf(view);
        double fromOpacity = fresh ? 0 : view.Opacity;
        double fromScale = fresh ? 0.975 : scale.ScaleX;
        double fromX = fresh ? 0 : offset.X;
        view.Visibility = Visibility.Visible;
        view.IsHitTestVisible = true;
        view.Effect = null;
        scale.ScaleX = scale.ScaleY = 1;
        offset.X = 0;
        view.Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation)
        {
            scale.AnimateScale(null);
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            view.BeginAnimation(OpacityProperty, null);
            return;
        }
        scale.AnimateScale(WeatherAnimation(fromScale, 1, 220));
        offset.BeginAnimation(TranslateTransform.XProperty, fromX == 0 ? null : WeatherAnimation(fromX, 0, 220));
        view.BeginAnimation(OpacityProperty, WeatherAnimation(fromOpacity, 1, 190));
    }

    static DoubleAnimation WeatherAnimation(double from, double to, double milliseconds) => new(from, to, Ms(milliseconds))
    {
        EasingFunction = WeatherEase,
        FillBehavior = FillBehavior.Stop,
    };

    void SlideOut(FrameworkElement view, int direction)
    {
        view.IsHitTestVisible = false;
        view.Effect = null;
        TranslateTransform offset = OffsetOf(view);
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset.X, -direction * PageSlideDistance, Ms(190))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
        });
        HideWhenFaded(view, Ms(150));
    }

    void SlideIn(FrameworkElement view, int direction)
    {
        bool fresh = view.Visibility != Visibility.Visible;
        ScaleTransform scale = FadeScaleOf(view);
        TranslateTransform offset = OffsetOf(view);
        double fromScale = scale.ScaleX;
        double fromX = offset.X;
        double fromOpacity = view.Opacity;
        view.Visibility = Visibility.Visible;
        view.IsHitTestVisible = true;
        view.Effect = null;

        // Resume an interrupted slide from the frame already on screen.
        if (!fresh)
        {
            scale.AnimateScale(new DoubleAnimation(fromScale, 1, Ms(380)) { EasingFunction = SlideEase });
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Ms(420)) { EasingFunction = SlideEase });
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, 1, Ms(200)));
            return;
        }

        scale.AnimateScale(null);
        double shift = direction * PageSlideDistance;
        TimeSpan wait = TimeSpan.FromMilliseconds(50), run = TimeSpan.FromMilliseconds(420), show = TimeSpan.FromMilliseconds(200);
        if (RowsOf(view) is not { Count: > 0 } rows)
        {
            offset.BeginAnimation(TranslateTransform.XProperty, Delayed(shift, 0, wait, run, SlideEase));
            view.BeginAnimation(OpacityProperty, Delayed(0, 1, wait, show));
            return;
        }

        offset.BeginAnimation(TranslateTransform.XProperty, null);
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(100)));
        var active = new List<RowSlide>(rows.Count);
        _rowSlides[view] = active;
        for (int i = 0; i < rows.Count; i++)
        {
            UIElement row = rows[i];
            Transform? original = row.RenderTransform;
            var move = new TranslateTransform();
            Transform display = original is null || original == Transform.Identity ? move : new TransformGroup { Children = { original, move } };
            active.Add(new RowSlide(row, original, display, move));
            row.SetCurrentValue(RenderTransformProperty, display);

            var slide = Delayed(shift, 0, wait, run, SlideEase);
            if (i == rows.Count - 1)
                slide.Completed += (_, _) =>
                {
                    if (ReferenceEquals(_rowSlides.GetValueOrDefault(view), active)) ClearRowSlides(view);
                };
            move.BeginAnimation(TranslateTransform.XProperty, slide);
            double opacity = (double)row.GetAnimationBaseValue(OpacityProperty);
            row.BeginAnimation(OpacityProperty, Delayed(0, opacity, wait, show));
            wait += RowStagger;
        }
    }

    void ClearRowSlides(FrameworkElement view)
    {
        if (!_rowSlides.Remove(view, out var active)) return;
        foreach (RowSlide slide in active)
        {
            slide.Shift.BeginAnimation(TranslateTransform.XProperty, null);
            slide.Row.BeginAnimation(OpacityProperty, null);
            if (ReferenceEquals(slide.Row.RenderTransform, slide.Display))
                slide.Row.SetCurrentValue(RenderTransformProperty, slide.Original);
        }
    }

    void FadeOut(FrameworkElement view, double extraMs)
    {
        view.IsHitTestVisible = false;
        FadeScaleOf(view).AnimateScale(new DoubleAnimation(FadeOutScale, Ms(170 + extraMs)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        HideWhenFaded(view, Ms(140 + extraMs));
    }

    void FadeIn(FrameworkElement view, double extraMs = 0)
    {
        bool fresh = view.Visibility != Visibility.Visible;
        ScaleTransform scale = FadeScaleOf(view);
        TranslateTransform offset = OffsetOf(view);
        double fromScale = fresh ? FadeInScale : scale.ScaleX;
        double fromOpacity = fresh ? 0 : view.Opacity;
        double fromX = fresh ? 0 : offset.X;
        view.Visibility = Visibility.Visible;
        view.IsHitTestVisible = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan grow = TimeSpan.FromMilliseconds(FadeInGrowMs + extraMs);
        scale.AnimateScale(Delayed(fromScale, 1, FadeInDelay, grow, ease));
        offset.BeginAnimation(TranslateTransform.XProperty, fromX == 0 ? null : Delayed(fromX, 0, FadeInDelay, grow, ease));
        view.BeginAnimation(OpacityProperty, Delayed(fromOpacity, 1, FadeInDelay, TimeSpan.FromMilliseconds(240 + extraMs)));
    }


    void HideWhenFaded(FrameworkElement view, Duration time, IEasingFunction? ease = null)
    {
        int version = _viewTransitionVersion;
        var fade = new DoubleAnimation(view.Opacity, 0, time) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (IsCurrent(view) || view != Shared && version != _viewTransitionVersion) return;
            CollapseView(view);
        };
        view.BeginAnimation(OpacityProperty, fade);
    }

    void CollapseView(FrameworkElement view)
    {
        view.IsHitTestVisible = false;
        view.Visibility = Visibility.Collapsed;
        ClearRowSlides(view);
        view.BeginAnimation(OpacityProperty, null);
        view.Opacity = 0;
        ScaleTransform scale = FadeScaleOf(view);
        scale.AnimateScale(null);
        scale.ScaleX = scale.ScaleY = 1;
        TranslateTransform offset = OffsetOf(view);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.X = 0;
        view.Effect = null;
    }
}
