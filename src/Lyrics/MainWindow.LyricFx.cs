using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland;

// «Эффекты текста» на вкладке «Текст песен»: раскрывающийся список из четырёх стилей, без доп. настроек.
public partial class MainWindow
{
    bool _lyricFxOpen;
    bool _lyricSourceListOpen = true;
    const double LyricSourceListHeight = 136;

    void LyricFxRow_Click(object sender, RoutedEventArgs e) => ToggleLyricFx();

    void LyricFxTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.TextEffect = (LyricFx)LyricFxStrip.Children.IndexOf((UIElement)sender);
        RefreshLyricFxPage();
        RebuildPlayerLyrics();
    }

    void RefreshLyricFxPage()
    {
        int style = (int)Settings.TextEffect;
        LyricFxText.Text = style switch
        {
            0 => "Выкл",
            1 => "Подъём",
            2 => "Сияние",
            _ => "Перелив",
        };
        for (int i = 0; i < LyricFxStrip.Children.Count; i++)
            ((RadioButton)LyricFxStrip.Children[i]).IsChecked = i == style;
    }

    void ToggleLyricFx()
    {
        _lyricFxOpen = !_lyricFxOpen;
        double tilesHeight = LyricFxStrip.Height + LyricFxStrip.Margin.Top + LyricFxStrip.Margin.Bottom;
        SlideSettingsTiles(LyricFxTiles, LyricFxChevron, tilesHeight, () => _lyricFxOpen,
            SyncLyricsSettingsHeight);
    }

    void LyricFxTiles_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) SyncLyricsSettingsHeight();
    }

    void LyricsSourceList_Click(object sender, RoutedEventArgs e)
    {
        _lyricSourceListOpen = !_lyricSourceListOpen;
        SlideSettingsTiles(LyricsSourceList, LyricsSourceListChevron, LyricSourceListHeight,
            () => _lyricSourceListOpen, SyncLyricsSettingsHeight);
    }

    void LyricsSourceList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) SyncLyricsSettingsHeight();
    }

    void SyncLyricsSettingsHeight()
    {
        LyricsPage.Height = PillShapes[View.LyricsSettings].Height + LyricFxTiles.Height
            + LyricsSourceList.Height - LyricSourceListHeight;
        if (_view != View.LyricsSettings) return;
        UpdateTargets();
        // Рамка следует за тем же раскрытием, что и строки, без второй анимации высоты.
        if (!_morphing) _height.Snap(_height.Target);
    }
}
