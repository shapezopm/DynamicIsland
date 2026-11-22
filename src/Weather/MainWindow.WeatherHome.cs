using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static DynamicIsland.Motion;

namespace DynamicIsland;

// Three separate things: the home city ("Моё расположение", set by hand), the saved cities (the dots) and the shown place
// (Settings.WeatherPlace). The screen's controls are Border rows, not buttons: a Button captures the mouse on press and
// would swallow the clicks of the controls inside it.
public partial class MainWindow
{
    const double WeatherTitleSize = 17, WeatherBodySize = 14;
    bool _weatherHomeSearch;
    bool _weatherEnsuring;

    bool WeatherTyping => _weatherWebEditing;

    // A press on a weather control must not reach the island: the island would start a press state and wait for a release it never gets.
    static void Claim(UIElement element) => element.MouseLeftButtonDown += (_, e) => e.Handled = true;

    static void WireWeatherAction(Border target, string name, Action invoke)
    {
        System.Windows.Automation.AutomationProperties.SetName(target, name);
        target.Focusable = true;
        if (target is WeatherActionBorder action) action.InvokeAction = invoke;
    }

    Border WeatherSearchResult(WeatherPlace place, Action choose)
    {
        var text = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        text.Children.Add(WeatherText(place.DisplayName, WeatherTitleSize, Brushes.White, weight: FontWeights.SemiBold));
        string detail = place.Name.Contains(',') ? place.Name[(place.Name.IndexOf(',') + 1)..].Trim() : place.Name;
        text.Children.Add(new TextBlock { Text = detail, FontSize = WeatherBodySize, Foreground = DimBrush,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var row = new WeatherActionBorder { Child = text, Background = Brushes.Transparent,
            BorderBrush = CardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22),
            Margin = new Thickness(0, 0, 12, 10), Cursor = Cursors.Hand };
        WireWeatherAction(row, place.Name, choose);
        Claim(row);
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; choose(); };
        return row;
    }

    // The first page is the home page when it is the home city, or the automatic location with no saved cities.
    bool HomeFirst => _weatherPages.Count > 0 &&
        (_weatherPages[0].Id == "auto" || WeatherWebData.Id(WeatherLocation.Read(Settings.WeatherHome)) == _weatherPages[0].Id);

