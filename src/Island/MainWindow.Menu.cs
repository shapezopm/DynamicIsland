using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double UpdatePagePadding = 14, UpdateNotesWheelStep = 54;
    const int MegabyteShift = 20;
    const double LookHeight = 430, LookTilesHeight = 72, LookWheelStep = 80;
    const double LookEdgeZone = 70, LookEdgeScale = 0.8;
    const int LargestScale = 130, ScaleStep = 5;
    const int LargestGap = 24, GapStep = 2;

    const int FirstSourceRow = 1;
    const int PointerLyricOff = 0, FirstPointerLyricShape = 1;

    static readonly string[] SourceNames = ["LRCLIB", "Deezer", "NetEase", "QQ Music", "Kugou", "Paxsenix", "Genius", "YouTube Music"];

    Border? _openLookTiles;
    readonly Spring _lookScroll = new(0, 260, 30);
    readonly Spring _updateNotesScroll = new(0, 260, 30);
    double _appliedLookScroll = double.NaN;

    void SettingsRow_Click(object sender, RoutedEventArgs e) => ShowPanelAndCheckUpdate(Panel.Settings);

    void UpdateRow_Click(object sender, RoutedEventArgs e) => ShowPanelAndCheckUpdate(Panel.Update);

    void UpdateBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Settings);

    void LookRow_Click(object sender, RoutedEventArgs e)
    {
        _lookScroll.Snap(0);
        _appliedLookScroll = double.NaN;
        ShowPanel(Panel.Look);
    }

    void SettingsBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void Exit_Click(object sender, RoutedEventArgs e) => Exit();

    void ShowPanelAndCheckUpdate(Panel panel)
    {
        ShowPanel(panel);
        _ = _updater.CheckAsync();
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(!Autostart.Enabled); }
        catch (Exception ex) { App.Log(ex); }
        UpdateSwitches(true);
    }

    void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        Settings.Lyrics = !Settings.Lyrics;
        UpdateSwitches(true);
        TrackLyrics();
    }

    async void LyricsRetry_Click(object sender, RoutedEventArgs e)
    {
        try { await _lyrics.RetryAsync(); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    void LyricsRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.LyricsSettings);
    void LyricsSettingsBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Settings);

    void SourcesRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Sources);



    void Source_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: LyricSource picked }) return;
        LyricSource left = Settings.LyricSources ^ picked;
        if (left != 0)
        {
            Settings.LyricSources = left;
            TrackLyrics();
        }
        UpdateSwitches(true);
    }

    void LyricEffects_Click(object sender, RoutedEventArgs e)
    {
        Settings.LyricEffects = !Settings.LyricEffects;
        UpdateSwitches(true);
        RebuildPlayerLyrics();
    }

    void UnsyncedLyrics_Click(object sender, RoutedEventArgs e)
    {
        Settings.UnsyncedLyrics = !Settings.UnsyncedLyrics;
        UpdateSwitches(true);
        // The same document may stay current; force the presentation to re-read the setting.
        _playerPresentationResult = null;
        RefreshLyricsPresentation();
        UpdateLyric();
    }

    void LyricParting_Click(object sender, RoutedEventArgs e)
    {
        Settings.LyricParting = !Settings.LyricParting;
        UpdateSwitches(true);
        StirLyrics();
    }

    void Rim_Click(object sender, RoutedEventArgs e)
    {
        Settings.Rim = !Settings.Rim;
        UpdateSwitches(true);
        SyncRim();
    }

    void AppVolume_Click(object sender, RoutedEventArgs e)
    {
        Settings.AppVolume = !Settings.AppVolume;
        UpdateSwitches(true);
    }

    void Network_Click(object sender, RoutedEventArgs e)
    {
        Settings.Network = !Settings.Network;
        UpdateSwitches(true);
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        Settings.HideFullscreen = !Settings.HideFullscreen;
        UpdateSwitches(true);
        CheckFullscreen();
    }

    void UpdateSwitches(bool animate)
    {
        SyncDiscordSwitches(animate);
        LyricsState.Text = Settings.Lyrics ? "Вкл" : "Выкл";
        LyricsSwitch.Set(Settings.Lyrics, animate);
        LyricEffectsSwitch.Set(Settings.LyricEffects, animate);
        RefreshLyricFxPage();
        UnsyncedLyricsSwitch.Set(Settings.UnsyncedLyrics, animate);
        PointerLyricSegments.Set(Settings.PointerLyric ? FirstPointerLyricShape + (int)Settings.PointerLyricShape : PointerLyricOff, animate);
        LyricPartingSwitch.Set(Settings.LyricParting, animate);
        RimSwitch.Set(Settings.Rim, animate);
        AppVolumeSwitch.Set(Settings.AppVolume, animate);
        NetworkSwitch.Set(Settings.Network, animate);
        WeatherSwitch.Set(Settings.Weather, animate);
        ShelfSwitch.Set(Settings.ShelfEnabled, animate);
        ShelfMenuRow.Visibility = Settings.ShelfEnabled ? Visibility.Visible : Visibility.Collapsed;
        MenuRows.InvalidateMeasure();
        MenuRows.Measure(new Size(MenuView.Width, double.PositiveInfinity));
        MenuView.Height = Math.Ceiling(MenuRows.DesiredSize.Height);
        FullscreenSwitch.Set(Settings.HideFullscreen, animate);
        AutostartSwitch.Set(Autostart.Enabled, animate);

        foreach (ToggleButton chip in SourceChips.Children)
            chip.IsChecked = chip.Tag is LyricSource source && Settings.LyricSources.HasFlag(source);
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updater.State == Updater.Stage.Available)
        {
            if (await _updater.InstallAsync()) Exit();
        }
        else await _updater.CheckAsync(force: true);
    }

    void RefreshUpdatePage()
    {
        Updater.Stage stage = _updater.State;
        bool loading = stage == Updater.Stage.Loading, found = loading || stage == Updater.Stage.Available;
        Version version = found ? _updater.LatestVersion! : Updater.CurrentVersion;

        UpdateText.Foreground = found ? _orange : _dim;
        UpdateText.Text = loading ? _updater.Percent + "%" : "v" + version;

        UpdateVersion.Text = version.ToString();
        UpdateFrom.Text = stage switch
        {
            Updater.Stage.Unconfigured => "Канал обновлений не настроен",
            Updater.Stage.Checking => "проверяю…",
            Updater.Stage.Latest => "последняя версия",
            Updater.Stage.Failed => "не получилось",
            _ => found ? "вместо " + Updater.CurrentVersion : "",
        };
        UpdateNotes.SetVisible(found && _updater.Notes.Length > 0);
        string notes = string.Join('\n', _updater.Notes.Select(note => "·  " + note));
        if (UpdateNotesText.Text != notes) _updateNotesScroll.Snap(0);
        UpdateNotesText.Text = notes;

        UpdateButton.SetVisible(!loading && stage != Updater.Stage.Unconfigured);
        UpdateButton.IsEnabled = stage != Updater.Stage.Checking;
        UpdateLoad.SetVisible(loading);
        UpdateButton.Content = stage switch
        {
            Updater.Stage.Available => "Обновить и перезапустить",
            Updater.Stage.Checking => "Проверяю…",
            Updater.Stage.Failed => "Попробовать ещё раз",
            _ => "Проверить ещё раз",
        };
        if (found) UpdateButton.Background = _orange;
        else UpdateButton.ClearValue(BackgroundProperty);
        UpdateButton.Foreground = found ? Brushes.Black : Brushes.White;

        if (loading) ShowUpdateProgress();
        FitUpdatePage();
        UpdateView();
    }

    void ShowUpdateProgress()
    {
        UpdateBytes.Text = $"{_updater.DownloadedBytes >> MegabyteShift} из {_updater.TotalBytes >> MegabyteShift} МБ";
        LoadingText.Text = _updater.Percent + "%";
        var fill = new DoubleAnimation(_updater.Percent / 100.0, Ms(200));
        UpdateRing.BeginAnimation(Ring.ProgressProperty, fill);
        LoadingRing.BeginAnimation(Ring.ProgressProperty, fill);
    }

    void FitUpdatePage()
    {
        UpdateBody.Measure(new Size(UpdatePage.Width, double.PositiveInfinity));
        double height = Math.Ceiling(UpdateBody.DesiredSize.Height) + UpdatePagePadding;
        if (height == UpdatePage.Height) return;

        UpdatePage.Height = height;
        if (_view == View.Update) UpdateTargets();
    }

    double UpdateNotesOverflow => Math.Max(UpdateNotesText.ActualHeight - UpdateNotesBox.ActualHeight, 0);

    void ScrollUpdateNotes(int notches)
    {
        _updateNotesScroll.Target = Math.Clamp(_updateNotesScroll.Target + notches * UpdateNotesWheelStep, 0, UpdateNotesOverflow);
        StartShapeLoop();
    }

    void ApplyUpdateNotesScroll()
    {
        double scrolled = Math.Min(_updateNotesScroll.Value, UpdateNotesOverflow);
        UpdateNotesMove.Y = -scrolled;
        UpdateNotesEdgeTop.Color = EdgeFade(scrolled);
        UpdateNotesEdgeBottom.Color = EdgeFade(UpdateNotesOverflow - scrolled);
    }

    void SizeSlider_Changed(object? sender, EventArgs e) => SetScale((int)SizeSlider.Value);

    void GapSlider_Changed(object? sender, EventArgs e) => SetGap((int)GapSlider.Value);

    void SetScale(int percent)
    {
        int was = Settings.Scale;
        Settings.Scale = percent;
        if (Settings.Scale != was) ApplyLook();
    }

    void SetGap(int px)
    {
        int was = Settings.Gap;
        Settings.Gap = px;
        if (Settings.Gap != was) ApplyLook();
    }

    void Dots_Click(object sender, RoutedEventArgs e)
    {
        Eq.Dots = Settings.Dots = DotsSegments.PickUnderPointer() == 1;
        RefreshLookPage();
    }

    void Glass_Click(object sender, RoutedEventArgs e)
    {
        Settings.Glass = !Settings.Glass;
        RefreshLookPage();
        SyncGlass(true);
    }

    void SeekStyleRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(SeekStyleTiles);

    void SeekStyleTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Content: SeekPreview preview }) return;
        Settings.LineBar = preview.Kind == SeekPreviewKind.Lines;
        Settings.SeekWave = preview.Kind == SeekPreviewKind.Wave;
        RefreshLookPage();
        SyncSeekStyle(true);
    }

    void SeekHoverRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(SeekHoverTiles);

    void SeekHoverTile_Click(object sender, RoutedEventArgs e)
    {
        if (UseTrackShape) return;
        int index = SeekHoverStrip.Children.IndexOf((UIElement)sender);
        if (Settings.SeekWave)
        {
            // Для «Волна» доступна только «Лупа», её можно выключить и включить.
            if (index == 0)
            {
                Settings.SeekHoverOn = !Settings.SeekHoverOn;
                HideSeekNote();
            }
            else ShowSeekNote(index == 1 ? SeekLiftNote : SeekRippleNote);
            RefreshLookPage();
            return;
        }
        Settings.SeekHover = (SeekHover)index;
        RefreshLookPage();
    }

    // Подсказка «Недоступно для «Волна»» на плитках «Строка» и «Волна» — 2.5 с, затем плавно гаснет.
    DelayedAction? _seekNoteTimer;
    Border? _seekNote;

    void ShowSeekNote(Border note)
    {
        if (_seekNote is { } shown && shown != note) shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(160)));
        _seekNote = note;
        note.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(200)));
        _seekNoteTimer ??= new DelayedAction(HideSeekNote);
        _seekNoteTimer.Start(TimeSpan.FromSeconds(2.5));
    }

    void HideSeekNote()
    {
        _seekNoteTimer?.Cancel();
        if (_seekNote is not { } note) return;
        note.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(320)));
        _seekNote = null;
    }

    void AccentRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(AccentTiles);

    void SeekAccentRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(SeekAccentTiles);

    void CompactTrackAccentRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(CompactTrackAccentTiles);

    void BackdropRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(BackdropTiles);

    void LyricChangeRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(LyricChangeTiles);

    void HoverRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(HoverTiles);

    void BackdropTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.Backdrop = (Backdrop)BackdropStrip.Children.IndexOf((UIElement)sender);
        RefreshLookPage();
        SyncBackdrop();
    }

    void LyricChangeTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.LyricChange = (LyricChange)LyricChangeStrip.Children.IndexOf((UIElement)sender);
        RefreshLookPage();
    }

    void HoverTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.Hover = (Hover)HoverStrip.Children.IndexOf((UIElement)sender);
        RefreshLookPage();
    }

    void ToggleLookTiles(Border tiles)
    {
        _openLookTiles = tiles == _openLookTiles ? null : tiles;
        _appliedLookScroll = double.NaN;
        SlideLookTiles(BackdropTiles, BackdropChevron);
        SlideLookTiles(LyricChangeTiles, LyricChangeChevron);
        SlideLookTiles(HoverTiles, HoverChevron);
        SlideLookTiles(SeekStyleTiles, SeekStyleChevron);
        SlideLookTiles(SeekHoverTiles, SeekHoverChevron);
        SlideLookTiles(AccentTiles, AccentChevron);
        SlideLookTiles(SeekAccentTiles, SeekAccentChevron);
        SlideLookTiles(CompactTrackAccentTiles, CompactTrackAccentChevron);
    }

    void SlideLookTiles(Border tiles, Icon chevron)
    {
        // Высота раскрытия задаётся в XAML через Tag (у строк с цветами она меньше плиток).
        double height = tiles.Tag is string tag ? double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture) : LookTilesHeight;
        SlideSettingsTiles(tiles, chevron, height, () => tiles == _openLookTiles);
    }

    static void SlideSettingsTiles(Border tiles, Icon chevron, double height, Func<bool> isOpen, Action? onClosed = null)
    {
        bool open = isOpen();
        if (!open && tiles.Visibility != Visibility.Visible) return;

        tiles.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(open ? height : 0, Ms(open ? 320 : 240)) { EasingFunction = ease };
        if (!open)
            slide.Completed += (_, _) =>
            {
                if (isOpen() || tiles.Height > 0) return;
                tiles.Visibility = Visibility.Collapsed;
                onClosed?.Invoke();
            };
        tiles.BeginAnimation(HeightProperty, slide);
        tiles.Child.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, Ms(open ? 260 : 160)));
        chevron.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 90 : 0, Ms(240)) { EasingFunction = ease });
    }

    double LookOverflow => Math.Max(LookRows.ActualHeight + LookRows.Margin.Top + LookRows.Margin.Bottom - LookView.Height, 0);

    void LookRows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _appliedLookScroll = double.NaN;
        double reveal = _openLookTiles == null ? 0
            : _openLookTiles.TranslatePoint(new Point(0, _openLookTiles.ActualHeight), LookRows).Y + LookRows.Margin.Top + LookRows.Margin.Bottom - LookView.Height;
        _lookScroll.Target = Math.Clamp(Math.Max(_lookScroll.Target, reveal), 0, LookOverflow);
        StartShapeLoop();
    }

    void ScrollLook(int notches)
    {
        _lookScroll.Target = Math.Clamp(_lookScroll.Target + notches * LookWheelStep, 0, LookOverflow);
        StartShapeLoop();
    }

    void ShrinkLookEdges(double scrolled)
    {
        double top = Math.Clamp(scrolled / LookEdgeZone, 0, 1), bottom = Math.Clamp((LookOverflow - scrolled) / LookEdgeZone, 0, 1);
        foreach (FrameworkElement row in LookRows.Children)
        {
            double middle = VisualTreeHelper.GetOffset(row).Y + LookRows.Margin.Top + row.ActualHeight / 2 - scrolled;
            double fromTop = Math.Clamp(middle / LookEdgeZone, 0, 1), fromBottom = Math.Clamp((LookView.Height - middle) / LookEdgeZone, 0, 1);
            double shrink = Math.Max(top * Smooth(1 - fromTop), bottom * Smooth(1 - fromBottom));

            if (row.RenderTransform is not ScaleTransform size)
            {
                row.RenderTransform = size = new ScaleTransform();
                row.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            size.ScaleX = size.ScaleY = 1 - (1 - LookEdgeScale) * shrink;
        }
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    void ApplyLookScroll()
    {
        double scrolled = _lookScroll.Value;
        if (!LookView.IsVisible || _appliedLookScroll == scrolled) return;
        _appliedLookScroll = scrolled;
        LookEdgeTop.Color = EdgeFade(scrolled);
        LookEdgeBottom.Color = EdgeFade(LookOverflow - scrolled);
        ShrinkLookEdges(scrolled);
        if (LookMove.Y == -scrolled) return;

        LookMove.Y = -scrolled;
        if (_view == View.Look) LookRows.FollowPointer();
    }

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        Settings.Accent = SwatchColor((RadioButton)sender);
        RefreshLookPage();
        SyncAccent();
        SyncRim();
    }

    void SeekAccent_Click(object sender, RoutedEventArgs e)
    {
        Settings.SeekAccent = SwatchColor((RadioButton)sender);
        RefreshLookPage();
        SyncSeekAccent();
    }

    void CompactTrackAccent_Click(object sender, RoutedEventArgs e)
    {
        Settings.CompactTrackAccent = SwatchColor((RadioButton)sender);
        RefreshColorSwatches(CompactTrackAccentStrip, CompactTrackAccentText, Settings.CompactTrackAccent);
        SyncCompactTrackAccent();
    }

    static Color? SwatchColor(RadioButton dot) => dot.Background is SolidColorBrush brush ? brush.Color : null;

    static void RefreshColorSwatches(UniformGrid palette, TextBlock label, Color? selected)
    {
        foreach (RadioButton dot in palette.Children)
        {
            if (SwatchColor(dot) != selected) continue;
            dot.IsChecked = true;
            label.Text = (string)dot.Tag;
            break;
        }
    }

    void RefreshLookPage()
    {
        SizeText.Text = Settings.Scale + "%";
        GapText.Text = Settings.Gap + " px";
        SizeSlider.Set(Settings.Scale, LookView.IsVisible);
        GapSlider.Set(Settings.Gap, LookView.IsVisible);
        DotsSegments.Set(Settings.Dots ? 1 : 0, LookView.IsVisible);
        SeekPreviewKind seekStyle = Settings.SeekWave ? SeekPreviewKind.Wave : Settings.LineBar ? SeekPreviewKind.Lines : SeekPreviewKind.TrackShape;
        foreach (RadioButton tile in SeekStyleStrip.Children)
        {
            if (tile.Content is not SeekPreview preview || preview.Kind != seekStyle) continue;
            tile.IsChecked = true;
            SeekStyleText.Text = (string)tile.Tag;
            break;
        }
        SeekHoverRow.Visibility = UseTrackShape ? Visibility.Collapsed : Visibility.Visible;
        if (UseTrackShape)
        {
            if (_openLookTiles == SeekHoverTiles) ToggleLookTiles(SeekHoverTiles);
        }
        else if (Settings.SeekWave)
        {
            // Для волны: только «Лупа», может быть выключена (тогда ничего не отмечено).
            SeekHoverText.Text = Settings.SeekHoverOn ? "Лупа" : "Выключено";
            foreach (RadioButton tile in SeekHoverStrip.Children) tile.IsChecked = false;
            if (Settings.SeekHoverOn) ((RadioButton)SeekHoverStrip.Children[0]).IsChecked = true;
        }
        else
        {
            SeekHoverText.Text = Settings.SeekHover switch { SeekHover.Lift => "Строка", SeekHover.Wave => "Волна", _ => "Лупа" };
            ((RadioButton)SeekHoverStrip.Children[(int)Settings.SeekHover]).IsChecked = true;
        }
        GlassSwitch.Set(Settings.Glass, LookView.IsVisible);
        ((RadioButton)BackdropStrip.Children[(int)Settings.Backdrop]).IsChecked = true;
        ((RadioButton)LyricChangeStrip.Children[(int)Settings.LyricChange]).IsChecked = true;
        ((RadioButton)HoverStrip.Children[(int)Settings.Hover]).IsChecked = true;
        BackdropText.Text = Settings.Backdrop switch
        {
            Backdrop.Matrix => "Матрица",
            Backdrop.Stars => "Звёзды",
            Backdrop.MatrixAndStars => "Матрица и звёзды",
            _ => "Свечение",
        };
        LyricChangeText.Text = Settings.LyricChange switch
        {
            LyricChange.Wave => "Волна по буквам",
            LyricChange.Drum => "Барабан по словам",
            _ => "Плавно",
        };
        HoverText.Text = Settings.Hover switch
        {
            Hover.Magnet => "Магнит",
            Hover.Flow => "Перетекание",
            _ => "Диск",
        };
        RefreshColorSwatches(AccentStrip, AccentText, Settings.Accent);
        RefreshColorSwatches(SeekAccentStrip, SeekAccentText, Settings.SeekAccent);
        RefreshColorSwatches(CompactTrackAccentStrip, CompactTrackAccentText, Settings.CompactTrackAccent);
    }

    void ApplyLook()
    {
        RefreshLookPage();
        SyncSeekAccent();
        _userScale.Target = Settings.Scale / 100.0;
        _topGap.Target = Settings.Gap;
        UpdateTargets();
    }
}
