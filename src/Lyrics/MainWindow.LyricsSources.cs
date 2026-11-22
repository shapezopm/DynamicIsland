using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland;

public partial class MainWindow
{
    bool _lyricsSourceDiagnostics;
    const double SourcesWidth = 360, SourcesHeight = 306, DiagnosticsWidth = 420, DiagnosticsHeight = 460;

    void RefreshLyricsSourceButton(LyricsResult result)
    {
        PlayerLyricSourcesAction.Visibility = _media.HasTrack && Settings.Lyrics ? Visibility.Visible : Visibility.Collapsed;
        string label = result.Candidate?.ProviderId ?? "Источники";
        string content = label + (_lyrics.IsSearchingSources ? " …" : "") + " ›";
        if (!Equals(PlayerLyricSourcesAction.Content, content)) PlayerLyricSourcesAction.Content = content;
        if (_panel == Panel.LyricsSources) RefreshLyricsSourcePanel();
    }

    void LyricsSources_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CurrentLyricsResult().Track is null) return;
        _lyricsSourceDiagnostics = false;
        // Leaving the report earlier (by the island timeout) must not keep text entry open.
        SetTextEntry(false);
        RefreshLyricsSourcePanel();
        ShowPanel(Panel.LyricsSources);
        if (!Island.IsMouseOver) _collapseTimeout.Start(LongCollapseDelay);
    }

    void RefreshLyricsSourcePanel()
    {
        LyricsResult result = CurrentLyricsResult();
        TrackSnapshot? track = result.Track;
        TrackSnapshot? lookup = _lyrics.SearchTrack ?? track;
        LyricsSourcesTitle.Text = lookup is null ? "Трек сменился" : lookup.Artist + " — " + lookup.Title;
        LyricsSourcesTitle.ToolTip = track is null ? null : "Исходные данные: " + track.Artist + " — " + track.Title;
        LyricsSourceRows.Children.Clear();
        if (track is not null)
        {
            // Галочка стоит: снятие отключает текст для трека. Галочка снята: возвращает автовыбор.
            AddSourceRow("Автоматически", !_lyrics.HasManualSelection && !_lyrics.IsOff, true,
                async () =>
                {
                    if (!_lyrics.HasManualSelection && !_lyrics.IsOff)
                    {
                        _lyrics.TurnOff(track);
                        RefreshLyricsSourcePanel();
                        return;
                    }
                    await _lyrics.UseAutomaticAsync(track);
                }, "Лучший доступный текст");
            foreach (LyricsCandidate candidate in _lyrics.Candidates)
                AddSourceRow(candidate.ProviderId + " · " + (LyricsMatcher.Manual(_lyrics.SearchTrack ?? track, candidate).Status
                    is LyricsStatus.Synced or LyricsStatus.AwaitingVerification ? "по строкам" : "обычный текст"),
                    SameLyricsSource(candidate, result.Candidate), LyricsSearchPreferences.ValidCandidate(candidate),
                    () => { _lyrics.SelectCandidate(track, candidate); }, candidate.Artist + " — " + candidate.Title);
        }
        if (_lyrics.Candidates.Length == 0)
            LyricsSourceRows.Children.Add(new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(14, 255, 255, 255)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new(1), CornerRadius = new(12), Margin = new(2, 3, 2, 3), Padding = new(12, 10, 12, 10),
                Child = new TextBlock
                {
                    Text = _lyrics.IsSearchingSources ? "Поиск продолжается…" : result.Status == LyricsStatus.RateLimited
                        ? "Источник ограничил запросы. Повторим позже" : result.Status == LyricsStatus.ProviderError
                        ? "Источник временно недоступен" : "Подходящих вариантов пока нет",
                    Foreground = _dim, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                },
            });
        LyricsSourceRetry.IsEnabled = track is not null && !_lyrics.HasManualSelection && !_lyrics.IsOff && !_lyrics.IsSearchingSources
            && (result.RetryAt is null || result.RetryAt <= DateTimeOffset.UtcNow);
        LyricsSourceOriginal.IsEnabled = track is not null && result.Candidate is not null;
        LyricsSourceScroll.Visibility = _lyricsSourceDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        LyricsSourceDiagnostics.Visibility = _lyricsSourceDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        LyricsDiagnosticsCaption.Visibility = _lyricsSourceDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        LyricsSourceActions.Visibility = _lyricsSourceDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        LyricsDiagnosticsCopy.Visibility = _lyricsSourceDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        LyricsSourceDiagnosticAction.Content = _lyricsSourceDiagnostics ? "К источникам" : "Диагностика";
        if (_lyricsSourceDiagnostics)
        {
            string report = JsonSerializer.Serialize(_lyrics.Diagnostics.Snapshot(), new JsonSerializerOptions { WriteIndented = true });
            // Assigning identical text again would drop the selection the user is copying from.
            if (LyricsSourceDiagnostics.Text != report) LyricsSourceDiagnostics.Text = report;
        }
        double width = _lyricsSourceDiagnostics ? DiagnosticsWidth : SourcesWidth;
        double height = _lyricsSourceDiagnostics ? DiagnosticsHeight : 164 + Math.Min(142, LyricsSourceRows.Children.Count * 58);
        if (LyricsSourcesView.Width != width || LyricsSourcesView.Height != height)
        {
            LyricsSourcesView.Width = width;
            LyricsSourcesView.Height = height;
            // The island shape is fixed per view (as FitRecordSetPage does); the report needs its own size.
            PillShapes[View.LyricsSources] = _lyricsSourceDiagnostics ? new(width, height, 34) : new(SourcesWidth, SourcesHeight, 34);
            if (_view == View.LyricsSources) UpdateTargets();
        }
    }

    static bool SameLyricsSource(LyricsCandidate candidate, LyricsCandidate? selected) => selected is not null
        && candidate.ProviderId == selected.ProviderId && candidate.RecordId == selected.RecordId
        && candidate.SourceUrl == selected.SourceUrl && candidate.Title == selected.Title && candidate.Artist == selected.Artist
        && candidate.Duration == selected.Duration && candidate.Lines.SequenceEqual(selected.Lines);

    void AddSourceRow(string label, bool selected, bool enabled, Action action, string? hint = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(hint))
            copy.Children.Add(new TextBlock { Text = hint, FontSize = 10.5, Foreground = _dim,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 2, 0, 0) });
        grid.Children.Add(copy);
        var check = new TextBlock { Text = selected ? "✓" : "", FontSize = 15,
            Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(check, 1);
        grid.Children.Add(check);
        var row = new Button { Style = (Style)FindResource("SourceOptionButton"), Content = grid,
            IsEnabled = enabled, ToolTip = hint, Background = new System.Windows.Media.SolidColorBrush(
                selected ? System.Windows.Media.Color.FromArgb(48, 255, 255, 255)
                         : System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                selected ? System.Windows.Media.Color.FromArgb(128, 255, 255, 255)
                         : System.Windows.Media.Color.FromArgb(34, 255, 255, 255)) };
        row.Click += (_, e) => { e.Handled = true; action(); };
        LyricsSourceRows.Children.Add(row);
    }

    void LyricsSourcesBack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SetTextEntry(false);
        ShowPanel(Panel.Player);
    }

    async void LyricsSourceRetry_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CurrentLyricsResult().Track is { } track) await _lyrics.RetryAsync(track);
    }

    void LyricsSourceOriginal_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenLyricsEditor(showSource: true); }

    void LyricsDiagnosticsCopy_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(LyricsSourceDiagnostics.Text)) Clipboard.SetText(LyricsSourceDiagnostics.Text);
    }

    void LyricsSourceDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _lyricsSourceDiagnostics = !_lyricsSourceDiagnostics;
        // Refresh first: the field must be visible before it can take keyboard focus for Ctrl+A / Ctrl+C.
        RefreshLyricsSourcePanel();
        SetTextEntry(_lyricsSourceDiagnostics, LyricsSourceDiagnostics);
    }

    void LyricsSourceScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroll) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0);
        else if (sender is TextBox box) box.ScrollToVerticalOffset(box.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }
}
