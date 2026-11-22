using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

// Saved cities are reordered by holding a row and dragging it. The rows move on the list; the order is kept
// when the drag ends, and the dots of the island follow that order because they come from the same list.
public partial class MainWindow
{
    const double CityHoldSeconds = 0.32, CityMoveTolerance = 6;
    readonly List<Border> _savedRows = [];
    DispatcherTimer? _holdTimer;
    Border? _pressRow, _dragRow;
    Action? _pressChoose;
    bool _pressReorderable, _pressMoved, _dragging;
    int _dragOrigin, _dragSlot;
    double _pressY, _dragStartY, _dragShift, _dragOriginTop;
    double[] _dragSlotCenters = [];

    DispatcherTimer CreateHoldTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CityHoldSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            StartCityDrag();
        };
        return timer;
    }

    void BeginCityPress(Border row, bool reorderable, double y, Action choose)
    {
        _pressRow = row;
        _pressReorderable = reorderable;
        _pressChoose = choose;
        _pressY = y;
        _pressMoved = false;
        _holdTimer ??= CreateHoldTimer();
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    void MoveCityPress(Border row, double y)
    {
        if (_pressRow != row) return;
        if (_dragging)
        {
            DragCity(y);
            return;
        }
        // A move beyond the tolerance starts the drag at once; on a row that cannot be reordered it only cancels the click.
        if (Math.Abs(y - _pressY) > CityMoveTolerance)
        {
            _holdTimer?.Stop();
            StartCityDrag();
            if (_dragging) DragCity(y);
            else _pressMoved = true;
        }
    }

    void EndCityPress(Border row, double y)
    {
        if (_pressRow != row) return;
        _holdTimer?.Stop();
        var choose = _pressChoose;
        bool dragged = _dragging;
        bool clean = !_pressMoved;
        if (dragged) FinishCityDrag(y);
        ClearCityPress();
        if (!dragged && clean) choose?.Invoke();
    }

    void StartCityDrag()
    {
        if (_pressRow is null || !_pressReorderable) return;
        _dragOrigin = _dragSlot = _savedRows.IndexOf(_pressRow);
        if (_dragOrigin < 0) return;
        _dragging = true;
        _dragRow = _pressRow;
        WeatherCityList.UpdateLayout();
        _dragSlotCenters = _savedRows.Select(row => row.TranslatePoint(new Point(0, row.ActualHeight / 2), WeatherCityList).Y).ToArray();
        _dragOriginTop = _dragRow.TranslatePoint(new Point(), WeatherCityList).Y;
        _dragShift = 0;
        _dragStartY = _pressY;
        _dragRow.Opacity = 0.7;
        _dragRow.CaptureMouse();
    }

    void DragCity(double y)
    {
        if (_dragRow is null) return;
        double dy = y - _dragStartY;
        double center = _dragSlotCenters[_dragOrigin] + dy;
        int target = Enumerable.Range(0, _dragSlotCenters.Length).MinBy(index => Math.Abs(_dragSlotCenters[index] - center));
        while (_dragSlot < target) MoveDraggedRow(1);
        while (_dragSlot > target) MoveDraggedRow(-1);
        // The row stays under the pointer while the others shift around it.
        _dragRow.RenderTransform = new TranslateTransform(0, dy - _dragShift);
    }

    void MoveDraggedRow(int step)
    {
        int to = _dragSlot + step;
        bool captured = _dragRow!.IsMouseCaptured;
        _savedRows.RemoveAt(_dragSlot);
        _savedRows.Insert(to, _dragRow!);
        WeatherCityList.Children.Remove(_dragRow!);
        int at = to > 0
            ? WeatherCityList.Children.IndexOf(_savedRows[to - 1]) + 1
            : WeatherCityList.Children.IndexOf(_savedRows[1]);
        WeatherCityList.Children.Insert(at, _dragRow!);
        // Layout, margins and section headings determine the row position, never a duplicated pitch constant.
        _dragRow!.RenderTransform = Transform.Identity;
        WeatherCityList.UpdateLayout();
        _dragShift = _dragRow.TranslatePoint(new Point(), WeatherCityList).Y - _dragOriginTop;
        if (captured && !_dragRow.IsMouseCaptured) _dragRow.CaptureMouse();
        _dragSlot = to;
    }

    void FinishCityDrag(double y)
    {
        var row = _dragRow!;
        if (IsOverHomeRow(y) && row.Tag is WeatherPlace dropped)
        {
            row.Opacity = 1;
            row.RenderTransform = Transform.Identity;
            if (row.IsMouseCaptured) row.ReleaseMouseCapture();
            _dragRow = null;
            _dragging = false;
            Safely(() => ReplaceWeatherHomeAsync(dropped));
            return;
        }
        row.RenderTransform = Transform.Identity;
        row.Opacity = 1;
        if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        var order = _savedRows.Select(r => (WeatherPlace)r.Tag!).ToList();
        var rest = _weatherCities.Where(city => !order.Any(o => WeatherCities.Same(o, city))).ToList();
        _weatherCities.Clear();
        _weatherCities.AddRange(order);
        _weatherCities.AddRange(rest);
        SaveWeatherCities();
        _dragRow = null;
        _dragging = false;
        PublishWeather();
    }

    // Section headings precede the home card. Only the card itself is a drop target.
    bool IsOverHomeRow(double y)
    {
        if (_weatherHits.Count > 0 || WeatherCityList.Children.OfType<Border>().FirstOrDefault() is not { } home) return false;
        double top = home.TranslatePoint(new Point(), WeatherCityList).Y;
        return y >= top && y <= top + home.ActualHeight;
    }

    // The dropped city becomes the home city; the previous home city moves to the second place in the list.
    async Task ReplaceWeatherHomeAsync(WeatherPlace place)
    {
        var previous = WeatherLocation.Read(Settings.WeatherHome);
        Settings.WeatherHome = JsonSerializer.Serialize(place);
        _weatherCities.RemoveAll(city => WeatherCities.Same(city, place));
        if (previous is not null && !WeatherCities.Same(previous, place)) _weatherCities.Insert(0, previous);
        SaveWeatherCities();
        PublishWeather();
        await _weather.SetPlaceAsync(place);
    }

    void ClearCityPress()
    {
        _pressRow = null;
        _pressChoose = null;
        _pressReorderable = false;
        _pressMoved = false;
    }
}
