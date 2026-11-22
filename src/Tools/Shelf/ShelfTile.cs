using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

sealed class ShelfTile : Grid
{
    public const double TileWidth = 64;
    const double FrameSize = 56, FrameRadius = 13;
    const double IconSize = 40;
    const double HoverScale = 1.06;

    readonly Border _frame;
    readonly ScaleTransform _frameScale = new(1, 1);
    readonly Border _cross;

    public ShelfTile(Shelf.Item item)
    {
        Item = item;
        Width = WidthFor(item);
        Background = Brushes.Transparent;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(FrameSize) });
        RowDefinitions.Add(new RowDefinition());
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);

        _frame = new Border
        {
            Width = item.IsTextNote ? Width - 8 : FrameSize,
            Height = FrameSize,
            CornerRadius = new CornerRadius(FrameRadius),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Clip = new RectangleGeometry(new Rect(0, 0, item.IsTextNote ? Width - 8 : FrameSize, FrameSize), FrameRadius, FrameRadius),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _frameScale,
        };
        RenderOptions.SetBitmapScalingMode(_frame, BitmapScalingMode.HighQuality);
        Children.Add(_frame);

        var name = new TextBlock
        {
            Text = item.IsTextNote ? "Заметка · " + NoteTime(item.Name) : item.Name,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
        };
        SetRow(name, 1);
        Children.Add(name);

        _cross = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x4A)),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -2, 0),
            Opacity = 0,
            Cursor = Cursors.Hand,
            Child = new Icon { Kind = Glyph.Cross, Width = 10, Height = 10 },
        };
        _cross.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _cross.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Removed?.Invoke(this);
        };
        Children.Add(_cross);

        Cursor = Cursors.Hand;
        ToolTip = item.IsTextNote ? "Открыть и редактировать текст" : item.Name;
        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);
        ShowPicture();
    }

    public Shelf.Item Item { get; }
    public static double WidthFor(Shelf.Item item) => item.IsTextNote ? 144 : TileWidth;

    static string NoteTime(string name) => name.Length >= 24 ? name.Substring(16, 5).Replace('-', ':') : "Текст";

    public event Action<ShelfTile>? Removed;

    public void ShowPicture()
    {
        if (Item.IsTextNote)
        {
            _frame.Background = new SolidColorBrush(Color.FromRgb(30, 32, 39));
            _frame.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 65, 78));
            _frame.BorderThickness = new Thickness(1);
            _frame.Child = new TextBlock
            {
                Text = Item.TextPreview.Length > 0 ? Item.TextPreview : "Текстовая заметка",
                FontSize = 11, LineHeight = 14, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.FromRgb(228, 230, 239)),
                Margin = new Thickness(9, 6, 9, 5), MaxHeight = 42,
            };
            return;
        }
        if (Item.Picture is not { } picture) return;
        FrameworkElement shown = Item.IsPhoto
            ? new Border
            {
                CornerRadius = new CornerRadius(FrameRadius),
                Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill },
            }
            : new Image { Source = picture, Stretch = Stretch.Uniform, Width = IconSize, Height = IconSize };
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(220)));
        _frame.Child = shown;
    }

    void SetHovered(bool on)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _frameScale.AnimateScale(new DoubleAnimation(on ? HoverScale : 1, Ms(on ? 160 : 260)) { EasingFunction = ease });
        _cross.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, Ms(on ? 120 : 220)));
    }
}
