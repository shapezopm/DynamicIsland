using System.Windows;

namespace DynamicIsland;

public partial class MainWindow
{
    // The mini island shows the weather when there is a report: the live one, or the last one cached for the shown place.
    WeatherService.Report? MiniWeatherReport() =>
        Settings.Weather ? _weather.Now ?? _weather.Cached(WeatherLocation.Read(Settings.WeatherPlace)) : null;
}