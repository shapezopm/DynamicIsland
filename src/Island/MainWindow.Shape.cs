using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public partial class MainWindow
{
    const double MinPillSize = 24;
    const double MinScale = 0.01;
    const double ShadowFadeHeight = 50;
    const double OffscreenMargin = 30;
    const double PressedScale = 0.93, PressedOpenScale = 0.975, HoverScale = 1.07;
    const double BubbleGap = 7;
    const double BubbleTuckFactor = 1.6;
    const double BubbleVisibleSplit = 0.01, BubbleClickableSplit = 0.75;
    const double RecordBubbleWidth = 78, TimerBubbleWidth = 78, ShelfBubbleWidth = 54;
    const double BubbleOverlap = 11;
    const double ShelfBubblePadding = 13, ShelfBubblePaddingWithTimer = 10;
    const double MinViewScale = 0.5, MaxViewScale = 1.15;
    const double CacheScaleTolerance = 0.001;
    const double ShadowReach = 100;
    static readonly TimeSpan GlassTime = TimeSpan.FromMilliseconds(300);

    readonly Spring _width = new(34), _height = new(34), _radius = new(17);
    readonly Spring _scale = new(1, 320, 20), _offsetY = new(0, 260, 26), _leanX = new(0);
    readonly Spring _userScale = new(Settings.Scale / 100.0, 240, 26), _topGap = new(Settings.Gap, 240, 26);
    readonly Spring _bubbleSplit = new(0), _bubbleScale = new(1, 320, 20);
    readonly Spring _lyricSnapX = new(0, 200, 28), _lyricSnapY = new(0, 200, 28);
    readonly Spring _bubbleRecord = new(0, 260, 24), _bubbleTimer = new(0, 260, 24), _bubbleShelf = new(0, 260, 24), _shelfBubbleWidth = new(ShelfBubbleWidth, 260, 24);
    readonly FrameLoop _shapeLoop;
    readonly Glass _glass;
    readonly Spring[] _springs;
    Rect _clipRect = Rect.Empty;
    double _clipRadius;
    bool _morphing;
    bool _shapeDirty;
    bool _shadowDeferred;
    bool _callBubbleOnLeft;

    void UpdateTargets()
    {
        PillShape shape = ShapeOf(_view);
        bool compact = shape.Height < CompactMaxHeight;
        double pull = RubberBand(_pull, MaxPull), lean = Math.Sign(_leanDrag) * RubberBand(Math.Abs(_leanDrag), MaxLean);
        bool dragging = _grab is Grab.Pull or Grab.Lean;
        _width.Target = shape.Width + pull * PullWidening + Math.Abs(lean) * LeanStretch;
        _height.Target = shape.Height + pull;
        _radius.Target = shape.Radius + pull * PullRounding;
        _leanX.Target = lean;
        if (TryGetMediaSpot(_view, out MediaSpot spot))
        {
            _coverLeft.Target = spot.CoverLeft;
            _coverTop.Target = spot.CoverTop + pull / 2;
            _barsCenterY.Target = spot.BarsCenterY + pull / 2;
            AimCoverAtWholePixels(spot, shape.Width);
        }
        if (dragging) TuneDragSprings(true);
        UpdateBubbleTargets(compact);
        _offsetY.Target = (HiddenByFullscreen || _sentAway) && !_ringing ? -(shape.Height + OffscreenMargin + Settings.Gap * 100.0 / Settings.Scale) : 0;
        _scale.Target = dragging ? 1 : _pressed ? (compact ? PressedScale : PressedOpenScale) : _hovered && compact ? HoverScale : 1;
        _bubbleScale.Target = _bubblePressed ? PressedScale : _bubbleHovered ? HoverScale : 1;
        _callVoiceScale.Target = _callVoicePressed ? PressedScale : _callVoiceHovered ? HoverScale : 1;
        StartShapeLoop();
    }

    void UpdateBubbleTargets(bool compact)
    {
        SyncMiniBubble(MiniWeatherReport());
        bool record = _obs.Recording && _view != View.Record;
        bool timer = _countdown.IsActive && _view != View.Timer, shelf = ShowShelfCount || ShowSeparateWeather;
        bool split = compact && (record || timer || shelf);
        if (_callBubbleOnLeft != WantsCallBubbleOnLeft)
        {
            if (_bubbleSplit.Value > BubbleVisibleSplit)
                split = false;
            else
                _callBubbleOnLeft = WantsCallBubbleOnLeft;
        }
        _bubbleSplit.Target = split ? 1 : 0;
        if (compact) _bubbleSplit.Tune(140, 17);
        else _bubbleSplit.Tune(300, 30);
        _callVoiceSplit.Tune(_bubbleSplit.Stiffness, _bubbleSplit.Damping);
        if (!split) return;

        _bubbleRecord.Target = record ? 1 : 0;
        _bubbleTimer.Target = timer ? 1 : 0;
        _bubbleShelf.Target = shelf ? 1 : 0;
        if (shelf) _shelfBubbleWidth.Target = MeasureShelfBubble();
        if (_bubbleSplit.Value >= BarelyVisible) return;

        _bubbleRecord.Snap(_bubbleRecord.Target);
        _bubbleTimer.Snap(_bubbleTimer.Target);
        _bubbleShelf.Snap(_bubbleShelf.Target);
        _shelfBubbleWidth.Snap(_shelfBubbleWidth.Target);
    }

    static double RubberBand(double distance, double limit) => limit * (1 - 1 / (distance * DragFollow / limit + 1));

    void TuneDragSprings(bool tight)
    {
        (double stiffness, double damping) = tight ? (900.0, 60.0) : (300.0, 21.0);
        _width.Tune(stiffness, damping);
        _height.Tune(stiffness, damping);
        foreach (Spring spring in _spotSprings) spring.Tune(stiffness, damping);
        _radius.Tune(tight ? 900 : 300, tight ? 60 : 30);
        _leanX.Tune(tight ? 900 : 230, tight ? 60 : 15);
    }

    bool ShowShelfCount => Settings.ShelfEnabled && _shelf.Items.Count > 0 && !_shelfBubbleHidden;
    bool ShowSeparateWeather => Settings.MiniWeatherSeparate && Settings.MiniComponents.HasFlag(MiniComponent.Weather) && MiniWeatherReport() is not null;

    double MeasureShelfBubble()
    {
        BubbleShelf.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(ShelfBubbleWidth, BubbleShelf.DesiredSize.Width - BubbleShelf.Margin.Right + 2 * ShelfBubblePadding);
    }

    void StartShapeLoop()
    {
        _shapeDirty = true;
        _shapeLoop.Start();
    }

    bool AdvanceShape(double dt)
    {
        bool sceneMoving = false, sceneChanged = false, weatherMoving = false, listMoving = false, weatherChanged = false;
        double morphDt = dt;
        if (_rootMenuMorphElapsed < RootMenuMorphSeconds)
        {
            double before = _rootMenuMorphElapsed / RootMenuMorphSeconds;
            _rootMenuMorphElapsed = Math.Min(_rootMenuMorphElapsed + dt, RootMenuMorphSeconds);
            double after = _rootMenuMorphElapsed / RootMenuMorphSeconds;
            // Delay the contour by the same amount as the menu fade while keeping the existing spring curve.
            morphDt -= (Smooth(after) - Smooth(before)) * RootMenuExtraMs / 1000;
        }
        if (!SystemParameters.ClientAreaAnimation)
        {
            _weatherScroll.Snap(_weatherScroll.Target);
            _weatherDaysScroll.Snap(_weatherDaysScroll.Target);
        }
        foreach (Spring spring in _springs)
        {
            bool weather = ReferenceEquals(spring, _weatherScroll)
                || ReferenceEquals(spring, _weatherDaysScroll)
                || ReferenceEquals(spring, _weatherPageSpring);
            if (spring.Value != spring.Target || spring.Velocity != 0)
            {
                if (weather) weatherChanged = true;
                else sceneChanged = true;
            }
            bool morphSpring = morphDt != dt && (spring == _width || spring == _height || spring == _radius || _spotSprings.Contains(spring));
            bool active = spring.Advance(morphSpring ? morphDt : dt);
            if (weather) weatherMoving |= active;
            else sceneMoving |= active;
        }
        foreach (var (viewer, spring) in _weatherLists)
        {
            if (!viewer.IsVisible) { spring.Snap(viewer.VerticalOffset); continue; }
            if (!(Math.Abs(spring.Value - spring.Target) < 0.005 && Math.Abs(spring.Velocity) < 0.05))
            {
                listMoving |= spring.Advance(dt);
                viewer.ScrollToVerticalOffset(Math.Clamp(spring.Value, 0, viewer.ScrollableHeight));
            }
        }
        if (_callBubbleOnLeft != WantsCallBubbleOnLeft
            && _bubbleSplit.Value <= BubbleVisibleSplit)
        {
            UpdateBubbleTargets(ShapeOf(_view).Height < CompactMaxHeight);
            sceneMoving = true;
        }
        bool moving = sceneMoving || weatherMoving || listMoving;
        bool redrawShape = _shapeDirty || sceneChanged || _morphing;
        _shapeDirty = false;
        if (redrawShape) ApplyShape();
        else if (weatherChanged || _weatherPageOverlay is not null)
        {
            ApplyWeatherScroll();
            ApplyWeatherPageTransition();
        }
        if (!moving)
            EndMorph();
        return moving || IsLyricSettling;
    }

    void ApplyShape()
    {
        double w = Math.Max(_width.Value, MinPillSize), h = Math.Max(_height.Value, MinPillSize);
        double r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        var pill = new Rect((HostWidth - w) / 2, 0, w, h);
        double scale = Math.Max(_scale.Value, MinScale), size = Math.Max(_userScale.Value, MinScale);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;

        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(r);
        if (!_shadowDeferred) Shadow.Opacity = ShadowOpacity(h);
        if (pill != _clipRect || r != _clipRadius)
        {
            _clipRect = pill;
            _clipRadius = r;
            Host.Clip = Shared.Clip = Squircle.Of(pill, r);
            if (!_shadowDeferred) UpdateShadowGeometry();
        }

        FitMediaViews(w, h);
        if (_morphing) FitViews(w, h);
        ApplyShelfScroll();
        ApplyWeatherScroll();
        ApplyWeatherPageTransition();
        ApplyLookScroll();
        ApplyUpdateNotesScroll();
        StretchVolumeBar();

        IslandScale.ScaleX = IslandScale.ScaleY = scale;
        RootSize.ScaleX = RootSize.ScaleY = size;
        RootMove.Y = _offsetY.Value + _topGap.Value / size;
        double pixels = Math.Max(size * dpi, MinScale);
        RootLean.X = Math.Round(_leanX.Value * pixels) / pixels;
        SharpenLyricCache(dpi);

        PlaceCoverAndBars(pill);
        Rect bubble = PlaceBubble(pill, r, scale);
        Rect voice = PlaceDetachedDiscordVoice(pill, r, scale);
        Body.SetShape(pill, r, bubble, voice);
        CutGlass(dpi);
    }

    void SharpenLyricCache(double dpi)
    {
        double sharp = Math.Max(_userScale.Target, MinScale) * dpi;
        if (Math.Abs(_lyricCache.RenderAtScale - sharp) > CacheScaleTolerance) _lyricCache.RenderAtScale = sharp;
        AimLyricAtWholePixels(dpi);
    }

    bool IsLyricSettling => _lyricSnapX.Value != _lyricSnapX.Target || _lyricSnapY.Value != _lyricSnapY.Target;

    void AimLyricAtWholePixels(double dpi)
    {
        if (_view == View.Media && LyricBox.IsVisible && Root.TransformToAncestor(this) is Transform place)
        {
            Point inIsland = LyricBox.TranslatePoint(new Point(-LyricSnap.X, -LyricSnap.Y), Island);
            Matrix toWindow = place.Value;
            Point unscaled = toWindow.Transform(inIsland + VisualTreeHelper.GetOffset(Island));
            double pixelsPerUnit = Math.Max(toWindow.M11, MinScale) * dpi;
            _lyricSnapX.Target = (Math.Round(unscaled.X * dpi) - unscaled.X * dpi) / pixelsPerUnit;
            _lyricSnapY.Target = (Math.Round(unscaled.Y * dpi) - unscaled.Y * dpi) / pixelsPerUnit;
        }
        LyricSnap.X = _lyricSnapX.Value;
        LyricSnap.Y = _lyricSnapY.Value;
    }

    static Geometry Around(Geometry hole)
    {
        var around = new GeometryGroup { FillRule = FillRule.EvenOdd };
        around.Children.Add(new RectangleGeometry(new Rect(-ShadowReach, -ShadowReach, HostWidth + 2 * ShadowReach, HostHeight + 2 * ShadowReach)));
        around.Children.Add(hole);
        return around;
    }

    void CutGlass(double dpi)
    {
        if (!_glass.IsOn || Body.TransformToAncestor(this) is not Transform place) return;
        Matrix toPixels = place.Value;
        toPixels.Scale(dpi, dpi);
        _glass.Cut(Body.PillInside, Body.BubbleInside, Body.OppositeBubbleInside, toPixels);
    }

    void SyncGlass(bool animate)
    {
        if (Settings.Glass)
        {
            _glass.Show();
            Reshape();
        }
        TimeSpan transition = animate ? GlassTime : TimeSpan.Zero;
        Body.Thin(Settings.Glass, transition, HideGlassOnceCovered);
    }

    void HideGlassOnceCovered()
    {
        if (Settings.Glass || !_glass.IsOn) return;
        _glass.Hide();
        Reshape();
    }

    void Reshape()
    {
        _clipRect = Rect.Empty;
        ApplyShape();
    }

    static double ShadowOpacity(double height) => Math.Clamp((height - CompactMaxHeight) / ShadowFadeHeight, 0, 1);

    void UpdateShadowGeometry()
    {
        Shadow.Data = Host.Clip;
        Shadow.Clip = _glass.IsOn ? Around(Shadow.Data) : null;
    }

    void DeferShadowDuringTransition()
    {
        // A changing outline needs a new shadow surface on every frame.
        if (_shadowDeferred) return;
        _shadowDeferred = true;
        Shadow.BeginAnimation(OpacityProperty, null);
        Shadow.Visibility = Visibility.Hidden;
    }

    double VisibleBubbleSplit(double split, double pillHeight) => ShowsVoice
        ? split * Math.Clamp((CompactMaxHeight + 10 - pillHeight) / 10, 0, 1) : split;

    static double BubbleContentOpacity(double split) => Math.Clamp(split * 4 - 3, 0, 1);

    static Rect SideBubbleShape(Rect pill, double radius, double pillScale, double width, double height,
        double sideScale, double split, bool left, out double offset)
    {
        double tuck = Math.Max(radius - height / 2, 0) * BubbleTuckFactor * (1 - Math.Clamp(split, 0, 1)) * pillScale;
        double position = pill.Width * pillScale / 2 + (BubbleGap + width) * split - width - tuck;
        if (left) position = -position - width * sideScale;
        offset = position + width / 2;
        return new Rect(pill.Left + pill.Width / 2 + position / pillScale, 0,
            width * sideScale / pillScale, height * sideScale / pillScale);
    }

    Rect PlaceBubble(Rect pill, double r, double scale)
    {
        double split = VisibleBubbleSplit(_bubbleSplit.Value, pill.Height);
        double bubble = Math.Max(_bubbleScale.Value, MinScale);
        double record = Math.Max(_bubbleRecord.Value, 0), timer = Math.Max(_bubbleTimer.Value, 0), shelf = Math.Max(_bubbleShelf.Value, 0);
        double seams = record * timer + timer * shelf + record * shelf * (1 - Math.Clamp(timer, 0, 1));
        double wide = Math.Max(RecordBubbleWidth * record + TimerBubbleWidth * timer + _shelfBubbleWidth.Value * shelf - BubbleOverlap * seams, Bubble.Height);
        bool apart = split > BubbleVisibleSplit;
        Bubble.SetVisible(apart);
        Bubble.Width = wide;
        BubbleRecord.Opacity = Math.Clamp(record * 2 - 1, 0, 1);
        BubbleTimer.Opacity = Math.Clamp(timer * 2 - 1, 0, 1);
        BubbleTimer.Margin = new Thickness((RecordBubbleWidth - BubbleOverlap) * record, 0, 0, 0);
        BubbleShelf.Opacity = Math.Clamp(shelf * 2 - 1, 0, 1);
        BubbleShelf.Margin = new Thickness(0, 0, ShelfBubblePadding + (ShelfBubblePaddingWithTimer - ShelfBubblePadding) * Math.Clamp(Math.Max(timer, record), 0, 1), 0);
        BubbleScale.ScaleX = BubbleScale.ScaleY = bubble;
        BubbleBody.Opacity = BubbleContentOpacity(split);
        Bubble.IsHitTestVisible = split > BubbleClickableSplit && (!ShowsVoice || _bubbleSplit.Target > 0);
        Rect shape = SideBubbleShape(pill, r, scale, wide, Bubble.Height, bubble, split, _callBubbleOnLeft, out double offset);
        BubbleMove.X = offset;
        return apart ? shape : Rect.Empty;
    }

    void FitViews(double w, double h)
    {
        foreach (FrameworkElement view in _views.Values)
        {
            if (view.Visibility != Visibility.Visible) continue;
            FitView(view, Math.Clamp(Math.Min(w / view.Width, h / view.Height), MinViewScale, MaxViewScale), (h - view.Height) / 2);
        }
    }

    static void FitView(FrameworkElement view, double scale, double offsetY)
    {
        ScaleTransform fit = FitScaleOf(view);
        fit.ScaleX = fit.ScaleY = scale;
        OffsetOf(view).Y = offsetY;
    }

    void EndMorph()
    {
        if (!_morphing || _grab == Grab.Pull) return;
        _morphing = false;
        _rootMenuMorphElapsed = RootMenuMorphSeconds;
        foreach (FrameworkElement view in _views.Values) FitView(view, 1, 0);
        if (!_shadowDeferred) return;
        _shadowDeferred = false;
        Shadow.Opacity = ShadowOpacity(_height.Value);
        UpdateShadowGeometry();
        Shadow.Visibility = Visibility.Visible;
        if (Shadow.Opacity > 0 && SystemParameters.ClientAreaAnimation)
            Shadow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Shadow.Opacity, Motion.Ms(120))
            {
                EasingFunction = WeatherEase,
                FillBehavior = FillBehavior.Stop,
            });
    }
}
