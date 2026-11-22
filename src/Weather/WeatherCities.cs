using System.Text.Json;

namespace DynamicIsland;

internal static class WeatherCities
{
    internal static bool Same(WeatherPlace? a, WeatherPlace? b) => a is null ? b is null : b is not null
        && Math.Round(a.Latitude, 2) == Math.Round(b.Latitude, 2) && Math.Round(a.Longitude, 2) == Math.Round(b.Longitude, 2);

    internal static WeatherPlace[] Read(string json, WeatherPlace? current)
    {
        var places = new List<WeatherPlace>();
        if (json.Length is > 0 and < 65536)
            try
            {
                foreach (var place in JsonSerializer.Deserialize<WeatherPlace?[]>(json) ?? [])
                    if (place is { Valid: true } && !places.Any(saved => Same(saved, place))) places.Add(place);
            }
            // Испорченный сохранённый список не ошибка работы: начинаем с пустого, следующая запись его перезапишет.
            catch (JsonException) { }
        if (current is { Valid: true })
        {
            int index = places.FindIndex(saved => Same(saved, current));
            if (index >= 0) places[index] = current;
            else { if (places.Count >= 32) places.RemoveRange(31, places.Count - 31); places.Add(current); }
        }
        return places.Take(32).ToArray();
    }
}
