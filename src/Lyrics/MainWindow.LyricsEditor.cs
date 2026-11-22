using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace DynamicIsland;

// Big lyrics container: opened from the small lyrics shelf in the player.
// Edits are kept in memory for the song until the island restarts; they never touch the timing of synced lines.
public partial class MainWindow
{
    const int FineAdvanceMs = LyricsOffsetStore.StepMs, CoarseAdvanceMs = 500;
    const string LyricsNoteIdle = "Текст можно править. Тайминг строк при этом не меняется.";
    const string LyricsNoteEdited = "Текст изменён. Правки хранятся до перезапуска острова.";
    static readonly UTF8Encoding Utf8WithBom = new(true);

    sealed record LyricsEdit(string Title, string Artist, string Album, string SourceApp, string Document, string Text)
    {
        public bool Matches(TrackSnapshot? track, string document) => Document == document && track is not null && track.Title == Title && track.Artist == Artist
            && track.Album == Album && track.SourceApp == SourceApp;
    }

    LyricsEdit? _lyricsEdit;
    TrackSnapshot? _lyricsEditorTrack;
    string _lyricsEditorOriginal = "";
    bool _lyricsEditorOpen, _loadingLyricsEditor;
    bool _lyricsEditorRaw;
    string _lyricsEditorDocument = "";

    static bool SameSong(TrackSnapshot a, TrackSnapshot b) => a.Title == b.Title && a.Artist == b.Artist
        && a.Album == b.Album && a.SourceApp == b.SourceApp;

    // ---- small shelf: the "Полный текст" chip and the per-track timing buttons

    void ShowEditorChip(LyricsPresentation presentation)
    {
        PlayerLyricFullAction.Visibility = presentation.CanOpenEditor ? Visibility.Visible : Visibility.Collapsed;
        PlayerLyricFullAction.Content = "Полный текст";
    }

    void SyncLyricAdjust(LyricsPresentation presentation)
    {
        SyncLyricAdvance();
        bool available = presentation.ShowTimed && _lyricOffsetKey is not null;
        Visibility visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (PlayerLyricSyncBox.Visibility != visibility) PlayerLyricSyncBox.Visibility = visibility;
        if (!available) return;
        string label = DescribeAdvance(_lyricAdvanceMs);
        if (!Equals(PlayerLyricAdvanceValue.Content, label)) PlayerLyricAdvanceValue.Content = label;
    }

    static string DescribeAdvance(int milliseconds) => (milliseconds > 0 ? "+" : milliseconds < 0 ? Minus : "")
        + (Math.Abs(milliseconds) / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " с";

    static int AdvanceStep => Native.IsShiftDown ? CoarseAdvanceMs : FineAdvanceMs;

    // "+" shows the text earlier (the lookup time moves forward), "−" later.
    void LyricsEarlier_Click(object sender, RoutedEventArgs e) => AdjustLyricAdvance(AdvanceStep, false, e);

    void LyricsLater_Click(object sender, RoutedEventArgs e) => AdjustLyricAdvance(-AdvanceStep, false, e);

    void LyricsAdvanceReset_Click(object sender, RoutedEventArgs e) => AdjustLyricAdvance(0, true, e);

    void AdjustLyricAdvance(int difference, bool reset, RoutedEventArgs e)
    {
        e.Handled = true;
        ChangeLyricAdvance(difference, reset);
        SyncLyricAdjust(RefreshLyricsPresentation());
    }

    // ---- big container

    void LyricsFullText_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OpenLyricsEditor();
    }

    void OpenLyricsEditor(bool showSource = false)
    {
        LyricsResult result = CurrentLyricsResult();
        string original = showSource ? LyricsPresentation.SourceTextForResult(result) : LyricsPresentation.TextForResult(result);
        if (result.Track is not { } track || original.Length == 0) return;

        _lyricsEditorRaw = showSource;
        _lyricsEditorDocument = LyricsDocumentIdentity.Key(result.Candidate);
        LyricsEditor.IsReadOnly = showSource;
        _lyrics.HoldDocument();
        LoadLyricsEditor(track, original);
        _lyricsEditorOpen = true;
        _collapseTimeout.Cancel();
        Native.AllowKeyboardInput(_hwnd, true);
        Focusable = true;
        ShowPanel(Panel.Lyrics);
        Activate();
        Dispatcher.InvokeAsync(() =>
        {
            if (!_lyricsEditorOpen) return;
            Keyboard.Focus(LyricsEditor);
            LyricsEditor.CaretIndex = 0;
        });
    }

