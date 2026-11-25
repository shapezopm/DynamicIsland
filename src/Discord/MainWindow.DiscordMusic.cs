using System.Windows;
using System.Windows.Input;

namespace DynamicIsland;

public partial class MainWindow
{
    const double CallMediaMiniWidth = 34, CallMediaTitleWidth = 164;
    static readonly TimeSpan CallTrackGap = TimeSpan.FromSeconds(3);
    readonly Spring _callVoiceSplit = new(0, 300, 30);
    readonly Spring _callVoiceScale = new(1, 320, 20);
    DateTime _lastCallTrackAt;
    bool _callVoiceHovered, _callVoicePressed;

    bool CallMediaEnabled => ShowsVoice && Settings.DiscordMusicBubble;
    bool CallTrackInGracePeriod => DateTime.UtcNow - _lastCallTrackAt < CallTrackGap;
    bool HoldingCallTrack => CallMediaEnabled && !_media.HasTrack && CallTrackInGracePeriod;
    bool CallMediaPrimary => CallMediaEnabled && (_media.HasTrack && _media.IsPlaying || CallTrackInGracePeriod);
    bool CallMediaMini => CallMediaPrimary && Settings.DiscordMusicState == DiscordMusicState.Mini;
    bool CallMediaTitleOnly => CallMediaPrimary && Settings.DiscordMusicState == DiscordMusicState.Compact;
    double MediaPillWidth => CallMediaMini ? CallMediaMiniWidth : CallMediaTitleOnly ? CallMediaTitleWidth : _compactMediaWidth;
    bool WantsCallBubbleOnLeft => CallMediaPrimary;

    void UpdateCallVoiceTarget()
    {
        bool separate = CallMediaPrimary;
        // Keep the avatar strip in the same visual tree while switching between panels.
        if (separate && VoiceAvatars.Parent == VoiceView)
        {
            VoiceView.Children.Remove(VoiceAvatars);
            DiscordDetachedVoiceContent.Children.Add(VoiceAvatars);
        }
        else if (!separate && VoiceAvatars.Parent == DiscordDetachedVoiceContent)
        {
            DiscordDetachedVoiceContent.Children.Remove(VoiceAvatars);
            VoiceView.Children.Add(VoiceAvatars);
        }

        _callVoiceSplit.Target = separate && _view == View.Media && _panel == Panel.None ? 1 : 0;
        if (!separate) _callVoiceSplit.Snap(0);
    }

    Rect PlaceDetachedDiscordVoice(Rect pill, double radius, double scale)
    {
        double split = VisibleBubbleSplit(_callVoiceSplit.Value, pill.Height);
        bool visible = split > BubbleVisibleSplit && !HiddenByFullscreen && !_sentAway;
        DiscordDetachedVoice.SetVisible(visible);
        DiscordDetachedVoice.IsHitTestVisible = visible && _callVoiceSplit.Target == 1 && split > BubbleClickableSplit;
        if (!visible) return Rect.Empty;
        DiscordDetachedVoice.Width = _voiceWidth;
        double voiceScale = Math.Max(MinScale, _callVoiceScale.Value);
        DiscordDetachedVoiceScale.ScaleX = DiscordDetachedVoiceScale.ScaleY = voiceScale;
        DiscordDetachedVoiceContent.Opacity = BubbleContentOpacity(split);
        Rect shape = SideBubbleShape(pill, radius, scale, _voiceWidth, DiscordDetachedVoice.Height, voiceScale, split, left: false,
            out double offset);
        DiscordDetachedVoiceMove.X = offset;
        return shape;
    }

    void DiscordDetachedVoiceEnter(object sender, MouseEventArgs e)
    {
        _callVoiceHovered = true;
        UpdateTargets();
    }

    void DiscordDetachedVoiceLeave(object sender, MouseEventArgs e)
    {
        _callVoiceHovered = _callVoicePressed = false;
        UpdateTargets();
    }

    void DiscordDetachedVoiceUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_callVoicePressed) return;
        _callVoicePressed = false;
        SelectPanel(Panel.Voice);
        UpdateView();
        _collapseTimeout.Start(LongCollapseDelay);
    }

    void DiscordDetachedVoiceDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _callVoicePressed = true;
        UpdateTargets();
    }

    void StepDiscordMusic(int direction)
    {
        var next = (DiscordMusicState)Math.Clamp((int)Settings.DiscordMusicState + direction,
            (int)DiscordMusicState.Mini, (int)DiscordMusicState.Normal);
        if (next == Settings.DiscordMusicState) return;
        Settings.DiscordMusicState = next;
        UpdateLyric(true);
        UpdateTargets();
    }
}
