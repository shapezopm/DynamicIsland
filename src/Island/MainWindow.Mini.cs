using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace DynamicIsland;

public partial class MainWindow
{
    readonly Digits _miniClockReference = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold };

    void MiniSettings_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.MiniSettings);

    void MiniSettingsBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Settings);

    void MiniClockPart_Click(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        var part = Enum.Parse<MiniComponent>((string)button.Tag);
        Settings.MiniComponents = button.IsChecked == true ? Settings.MiniComponents | part : Settings.MiniComponents & ~part;
        UpdateClock();
    }

    void MiniWeather_Click(object sender, RoutedEventArgs e)
    {
        Settings.MiniWeatherSeparate = MiniWeatherSegments.PickUnderPointer() == 1;
        Settings.MiniComponents |= MiniComponent.Weather;
        if (!Settings.Weather) SetWeather(true);
        UpdateClock();
        UpdateTargets();
        _ = _weather.RefreshAsync();
    }

    void MiniWeekday_Click(object sender, RoutedEventArgs e)
    {
        Settings.MiniComponents ^= MiniComponent.Weekday;
        UpdateClock();
    }

    void SyncMiniBubble(WeatherService.Report? weather)
    {
        bool wantWeather = Settings.MiniComponents.HasFlag(MiniComponent.Weather);
        bool separateWeather = wantWeather && Settings.MiniWeatherSeparate && weather is not null;
        BubbleWeather.Visibility = separateWeather ? Visibility.Visible : Visibility.Collapsed;
        BubbleShelfCount.Visibility = Settings.ShelfEnabled && _shelf.Items.Count > 0 && !_shelfBubbleHidden ? Visibility.Visible : Visibility.Collapsed;
        BubbleWeather.Margin = new Thickness(0, 0, separateWeather && BubbleShelfCount.Visibility == Visibility.Visible ? 12 : 0, 0);
        BubbleWeatherIcon.Kind = weather is { } bubbleReport ? GlyphOf(bubbleReport.Sky, bubbleReport.Night) : Glyph.Cloud;
        BubbleWeatherDegrees.Text = weather is { } bubbleValue ? DescribeDegrees(bubbleValue.Degrees) : "";
    }

    internal static string MiniClockText(DateTime now, MiniComponent parts)
    {
        var values = new List<string>(3);
        if (parts.HasFlag(MiniComponent.Hours)) values.Add(now.ToString("HH", CultureInfo.InvariantCulture));
        if (parts.HasFlag(MiniComponent.Minutes)) values.Add(now.ToString("mm", CultureInfo.InvariantCulture));
        if (parts.HasFlag(MiniComponent.Seconds)) values.Add(now.ToString("ss", CultureInfo.InvariantCulture));
        return string.Join(":", values);
    }

    void UpdateMiniIsland(DateTime now, WeatherService.Report? weather)
    {
        MiniComponent parts = Settings.MiniComponents;
        string clock = MiniClockText(now, parts);
        IdleClock.Text = clock;
        IdleClock.Visibility = clock.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleWeekday.Text = Russian.TextInfo.ToTitleCase(now.ToString("ddd", Russian));
        IdleWeekday.Visibility = parts.HasFlag(MiniComponent.Weekday) ? Visibility.Visible : Visibility.Collapsed;
        bool wantWeather = parts.HasFlag(MiniComponent.Weather);
        bool showWeather = wantWeather && !Settings.MiniWeatherSeparate && weather is not null;
        SyncMiniBubble(weather);
        IdleWeather.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
        IdleWeatherIcon.Kind = weather is { } report ? GlyphOf(report.Sky, report.Night) : Glyph.Cloud;
        IdleWeatherDegrees.Text = weather is { } value ? DescribeDegrees(value.Degrees) : "";

        // Spacing belongs only to visible groups, so disabled components leave no holes.
        bool previous = false;
        foreach (FrameworkElement group in new FrameworkElement[] { IdleClock, IdleWeather, IdleWeekday })
        {
            group.Margin = new Thickness(previous && group.Visibility == Visibility.Visible ? 12 : 0, 0, 0, 0);
            previous |= group.Visibility == Visibility.Visible;
        }
        IdleComponents.UpdateLayout();
        IdleComponents.Measure(new Size(double.PositiveInfinity, 34));
        double contentWidth = Math.Ceiling(IdleComponents.DesiredSize.Width);
        double baseWidth = PillShapes[View.Idle].Width;
        _miniClockReference.FontFamily = IdleClock.FontFamily;
        _miniClockReference.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _miniClockReference.Measure(new Size(double.PositiveInfinity, 34));
        // Preserve the same side space as the original 118 px clock, with this font.
        double sidePadding = Math.Max(0, baseWidth - Math.Ceiling(_miniClockReference.DesiredSize.Width));
        double width = Math.Max(baseWidth, Math.Ceiling(contentWidth + sidePadding));
        if (IdleView.Width != width)
        {
            IdleView.Width = width;
            if (_view == View.Idle) UpdateTargets();
        }

        MiniHours.IsChecked = parts.HasFlag(MiniComponent.Hours);
        MiniMinutes.IsChecked = parts.HasFlag(MiniComponent.Minutes);
        MiniSeconds.IsChecked = parts.HasFlag(MiniComponent.Seconds);
        MiniHourValue.Text = now.ToString("HH");
        MiniMinuteValue.Text = now.ToString("mm");
        MiniSecondValue.Text = now.ToString("ss");
        MiniWeatherSegments.Set(Settings.MiniWeatherSeparate ? 1 : 0, MiniSettingsView.IsVisible);
        MiniWeekdaySwitch.Set(parts.HasFlag(MiniComponent.Weekday), MiniSettingsView.IsVisible);
    }
}
