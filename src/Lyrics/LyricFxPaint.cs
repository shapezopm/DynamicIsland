using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

// Кисти живого текста. Цвет перелива — тот же, что у полосы трека: свой выбор или цвет из обложки.
// Значение задаёт MainWindow.SyncSeekAccent; пока оно не задано, берётся ресурс «Indigo».
static class LyricFxPaint
{
    public static Color? Outline { get; set; }

    public static Color Accent(FrameworkElement owner) =>
        Outline ?? (owner.TryFindResource("Indigo") is SolidColorBrush brush ? brush.Color : Colors.LightSteelBlue);

    public static Brush LetterBrush(FrameworkElement owner, double shade) =>
        shade <= 0 ? Brushes.White : new SolidColorBrush(Lerp(Colors.White, Accent(owner), shade));

    static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));
}
