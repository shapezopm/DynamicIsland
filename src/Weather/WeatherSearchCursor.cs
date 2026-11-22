using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland;

// The native caret snaps on/off and its spacing is controlled by the OS. This
// overlay fades smoothly, while its position still follows WPF's text layout.
internal sealed class WeatherSearchCursor
{
    readonly TextBox _search;
    readonly TextBlock _hint;
    readonly Grid _frame;
    readonly Border _cursor;

    internal WeatherSearchCursor(TextBox search, TextBlock hint, Grid frame)
    {
        _search = search;
        _hint = hint;
        _frame = frame;
        search.CaretBrush = Brushes.Transparent;

        var overlay = new Canvas { IsHitTestVisible = false };
        _cursor = new Border
        {
            Width = 1.5, Height = 19, CornerRadius = new CornerRadius(0.75),
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        overlay.Children.Add(_cursor);
        frame.Children.Add(overlay);

        search.TextChanged += (_, _) => Refresh();
        search.SelectionChanged += (_, _) => Refresh();
        search.SizeChanged += (_, _) => Refresh();
        search.GotKeyboardFocus += (_, _) =>
        {
            Refresh();
            if (_cursor.Visibility == Visibility.Visible)
                _cursor.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(650))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            search.Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Loaded);
        };
        search.LostKeyboardFocus += (_, _) =>
        {
            _cursor.BeginAnimation(UIElement.OpacityProperty, null);
            _cursor.Visibility = Visibility.Collapsed;
            Refresh();
        };
        search.Loaded += (_, _) => Refresh();
        Refresh();
    }

    internal void Refresh()
    {
        double textStart = _search.Padding.Left + _search.BorderThickness.Left;
        Rect first = _search.GetRectFromCharacterIndex(0, false);
        if (!first.IsEmpty && double.IsFinite(first.X)) textStart = first.X;
        double x = textStart - _cursor.Width - 0.5;
        if (_search.Text.Length > 0)
        {
            int index = _search.CaretIndex;
            Rect glyph = index > 0
                ? _search.GetRectFromCharacterIndex(index - 1, true)
                : _search.GetRectFromCharacterIndex(0, false);
            if (!glyph.IsEmpty && double.IsFinite(glyph.X)) x = glyph.X;
        }
        Canvas.SetLeft(_cursor, Math.Clamp(x, 0, Math.Max(0, _frame.ActualWidth - _cursor.Width - 2)));
        Canvas.SetTop(_cursor, Math.Max(0, (_frame.ActualHeight - _cursor.Height) / 2));
        if (_search.Text.Length == 0)
            _hint.Margin = new Thickness(textStart, _hint.Margin.Top, _hint.Margin.Right, _hint.Margin.Bottom);
        _cursor.Visibility = _search.IsKeyboardFocusWithin && _search.SelectionLength == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
