namespace DynamicIsland;

using System.Windows;

// The "Полка" switch in settings. Turning it off hides the shelf bubble, its menu row and its page.
// The files stay on the shelf and come back when the switch is turned on again.
public partial class MainWindow
{
    void Shelf_Click(object sender, RoutedEventArgs e)
    {
        Settings.ShelfEnabled = !Settings.ShelfEnabled;
        _clipboardShelfCapture?.SetListening(Settings.ShelfEnabled);
        if (!Settings.ShelfEnabled)
        {
            HideShelfBubble();
            if (_view == View.Shelf) SelectPanel(Panel.None);
        }
        UpdateSwitches(true);
        UpdateView();
        UpdateTargets();
    }
}
