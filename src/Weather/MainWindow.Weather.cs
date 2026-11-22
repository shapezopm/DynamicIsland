using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class MainWindow
{
    const string Minus = "−";
    const int ForecastDays = 5, MaxWeatherCities = 32;
    const double HourColumn = 96, WeatherHorizontalWheelStep = HourColumn / 3, DayRowHeight = 42;
    const int VisibleDays = 3;
    static readonly string[] WindArrows = ["↓", "↙", "←", "↖", "↑", "↗", "→", "↘"];

    sealed record WeatherCityView(string Id, string Name, WeatherService.Report? Report);

    readonly WeatherService _weather = new();
    readonly List<WeatherPlace> _weatherCities = WeatherCities.Read(Settings.WeatherCities, WeatherLocation.Read(Settings.WeatherPlace)).Where(city => !WeatherCities.Same(city, WeatherLocation.Read(Settings.WeatherHome))).ToList();
    readonly Dictionary<string, WeatherPlace> _weatherSearchResults = new();
    readonly Spring _weatherScroll = new(0, 260, 30), _weatherDaysScroll = new(0, 260, 30);
    readonly DispatcherTimer _weatherSearchDelay = new() { Interval = TimeSpan.FromMilliseconds(320) };
    List<WeatherCityView> _weatherPages = [];
    List<(string Id, string Name)> _weatherHits = [];
    CancellationTokenSource? _weatherLocationSearch;
    string? _weatherShownCity, _weatherSelectedId;
    WeatherService.Report? _weatherRenderedReport;
    DateOnly _weatherRenderedDate;
    double _weatherOverflow, _weatherDaysOverflow, _weatherPanFrom, _weatherPanStart;
    bool _weatherWebPane, _weatherWebEditing, _weatherReady, _weatherPanHorizontal;
    FrameworkElement? _weatherPan;
    MouseButton _weatherPanButton;
    bool WeatherSubpageOpen => _weatherWebPane || _weatherWebEditing || _addOpen || _previewPlace is not null;

    Brush DimBrush => (Brush)FindResource("Dim");
    Brush FaintBrush => (Brush)FindResource("Faint");

    void SyncWeather()
    {
        EnsureWeatherPlace();
        var now = Settings.Weather ? _weather.Now : null;
        InfoWeatherRow.SetVisible(now is not null);
        MenuWeather.Text = now is null ? "" : DescribeDegrees(now.Degrees);
        if (now is not null)
        {
            InfoWeather.Text = DescribeDegrees(now.Degrees);
            InfoWeatherIcon.Kind = MenuWeatherIcon.Kind = GlyphOf(now.Sky, now.Night);
        }
        PublishWeather(); UpdateMiniIsland(DateTime.Now, now);
    }

    // The page is part of the island's own tree (see WeatherView in MainWindow.xaml); the body behind it is the
    // island's Goo, so Glass, the rim tint, scale and the morph apply to it exactly as they do to Timer and Shelf.
    void PublishWeather()
    {
        if (_previewPlace is not null) return;
        var selected = WeatherLocation.Read(Settings.WeatherPlace) ?? WeatherLocation.Read(Settings.WeatherHome);
        var home = WeatherLocation.Read(Settings.WeatherHome);
        var pages = new List<WeatherPlace?>();
        if (home is not null) pages.Add(home);
        else if (_weather.PlaceName is not null && _weatherCities.Count == 0) pages.Add(null);
        foreach (var saved in _weatherCities)
            if (!pages.Any(page => SamePage(page, saved))) pages.Add(saved);
        if (selected is not null && !pages.Any(page => SamePage(page, selected))) pages.Add(selected);
        // The service loads the selected place; it may be listed under another page (e.g. the automatic location with the same name).
        int selectedPage = pages.FindIndex(page => SamePage(page, selected));
        _weatherSelectedId = selectedPage >= 0 ? WeatherWebData.Id(pages[selectedPage]) : WeatherWebData.Id(selected);
        _weatherPages = pages.Select((place, index) => new WeatherCityView(WeatherWebData.Id(place), place?.DisplayName ?? _weather.PlaceName ?? "Моё расположение",
            Settings.Weather ? (index == selectedPage ? _weather.Now : _weather.Cached(place)) : null)).ToList();

        var city = _weatherPages.FirstOrDefault(page => page.Id == _weatherSelectedId) ?? _weatherPages.FirstOrDefault();
        var report = city?.Report;
        _weatherReady = report is not null && _weather.State == WeatherService.Stage.Ready;
        if (_weatherReady) FillWeather(city!, report!);
        else
        {
            var (message, action) = _weather.State switch
            {
                WeatherService.Stage.Off => ("Погода выключена", "Включить"),
                WeatherService.Stage.NoPlace => ("Выберите город, чтобы узнать погоду", "Указать"),
                WeatherService.Stage.Failed => ("Не получилось узнать погоду", "Попробовать ещё раз"),
                _ => ("Узнаю погоду…", ""),
            };
            WeatherNoticeText.Text = message;
            WeatherNoticeActionText.Text = action;
            WeatherNoticeAction.Visibility = action.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        WeatherCityName.Text = city?.Name ?? "";
        WeatherPlace.Text = city?.Name ?? "";
        UpdateWeatherFreshness();
        RefreshWeatherDots();
        LayoutWeatherPage();
        if (_weatherWebPane) RefreshWeatherCities();
    }

    void LayoutWeatherPage()
    {
        if (_addOpen)
        {
            WeatherFreshness.Visibility = WeatherCityPage.Visibility = WeatherFooter.Visibility = WeatherPlace.Visibility = Visibility.Collapsed;
            WeatherContent.Visibility = WeatherNotice.Visibility = Visibility.Collapsed;
            return;
        }
        bool cities = _weatherWebPane;
        bool closing = !cities && _weatherListShown && SystemParameters.ClientAreaAnimation;
        if (cities && _weatherListShown) WeatherCityPage.Visibility = Visibility.Visible;
        if (cities != _weatherListShown)
        {
            _weatherListShown = cities;
            AnimateWeatherList(cities);
        }
        if (closing) return;
        WeatherFreshness.Visibility = WeatherFooter.Visibility = WeatherPlace.Visibility = cities ? Visibility.Collapsed : Visibility.Visible;
        WeatherContent.Visibility = !cities && _weatherReady ? Visibility.Visible : Visibility.Collapsed;
        WeatherNotice.Visibility = !cities && !_weatherReady ? Visibility.Visible : Visibility.Collapsed;
    }

    // The list opens with a fade and a short rise, and closes the same way in reverse.
    void AnimateWeatherList(bool open)
    {
        var list = WeatherCityPage;
        if (!SystemParameters.ClientAreaAnimation)
        {
            list.BeginAnimation(OpacityProperty, null); list.Opacity = 1;
            list.RenderTransform = Transform.Identity;
            list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var scale = list.RenderTransform as ScaleTransform ?? new ScaleTransform();
        list.RenderTransform = scale;
        list.RenderTransformOrigin = new Point(0.5, 1);
        if (open)
        {
            list.Visibility = Visibility.Visible;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, Ms(220)) { EasingFunction = WeatherOut, FillBehavior = FillBehavior.HoldEnd });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, Ms(220)) { EasingFunction = WeatherOut, FillBehavior = FillBehavior.HoldEnd });
            list.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(220)) { EasingFunction = WeatherEase, FillBehavior = FillBehavior.HoldEnd });
            return;
        }
        var fade = new DoubleAnimation(0, Ms(180)) { EasingFunction = WeatherEase, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            if (!_weatherListShown) { list.Visibility = Visibility.Collapsed; LayoutWeatherPage(); }
        };
        list.BeginAnimation(OpacityProperty, fade);
    }

    static string Deg(int value) => (value < 0 ? "−" : "") + Math.Abs(value) + "°";

    void FillWeather(WeatherCityView city, WeatherService.Report report)
    {
        bool another = _weatherShownCity != city.Id;
        _weatherShownCity = city.Id;
        WeatherPlace.Text = city.Name;
        WeatherDegrees.Text = Deg(report.Degrees);
        WeatherSky.Text = WeatherWebData.Condition(report.Sky);
        var today = report.LocalDate(DateTime.UtcNow);
        var day = report.Days.FirstOrDefault(d => d.Date == today);
        WeatherRange.Text = day.Date == today ? $"{Deg(day.High)}/{Deg(day.Low)}" : "";
        WeatherAirBadge.Visibility = report.AirQuality is null ? Visibility.Collapsed : Visibility.Visible;
        if (report.AirQuality is { } quality)
        {
            var (label, _) = WeatherAirQuality(quality);
            WeatherAir.Text = $"{label} · {quality}";
            WeatherAir.Foreground = DimBrush;
            WeatherAirBadge.ToolTip = $"Качество воздуха: {label}. Европейский индекс: {quality}";
            System.Windows.Automation.AutomationProperties.SetName(WeatherAirBadge, (string)WeatherAirBadge.ToolTip);
        }
        if (another) _weatherDaysScroll.Snap(0);
        if (another || !ReferenceEquals(_weatherRenderedReport, report) || _weatherRenderedDate != today)
        {
            _weatherRenderedDate = today;
            _weatherRenderedReport = report;
            BuildWeatherDays(report.Days);
            BuildWeatherHours(report, another);
        }

    }

    TextBlock WeatherText(string text, double size, Brush brush, TextAlignment align = TextAlignment.Left, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = brush, TextAlignment = align, FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
    };

    Icon WeatherIcon(Glyph kind, double size, Brush? fill = null) =>
        new() { Kind = kind, Width = size, Height = size, Fill = fill ?? DimBrush, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    void BuildWeatherDays(WeatherService.Day[] days)
    {
        WeatherDays.Children.Clear();
        int count = Math.Min(ForecastDays, days.Length);
        WeatherDays.Height = count * DayRowHeight;
        _weatherDaysOverflow = Math.Max(0, count - VisibleDays) * DayRowHeight;
        _weatherDaysScroll.Target = Math.Clamp(_weatherDaysScroll.Target, 0, _weatherDaysOverflow);
        if (_weatherDaysScroll.Value > _weatherDaysOverflow) _weatherDaysScroll.Snap(_weatherDaysOverflow);
        ApplyWeatherScroll();
        if (count == 0) return;
        int low = days.Take(count).Min(day => day.Low), high = days.Take(count).Max(day => day.High);
        double span = Math.Max(1, high - low);
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var separator = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        for (int i = 0; i < count; i++)
        {
            var day = days[i];
            var row = new Grid { Height = DayRowHeight };
            foreach (double width in new[] { 66, 28, 34 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            string name = WeatherDayLabel(day.Date, _weatherRenderedDate == default ? DateOnly.FromDateTime(DateTime.Today) : _weatherRenderedDate);
            Place(row, WeatherText(name, 14, Brushes.White), 0);
            Place(row, WeatherIcon(GlyphOf(day.Sky, false), 20), 1);
            Place(row, WeatherText(Deg(day.Low), 14, DimBrush, TextAlignment.Right), 2);

            double start = (day.Low - low) / span, share = Math.Min(Math.Max(0.08, (day.High - day.Low) / span), 1 - start);
            var bar = new Grid { Height = 4, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(start, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - start - share), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)) };
            Grid.SetColumnSpan(track, 3);
            bar.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(2), Background = new LinearGradientBrush(Color.FromRgb(0x6F, 0xD3, 0xFF), Color.FromRgb(0xE8, 0xF0, 0x9A), 0) };
            Grid.SetColumn(fill, 1);
            bar.Children.Add(fill);
            Place(row, bar, 3);
            Place(row, WeatherText(Deg(day.High), 14, Brushes.White, TextAlignment.Right), 4);
            if (i < count - 1)
            {
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = separator, IsHitTestVisible = false };
                Grid.SetColumnSpan(line, 5);
                row.Children.Add(line);
            }
            row.Width = 238; Canvas.SetTop(row, i * DayRowHeight); WeatherDays.Children.Add(row);
        }
    }
    static void Place(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    static void At(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }

    void BuildWeatherHours(WeatherService.Report report, bool resetScroll)
    {
        var slots = WeatherWebData.SlotsFor(report);
        WeatherHours.Children.Clear();
        _weatherOverflow = Math.Max(0, slots.Length * HourColumn - WeatherHours.Width);
        if (resetScroll) _weatherScroll.Snap(0);
        _weatherScroll.Target = Math.Clamp(_weatherScroll.Target, 0, _weatherOverflow);
        if (_weatherScroll.Value > _weatherOverflow) _weatherScroll.Snap(_weatherOverflow);
        ApplyWeatherScroll();
        if (slots.Length == 0) return;

        double[] temperatures = slots.Select(slot => WeatherWebData.TemperatureAt(slots, slot)).ToArray();
        // The strip is a window onto a longer one: the hour before the first slot and the hour after the last are not drawn
        // as columns, but the temperature line runs through them, so it enters and leaves the strip instead of starting
        // and ending on a dot.
        double before = report.PreviousHour?.Degrees ?? temperatures[0], after = report.FollowingHour?.Degrees ?? temperatures[^1];
        double low = Math.Min(temperatures.Min(), Math.Min(before, after)), high = Math.Max(temperatures.Max(), Math.Max(before, after));
        const double bandTop = 78, bandHeight = 10;
        double Y(double value) => bandTop + (high - value) / Math.Max(1, high - low) * bandHeight;
        var points = new Point[slots.Length];
        var line = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            double x = i * HourColumn;
            points[i] = new Point(x + HourColumn / 2, Y(temperatures[i]));
            if (i > 0) WeatherHours.Children.Add(new Line { X1 = x, X2 = x, Y1 = 4, Y2 = 108, Stroke = line, StrokeThickness = 1, IsHitTestVisible = false });
            string time = i == 0 ? (report.At != default && DateOnly.FromDateTime(report.At) < report.LocalDate(DateTime.UtcNow) ? report.At.ToString("dd.MM HH:mm") : "Сейчас") : slot.Event is null && slot.At.Hour == 0 ? slot.At.ToString("dd.MM") : slot.At.ToString("HH:mm");
            var label = WeatherText(time, 11, DimBrush, TextAlignment.Center); label.Width = HourColumn; At(label, x, 2);
            WeatherHours.Children.Add(label);
            if (slot.Event is not null)
            {
                var name = WeatherText(slot.Event, 13, Brushes.White, TextAlignment.Center, FontWeights.SemiBold); name.Width = HourColumn; At(name, x, 20);
                var sun = WeatherIcon(Glyph.Sun, 22, (Brush)FindResource("Orange")); At(sun, x + (HourColumn - 22) / 2, 46);
                WeatherHours.Children.Add(name); WeatherHours.Children.Add(sun);
                continue;
            }
            var degrees = WeatherText(Deg(slot.Degrees ?? 0), 15, Brushes.White, TextAlignment.Center, FontWeights.SemiBold); degrees.Width = HourColumn; At(degrees, x, 20);
            var icon = WeatherIcon(GlyphOf(slot.Sky, slot.Night), 28); At(icon, x + (HourColumn - 28) / 2, 44);
            WeatherHours.Children.Add(degrees); WeatherHours.Children.Add(icon);
            if (slot.Wind is { } wind)
            {
                string arrow = slot.Direction is { } direction ? WindArrows[((int)Math.Round(direction / 45) % 8 + 8) % 8] + " " : "";
                var speed = WeatherText($"{arrow}{wind:0.0} м/с", 10, FaintBrush, TextAlignment.Center); speed.Width = HourColumn; At(speed, x, 94);
                WeatherHours.Children.Add(speed);
            }
        }
        var curve = new List<Point>(points.Length + 2) { new(points[0].X - HourColumn, Y(before)) };
        curve.AddRange(points);
        curve.Add(new Point(points[^1].X + HourColumn, Y(after)));
        WeatherHours.Children.Add(WeatherCurve(curve.ToArray()));
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Event is not null) continue;
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White, IsHitTestVisible = false };
            At(dot, points[i].X - 2.5, points[i].Y - 2.5);
            WeatherHours.Children.Add(dot);
        }
    }
    Path WeatherCurve(Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0] };
        for (int i = 0; i < points.Length - 1; i++)
        {
            Point before = points[Math.Max(0, i - 1)], from = points[i], to = points[i + 1], after = points[Math.Min(points.Length - 1, i + 2)];
            figure.Segments.Add(new BezierSegment(
                new Point(from.X + (to.X - before.X) / 6, from.Y + (to.Y - before.Y) / 6),
                new Point(to.X - (after.X - from.X) / 6, to.Y - (after.Y - from.Y) / 6), to, true));
        }
        return new Path { Data = new PathGeometry([figure]), Stroke = DimBrush, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    }

    // Scrolling of both forecast blocks rides the island's own spring loop (like the Shelf), applied from ApplyShape.
    void ApplyWeatherScroll()
    {
        double hours = _weatherScroll.Value, days = _weatherDaysScroll.Value;
        WeatherMove.X = -hours;
        WeatherEdgeLeft.Color = EdgeFade(hours);
        WeatherEdgeRight.Color = EdgeFade(_weatherOverflow - hours);
        WeatherDaysMove.Y = -days;
        WeatherDaysEdgeTop.Color = EdgeFade(days);
        WeatherDaysEdgeBottom.Color = EdgeFade(_weatherDaysOverflow - days);
    }

    readonly List<(ScrollViewer Viewer, Spring Motion)> _weatherLists = [];

    void EnableWeatherSmoothScroll(ScrollViewer viewer)
    {
        var spring = new Spring(0, 260, 30);
        _weatherLists.Add((viewer, spring));
        viewer.PreviewMouseWheel += (_, e) =>
        {
            if (!SystemParameters.ClientAreaAnimation) { viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta); e.Handled = true; return; }
            if ((Math.Abs(spring.Value - spring.Target) < 0.005 && Math.Abs(spring.Velocity) < 0.05)) spring.Snap(viewer.VerticalOffset);
            spring.Target = Math.Clamp(spring.Target - e.Delta * 0.65, 0, viewer.ScrollableHeight);
            StartShapeLoop();
            e.Handled = true;
        };
        viewer.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                spring.Snap(Math.Clamp(viewer.VerticalOffset, 0, viewer.ScrollableHeight));
            if (ReferenceEquals(viewer, _addScroll)) ApplyAddEdges(viewer);
        };
    }

    void ScrollWeather(double distance)
    {
        _weatherScroll.Target = Math.Clamp(_weatherScroll.Target + distance, 0, _weatherOverflow);
        StartShapeLoop();
    }

    void ScrollWeatherDays(int steps)
    {
        _weatherDaysScroll.Target = Math.Clamp(_weatherDaysScroll.Target + steps * DayRowHeight, 0, _weatherDaysOverflow);
        StartShapeLoop();
    }
    // Wires the weather page's own controls. The page is part of the island, so there is no browser message channel.
    void WireWeatherPane()
    {
        WireWeatherSwipe();
        WireWeatherKeyboard();
        EnableWeatherSmoothScroll(WeatherCitiesScroll);
        // Left presses use the shared click/drag recognizer; middle-button pans remain local.
        WeatherStrip.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) BeginWeatherPan(WeatherStrip, true, e); };
        WeatherDaysView.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) BeginWeatherPan(WeatherDaysView, false, e); };
        foreach (var area in new FrameworkElement[] { WeatherStrip, WeatherDaysView })
        {
            area.MouseMove += (_, e) => MoveWeatherPan(e);
            area.MouseUp += (_, e) => EndWeatherPan(e);
            area.LostMouseCapture += (_, _) => FinishWeatherPan();
        }

        WeatherSearch.PreviewMouseLeftButtonDown += (_, _) => SetWeatherEditing(true);
        WeatherSearch.GotKeyboardFocus += (_, _) => SetWeatherEditing(true);
        WireWeatherSearchHint(WeatherSearch, WeatherSearchHint);
        _ = new WeatherSearchCursor(WeatherSearch, WeatherSearchHint, (Grid)WeatherSearch.Parent);
        
        WeatherSearch.TextChanged += (_, _) =>
        {
            _weatherSearchDelay.Stop();
            if (WeatherSearch.Text.Trim().Length > 0) { _weatherSearchDelay.Start(); return; }
            _weatherLocationSearch?.Cancel(); _weatherHits = []; WeatherSearchMessage.Text = "";
            if (_weatherWebPane) RefreshWeatherCities();
        };
        _weatherSearchDelay.Tick += (_, _) => { _weatherSearchDelay.Stop(); Safely(() => SearchWeatherAsync(WeatherSearch.Text.Trim(), false)); };
        WeatherSearch.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseWeatherSubpages(); e.Handled = true; }
            else if (e.Key == Key.Enter) { e.Handled = true; _weatherSearchDelay.Stop(); Safely(() => SearchWeatherAsync(WeatherSearch.Text.Trim(), true)); }
        };
    }

    void BeginWeatherPan(FrameworkElement area, bool horizontal, MouseButtonEventArgs e)
    {
        _collapseTimeout.Cancel();
        _weatherPan = area; _weatherPanHorizontal = horizontal; _weatherPanButton = e.ChangedButton;
        _weatherPanFrom = PanCoordinate(e.GetPosition(area));
        _weatherPanStart = (horizontal ? _weatherScroll : _weatherDaysScroll).Target;
        area.CaptureMouse();
        e.Handled = true;
    }

    double PanCoordinate(Point point) => _weatherPanHorizontal ? point.X : point.Y;

    void MoveWeatherPan(MouseEventArgs e)
    {
        if (_weatherPan is null) return;
        var spring = _weatherPanHorizontal ? _weatherScroll : _weatherDaysScroll;
        double overflow = _weatherPanHorizontal ? _weatherOverflow : _weatherDaysOverflow;
        spring.Target = Math.Clamp(_weatherPanStart - (PanCoordinate(e.GetPosition(_weatherPan)) - _weatherPanFrom), 0, overflow);
        StartShapeLoop();
    }

    void EndWeatherPan(MouseButtonEventArgs e)
    {
        if (_weatherPan is null || e.ChangedButton != _weatherPanButton) return;
        FinishWeatherPan();
        e.Handled = true;
    }

    void FinishWeatherPan()
    {
        if (_weatherPan is not { } area) return;
        _weatherPan = null;
        if (area.IsMouseCaptured) area.ReleaseMouseCapture();
        if (_weatherPanHorizontal) return;
        // The day list pages row by row.
        _weatherDaysScroll.Target = Math.Clamp(Math.Round(_weatherDaysScroll.Target / DayRowHeight) * DayRowHeight, 0, _weatherDaysOverflow);
        StartShapeLoop();
    }

    void OpenWeatherCities(bool focusSearch)
    {
        _weatherWebPane = true;
        LayoutWeatherPage(); RefreshWeatherCities();
        SetWeatherEditing(true);
        if (!focusSearch) { Dispatcher.BeginInvoke(() => WeatherSearch.Focus(), DispatcherPriority.Input); return; }
        SetWeatherEditing(true);
        Dispatcher.BeginInvoke(() => Keyboard.Focus(WeatherSearch), DispatcherPriority.Input);
    }

    void CloseWeatherSubpages()
    {
        _weatherSearchDelay.Stop(); _weatherLocationSearch?.Cancel();
        _weatherWebPane = false; _weatherHomeSearch = false; _weatherHits = []; HideAddPage();
        WeatherSearch.Text = ""; WeatherSearchMessage.Text = "";
        SetWeatherEditing(false);
        LayoutWeatherPage();
    }

    void RefreshWeatherCities()
    {
        if (_dragging) return;
        WeatherCityList.Children.Clear();
        _savedRows.Clear();
        if (_weatherHits.Count > 0)
        {
            WeatherCityList.Children.Add(WeatherHeading("РЕЗУЛЬТАТЫ ПОИСКА"));
            foreach (var hit in _weatherHits)
            {
                string id = hit.Id;
                if (_weatherSearchResults.TryGetValue(id, out var found))
                    WeatherCityList.Children.Add(WeatherSearchResult(found, () => Safely(() => ChooseSearchResultAsync(found))));
            }
        }
        bool homeFirst = HomeFirst;
        var first = _weatherPages.FirstOrDefault();
        var currentHeading = WeatherHeading("Текущее место");
        currentHeading.Margin = new Thickness(14, 0, 0, 8);
        WeatherCityList.Children.Add(currentHeading);
        WeatherCityList.Children.Add(HomeRow(homeFirst ? first : null, homeFirst && first is not null && first.Id == _weatherSelectedId));
        WeatherCityList.Children.Add(WeatherHeading("Добавленные места"));
        if (_weatherCities.Count == 0) WeatherCityList.Children.Add(new TextBlock { Text = "Добавленных мест пока нет", FontSize = WeatherBodySize, Foreground = DimBrush, Margin = new Thickness(22, 6, 22, 0), TextWrapping = TextWrapping.Wrap });
        var rest = homeFirst ? _weatherPages.Skip(1) : _weatherPages;
        foreach (var page in rest)
        {
            string id = page.Id;
            var place = _weatherCities.FirstOrDefault(city => WeatherWebData.Id(city) == id);
            Glyph? mark = _selectMode
                ? (_selectedIds.Contains(id) ? Glyph.Check : (Glyph?)null)
                : (Glyph?)null;
            var row = CityRow(page, place, mark, () =>
            {
                if (_selectMode) ToggleCitySelection(id);
                else Safely(async () =>
                {
                    CloseWeatherSubpages();
                    await SelectWeatherCityAsync(id);
                });
            }, null);
            WeatherCityList.Children.Add(row);
            if (row.Tag is WeatherPlace) _savedRows.Add(row);
        }
    }

    TextBlock WeatherHeading(string text) => new()
    {
        Text = text, FontSize = WeatherBodySize, FontWeight = FontWeights.SemiBold, Foreground = DimBrush,
        Margin = new Thickness(14, 6, 0, 8), IsHitTestVisible = false,
    };

    static void WireWeatherSearchHint(TextBox search, TextBlock hint)
    {
        void Update() => hint.Visibility = search.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        search.TextChanged += (_, _) => Update();
        search.GotKeyboardFocus += (_, _) => Update();
        search.LostKeyboardFocus += (_, _) => Update();
        Update();
    }


    async Task SelectWeatherCityAsync(string id)
    {
        var place = PlaceFor(id);
        if ((place is not null || id == "auto") && !WeatherCities.Same(place, WeatherLocation.Read(Settings.WeatherPlace)))
            await _weather.SetPlaceAsync(place);
    }

    async Task RemoveWeatherCityAsync(string id)
    {
        if (_weatherCities.FirstOrDefault(city => WeatherWebData.Id(city) == id) is not { } removed) return;
        _weatherCities.Remove(removed); SaveWeatherCities();
        if (WeatherCities.Same(removed, WeatherLocation.Read(Settings.WeatherPlace)))
            await _weather.SetPlaceAsync(WeatherLocation.Read(Settings.WeatherHome) ?? _weatherCities.FirstOrDefault());
        PublishWeather();
    }

    static async void Safely(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { App.Log(ex); }
    }


    async Task SearchWeatherAsync(string query, bool submit)
    {
        _weatherLocationSearch?.Cancel(); _weatherLocationSearch?.Dispose();
        var cancellation = _weatherLocationSearch = new();
        _weatherSearchResults.Clear();
        void Results(string text)
        {
            if (!_weatherWebPane || query != WeatherSearch.Text.Trim()) return;
            _weatherHits = _weatherSearchResults.Select(pair => (pair.Key, pair.Value.Name)).ToList();
            WeatherSearchMessage.Text = text;
            RefreshWeatherCities();
        }
        if (submit && WeatherLocation.Coordinates(query) is { } coordinates) { await ChooseSearchResultAsync(coordinates); return; }
        if (query.Length is < 2 or > 100 || query.Any(char.IsControl) || char.IsDigit(query[0]) || query[0] is '+' or '-' or '−')
        { Results("Введите город или координаты. Координаты применяются по Enter."); return; }
        try
        {
            var places = await WeatherLocation.SearchAsync(query, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            foreach (var place in places) _weatherSearchResults[WeatherWebData.Id(place)] = place;
            Results(places.Length == 0 ? "Город не найден. Уточните название." : "");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { if (!cancellation.IsCancellationRequested) Results("Поиск недоступен. Попробуйте позже или введите координаты."); }
    }

    async Task AddWeatherCityAsync(WeatherPlace place)
    {
        int index = _weatherCities.FindIndex(saved => WeatherCities.Same(saved, place));
        if (index < 0 && _weatherCities.Count >= MaxWeatherCities)
        { WeatherSearchMessage.Text = "Сохранено 32 города. Удалите один, чтобы добавить новый."; return; }
        if (index < 0) _weatherCities.Add(place); else _weatherCities[index] = place;
        SaveWeatherCities(); CloseWeatherSubpages();
        if (!Settings.Weather) SetWeather(true);
        await _weather.SetPlaceAsync(place);
    }

    void SaveWeatherCities() => Settings.WeatherCities = JsonSerializer.Serialize(_weatherCities);

    void SetWeatherEditing(bool editing)
    {
        if (_weatherWebEditing == editing) return;
        _weatherWebEditing = editing; Native.AllowKeyboardInput(_hwnd, editing); Focusable = editing;
        if (editing) { _collapseTimeout.Cancel(); Activate(); } else Keyboard.ClearFocus();
    }

    void WeatherList_Click(object sender, RoutedEventArgs e) => OpenWeatherCities(false);

    void WeatherAdd_Click(object sender, RoutedEventArgs e) => ShowAddPage();

    void WeatherBack_Click(object sender, RoutedEventArgs e) => CloseWeatherSubpages();

    void WeatherAutomatic_Click(object sender, RoutedEventArgs e) => Safely(async () =>
    {
        if (!Settings.Weather) SetWeather(true);
        CloseWeatherSubpages();
        await _weather.SetPlaceAsync(null);
    });

    void WeatherNotice_Click(object sender, RoutedEventArgs e)
    {
        if (_weather.State == WeatherService.Stage.NoPlace) { EditWeatherHome(); return; }
        Safely(async () =>
        {
            if (!Settings.Weather) SetWeather(true);
            await _weather.RefreshAsync(true);
        });
    }

    void WeatherView_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (WeatherCityPage is null) return;
        if (!WeatherView.IsVisible) { CancelWeatherGesture(); FinishWeatherPan(); CloseWeatherSubpages(); }
    }

    void WeatherRow_Click(object sender, RoutedEventArgs e)
    { ShowPanel(Panel.Weather); PublishWeather(); _ = _weather.RefreshAsync(_weather.State != WeatherService.Stage.Ready); }

    void Weather_Click(object sender, RoutedEventArgs e)
    { SetWeather(!Settings.Weather); _ = _weather.RefreshAsync(true); }

    void SetWeather(bool enabled) { Settings.Weather = enabled; if (!enabled) _weather.Stop(); else { _savedWeatherRetryAt = default; Safely(WarmSavedWeatherAsync); } UpdateSwitches(true); SyncWeather(); }

    static string DescribeDegrees(int degrees) => (degrees > 0 ? "+" : degrees < 0 ? "−" : "") + Math.Abs(degrees) + "°";

    static Glyph GlyphOf(Sky sky, bool night) => sky switch
    {
        Sky.Clear => night ? Glyph.Moon : Glyph.Sun,
        Sky.Cloudy => night ? Glyph.CloudMoon : Glyph.CloudSun,
        Sky.Overcast => Glyph.Cloud,
        Sky.Fog => Glyph.Fog,
        Sky.Rain => Glyph.Rain,
        Sky.Snow => Glyph.Snow,
        _ => Glyph.Storm,
    };
}
