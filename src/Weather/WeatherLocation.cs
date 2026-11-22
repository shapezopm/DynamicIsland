using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

internal sealed record WeatherPlace(string Name, double Latitude, double Longitude)
{
    internal string DisplayName => WeatherLocation.Coordinates(Name) is not null ? "Выбранное место" : Name.Split(',')[0].Trim();
    internal bool Valid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 180
        && double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

internal static class WeatherLocation
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    internal static WeatherPlace? Coordinates(string input)
    {
        if (input.Length > 100) return null;
        string[] parts = input.Trim().Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        bool Number(string value, out double result) => double.TryParse(value.Trim().TrimEnd(',').Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
        if (!Number(parts[0], out double latitude) || !Number(parts[1], out double longitude)) return null;
        string label = string.Create(CultureInfo.InvariantCulture, $"{latitude:0.####}, {longitude:0.####}");
        var place = new WeatherPlace(label, latitude, longitude);
        return place.Valid ? place : null;
    }

    internal static WeatherPlace? Read(string json)
    {
        if (json.Length is 0 or > 1024) return null;
        try { return JsonSerializer.Deserialize<WeatherPlace>(json) is { Valid: true } place ? place : null; }
        catch (JsonException) { return null; }
    }

    internal static async Task<WeatherPlace[]> SearchAsync(string query, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 100 || query.Any(char.IsControl)) return [];
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=ru&format=json&name=" + Uri.EscapeDataString(query);
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 64 * 1024) throw new HttpRequestException("Geocoding response too large.");
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var body = new System.IO.MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (body.Length + read > 64 * 1024) throw new HttpRequestException("Geocoding response too large.");
            body.Write(buffer, 0, read);
        }
        using JsonDocument json = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length), new() { MaxDepth = 16 });
        return ParseSearch(json.RootElement);
    }

    internal static WeatherPlace[] ParseSearch(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array) return [];
        var places = new List<WeatherPlace>();
        foreach (JsonElement item in results.EnumerateArray().Take(6))
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("latitude", out var lat) || lat.ValueKind != JsonValueKind.Number || !lat.TryGetDouble(out double latitude)
                || !item.TryGetProperty("longitude", out var lon) || lon.ValueKind != JsonValueKind.Number || !lon.TryGetDouble(out double longitude)) continue;
            string Field(string name) => item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
            string name = string.Join(", ", new[] { Field("name"), Field("admin1"), Field("country") }
                .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var place = new WeatherPlace(name, latitude, longitude);
            if (place.Valid) places.Add(place);
        }
        return places.Distinct().ToArray();
    }
}