    // Two pages are the same city: equal coordinates, or the automatic location and a city with the same name.
    bool SamePage(WeatherPlace? a, WeatherPlace? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null)
        {
            var other = a ?? b;
            return _weather.PlaceName is { } name && string.Equals(other!.DisplayName, name, StringComparison.OrdinalIgnoreCase);
        }
        return WeatherCities.Same(a, b);
    }

    // The entry field decides the meaning: after "Указать" or the pencil the city becomes the home city;
    // otherwise a chosen result is added to the saved cities and does not touch the home city.
    Task ChooseSearchResultAsync(WeatherPlace place) =>
        _weatherHomeSearch ? SetWeatherHomeAsync(place) : AddWeatherCityAsync(place);

    async Task SetWeatherHomeAsync(WeatherPlace place)
    {
        Settings.WeatherHome = JsonSerializer.Serialize(place);
        _weatherCities.RemoveAll(saved => WeatherCities.Same(saved, place));
        SaveWeatherCities();
        CloseWeatherSubpages();
        if (!Settings.Weather) SetWeather(true);
        await _weather.SetPlaceAsync(place);
    }

    // "Указать" and the pencil stay on the cities screen: the entry field is focused and highlighted for about a second.
    void EditWeatherHome()
    {
        
        _weatherHomeSearch = true;
        OpenWeatherCities(true);
        HighlightWeatherSearch();
    }

    void HighlightWeatherSearch()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0xF0, 0xFF));
        WeatherSearch.Background = brush;
        if (!SystemParameters.ClientAreaAnimation) return;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x80, 0xE3, 0xF0, 0xFF), Ms(500))
        {
            AutoReverse = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    // The shown place is the home city when it is set, otherwise the first saved city. It is kept valid when the list changes.
    void EnsureWeatherPlace()
    {
        if (_weatherEnsuring || !Settings.Weather) return;
        var home = WeatherLocation.Read(Settings.WeatherHome);
        var shown = WeatherLocation.Read(Settings.WeatherPlace);
        if (shown is not null && (WeatherCities.Same(shown, home) || _weatherCities.Any(city => WeatherCities.Same(city, shown)))) return;
        var target = home ?? _weatherCities.FirstOrDefault();
        if (shown is null && target is null) return;
        _weatherEnsuring = true;
        Safely(async () =>
        {
            try { await _weather.SetPlaceAsync(target); }
            finally { _weatherEnsuring = false; }
        });
    }

    // Cards: an outlined rounded surface, a tile with an icon on the left, the name and the degrees, the condition and the minimum / maximum.
    // In the select mode an added place shows a select circle on the right; the current place has none.
    static readonly SolidColorBrush CardEdge = new(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    static readonly SolidColorBrush CardTile = new(Color.FromArgb(0x26, 0xE3, 0xF0, 0xFF));
    static readonly SolidColorBrush SelectBlue = new(Color.FromRgb(0x0A, 0x84, 0xFF));

    static void Put2(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    Border Card(UIElement child) => new WeatherActionBorder
    {
        Height = 80, CornerRadius = new CornerRadius(22), BorderBrush = CardEdge, BorderThickness = new Thickness(1),
        Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand,
        VerticalAlignment = VerticalAlignment.Top, Child = child,
    };

    Grid WithTile(UIElement content, Glyph glyph)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = CardTile,
            Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = WeatherIcon(glyph, 14, Brushes.White),
        };
        Put2(grid, tile, 0, 0);
        Put2(grid, content, 0, 1);
        return grid;
    }

    Grid CardGrid(string name, WeatherService.Report? report, Glyph? mark, bool ring)
    {
        var grid = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_selectMode ? 34 : 0) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        Put2(grid, WeatherText(name, WeatherTitleSize, Brushes.White, TextAlignment.Left, FontWeights.SemiBold), 0, 0);
        Put2(grid, WeatherText(report is null ? "" : Deg(report.Degrees), 30, Brushes.White, TextAlignment.Right, FontWeights.Light), 0, 1);
        Put2(grid, WeatherText(report is null ? "" : WeatherWebData.Condition(report.Sky), WeatherBodySize, DimBrush), 1, 0);
        var today = report?.LocalDate(DateTime.UtcNow);
        var currentDay = report?.Days.FirstOrDefault(d => d.Date == today);
        string range = currentDay is { } day && day.Date == today ? $"{Deg(day.High)} / {Deg(day.Low)}" : "";
        Put2(grid, WeatherText(range, WeatherBodySize, DimBrush, TextAlignment.Right), 1, 1);
        if (mark is { } kind)
        {
            var circle = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = SelectBlue,
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = WeatherIcon(kind, 11, Brushes.White),
            };
            Put2(grid, circle, 0, 2);
            Grid.SetRowSpan(circle, 2);
        }
        else if (ring)
        {
            var circle = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderBrush = DimBrush, BorderThickness = new Thickness(1.3),
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            Put2(grid, circle, 0, 2);
            Grid.SetRowSpan(circle, 2);
        }
        return grid;
    }

    // The first card is always "Моё расположение": the home city (or the automatic page); its pencil edits the home city.
    Border HomeRow(WeatherCityView? page, bool selected)
    {
        var home = WeatherLocation.Read(Settings.WeatherHome);
        string label = home?.DisplayName ?? page?.Name ?? "Указать";
        var grid = CardGrid(label, page?.Report, null, false);
        var card = Card(WithTile(grid, Glyph.Location));
        void Choose()
        {
            if (page is null) { EditWeatherHome(); return; }
            Safely(async () => { CloseWeatherSubpages(); await SelectWeatherCityAsync(page.Id); });
        }
        WireWeatherAction(card, label, Choose);
        Claim(card);
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(); };
        return card;
    }

    // A saved city card: a press selects the city or, in the select mode, toggles it; a hold and a drag reorder it.
    Border CityRow(WeatherCityView page, WeatherPlace? place, Glyph? mark, Action choose, Action? remove)
    {
        bool reorderable = place is not null && _weatherCities.Any(city => WeatherCities.Same(city, place));
        var card = Card(WithTile(CardGrid(page.Name, page.Report, mark, _selectMode), Glyph.Pin));
        card.Tag = reorderable ? place : null;
        WireWeatherAction(card, page.Name + (_selectMode ? (mark is null ? ", не выбран" : ", выбран") : ""), choose);
        card.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            BeginCityPress(card, reorderable, e.GetPosition(WeatherCityList).Y, choose);
        };
        card.MouseMove += (_, e) => MoveCityPress(card, e.GetPosition(WeatherCityList).Y);
        card.MouseLeftButtonUp += (_, e) => EndCityPress(card, e.GetPosition(WeatherCityList).Y);
        return card;
    }
}
