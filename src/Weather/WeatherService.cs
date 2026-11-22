using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using Windows.Devices.Geolocation;

namespace DynamicIsland;

enum Sky
{
    Clear,
    Cloudy,
    Overcast,
    Fog,
    Rain,
    Snow,
    Storm,
}

sealed class WeatherService
{
    public enum Stage { Off, Loading, Ready, NoPlace, Failed }

    public readonly record struct Hour(int At, int Degrees, Sky Sky, bool Night)
    {
        public DateTime Time { get; init; }
        public double? Wind { get; init; }
        public double? Direction { get; init; }
    }
    public readonly record struct Day(DateOnly Date, int Low, int High, Sky Sky, double Rain)
    {
        public DateTime? Sunrise { get; init; }
        public DateTime? Sunset { get; init; }
    }

    public sealed record Report(int Degrees, int Feels, int Wind, Sky Sky, bool Night, Hour[] Hours)
    {
        public Day[] Days { get; init; } = [];
        public int Humidity { get; init; }
        public DateTime At { get; init; }
        public Hour? PreviousHour { get; init; }
        public Hour? FollowingHour { get; init; }
        public int? AirQuality { get; init; }
        public int UtcOffsetSeconds { get; init; }
        public string? TimeZoneId { get; init; }

        public DateOnly LocalDate(DateTime utcNow)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            if (!string.IsNullOrEmpty(TimeZoneId))
            {
                try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId))); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return DateOnly.FromDateTime(utc.AddSeconds(UtcOffsetSeconds));
        }
    }

    public const int ShownHours = 24;

    const string ForecastUrl = "https://api.open-meteo.com/v1/forecast"
        + "?current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day"
        + "&hourly=temperature_2m,weather_code,is_day,wind_speed_10m,wind_direction_10m&daily=temperature_2m_min,temperature_2m_max,weather_code,precipitation_sum,sunrise,sunset"
        + "&forecast_days=7&past_hours=2&forecast_hours=26&wind_speed_unit=ms&timezone=auto";
    const uint PlaceAccuracyMeters = 5000;
    const int PlaceDigits = 2;
    static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(15);
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    static readonly TimeSpan PlaceAge = TimeSpan.FromHours(1);
    static readonly TimeSpan PlaceTimeout = TimeSpan.FromSeconds(20);
    static readonly HttpClient Http = CreateClient();

    Geolocator? _locator;
    DateTime _readAgainAt;
    CancellationTokenSource? _request;
    readonly Dictionary<string, (Report Report, DateTime At)> _cache = new();
    readonly Func<WeatherPlace?, CancellationToken, Task<Report?>> _fetch;
    readonly Func<DateTime> _utcNow;
    readonly Dictionary<string, DateTime> _previewRetryAt = new();
    readonly object _cacheLock = new();
    int _placeVersion;

    internal WeatherService(Func<WeatherPlace?, CancellationToken, Task<Report?>>? fetch = null, Func<DateTime>? utcNow = null)
    {
        _fetch = fetch ?? ReadAsync;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }
    static string Key(WeatherPlace? place) => place is null ? "auto" : FormattableString.Invariant($"{place.Latitude:F2},{place.Longitude:F2}");
    internal Report? Cached(WeatherPlace? place)
    {
        lock (_cacheLock) return _cache.TryGetValue(Key(place), out var cached) ? cached.Report : null;
    }
    internal DateTime? UpdatedAt(WeatherPlace? place)
    {
        lock (_cacheLock) return _cache.TryGetValue(Key(place), out var cached) ? cached.At : null;
    }
    internal bool IsStale(WeatherPlace? place) => UpdatedAt(place) is not { } at || _utcNow() - at >= ReadEvery;
    readonly Dictionary<string, Task<Report?>> _previews = new();

    void Cache(WeatherPlace? place, Report report)
    {
        lock (_cacheLock)
        {
            string key = Key(place);
            _cache[key] = (report, _utcNow());
            _previewRetryAt.Remove(key);
            if (_cache.Count > 64)
            {
                string oldest = _cache.OrderBy(entry => entry.Value.At).First().Key;
                _cache.Remove(oldest);
                _previewRetryAt.Remove(oldest);
            }
        }
    }

    internal async Task<Report?> FetchPreviewAsync(WeatherPlace place, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsStale(place) && Cached(place) is { } cached) return cached;
        string key = Key(place);
        TaskCompletionSource<Report?>? completion = null;
        Task<Report?> pending;
        lock (_cacheLock)
        {
            if (!_previews.TryGetValue(key, out pending!))
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = completion.Task;
                _previews[key] = pending;
            }
        }
        if (completion is not null) _ = CompletePreviewAsync(place, token, completion);
        return await pending.WaitAsync(token);
    }

    async Task CompletePreviewAsync(WeatherPlace place, CancellationToken token, TaskCompletionSource<Report?> completion)
    {
        Report? report = null;
        Exception? failure = null;
        bool canceled = false;
        try
        {
            report = await _fetch(place, token);
            token.ThrowIfCancellationRequested();
            if (report is not null)
            {
                Cache(place, report);
                Changed?.Invoke();
            }
            else lock (_cacheLock) _previewRetryAt[Key(place)] = _utcNow() + RetryAfter;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { canceled = true; }
        catch (Exception ex)
        {
            lock (_cacheLock) _previewRetryAt[Key(place)] = _utcNow() + RetryAfter;
            failure = ex;
        }
        finally { lock (_cacheLock) _previews.Remove(Key(place)); }
        // Remove the old flight before callers can start an immediate retry.
        if (canceled) completion.TrySetCanceled(token);
        else if (failure is not null) completion.TrySetException(failure);
        else completion.TrySetResult(report);
    }

    internal async Task PreloadAsync(IEnumerable<WeatherPlace> places, CancellationToken token)
    {
        using var slots = new SemaphoreSlim(3);
        var selected = WeatherLocation.Read(Settings.WeatherPlace);
        async Task Load(WeatherPlace place)
        {
            try
            {
                await slots.WaitAsync(token);
                try
                {
                    // Background retries are throttled; an explicit search/selection may retry immediately.
                    lock (_cacheLock)
                        if (_previewRetryAt.TryGetValue(Key(place), out var retryAt) && _utcNow() < retryAt) return;
                    await FetchPreviewAsync(place, token);
                }
                finally { slots.Release(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException) { App.Log(ex); }
        }
        await Task.WhenAll(places.Where(place => place.Valid && !WeatherCities.Same(place, selected))
            .DistinctBy(Key).Select(Load));
    }

    internal void Stop() { _placeVersion++; _request?.Cancel(); _request = null; }

    public event Action? Changed;

    public Stage State { get; private set; } = Settings.Weather ? Stage.Loading : Stage.Off;

    public Report? Now { get; private set; }
    public string PlaceName { get; private set; } = WeatherLocation.Read(Settings.WeatherPlace)?.DisplayName ?? "Моё расположение";

    public Task SetPlaceAsync(WeatherPlace? place)
    {
        if (place is not null && !place.Valid) throw new ArgumentException("Invalid weather location.", nameof(place));
        Settings.WeatherPlace = place is null ? "" : JsonSerializer.Serialize(place);
        Stop();
        Now = null;
        PlaceName = place?.DisplayName ?? "Моё расположение";
        _readAgainAt = default;
        if (Cached(place) is { } cached)
        {
            Now = cached;
            _readAgainAt = UpdatedAt(place)!.Value + ReadEvery;
        }
        Set(!Settings.Weather ? Stage.Off : Now is null ? Stage.Loading : Stage.Ready);
        _request?.Cancel();
        _request = null;
        return RefreshAsync();
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (!Settings.Weather)
        {
            if (State != Stage.Off) Set(Stage.Off);
            return;
        }
        // No location: wait for SetPlaceAsync (city selection or explicit automatic-location retry).
        if (_request != null || State == Stage.NoPlace || !force && _utcNow() < _readAgainAt) return;

        using var request = new CancellationTokenSource();
        _request = request;
        int version = _placeVersion;
        WeatherPlace? chosen = WeatherLocation.Read(Settings.WeatherPlace);
        if (Now == null) Set(Stage.Loading);
        Stage reached;
        try
        {
            Report? report = await _fetch(chosen, request.Token);
            if (version != _placeVersion || request.IsCancellationRequested) return;
            reached = report is null ? Stage.NoPlace : Stage.Ready;
            if (report is not null)
            {
                Now = report;
                Cache(chosen, report);
            }
        }
        catch (Exception ex)
        {
            if (version != _placeVersion || request.IsCancellationRequested) return;
            if (ex is not (HttpRequestException or TaskCanceledException)) App.Log(ex);
            reached = Stage.Failed;
        }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
        }

        _readAgainAt = _utcNow() + (reached == Stage.Ready ? ReadEvery : RetryAfter);
        // A failed refresh never removes the last successful forecast. The UI can expose its age.
        Set(!Settings.Weather ? Stage.Off : Now is not null ? Stage.Ready : reached);
    }

    void Set(Stage stage)
    {
        State = stage;
        if (stage != Stage.Ready) Now = null;
        Changed?.Invoke();
    }

    async Task<Report?> ReadAsync(WeatherPlace? chosen, CancellationToken token)
    {
        BasicGeoposition? located = chosen is null ? await LocateAsync(token) : new BasicGeoposition { Latitude = chosen.Latitude, Longitude = chosen.Longitude };
        token.ThrowIfCancellationRequested();
        if (located is not { } place) return null;

        string url = string.Create(CultureInfo.InvariantCulture,
            $"{ForecastUrl}&latitude={Math.Round(place.Latitude, PlaceDigits)}&longitude={Math.Round(place.Longitude, PlaceDigits)}");
        using var stream = await Http.GetStreamAsync(url, token);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var report = Parse(json.RootElement);
        return report with { AirQuality = await ReadAirQualityAsync(place, token) };
    }

    static async Task<int?> ReadAirQualityAsync(BasicGeoposition place, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        string url = FormattableString.Invariant($"https://air-quality-api.open-meteo.com/v1/air-quality?current=european_aqi&latitude={Math.Round(place.Latitude, PlaceDigits)}&longitude={Math.Round(place.Longitude, PlaceDigits)}");
        try
        {
            using var stream = await Http.GetStreamAsync(url, timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            return ParseAirQuality(json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        { token.ThrowIfCancellationRequested(); return null; }
    }

    internal static int? ParseAirQuality(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object
        && current.TryGetProperty("european_aqi", out var aqi) && aqi.ValueKind == JsonValueKind.Number
        && aqi.TryGetDouble(out double value) && double.IsFinite(value) && value is >= 0 and <= 1000
        ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : null;

    async Task<BasicGeoposition?> LocateAsync(CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(PlaceTimeout);
            if (await Geolocator.RequestAccessAsync().AsTask(timeout.Token) != GeolocationAccessStatus.Allowed) return null;
            _locator ??= new Geolocator { DesiredAccuracyInMeters = PlaceAccuracyMeters };
            Geoposition found = await _locator.GetGeopositionAsync(PlaceAge, PlaceTimeout).AsTask(timeout.Token);
            return found.Coordinate.Point.Position;
        }
        catch (Exception exception) when (LocationUnavailable(exception) || exception is OperationCanceledException)
        {
            return null;
        }
    }

    internal static bool LocationUnavailable(Exception exception) => exception is UnauthorizedAccessException
        || exception is COMException com && com.HResult is unchecked((int)0x80070422)
            or unchecked((int)0x80070005) or unchecked((int)0x800706BA);

    internal static Report Parse(JsonElement root)
    {
        JsonElement now = root.GetProperty("current"), hourly = root.GetProperty("hourly");
        JsonElement times = hourly.GetProperty("time"), degrees = hourly.GetProperty("temperature_2m");
        JsonElement codes = hourly.GetProperty("weather_code"), days = hourly.GetProperty("is_day");

        var hours = new List<Hour>();
        Hour? previous = null, following = null;
        DateTime current = DateTime.Parse(now.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
        for (int i = 0; i < times.GetArrayLength() && following is null; i++)
        {
            DateTime atTime = DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture);
            int at = atTime.Hour;
            double? Optional(string name) => hourly.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
                && i < values.GetArrayLength() && values[i].ValueKind == JsonValueKind.Number
                && values[i].TryGetDouble(out double value) && double.IsFinite(value) ? value : null;
            var sample = new Hour(at, Whole(degrees[i]), SkyOf(codes[i].GetInt32()), days[i].GetInt32() == 0)
                { Time = atTime, Wind = Optional("wind_speed_10m"), Direction = Optional("wind_direction_10m") };
            if (atTime <= current)
            {
                if (atTime < current.Date.AddHours(current.Hour)) previous = sample;
                continue;
            }
            if (hours.Count < ShownHours) hours.Add(sample); else following = sample;
        }
        var daily = new List<Day>();
        if (root.TryGetProperty("daily", out var dailyDays))
        {
            var dates = dailyDays.GetProperty("time");
            for (int i = 0; i < dates.GetArrayLength() && i < 7; i++)
            {
                DateTime? Solar(string name) => dailyDays.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array && i < values.GetArrayLength()
                    && values[i].ValueKind == JsonValueKind.String && DateTime.TryParse(values[i].GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime value) ? value : null;
                daily.Add(new(DateOnly.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                    Whole(dailyDays.GetProperty("temperature_2m_min")[i]), Whole(dailyDays.GetProperty("temperature_2m_max")[i]),
                    SkyOf(dailyDays.GetProperty("weather_code")[i].GetInt32()), dailyDays.GetProperty("precipitation_sum")[i].GetDouble())
                    { Sunrise = Solar("sunrise"), Sunset = Solar("sunset") });
            }
        }
        return new Report(Whole(now.GetProperty("temperature_2m")), Whole(now.GetProperty("apparent_temperature")),
            Whole(now.GetProperty("wind_speed_10m")), SkyOf(now.GetProperty("weather_code").GetInt32()),
            now.GetProperty("is_day").GetInt32() == 0, hours.ToArray())
        { UtcOffsetSeconds = root.TryGetProperty("utc_offset_seconds", out var offset) && offset.TryGetInt32(out int seconds) ? seconds : 0,
            TimeZoneId = root.TryGetProperty("timezone", out var zone) && zone.ValueKind == JsonValueKind.String ? zone.GetString() : null,
            At = current, PreviousHour = previous, FollowingHour = following, Days = daily.ToArray(), Humidity = now.TryGetProperty("relative_humidity_2m", out var humidity) ? Whole(humidity) : 0 };
    }

    static int Whole(JsonElement number) => (int)Math.Round(number.GetDouble(), MidpointRounding.AwayFromZero);

    static Sky SkyOf(int code) => code switch
    {
        0 or 1 => Sky.Clear,
        2 => Sky.Cloudy,
        3 => Sky.Overcast,
        45 or 48 => Sky.Fog,
        >= 71 and <= 77 or 85 or 86 => Sky.Snow,
        >= 95 => Sky.Storm,
        _ => Sky.Rain,
    };

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppVersion.Product);
        return http;
    }
}
