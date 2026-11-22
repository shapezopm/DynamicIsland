using System.Numerics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace DynamicIsland;

sealed class Glass
{
    sealed class Pane
    {
        public readonly SpriteVisual Visual;
        readonly CompositionRoundedRectangleGeometry _outline;
        bool _visible;
        Vector3 _offset;
        Vector2 _size, _cornerRadius;

        public Pane(Compositor compositor, CompositionBrush backdrop)
        {
            _outline = compositor.CreateRoundedRectangleGeometry();
            Visual = compositor.CreateSpriteVisual();
            Visual.Brush = backdrop;
            Visual.Clip = compositor.CreateGeometricClip(_outline);
            Visual.IsVisible = false;
        }

        public void Fit((Rect Box, double Radius) part, Matrix toPixels)
        {
            bool visible = !part.Box.IsEmpty;
            if (_visible != visible) Visual.IsVisible = _visible = visible;
            if (!visible) return;
            Point from = toPixels.Transform(part.Box.TopLeft), to = toPixels.Transform(part.Box.BottomRight);
            Vector3 offset = new((float)from.X, (float)from.Y, 0);
            Vector2 size = new((float)(to.X - from.X), (float)(to.Y - from.Y));
            Vector2 cornerRadius = new((float)(part.Radius * toPixels.M11), (float)(part.Radius * toPixels.M22));
            if (_offset != offset) Visual.Offset = _offset = offset;
            if (_size != size)
            {
                Visual.Size = _outline.Size = size;
                _size = size;
            }
            if (_cornerRadius != cornerRadius) _outline.CornerRadius = _cornerRadius = cornerRadius;
        }
    }

    readonly Window _over;
    Compositor? _compositor;
    DesktopWindowTarget? _target;
    IntPtr _hwnd;
    Pane? _pill, _bubble, _music;

    public Glass(Window over) => _over = over;

    public bool IsOn => _target != null;

    public IntPtr Handle => _hwnd;

    public void Show()
    {
        if (_target != null) return;
        try
        {
            Native.EnsureDispatcherQueue();
            _hwnd = Native.CreateBackdropWindow();
            _compositor = new Compositor();
            _target = DesktopComposition.TargetFor(_compositor, _hwnd);

            CompositionBrush backdrop = _compositor.CreateHostBackdropBrush();
            Windows.UI.Composition.ContainerVisual root = _compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            _pill = new Pane(_compositor, backdrop);
            _bubble = new Pane(_compositor, backdrop);
            _music = new Pane(_compositor, backdrop);
            root.Children.InsertAtTop(_music.Visual);
            root.Children.InsertAtTop(_pill.Visual);
            root.Children.InsertAtTop(_bubble.Visual);
            _target.Root = root;
            Place();
            App.Log("island glass: Windows Composition backdrop active");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Hide();
        }
    }

    public void Hide()
    {
        _target?.Dispose();
        _compositor?.Dispose();
        if (_hwnd != IntPtr.Zero) Native.Destroy(_hwnd);
        _target = null;
        _compositor = null;
        _hwnd = IntPtr.Zero;
        _pill = _bubble = _music = null;
    }

    public void Place()
    {
        if (_hwnd != IntPtr.Zero) Native.PlaceBehind(_hwnd, new WindowInteropHelper(_over).Handle);
    }

    public void Cut((Rect Box, double Radius) pill, (Rect Box, double Radius) bubble, Matrix toPixels)
    {
        _pill?.Fit(pill, toPixels);
        _bubble?.Fit(bubble, toPixels);
    }

    public void CutMusic((Rect Box, double Radius) music, Matrix toPixels) => _music?.Fit(music, toPixels);
}
