using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public partial class MainWindow
{
    internal static (string Label, Color Color) WeatherAirQuality(int value) => value switch
    {
        <= 20 => ("Хороший", Color.FromRgb(96, 211, 148)),
        <= 40 => ("Приемлемый", Color.FromRgb(172, 213, 102)),
        <= 60 => ("Умеренный", Color.FromRgb(255, 211, 92)),
        <= 80 => ("Плохой", Color.FromRgb(255, 155, 85)),
        <= 100 => ("Очень плохой", Color.FromRgb(255, 105, 97)),
        _ => ("Крайне плохой", Color.FromRgb(213, 156, 255)),
    };

    internal static string WeatherDayLabel(DateOnly date, DateOnly today)
    {
        if (date == today) return "Сегодня";
        if (date == today.AddDays(1)) return "Завтра";
        if (date < today) return date.ToString("dd.MM", CultureInfo.InvariantCulture);
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        return culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek));
    }

    void UpdateWeatherFreshness()
    {
        var place = _previewPlace ?? PlaceFor(_weatherSelectedId ?? "auto");
        var at = _weather.UpdatedAt(place);
        WeatherFreshness.Text = !Settings.Weather || at is null ? ""
            : _weather.IsStale(place) ? "Данные устарели" : $"Обновлено {at.Value.ToLocalTime():HH:mm}";
        WeatherPlace.ToolTip = (place?.Name ?? _weather.PlaceName) + (at is null ? "" : $"\nОбновлено {at.Value.ToLocalTime():dd.MM HH:mm}");
    }

    void WireWeatherKeyboard()
    {
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(WeatherCityPage, KeyboardNavigationMode.Cycle);
        if (_addPage is not null) KeyboardNavigation.SetTabNavigation(_addPage, KeyboardNavigationMode.Cycle);
        WeatherView.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_previewPlace is not null) HidePreview();
            else if (_addOpen) HideAddPage();
            else if (_selectMode) ExitSelectMode();
            else if (_weatherWebPane) CloseWeatherSubpages();
            else { ToggleOpen(); UpdateTargets(); }
        };
        WeatherView.GotKeyboardFocus += (_, _) => _collapseTimeout.Cancel();
    }
}
