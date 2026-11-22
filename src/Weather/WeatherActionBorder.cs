using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

// Keeps the weather cards' existing pointer/drag behavior while exposing a normal
// keyboard and automation action. Pointer activation is wired by the caller.
public class WeatherActionBorder : Border
{
    Key? _pressedKey;
    AdornerLayer? _focusLayer;
    FocusRing? _focusRing;

    internal Action? InvokeAction { get; set; }

    public WeatherActionBorder()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        IsEnabledChanged += (_, _) => { if (!IsEnabled) _pressedKey = null; };
        Unloaded += (_, _) => { _pressedKey = null; RemoveFocusRing(); };
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ActionPeer(this);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || e.Key is not (Key.Enter or Key.Space)) return;
        if (!e.IsRepeat && _pressedKey is null) _pressedKey = e.Key;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        bool invoke = IsEnabled && _pressedKey == e.Key;
        if (_pressedKey == e.Key) _pressedKey = null;
        e.Handled = true;
        if (invoke) InvokeAction?.Invoke();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (!IsKeyboardFocused || _focusRing is not null) return;
        _focusLayer = AdornerLayer.GetAdornerLayer(this);
        if (_focusLayer is null) return;
        _focusRing = new FocusRing(this);
        _focusLayer.Add(_focusRing);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _pressedKey = null;
        RemoveFocusRing();
    }

    void RemoveFocusRing()
    {
        if (_focusRing is not null) _focusLayer?.Remove(_focusRing);
        _focusRing = null;
        _focusLayer = null;
    }

    sealed class FocusRing : Adorner
    {
        readonly WeatherActionBorder _owner;

        public FocusRing(WeatherActionBorder owner) : base(owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var bounds = new Rect(_owner.RenderSize);
            bounds.Inflate(-2, -2);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            double radius = Math.Max(0, _owner.CornerRadius.TopLeft - 2);
            drawingContext.DrawRoundedRectangle(null, new Pen(Brushes.Black, 3), bounds, radius, radius);
            drawingContext.DrawRoundedRectangle(null, new Pen(Brushes.LightSkyBlue, 1.5), bounds, radius, radius);
        }
    }

    sealed class ActionPeer(WeatherActionBorder owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => nameof(WeatherActionBorder);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        public void Invoke()
        {
            bool enabled = owner.Dispatcher.CheckAccess()
                ? owner.IsEnabled : owner.Dispatcher.Invoke(() => owner.IsEnabled);
            if (!enabled) throw new ElementNotEnabledException();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (owner.IsEnabled) owner.InvokeAction?.Invoke();
            }));
        }
    }
}
