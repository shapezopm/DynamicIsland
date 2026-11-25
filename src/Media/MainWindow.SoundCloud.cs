using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    TrackSnapshot? _soundCloudSnapshot;
    Uri? _soundCloudTrack;
    int _soundCloudLookupVersion;
    bool _soundCloudHasTrack, _soundCloudPlaying, _soundCloudBusy, _soundCloudLiked;
    SoundCloudBrowser? _soundCloudBrowser;

    async void RefreshSoundCloudHeart()
    {
        TrackSnapshot track = _media.Snapshot;
        bool hasTrack = _media.HasTrack;
        bool playing = hasTrack && _media.IsPlaying;
        bool resumed = playing && !_soundCloudPlaying;
        _soundCloudPlaying = playing;
        if (hasTrack == _soundCloudHasTrack && track.SameIdentity(_soundCloudSnapshot)
            && (!resumed || _soundCloudTrack is not null)) return;
        int lookupVersion = ++_soundCloudLookupVersion;
        _soundCloudSnapshot = track;
        _soundCloudHasTrack = hasTrack;
        _soundCloudTrack = null;
        SoundCloudHeart.SetVisible(false);
        SetSoundCloudHeart(false, animate: false);
        if (!hasTrack) return;

        try
        {
            SourceApp.SoundCloudPlayer? player = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (lookupVersion != _soundCloudLookupVersion) return;
                player = await Task.Run(() => SourceApp.ReadSoundCloudPlayer(track));
                if (player is not null) break;
                if (attempt < 2) await Task.Delay(350);
            }
            if (lookupVersion != _soundCloudLookupVersion || player is not { } known) return;
            _soundCloudTrack = known.Track;
            // Временно скрыто до следующей итерации интерфейса лайка.
            SetSoundCloudHeart(known.State == SourceApp.SoundCloudLike.Liked, animate: false);
            if (known.State == SourceApp.SoundCloudLike.SignInRequired)
                SoundCloudHeart.ToolTip = "Войдите в SoundCloud в браузере, где играет трек";
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void RefreshSoundCloudSignInStatus() => SoundCloudLinkStatus.Text =
        Settings.SoundCloudSignInSaved ? "Вход ранее подтверждён" : "Отдельный вход";

    async void SoundCloudLink_Click(object sender, RoutedEventArgs e)
    {
        SoundCloudLinkStatus.Text = "Ожидаю входа…";
        await OpenSoundCloudAsync(new Uri("https://soundcloud.com/login"), keepPage: false);
        _soundCloudBrowser?.WatchSignIn();
    }

    async void SoundCloudHeart_Click(object sender, RoutedEventArgs e)
    {
        if (_soundCloudBusy || !_media.HasTrack || _soundCloudSnapshot is not { } track
            || _soundCloudTrack is not { } address || !_media.Snapshot.SameIdentity(track)) return;
        _soundCloudBusy = true;
        bool like = !_soundCloudLiked;
        var desired = like ? SourceApp.SoundCloudLike.Liked : SourceApp.SoundCloudLike.Unliked;
        SoundCloudHeart.ToolTip = like ? "Ставлю лайк…" : "Снимаю лайк…";
        try
        {
            var result = await Task.Run(() => SourceApp.ReadSoundCloudPlayer(track, address, desired));
            bool sent = result?.State == SourceApp.SoundCloudLike.Pending;
            if (sent)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 500 : 300);
                    result = await Task.Run(() => SourceApp.ReadSoundCloudPlayer(track, address));
                    if (result?.State == desired) break;
                }
            }
            App.Log($"SoundCloud browser like: {result?.State.ToString() ?? "player no longer matched"}; requested={desired}; button invoked={sent}.");
            if (_soundCloudTrack != address || !_media.Snapshot.SameIdentity(track)) return;
            if (result?.State == desired)
                SetSoundCloudHeart(like, animate: sent);
            else
                SoundCloudHeart.ToolTip = result?.State == SourceApp.SoundCloudLike.SignInRequired
                    ? "Войдите в SoundCloud в браузере, где играет трек"
                    : like ? "SoundCloud не подтвердил лайк" : "SoundCloud не подтвердил снятие лайка";
        }
        catch (Exception ex) { App.Log(ex); }
        finally { _soundCloudBusy = false; }
    }
    void SetSoundCloudHeart(bool liked, bool animate)
    {
        _soundCloudLiked = liked;
        Rect from = SoundCloudHeartReveal.Rect;
        var to = liked ? new Rect(0, 0, 19, 18) : new Rect(0, 18, 19, 0);
        SoundCloudHeartReveal.BeginAnimation(RectangleGeometry.RectProperty, null);
        SoundCloudHeartReveal.Rect = to;
        if (animate && from != to)
            SoundCloudHeartReveal.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(from, to, Ms(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        SoundCloudHeart.ToolTip = liked ? "Убрать из понравившихся в SoundCloud" : "Добавить в понравившиеся в SoundCloud";
        AutomationProperties.SetName(SoundCloudHeart, (string)SoundCloudHeart.ToolTip);
    }

    async Task OpenSoundCloudAsync(Uri address, bool keepPage = false)
    {
        if (!SoundCloudWaveform.IsSite(address)) return;
        var browser = _soundCloudBrowser;
        try
        {
            bool created = browser is null;
            if (browser is null)
            {
                browser = _soundCloudBrowser = new SoundCloudBrowser { Owner = this };
                browser.SignedIn += () =>
                {
                    Settings.SoundCloudSignInSaved = true;
                    browser.Close();
                };
                browser.Closed += (_, _) =>
                {
                    _soundCloudBrowser = null;
                    RefreshSoundCloudSignInStatus();
                };
                browser.Show();
            }
            if (browser.WindowState == WindowState.Minimized) browser.WindowState = WindowState.Normal;
            browser.Activate();
            if (created || !keepPage) await browser.NavigateAsync(address);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            browser?.Close();
            SoundCloudLinkStatus.Text = "Ошибка открытия окна";
        }
    }
}
