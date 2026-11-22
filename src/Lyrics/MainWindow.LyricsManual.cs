using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DynamicIsland;

// Ручной поиск текста по исполнителю и названию: запрос уходит в источники как введён,
// без подстановки из плеера и без проверки длительности трека.
public partial class MainWindow
{
    // Островное окно открыто с WS_EX_NOACTIVATE: без включения клавиатуры поля не принимают ввод.
    // Тот же приём, что у редактора текста и погоды. Пока поле открыто, автосворачивание не работает.
    bool _textEntryOpen;

    void SetTextEntry(bool on, TextBox? field = null)
    {
        _textEntryOpen = on;
        Native.AllowKeyboardInput(_hwnd, on);
        Focusable = on;
        if (on)
        {
            _collapseTimeout.Cancel();
            Activate();
            TextBox target = field ?? LyricsManualArtist;
            Dispatcher.InvokeAsync(() =>
            {
                if (!_textEntryOpen) return;
                Keyboard.Focus(target);
                target.CaretIndex = target.Text.Length;
            }, DispatcherPriority.Input);
        }
        else Keyboard.ClearFocus();
    }

    void LyricsSourceEdit_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CurrentLyricsResult().Track is not { } track) return;
        TrackSnapshot lookup = _lyrics.SearchTrack ?? track;
        LyricsManualArtist.Text = lookup.Artist;
        LyricsManualTitle.Text = lookup.Title;
        ShowPanel(Panel.LyricsManual);
        SetTextEntry(true, LyricsManualArtist);
    }

    async void LyricsManualFind_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SubmitLyricsManualSearchAsync();
    }

    async Task SubmitLyricsManualSearchAsync()
    {
        if (_panel != Panel.LyricsManual) return;
        if (CurrentLyricsResult().Track is not { } track) return;
        if (string.IsNullOrWhiteSpace(LyricsManualTitle.Text))
        {
            LyricsManualTitle.Focus();
            return;
        }
        Task search = _lyrics.CorrectSearchAsync(track, LyricsManualTitle.Text, LyricsManualArtist.Text);
        ReturnToLyricsSources();
        await search;
    }

    async void LyricsManual_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (!e.IsRepeat) await SubmitLyricsManualSearchAsync();
    }

    async void LyricsManualReset_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CurrentLyricsResult().Track is not { } track) return;
        await _lyrics.CorrectSearchAsync(track, "", "", reset: true);
        ReturnToLyricsSources();
    }

    void LyricsManualBack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ReturnToLyricsSources();
    }

    void ReturnToLyricsSources()
    {
        SetTextEntry(false);
        RefreshLyricsSourcePanel();
        ShowPanel(Panel.LyricsSources);
    }
}