    void LoadLyricsEditor(TrackSnapshot track, string original)
    {
        _lyricsEditorTrack = track;
        _lyricsEditorOriginal = original;
        string text = !_lyricsEditorRaw && _lyricsEdit is { } edit && edit.Matches(track, _lyricsEditorDocument) ? edit.Text : original;
        _loadingLyricsEditor = true;
        try { LyricsEditor.Text = text; }
        finally { _loadingLyricsEditor = false; }
        LyricsEditor.ScrollToHome();
        LyricsEditorTitle.Text = string.IsNullOrWhiteSpace(track.Artist) ? track.Title : track.Artist + " — " + track.Title;
        SyncLyricsEditorNote();
    }

    void SyncLyricsEditorNote(string? message = null)
    {
        bool edited = !_lyricsEditorRaw && _lyricsEdit?.Matches(_lyricsEditorTrack, _lyricsEditorDocument) == true;
        LyricsEditorNote.Text = message ?? (_lyricsEditorRaw ? "Оригинал источника, включая служебные строки. Только чтение." : edited ? LyricsNoteEdited : LyricsNoteIdle);
        LyricsEditorReset.IsEnabled = edited;
        LyricsEditorReset.Opacity = edited ? 1 : 0.4;
    }

    void LyricsEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingLyricsEditor || _lyricsEditorRaw || _lyricsEditorTrack is not { } track) return;
        string text = LyricsEditor.Text;
        // Unchanged or emptied text means "no edit": the original document comes back.
        _lyricsEdit = text == _lyricsEditorOriginal || string.IsNullOrWhiteSpace(text) ? null
            : new LyricsEdit(track.Title, track.Artist, track.Album, track.SourceApp, _lyricsEditorDocument, text);
        SyncLyricsEditorNote();
    }

    void LyricsEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ShowPanel(Panel.Player);
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveLyricsToFile();
        }
    }

    void LyricsEditor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The wheel must not fall through to the island volume control, at the bounds too.
        e.Handled = true;
        if (Native.IsCtrlDown) return;
        double maximum = Math.Max(0, LyricsEditor.ExtentHeight - LyricsEditor.ViewportHeight);
        LyricsEditor.ScrollToVerticalOffset(Math.Clamp(LyricsEditor.VerticalOffset - e.Delta / 120d * 36, 0, maximum));
    }

    void LyricsBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Player);

    void LyricsReset_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        LyricsEditor.Text = _lyricsEditorOriginal;
        LyricsEditor.ScrollToHome();
    }

    void CloseLyricsEditor()
    {
        _lyricsEditorOpen = false;
        _lyricsEditorRaw = false;
        LyricsEditor.IsReadOnly = false;
        Keyboard.ClearFocus();
        Focusable = false;
        Native.AllowKeyboardInput(_hwnd, false);
        _lyrics.ReleaseDocument();
        ApplyLyricsEdit();
    }

    // The small shelf shows the edited plain text; a new document text must be rebuilt, not reused.
    void ApplyLyricsEdit()
    {
        _fullTextCandidate = null;
        _playerPresentationResult = null;
        RefreshLyricsPresentation();
    }

    void KeepLyricsEditorOnTrack()
    {
        if (_lyricsEditorTrack is { } shown && !SameSong(shown, _media.Snapshot)) ShowPanel(Panel.Player);
    }

    // ---- Notepad and file

    string LyricsEditorText() => LyricsEditor.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);

    string LyricsFileName()
    {
        string name = _lyricsEditorTrack is { } track ? $"{track.Artist} - {track.Title}".Trim(' ', '-') : "";
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        if (name.Length > 80) name = name[..80].TrimEnd();
        return (name.Length == 0 ? "Текст песни" : name) + ".txt";
    }

    void LyricsNotepad_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "DynamicIsland");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, LyricsFileName());
            File.WriteAllText(path, LyricsEditorText(), Utf8WithBom);
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
            SyncLyricsEditorNote("Открыто копией в Блокноте. Сохраните там файл, если нужно.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            App.Log(ex);
            SyncLyricsEditorNote("Не удалось открыть Блокнот.");
        }
    }

    void LyricsSave_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SaveLyricsToFile();
    }

    void SaveLyricsToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить текст песни",
            Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = LyricsFileName(),
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
        };
        bool picked;
        _pickingFiles = true;
        try { picked = dialog.ShowDialog(this) == true; }
        catch (Exception ex)
        {
            App.Log(ex);
            picked = false;
        }
        finally { _pickingFiles = false; }

        if (picked)
        {
            try
            {
                File.WriteAllText(dialog.FileName, LyricsEditorText(), Utf8WithBom);
                SyncLyricsEditorNote("Сохранено: " + Path.GetFileName(dialog.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log(ex);
                SyncLyricsEditorNote("Не удалось сохранить файл.");
            }
        }
        ResyncPointer(LongCollapseDelay);
        if (_lyricsEditorOpen) Keyboard.Focus(LyricsEditor);
    }
}
