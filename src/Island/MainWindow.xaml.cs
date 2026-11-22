using System.Diagnostics;
using DynamicIsland.Discord;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    enum View { Idle, Media, Timer, Record, Volume, Charge, Focus, Toast, Notice, MediaBig, IdleBig, TimerBig, TimerSet, RecordBig, RecordSet, Menu, Settings, Look, Shelf, Weather, Qr, Update, Sources, Lyrics, Loading, LyricsSources, MiniSettings, LyricsSettings, Headset, SystemToggle, Brightness, LyricsManual, Voice, VoiceBig, Message, Discord, DiscordVoice, DiscordMessages, DiscordPlugin }

    enum Panel { None, Player, Timer, TimerSet, Record, Menu, Settings, Look, Shelf, Weather, Qr, Update, Sources, Lyrics, LyricsSources, MiniSettings, LyricsSettings, LyricsManual, Voice, Discord, DiscordVoice, DiscordMessages, DiscordPlugin }

    readonly record struct PillShape(double Width, double Height, double Radius);

    static readonly Dictionary<View, PillShape> PillShapes = new()
    {
        [View.Idle] = new(118, 34, 17),
        [View.Media] = new(210, 34, 17),
        [View.Timer] = new(132, 34, 17),
        [View.Record] = new(132, 34, 17),
        [View.Volume] = new(250, 34, 17),
        [View.Charge] = new(230, 34, 17),
        [View.Headset] = new(320, 64, 29),
        [View.Focus] = new(236, 34, 17),
        [View.SystemToggle] = new(260, 34, 17),
        [View.Brightness] = new(250, 34, 17),
        [View.Toast] = new(340, 68, 30),
        [View.Notice] = new(320, 64, 29),
        [View.MediaBig] = new(380, PlayerHeight, 40),
        [View.IdleBig] = new(320, 124, 38),
        [View.TimerBig] = new(330, 92, 40),
        [View.TimerSet] = new(300, 190, 38),
        [View.RecordBig] = new(340, 92, 40),
        [View.RecordSet] = new(320, 92, 40),
        [View.Menu] = new(300, 368, 34),
        [View.Settings] = new(320, 452, 34),
        [View.MiniSettings] = new(340, 266, 34),
        [View.Look] = new(352, LookHeight, 34),
        [View.Shelf] = new(380, 136, 34),
        [View.Weather] = new(522, 190, 30),
        [View.Qr] = new(320, 104, 34),
        [View.Update] = new(320, 150, 34),
        [View.Sources] = new(320, 374, 34),
        [View.LyricsSettings] = new(352, 526, 34),
        [View.Lyrics] = new(440, 340, 34),
        [View.LyricsSources] = new(360, 306, 34),
        [View.LyricsManual] = new(380, 284, 34),
        [View.Voice] = new(64, 34, 17),
        [View.VoiceBig] = new(360, 170, 38),
        [View.Message] = new(340, 72, 31),
        [View.Discord] = new(342, 250, 34),
        [View.DiscordVoice] = new(340, 296, 34),
        [View.DiscordMessages] = new(340, 298, 34),
        [View.DiscordPlugin] = new(342, 236, 34),
        [View.Loading] = new(118, 34, 17),
    };

    readonly record struct MediaSpot(
        double CoverLeft, double CoverTop, double CoverSize,
        double BarsRight, double BarsCenterY, double BarsWidth, double BarsHeight, double BarsOpen)
    {
        public double[] Values => [CoverLeft, CoverTop, CoverSize, BarsRight, BarsCenterY, BarsWidth, BarsHeight, BarsOpen];
    }

    static readonly Dictionary<View, MediaSpot> MediaSpots = new()
    {
        [View.Media] = new(7, 6, 22, 13, 17, 21, 16, 0),
        [View.Toast] = new(12, 12, 44, 20, 34, 23, 20, 0),
        [View.MediaBig] = new(20, 20, 64, 22, 52, 38, 26, 1),
    };

    static readonly View[] MenuPages = [View.Discord, View.Settings, View.Look, View.TimerSet, View.TimerBig, View.RecordSet, View.RecordBig, View.Shelf, View.Weather, View.Qr];

    static readonly View[] SettingsPages = [View.Update, View.LyricsSettings, View.MiniSettings];


    const double HostWidth = 620, HostHeight = 592;
    const double CompactMaxHeight = 40;
    const double BarelyVisible = 0.05;
    const double IntroScale = 0.3, IntroOffset = -50;
    const int FullscreenCheckTicks = 5, ClockUpdateTicks = 10, HeadsetReadTicks = 300;
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan PausedMediaLinger = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(550);
    static readonly TimeSpan LongCollapseDelay = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan AwayDuration = TimeSpan.FromSeconds(5);

    readonly Dictionary<View, FrameworkElement> _views;
    readonly SolidColorBrush _dim, _orange, _green, _red, _indigo;
    readonly AudioService _audio = new();
    readonly MediaService _media;
    readonly LyricsService _lyrics;
    readonly NetworkService _network;
    readonly Updater _updater = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _ticker = new() { Interval = TickInterval };
    SpacePlaybackHook? _spacePlaybackHook;
    readonly DelayedAction _transientTimeout, _collapseTimeout, _awayTimeout;
    readonly View? _forcedView;
    readonly double _forcedTimerSeconds;
    readonly int _forcedHeadsetCharge;

    readonly DiscordService _discord;

    View _view = View.Idle;
    View? _transientView;
    Panel _panel;
    bool _hovered, _pressed, _hiddenByFullscreen, _sentAway;
    IntPtr _hwnd;
    int _ticks;

    public MainWindow()
    {
        _lyrics = new(context: new DispatcherSynchronizationContext(Dispatcher));
        InitializeComponent();
        RefreshSoundCloudSignInStatus();
        InitTimerPresets();
        Width *= LargestScale / 100.0;
        Height = Height * LargestScale / 100.0 + LargestGap;

        _dim = BrushResource("Dim");
        _orange = BrushResource("Orange");
        _green = BrushResource("Green");
        _red = BrushResource("Red");
        _indigo = BrushResource("Indigo");

        _views = new()
        {
            [View.Idle] = IdleView,
            [View.Media] = MediaView,
            [View.Timer] = TimerView,
            [View.Record] = RecordView,
            [View.Volume] = VolumeView,
            [View.Charge] = ChargeView,
            [View.Headset] = HeadsetView,
            [View.Focus] = FocusView,
            [View.SystemToggle] = SystemToggleView,
            [View.Brightness] = BrightnessView,
            [View.Toast] = ToastView,
            [View.Notice] = NoticeView,
            [View.MediaBig] = MediaBigView,
            [View.IdleBig] = IdleBigView,
            [View.TimerBig] = TimerBigView,
            [View.TimerSet] = TimerSetView,
            [View.RecordBig] = RecordBigView,
            [View.RecordSet] = RecordSetView,
            [View.Menu] = MenuView,
            [View.Settings] = SettingsView,
            [View.MiniSettings] = MiniSettingsView,
            [View.Look] = LookView,
            [View.Shelf] = ShelfView,
            [View.Weather] = WeatherView,
            [View.Qr] = QrView,
            [View.Update] = UpdatePage,
            [View.LyricsSettings] = LyricsPage,
            [View.Lyrics] = LyricsView,
            [View.LyricsSources] = LyricsSourcesView,
            [View.LyricsManual] = LyricsManualView,
            [View.Voice] = VoiceView,
            [View.VoiceBig] = VoiceBigView,
            [View.Message] = MessageView,
            [View.Discord] = DiscordPage,
            [View.DiscordVoice] = DiscordVoicePage,
            [View.DiscordMessages] = DiscordMessagesPage,
            [View.DiscordPlugin] = DiscordPluginPage,
            [View.Loading] = LoadingView,
        };
        _spotSprings = [_coverLeft, _coverTop, _coverSize, _barsRight, _barsCenterY, _barsWidth, _barsHeight, _barsOpen];
        _springs =
        [
            _width, _height, _radius, _scale, _offsetY, _userScale, _topGap, _bubbleSplit, _bubbleScale, _bubbleRecord, _bubbleTimer, _bubbleShelf, _shelfBubbleWidth, _discordMusicWidth, _discordMusicX, _discordMusicY, _discordMusicOpacity,
            _shelfScroll, _weatherScroll, _weatherDaysScroll, _weatherPageSpring, _lookScroll, _updateNotesScroll, _volumeOvershoot, _leanX, _coverScale, _coverSnapX, _coverSnapY, _lyricSnapX, _lyricSnapY, .. _spotSprings,
        ];
        _timerTint = new SolidColorBrush(_orange.Color);
        _recordTint = new SolidColorBrush(_red.Color);
        _lyricBlock = LyricA;

        _forcedView = Enum.TryParse(Argument("--view"), true, out View forced) ? forced : null;
        _forcedTimerSeconds = double.TryParse(Argument("--timer"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? seconds : 0;
        _forcedHeadsetCharge = int.TryParse(Argument("--headset"), out int charge) ? Math.Clamp(charge, 0, 100) : Headset.Unknown;

        _shapeLoop = new FrameLoop(AdvanceShape);
        _glass = new Glass(this);
        _eqLoop = new FrameLoop(AdvanceEq, EqFrameSeconds);
        _seekLoop = new FrameLoop(AdvanceSeek);
        _partLoop = new FrameLoop(AdvanceLyricParting);
        _transientTimeout = new DelayedAction(EndTransient);
        _collapseTimeout = new DelayedAction(Collapse);
        _awayTimeout = new DelayedAction(ReturnFromAway);
        _overshootTimeout = new DelayedAction(ReleaseVolumeOvershoot);
        _dragLeaveTimeout = new DelayedAction(OnDragLeft);
        _shelfBubbleTimeout = new DelayedAction(HideShelfBubble);

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        _lyrics.Changed += OnLyricsChanged;
        _network = new NetworkService(Dispatcher);
        _network.Changed += OnNetworkChanged;
        _updater.Changed += RefreshUpdatePage;
        _shelf = new Shelf(Dispatcher);
        _shelf.Changed += SyncShelf;
        _shelf.PictureLoaded += OnShelfPictureLoaded;
        _obs.Changed += OnObsChanged;
        _obs.Saved += OnRecordingSaved;
        _weather.Changed += SyncWeather;
        WireWeatherPane();

        _discord = new DiscordService(Dispatcher);
        PrepareDiscord();
        PrepareViews();
        ApplyTimerTint();
        ApplyRecordTint();
        TuneDragSprings(false);
        _ticker.Tick += (_, _) => TickSafely();
        Loaded += OnLoaded;
        Closed += (_, _) => {
            _clipboardShelfCapture?.Dispose(); _spacePlaybackHook?.Dispose();
            _shelfBubbleTimeout.Cancel();
            _weatherLocationSearch?.Cancel();
            _weatherPreload.Cancel();
            _weather.Stop();
            _discord.Dispose();
        };
    }

    void PrepareViews()
    {
        foreach (FrameworkElement view in _views.Values)
        {
            view.RenderTransformOrigin = new Point(0.5, 0.5);
            view.RenderTransform = CreateViewTransforms();
            view.Visibility = Visibility.Collapsed;
            view.Opacity = 0;
        }
        IdleView.Visibility = Visibility.Visible;
        IdleView.Opacity = 1;

        Shared.RenderTransform = CreateViewTransforms();
        ArtSpot.Clip = Squircle.Of(new Rect(0, 0, FullCoverSize, FullCoverSize), CoverRadius);
        LyricBox.CacheMode = _lyricCache;
        Eq.Fill = _accentBrush;
        Eq.Dots = Settings.Dots;
        SeekLines.Accent = _accentBrush;
        PlayerMatrix.Rounding = PillShapes[View.MediaBig].Radius;
        SyncBackdrop();
        foreach (FrameworkElement icon in new FrameworkElement[] { PlayIcon, PauseIcon, TimerPlayIcon, TimerPauseIcon })
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new ScaleTransform(1, 1);
        }
    }

    bool IsMediaActive => _media.HasTrack && (_media.IsPlaying || DateTime.UtcNow - _lastPlayedAt < PausedMediaLinger);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // The layered WPF window previously exhausted native D3D resources. A comparison
        // with Glass enabled also found more consistent frame intervals in software mode.
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
        {
            target.RenderMode = RenderMode.SoftwareOnly;
            App.Log("island renderer: SoftwareOnly (native D3D resource exhaustion workaround)");
        }
        _clipboardShelfCapture = new ClipboardShelfCapture(_hwnd, file => {
            _shelf.Add([file]);
            ShowShelfBubble(); // Refresh even when the copied file already exists on the shelf.
        });
        Native.HideFromTaskSwitcher(_hwnd);
        Native.EnableTouchpadInputSource(_hwnd);
        HwndSource.FromHwnd(_hwnd).AddHook(OnHorizontalWheel);
        if (Native.RegisterShellHook(_hwnd)) HwndSource.FromHwnd(_hwnd).AddHook(OnShellMessage);
        try { _spacePlaybackHook = new(CanHandleSpacePlayback, QueueSpacePlayback); }
        catch (System.ComponentModel.Win32Exception ex) { App.Log(ex); }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyShape();
    }

    static string? Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    SolidColorBrush BrushResource(string key) => (SolidColorBrush)FindResource(key);

    IntPtr OnShellMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (Native.MediaKeyOf(msg, wParam, lParam))
        {
            case Native.MediaKey.VolumeUp when IsVolumeAtLimit(true):
                OvershootVolume(true);
                break;
            case Native.MediaKey.VolumeDown when IsVolumeAtLimit(false):
                OvershootVolume(false);
                break;
            case Native.MediaKey.NextTrack:
                RememberSkip(1);
                NextIcon.Play();
                break;
            case Native.MediaKey.PreviousTrack:
                RememberSkip(-1);
                PrevIcon.Play();
                break;
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnScreen();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(CenterOnScreen);
        UpdateClock();
        StartDiscord();
        UpdateSwitches(false);
        RefreshLookPage();
        RefreshUpdatePage();
        SyncAccent(false);
        SyncShelf();
        SyncRecord();
        SyncWeather();
        SyncGlass(false);
        Safely(WarmSavedWeatherAsync);
        PlayIntro();
        _ticker.Start();
        if (_forcedTimerSeconds > 0) StartTimer(TimeSpan.FromSeconds(_forcedTimerSeconds));
        _ = _updater.CheckAsync();
        if (_forcedHeadsetCharge >= 0)
            ShowHeadset("AirPods Pro", Enum.TryParse(Argument("--gadget"), true, out Gadget.Model model) ? model : Gadget.Model.Earbuds,
                _forcedHeadsetCharge, _forcedHeadsetCharge <= HeadsetLowPercent);

        try { await _media.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        try { await _network.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        _obs.Start();
    }

    void CenterOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        _glass.Place();
    }

    void PlayIntro()
    {
        _scale.Value = IntroScale;
        _offsetY.Value = IntroOffset;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)));
        UpdateView();
        UpdateTargets();
    }

    void Exit()
    {
        _trackShapeLookup?.Cancel();
        _media.Changed -= OnMediaChanged;
        _lyrics.Dispose();
        _ticker.Stop();
        _spacePlaybackHook?.Dispose();
        _alarm.Stop();
        _pointerLyric?.ShowLine("", 0, 0);
        _glass.Hide();
        var fade = new DoubleAnimation(0, Ms(220));
        fade.Completed += (_, _) => Application.Current.Shutdown();
        Root.BeginAnimation(OpacityProperty, fade);
        _scale.Target = _bubbleScale.Target = 0.5;
        StartShapeLoop();
    }

    bool CanHandleSpacePlayback() => !Native.IsPlaybackModifierDown && Island.IsMouseOver && _media.HasTrack
        && _view is View.Media or View.Toast or View.MediaBig or View.Idle or View.IdleBig
        && _panel is Panel.None or Panel.Player;

    void QueueSpacePlayback()
    {
        var target = _media.Snapshot;
        Dispatcher.InvokeAsync(() =>
        {
            if (_media.HasTrack && target.SameIdentity(_media.Snapshot)) _media.TogglePlay();
        }, DispatcherPriority.Input);
    }

    static void Safely(Action step) { try { step(); } catch (Exception ex) { App.Log(ex); } }

    void TickSafely()
    {
        try
        {
            OnTick();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void OnTick()
    {
        UpdateVoiceTime();
        _ticks++;
        Safely(TaskbarHiddenOpacity.Poll);
        Safely(PollVolume);
        Safely(PollBrightness);
        if (_ticks % HeadsetReadTicks == 1) Safely(() => ReadHeadset(_audio.Device));
        Safely(() => UpdateLyric());
        Safely(UpdatePointerLyric);
        Safely(UpdateTimer);
        Safely(UpdateRecord);
        if (_ticks % FullscreenCheckTicks == 0)
        {
            Safely(CheckFullscreen);
            Safely(PollDoNotDisturb);
            Safely(PollSystemState);
        }
        else if (_fullscreenRevealAllowed) Safely(() => UpdateFullscreenReveal());
        if (_ticks % ClockUpdateTicks == 0)
        {
            Safely(ShowDiscordPluginNotice);
            Safely(ShowDiscordLinkNotice);
            Safely(UpdateClock);
            Safely(PollPower);
            _ = _weather.RefreshAsync();
            Safely(WarmSavedWeatherAsync);
            Safely(StayOnTop);
            if (_media.IsPlaying) _lastPlayedAt = DateTime.UtcNow;
            Safely(UpdateView);
        }
    }

    View TargetView()
    {
        if (_forcedView is null && _transientView == View.Message && _panel == Panel.Player)
            return View.Message;

        View target = _forcedView ?? _panel switch
        {
            Panel.Discord => View.Discord,
            Panel.DiscordVoice => View.DiscordVoice,
            Panel.DiscordMessages => View.DiscordMessages,
            Panel.DiscordPlugin => View.DiscordPlugin,
            Panel.Voice => ShowsVoice ? View.VoiceBig : View.IdleBig,
            Panel.Menu => View.Menu,
            Panel.Settings => View.Settings,
            Panel.MiniSettings => View.MiniSettings,
            Panel.Look => View.Look,
            Panel.Update => View.Update,
            Panel.LyricsSettings or Panel.Sources => View.LyricsSettings,
            Panel.Shelf => View.Shelf,
            Panel.Weather => View.Weather,
            Panel.Lyrics => View.Lyrics,
            Panel.LyricsSources => View.LyricsSources,
            Panel.LyricsManual => View.LyricsManual,
            Panel.Qr => View.Qr,
            Panel.TimerSet => View.TimerSet,
            Panel.Record => _obs.Recording ? View.RecordBig : View.RecordSet,
            Panel.Timer when _countdown.IsActive => View.TimerBig,
            Panel.Timer or Panel.Player => _media.HasTrack ? View.MediaBig : View.IdleBig,
            _ => _transientView ?? (_updater.State == Updater.Stage.Loading ? View.Loading
                : ShowsVoice ? View.Voice : IsMediaActive ? View.Media : _obs.Recording ? View.Record : _countdown.IsActive ? View.Timer : View.Idle),
        };
        return target == View.Toast && !_media.HasTrack ? View.Idle : target;
    }

    void UpdateView()
    {
        View target = TargetView();
        bool changed = target != _view;
        if (changed) MorphTo(target);

        SyncDiscordMusic();
        SyncEq();
        SyncRim();
        SyncCover();
        if (!changed) return;
        if (target == View.MediaBig)
        {
            StartSeekLoop();
            RefreshSoundCloudHeart(force: true);
        }
        if (target == View.Media) UpdateLyric(true);
    }

    void MorphTo(View target)
    {
        View previous = _view;
        PillShape from = ShapeOf(previous);
        _view = target;
        if (target == View.MediaBig) UpdatePlayerLyrics(true);
        else ShowLyricsPlaceholder(false);
        PillShape to = ShapeOf(target);
        bool weatherTransition = previous == View.Weather || target == View.Weather;
        bool rootMenuFade = !weatherTransition && (previous == View.Menu || target == View.Menu)
            && MenuDirection(previous, target) == 0;
        double extraMs = rootMenuFade ? RootMenuExtraMs : 0;
        _rootMenuMorphElapsed = rootMenuFade ? 0 : RootMenuMorphSeconds;
        bool growing = to.Width * to.Height >= from.Width * from.Height;
        _width.Tune(growing ? 330 : 260, growing ? 22 : 27);
        _height.Tune(growing ? 220 : 400, growing ? 22 : 33);
        if (weatherTransition)
        {
            _width.Tune(300, 30);
            _height.Tune(260, 30);
        }
        if ((IsMenuOrLyricsView(previous) || IsMenuOrLyricsView(target))
            && (_width.Value != to.Width || _height.Value != to.Height || _radius.Value != to.Radius))
            DeferShadowDuringTransition();
        foreach (Spring spring in _spotSprings) spring.Tune(_height.Stiffness, _height.Damping);

        _morphing = true;
        SwitchView(previous, target, extraMs);
        MoveMediaSpot(previous, target, to, extraMs);
        UpdateTargets();
    }

    void MoveMediaSpot(View from, View to, PillShape size, double extraMs)
    {
        bool had = MediaSpots.ContainsKey(from), has = MediaSpots.TryGetValue(to, out MediaSpot spot);
        if (has)
        {
            bool fresh = IsHidden(Shared);
            double[] places = spot.Values;
            for (int i = 0; i < _spotSprings.Length; i++)
            {
                if (fresh) _spotSprings[i].Snap(places[i]);
                else _spotSprings[i].Target = places[i];
            }
        }
        ArtSpot.Cursor = to == View.MediaBig ? Cursors.Hand : null;
        if (has == had) return;

        ScaleTransform scale = FadeScaleOf(Shared);
        scale.CenterX = HostWidth / 2;
        scale.CenterY = (has ? size : ShapeOf(from)).Height / 2;
        if (has) FadeIn(Shared, extraMs);
        else FadeOut(Shared, extraMs);
    }

    static int MenuDirection(View from, View to) =>
        IsPageOf(from, to) ? 1 : IsPageOf(to, from) ? -1 : 0;

    static bool IsMenuOrLyricsView(View view) => view is View.Menu or View.Sources or View.Lyrics or View.LyricsSources or View.LyricsManual
        || MenuPages.Contains(view) || SettingsPages.Contains(view);

    static bool IsPageOf(View parent, View page) =>
        parent == View.Discord && page is (View.DiscordVoice or View.DiscordMessages or View.DiscordPlugin) ||
        parent == View.Menu && MenuPages.Contains(page) || parent == View.Settings && SettingsPages.Contains(page);

    PillShape ShapeOf(View view) => view switch
    {
        View.Voice => PillShapes[view] with { Width = _voiceWidth },
        View.VoiceBig => PillShapes[view] with { Height = VoiceBigView.Height },
        View.Idle => PillShapes[view] with { Width = IdleView.Width },
        View.Media => PillShapes[view] with { Width = _compactMediaWidth },
        View.MediaBig when _playerHasLyricRoom => PillShapes[view] with { Height = PlayerHeight + PlayerLyricsHeight },
        View.RecordSet => PillShapes[view] with { Width = RecordSetView.Width },
        View.Update => PillShapes[view] with { Height = UpdatePage.Height },
        View.Weather => PillShapes[view] with { Height = WeatherView.Height },
        View.Look => PillShapes[view] with { Height = LookView.Height },
        // Constant height: the shelf row stays in the menu, dimmed when the shelf is off.
        View.Menu => PillShapes[view] with { Height = MenuView.Height },
        // Высота меняется при раскрытии «Эффекты текста».
        View.LyricsSettings => PillShapes[view] with { Height = LyricsPage.Height },
        View.LyricsSources => PillShapes[view] with { Height = LyricsSourcesView.Height },
        View.LyricsManual => PillShapes[view] with { Height = LyricsManualView.Height },
        _ => PillShapes[view],
    };

    void ShowTransient(View view, double seconds, bool force = false)
    {
        if (force) _panel = Panel.None;
        else
        {
            if (_hiddenByFullscreen || _sentAway) return;
            if (_panel != Panel.None && !(view == View.Message && _panel == Panel.Player)) return;
            if (_transientView == View.Message && view != View.Message) return;
        }
        if (view == View.Message && _panel == Panel.Player) _collapseTimeout.Cancel();
        _transientView = view;
        _transientTimeout.Start(TimeSpan.FromSeconds(seconds));
        UpdateView();
    }

    void EndTransient()
    {
        bool returnToPlayer = _transientView == View.Message && _panel == Panel.Player;
        _transientView = null;
        SilenceAlarm();
        UpdateView();
        if (returnToPlayer && !_hovered) _collapseTimeout.Start(CollapseDelay);
    }

    void SelectPanel(Panel panel)
    {
        if (panel != Panel.Weather) CancelWeatherGesture();
        if (panel != Panel.Weather && WeatherSubpageOpen) CloseWeatherSubpages();
        if (panel != Panel.Lyrics && _lyricsEditorOpen) CloseLyricsEditor();
        _panel = panel;
        _transientView = null;
        _transientTimeout.Cancel();
        SilenceAlarm();
    }

    void ShowPanel(Panel panel)
    {
        if (panel != Panel.Weather) CancelWeatherGesture();
        if (panel != Panel.Weather && WeatherSubpageOpen) CloseWeatherSubpages();
        if (panel != Panel.Lyrics && _lyricsEditorOpen) CloseLyricsEditor();
        _panel = panel;
        UpdateView();
    }

    void Collapse()
    {
        // The read-only report never holds the island open: leaving it collapses normally, and its keyboard hook is released.
        bool reportOnly = _lyricsSourceDiagnostics;
        if (_hovered || _pickingFiles || WeatherTyping || _lyricsEditorOpen || _textEntryOpen && !reportOnly
            || _weatherGesture.Active || _weatherPan is not null) return;
        if (reportOnly) SetTextEntry(false);
        ShowPanel(Panel.None);
    }

    void ReturnFromAway()
    {
        _sentAway = false;
        UpdateTargets();
    }
}
