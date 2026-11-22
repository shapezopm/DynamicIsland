# Graph Report - DynamicIsland-1.30.0  (2026-11-23)

## Corpus Check
- 179 files · ~135,166 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 12 file(s) not represented in the graph (top: .otf 4, .ttf 4, (none) 3)

## Summary
- 4214 nodes · 10656 edges · 200 communities (122 shown, 78 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 1036 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- TrackSnapshot
- Native
- Window
- Glyph
- MainWindow.BuildWeatherHours
- ClipboardShelfCapture
- system_windows
- SourceApp
- MediaService
- Glass
- Settings
- MainWindow
- Grid
- MainWindow.StartShapeLoop
- ObsService
- MainWindow.UpdateMiniIsland
- Border
- FrameworkElement
- Border
- DrawingContext
- Digits
- Shelf
- DateTime
- DynamicIsland
- View
- Settings
- Point
- LineBar
- LyricsSources.Create
- Task
- Scrubber
- PlaybackClock
- Motion.Ms
- Build-TrackIdentityIndex.py
- Icon
- LyricsOffsetStore
- Button
- Goo
- smtc-players.md
- DynamicIslandDiscordBridge.plugin.js
- Task
- AreaPicker
- Updater
- Toggle
- CoverFallback
- PlayerLine
- steps
- Point
- Avatar
- LyricsResult
- Equalizer
- LyricsCandidate
- MainWindow.BuildAddPage
- DynamicIslandDiscordBridge
- steps
- StackPanel
- DiscordService
- Spring.Snap
- LyricSource
- WaveBar
- PreserveSig
- Panel
- LyricsNormalizer
- Settings.Read
- HoverPreview
- Uri
- ActionPeer
- Toggle
- Uri
- AreaPicker
- UniformGrid
- ScaleTransform
- GradientStop
- PlayerBar
- CatalogueLyricsProvider
- LrclibOptions
- DotMatrix
- LyricsMatcher
- MainWindow.PublishWeather
- Gadget.cs
- DllImport
- SpacePlaybackHook
- Canvas
- Backdrop
- TimeSpan
- MainWindow.UpdatePlayerLyrics
- SpacePlaybackGesture
- Ellipse
- Stage
- MainWindow
- Geometry
- Ring
- TextBox
- LyricsCache
- MainWindow
- RotateTransform
- ScrollViewer
- LyricsResult
- Updater.CheckAsync
- TrackShapeBar
- ShelfCaptureStore
- Segments
- ToggleButton
- Cover
- Visuals
- LyricsCacheOptions
- LyricChange
- CompactLyric
- Autostart
- SeekPreviewKind
- GapSlider
- LookRows
- LyricA
- NextIcon
- SeekLines
- PointerLyricShape
- AGENTS.md
- Fonts/README.md
- Art
- Body
- Eq
- Glow
- HeadsetGadget
- PART_Track
- PlayerLyricWait
- PlayerMatrix
- PlayerStars
- SeekTrackShape
- SeekWave
- ShelfZone
- WeatherHomeHit
- TtmlParser.Parse
- Lyric
- Op
- LyricFxPlan.Sample
- DrawingSurfaces
- FocusRing
- MainWindow.SearchWeatherAsync
- NetworkService
- Link
- CacheDocument
- SoundCloudBrowser
- MainWindow.Notify
- Stage
- DurationOrigin
- App.Log
- Grab
- MainWindow
- MediaKey
- SoundCloudHeartReveal
- Spring
- Piece
- DiscordBridgeServer.Command
- Symbol
- Color
- Gadget
- Path
- PointerLyric
- TurnEase
- DiscordModels.cs
- Link
- SoundCloudLike
- AdvanceStep
- VoiceDeafenIcon
- MessageAvatar
- IsFocusUnsettled
- WeatherTyping
- ChipCorners
- MainWindow.ShowTransient
- AccentColor
- IsEqVisible

## God Nodes (most connected - your core abstractions)
1. `Window` - 436 edges
2. `MainWindow` - 269 edges
3. `DynamicIsland` - 155 edges
4. `MediaService` - 91 edges
5. `Native` - 89 edges
6. `TrackSnapshot` - 81 edges
7. `LyricsService` - 76 edges
8. `TextBlock` - 73 edges
9. `MainWindow` - 71 edges
10. `MainWindow` - 70 edges

## Surprising Connections (you probably didn't know these)
- `Dynamic Island для Vencord` --references--> `DynamicIslandDiscordBridge`  [INFERRED]
  src/Discord/Vencord/dynamicIslandBridge.desktop/README.md → src/Discord/Vencord/dynamicIslandBridge.desktop/bridge.js
- `MainWindow` --inherits--> `Window`  [EXTRACTED]
  src/Discord/MainWindow.DiscordPlugin.cs → MainWindow.xaml
- `SoundCloudBrowser` --inherits--> `Window`  [EXTRACTED]
  src/Media/SoundCloudBrowser.cs → MainWindow.xaml
- `Glass` --references--> `Window`  [EXTRACTED]
  src/UI/Effects/Glass.cs → MainWindow.xaml
- `AreaPicker` --inherits--> `Window`  [EXTRACTED]
  src/Windows/AreaPicker.cs → MainWindow.xaml

## Import Cycles
- None detected.

## Communities (200 total, 78 thin omitted)

### Community 0 - "TrackSnapshot"
Cohesion: 0.14
Nodes (7): ProviderSearchResult, NeedsRefresh, PreparedLyricsPool, TrackSnapshot, IdentityArtistAliases, IdentityPrimaryArtist, IdentityRequiresDuration

### Community 1 - "Native"
Cohesion: 0.05
Nodes (17): MaterialgramWindowMode, Native, CursorPosition, DispatcherQueueOptions, DISPLAYMODE, ForegroundWindow, InputMessageSource, IsCtrlDown (+9 more)

### Community 3 - "Window"
Cohesion: 0.05
Nodes (75): Content, Tag, AccentText, ArtistBig, BackdropText, BetterDiscordInstallPath, BigDate, BigTimerLabel (+67 more)

### Community 4 - "Glyph"
Cohesion: 0.03
Nodes (59): Glyph, Back, Bars, Battery, Bell, Bolt, Bulge, Check (+51 more)

### Community 5 - "MainWindow.BuildWeatherHours"
Cohesion: 0.13
Nodes (3): SoundCloudHeartFill, WeatherWebData, SolidColorBrush

### Community 9 - "SourceApp"
Cohesion: 0.08
Nodes (5): SourceApp, Destination, MediaWindow, SoundCloudPlayer, WindowProc

### Community 10 - "MediaService"
Cohesion: 0.06
Nodes (24): MediaService, Accent, Album, Art, Artist, DisplayDuration, Duration, HasBarPosition (+16 more)

### Community 11 - "Glass"
Cohesion: 0.13
Nodes (6): Glass, Handle, IsOn, Pane, DesktopComposition, ICompositorDesktopInterop

### Community 12 - "Settings"
Cohesion: 0.10
Nodes (19): Settings, DiscordAvatarCount, DiscordAvatars, DiscordDm, DiscordEnabled, DiscordEveryone, DiscordInGames, DiscordMention (+11 more)

### Community 13 - "MainWindow"
Cohesion: 0.15
Nodes (7): MainWindow, SeekDisplay, Lines, Plain, TrackShape, Wave, UseTrackShape

### Community 14 - "Grid"
Cohesion: 0.04
Nodes (54): ArtSpot, BrightnessView, BubbleBody, BubbleRecord, BubbleTimer, ChargeView, DiscordMessagesPage, DiscordMusicCover (+46 more)

### Community 16 - "ObsService"
Cohesion: 0.14
Nodes (9): ObsService, Busy, Bytes, Elapsed, IsReady, Paused, Recording, Scene (+1 more)

### Community 20 - "Border"
Cohesion: 0.06
Nodes (40): ActualHeight, ChipCorners, ToolTip, Visibility, AccentTiles, BackdropTiles, BrightFill, Bubble (+32 more)

### Community 21 - "DrawingContext"
Cohesion: 0.10
Nodes (8): LyricMask, Rect, SeekPreview, Kind, BackdropPreview, Kind, Shades, LinearGradientBrush

### Community 22 - "Digits"
Cohesion: 0.05
Nodes (37): BigClock, BigTimer, BrightText, BubbleRecordText, BubbleShelfText, BubbleText, BubbleWeatherDegrees, ChargeText (+29 more)

### Community 23 - "Shelf"
Cohesion: 0.09
Nodes (13): Shelf, BITMAP, BITMAPINFOHEADER, IShellItemImageFactory, Item, IsPhoto, IsTextNote, Name (+5 more)

### Community 24 - "DateTime"
Cohesion: 0.05
Nodes (34): HourSlot, MiniComponent, All, Hours, Minutes, None, Seconds, Weather (+26 more)

### Community 26 - "View"
Cohesion: 0.05
Nodes (40): View, Brightness, Charge, Discord, DiscordMessages, DiscordPlugin, DiscordVoice, Focus (+32 more)

### Community 27 - "Settings"
Cohesion: 0.12
Nodes (32): Settings, Accent, AppVolume, Backdrop, CompactTrackAccent, Dots, Gap, Glass (+24 more)

### Community 28 - "Point"
Cohesion: 0.16
Nodes (4): Liquid, Drop, At, Style

### Community 29 - "LineBar"
Cohesion: 0.12
Nodes (13): LineBar, Column, Center, Left, Raised, Right, Rise, Text (+5 more)

### Community 30 - "LyricsSources.Create"
Cohesion: 0.07
Nodes (18): BetterLyricsProvider, LiriQoProvider, ILyricsProvider, IProgressiveLyricsProvider, LrclibProvider, RequestResult, LrclibRequestCoordinator, LrcMuxProvider (+10 more)

### Community 31 - "Task"
Cohesion: 0.11
Nodes (6): DiscordUser, DiscordRpcClient, InCall, IsConnected, Me, Voice

### Community 32 - "Scrubber"
Cohesion: 0.19
Nodes (5): Scrubber, Maximum, Minimum, Step, Value

### Community 34 - "PlaybackClock"
Cohesion: 0.21
Nodes (7): PlaybackClock, HasPosition, IsPlaying, LastTimelineReliable, Position, Rate, Stalled

### Community 35 - "Motion.Ms"
Cohesion: 0.12
Nodes (3): ShelfOverflow, ShelfTile, Item

### Community 36 - "Build-TrackIdentityIndex.py"
Cohesion: 0.13
Nodes (4): reach(), key(), musicbrainz_index(), source_index()

### Community 37 - "Icon"
Cohesion: 0.07
Nodes (27): AccentChevron, BackdropChevron, BrightIcon, BubbleWeatherIcon, CompactTrackAccentChevron, FocusIcon, HoverChevron, IdleWeatherIcon (+19 more)

### Community 39 - "Button"
Cohesion: 0.07
Nodes (28): DiscordPluginRow, GapRow, LyricsDiagnosticsCopy, LyricsEditorReset, LyricsSourceDiagnosticAction, LyricsSourceEdit, LyricsSourceOriginal, LyricsSourceRetry (+20 more)

### Community 40 - "Goo"
Cohesion: 0.07
Nodes (15): Glyph, Center, Effect, Fill, Glow, Index, Shade, Solid (+7 more)

### Community 42 - "smtc-players.md"
Cohesion: 0.08
Nodes (20): Галерея DynamicIsland, Островок в движении, Экраны, Провайдеры, Тексты песен и источники, Медиаплееры Windows, Dynamic Island для Windows, Запуск и сборка (+12 more)

### Community 43 - "DynamicIslandDiscordBridge.plugin.js"
Cohesion: 0.15
Nodes (25): avatarOf(), connect(), DEFAULTS, discordVersion(), err(), getSettingsPanel(), guard(), later() (+17 more)

### Community 44 - "Task"
Cohesion: 0.09
Nodes (14): LyricsSearchPreference, LyricsSearchPreferences, LyricsService, Candidates, Diagnostics, HasManualSelection, IsOff, IsPending (+6 more)

### Community 46 - "AreaPicker"
Cohesion: 0.08
Nodes (3): LyricsEdit, AreaPicker, Picked

### Community 47 - "Updater"
Cohesion: 0.13
Nodes (12): Updater, CurrentVersion, DownloadedBytes, ExePath, LatestVersion, Notes, OldPath, Percent (+4 more)

### Community 50 - "PlayerLine"
Cohesion: 0.15
Nodes (5): PlayerLine, DueAt, Middle, Row, Y

### Community 51 - "steps"
Cohesion: 0.09
Nodes (21): GH_TOKEN, TAG, UPDATE_REPOSITORY, jobs, release, name, on, push (+13 more)

### Community 52 - "Point"
Cohesion: 0.09
Nodes (15): WeatherGesture, Active, Delta, Kind, Motion, City, Close, Days (+7 more)

### Community 53 - "Avatar"
Cohesion: 0.06
Nodes (31): Avatar, Dimmed, Label, Live, Source, Speaking, Status, AvatarStatus (+23 more)

### Community 54 - "LyricsResult"
Cohesion: 0.08
Nodes (14): LyricsContentCleaner, LyricsResult, CacheSource, Empty, ErrorCode, MatchEvidence, ReasonCodes, RetryAt (+6 more)

### Community 55 - "Equalizer"
Cohesion: 0.07
Nodes (15): SpectrumLevels, Moved, this[], BandMeter, Heights, Equalizer, Bars, Dots (+7 more)

### Community 56 - "LyricsCandidate"
Cohesion: 0.11
Nodes (10): ProviderJson, LyricsDocumentIdentity, LyricsCandidate, HasValidSync, ProviderIdentityVerified, SourceUrl, TimingWarnings, Upstream (+2 more)

### Community 57 - "MainWindow.BuildAddPage"
Cohesion: 0.15
Nodes (6): Digits, BlurRadius, RollDown, Text, TravelDistance, WeatherSearchCursor

### Community 59 - "DynamicIslandDiscordBridge"
Cohesion: 0.10
Nodes (7): DynamicIslandDiscordBridge, MESSAGE_TYPES, logger, native, settings, start(), Dynamic Island для Vencord

### Community 60 - "steps"
Cohesion: 0.11
Nodes (18): runs-on, steps, timeout-minutes, jobs, build, name, on, pull_request (+10 more)

### Community 61 - "StackPanel"
Cohesion: 0.09
Nodes (22): BubbleShelf, BubbleShelfCount, BubbleWeather, DiscordMusicInfo, IdleComponents, IdleWeather, InfoBatRow, InfoHeadsetRow (+14 more)

### Community 62 - "DiscordService"
Cohesion: 0.07
Nodes (13): DiscordService, ConnectionMode, DiscordVersion, InCall, IsBridgeConnected, IsRpcConnected, JoinedAt, Linked (+5 more)

### Community 63 - "Spring.Snap"
Cohesion: 0.12
Nodes (3): FrameLoop, Running, RowList

### Community 64 - "LyricSource"
Cohesion: 0.11
Nodes (18): LyricSource, All, AmllTtmlDb, BetterLyrics, Deezer, Default, Genius, Karalyr (+10 more)

### Community 65 - "WaveBar"
Cohesion: 0.14
Nodes (11): SeekHover, Lift, Magnifier, Wave, WaveBar, Amplitude, Energy, Fill (+3 more)

### Community 66 - "PreserveSig"
Cohesion: 0.05
Nodes (19): AudioService, Device, Output, AudioEndpoint, IAudioClient, IAudioEndpointVolume, IAudioMeterInformation, IAudioSessionControl2 (+11 more)

### Community 67 - "Panel"
Cohesion: 0.08
Nodes (24): Panel, Discord, DiscordMessages, DiscordPlugin, DiscordVoice, Look, Lyrics, LyricsManual (+16 more)

### Community 68 - "LyricsNormalizer"
Cohesion: 0.05
Nodes (26): LyricsSecondaryQueries, AvatarImages, LyricsArtistAliases, LyricsNormalizer, LyricsQuery, Artist, Title, LyricsQueryKind (+18 more)

### Community 69 - "Settings.Read"
Cohesion: 0.17
Nodes (7): Shelf, TimerLastMinutes, TimerPresets, TimerPresetSlot, WeatherCities, WeatherHome, WeatherPlace

### Community 70 - "HoverPreview"
Cohesion: 0.10
Nodes (8): Hover, Disc, Flow, Magnet, HoverPreview, Kind, Waypoint, Puddle

### Community 74 - "Toggle"
Cohesion: 0.08
Nodes (25): AppVolumeSwitch, AutostartSwitch, DiscordDmSwitch, DiscordEnabledSwitch, DiscordEveryoneSwitch, DiscordInGamesSwitch, DiscordMentionSwitch, DiscordMovesSwitch (+17 more)

### Community 77 - "AreaPicker"
Cohesion: 0.15
Nodes (4): IAudioCaptureClient, SpectrumAnalyzer, SpectrumService, Active

### Community 78 - "UniformGrid"
Cohesion: 0.14
Nodes (14): AccentStrip, BackdropStrip, CompactTrackAccentStrip, DiscordPluginDownload, HoverStrip, LyricChangeStrip, LyricFxStrip, LyricsSourceActions (+6 more)

### Community 79 - "ScaleTransform"
Cohesion: 0.07
Nodes (26): ArtMove, ArtSize, BoltSize, BubbleMove, BubbleScale, DiscordMusicMove, EqMove, FocusSize (+18 more)

### Community 80 - "GradientStop"
Cohesion: 0.15
Nodes (13): LookEdgeBottom, LookEdgeTop, LyricMaskIn, LyricMaskOut, ShelfEdgeLeft, ShelfEdgeRight, UpdateNotesEdgeBottom, UpdateNotesEdgeTop (+5 more)

### Community 81 - "PlayerBar"
Cohesion: 0.27
Nodes (3): PlayerBar, POINT, Reading

### Community 82 - "CatalogueLyricsProvider"
Cohesion: 0.13
Nodes (6): CatalogueLyricsProvider, ProviderId, Site, Source, LyricsRemoteHttp, YouTubeMusicLyricsProvider

### Community 83 - "LrclibOptions"
Cohesion: 0.20
Nodes (10): LrclibOptions, DefaultCooldown, MaxBodyBytes, MaxCandidates, MaxRequests, RefinePlain, RequestSpacing, RequestTimeout (+2 more)

### Community 84 - "DotMatrix"
Cohesion: 0.20
Nodes (3): DotMatrix, Column, Rounding

### Community 86 - "LyricsMatcher"
Cohesion: 0.10
Nodes (11): LyricsIdentityHypothesis, RequiresConfirmation, RequiresDuration, LyricsMatch, LyricsMatcher, Scored, LyricsMatchEvidence, PreparedLyricsCandidate (+3 more)

### Community 87 - "MainWindow.PublishWeather"
Cohesion: 0.11
Nodes (10): ShowSeparateWeather, WeatherCityView, HomeFirst, WeatherCities, WeatherPlace, Valid, WeatherService, Now (+2 more)

### Community 88 - "Gadget.cs"
Cohesion: 0.18
Nodes (7): Откуда берутся картинки, Структура DynamicIsland, net8.0-windows10.0.19041.0, Microsoft.Data.Sqlite (8.0.32), Microsoft.Web.WebView2 (1.0.3537.50), ZXing.Net (0.16.11), Microsoft.NET.Sdk

### Community 89 - "DllImport"
Cohesion: 0.13
Nodes (5): TaskbarHiddenOpacity, MonitorInfo, QrReader, BITMAPINFOHEADER, Desktop

### Community 91 - "Canvas"
Cohesion: 0.25
Nodes (8): DiscordMusicLyrics, LyricBox, PlayerLyricLines, PlayerTimedViewport, Shared, WeatherDays, WeatherHours, Canvas

### Community 92 - "Backdrop"
Cohesion: 0.40
Nodes (5): Backdrop, Glow, Matrix, MatrixAndStars, Stars

### Community 93 - "TimeSpan"
Cohesion: 0.17
Nodes (6): LyricTiming, LrcParseResult, TrackDuration, CanMatch, IsKnown, Unknown

### Community 95 - "SpacePlaybackGesture"
Cohesion: 0.22
Nodes (5): SpacePlaybackGesture, Result, Consume, Pass, Toggle

### Community 96 - "Ellipse"
Cohesion: 0.20
Nodes (10): BubbleRecordDot, DiscordDot, RecordBigDot, RecordDot, RecordPauseDisc, Ring, TimerDisc, VoiceDeafenDisc (+2 more)

### Community 97 - "Stage"
Cohesion: 0.25
Nodes (8): Stage, Available, Checking, Failed, Idle, Latest, Loading, Unconfigured

### Community 99 - "MainWindow"
Cohesion: 0.06
Nodes (3): MainWindow, LookOverflow, UpdateNotesOverflow

### Community 101 - "Ring"
Cohesion: 0.29
Nodes (7): BigRing, BubbleRing, DiscordPluginRing, LoadingRing, TimerRing, UpdateRing, Ring

### Community 102 - "TextBox"
Cohesion: 0.33
Nodes (6): LyricsEditor, LyricsManualArtist, LyricsManualTitle, LyricsSourceDiagnostics, WeatherSearch, TextBox

### Community 104 - "MainWindow"
Cohesion: 0.06
Nodes (7): MainWindow, VencordInstallDirectory, VencordPluginConnected, IsMediaActive, MediaSpot, Values, PillShape

### Community 105 - "RotateTransform"
Cohesion: 0.40
Nodes (5): BoltTurn, FocusTurn, NoticeTurn, SysTurn, RotateTransform

### Community 106 - "ScrollViewer"
Cohesion: 0.40
Nodes (5): LyricsSourceScroll, PART_ContentHost, PlayerPlainScroll, WeatherCitiesScroll, ScrollViewer

### Community 107 - "LyricsResult"
Cohesion: 0.09
Nodes (21): LyricsDiagnosticEntry, Providers, LyricsDiagnostics, LyricsProviderDiagnostic, LyricsStatus, Ambiguous, AwaitingVerification, Disabled (+13 more)

### Community 108 - "Updater.CheckAsync"
Cohesion: 0.31
Nodes (3): PluginInstaller, BetterDiscordPath, Configured

### Community 112 - "Segments"
Cohesion: 0.33
Nodes (6): DiscordAvatarsSegments, DiscordMessageTimeSegments, DotsSegments, MiniWeatherSegments, PointerLyricSegments, Segments

### Community 113 - "ToggleButton"
Cohesion: 0.50
Nodes (4): MiniHours, MiniMinutes, MiniSeconds, ToggleButton

### Community 116 - "LyricsCacheOptions"
Cohesion: 0.18
Nodes (11): LyricsCacheOptions, HitTtl, MaxCandidates, MaxDiskBytes, MaxDiskEntries, MaxFileBytes, MaxMemoryBytes, MaxMemoryCandidates (+3 more)

### Community 117 - "LyricChange"
Cohesion: 0.50
Nodes (4): LyricChange, Drum, Smooth, Wave

### Community 118 - "CompactLyric"
Cohesion: 0.06
Nodes (20): CompactLyric, Centring, Cluster, Pieces, EffectStrength, IsEntered, IslandWidth, Offset (+12 more)

### Community 119 - "Autostart"
Cohesion: 0.50
Nodes (3): Autostart, Enabled, StartupCommand

### Community 120 - "SeekPreviewKind"
Cohesion: 0.29
Nodes (7): SeekPreviewKind, Lift, Lines, Magnifier, Ripple, TrackShape, Wave

### Community 121 - "GapSlider"
Cohesion: 0.67
Nodes (3): GapSlider, SizeSlider, Scrubber

### Community 122 - "LookRows"
Cohesion: 0.50
Nodes (4): LookRows, MenuRows, WeatherCityList, RowList

### Community 123 - "LyricA"
Cohesion: 0.40
Nodes (5): DiscordLyricA, DiscordLyricB, LyricA, LyricB, CompactLyric

### Community 124 - "NextIcon"
Cohesion: 0.67
Nodes (3): NextIcon, PrevIcon, Skip

### Community 125 - "SeekLines"
Cohesion: 0.67
Nodes (3): SeekLines, SeekPlain, LineBar

### Community 126 - "PointerLyricShape"
Cohesion: 0.67
Nodes (3): PointerLyricShape, Column, Line

### Community 129 - "Art"
Cohesion: 0.67
Nodes (3): Art, DiscordMusicArt, Cover

### Community 131 - "Eq"
Cohesion: 0.67
Nodes (3): DiscordMusicEq, Eq, Equalizer

### Community 132 - "Glow"
Cohesion: 0.67
Nodes (3): DiscordMusicGlow, Glow, Aura

### Community 143 - "Lyric"
Cohesion: 0.12
Nodes (5): Lyric, EffectStrength, EffectVisible, Tempo, TextEffect

### Community 144 - "Op"
Cohesion: 0.29
Nodes (7): Op, Event, Hello, Identified, Identify, Request, RequestResponse

### Community 145 - "LyricFxPlan.Sample"
Cohesion: 0.13
Nodes (10): LyricFx, Glow, Lift, Off, Shimmer, LyricFxFrame, LyricFxLetter, LyricFxPlan (+2 more)

### Community 146 - "DrawingSurfaces"
Cohesion: 0.09
Nodes (7): DrawingSurfaces, Box, ICompositionDrawingSurfaceInterop, ICompositorInterop, Offset, SetMultithreadProtectedCall, UpdateSubresourceCall

### Community 149 - "NetworkService"
Cohesion: 0.23
Nodes (3): NetworkService, State, Tunnels

### Community 151 - "Link"
Cohesion: 0.40
Nodes (5): Link, Closed, Disabled, Ready, Refused

### Community 152 - "CacheDocument"
Cohesion: 0.22
Nodes (9): CacheDocument, ByteSize, CreatedUtc, ExpiresUtc, Format, Key, NormalizationVersion, Result (+1 more)

### Community 160 - "MainWindow.Notify"
Cohesion: 0.05
Nodes (6): Countdown, IsActive, IsRunning, Remaining, RemainingShare, Total

### Community 163 - "Stage"
Cohesion: 0.33
Nodes (6): Stage, Failed, Loading, NoPlace, Off, Ready

### Community 164 - "DurationOrigin"
Cohesion: 0.40
Nodes (5): DurationOrigin, LyricsEstimate, MediaSession, ProgressEstimate, Unknown

### Community 167 - "App.Log"
Cohesion: 0.13
Nodes (4): App, Application, App, Alarm

### Community 168 - "Grab"
Cohesion: 0.40
Nodes (5): Grab, Held, Lean, None, Pull

### Community 169 - "MainWindow"
Cohesion: 0.03
Nodes (14): MainWindow, DiscordActive, ShowsVoice, DiscordNormalMusic, WantsDiscordMusic, IsLyricSettling, ShowShelfCount, HiddenByFullscreen (+6 more)

### Community 170 - "MediaKey"
Cohesion: 0.40
Nodes (5): MediaKey, NextTrack, PreviousTrack, VolumeDown, VolumeUp

### Community 172 - "Spring"
Cohesion: 0.08
Nodes (13): Spring, Damping, Stiffness, Target, Value, Velocity, Segments, Labels (+5 more)

### Community 174 - "DiscordBridgeServer.Command"
Cohesion: 0.10
Nodes (5): DiscordBridgeServer, Connection, Socket, IsConnected, Port

### Community 177 - "Color"
Cohesion: 0.08
Nodes (7): LyricFxPaint, Outline, MediaSessionSelection, CoverPalette, Aura, Patch, Shimmer

### Community 178 - "Gadget"
Cohesion: 0.18
Nodes (7): Motion, Gadget, Model, Earbuds, Headphones, Speaker, Turn

### Community 180 - "Path"
Cohesion: 0.25
Nodes (8): PauseIcon, PlayIcon, RecordPauseIcon, RecordResumeIcon, Shadow, TimerPauseIcon, TimerPlayIcon, Path

### Community 181 - "PointerLyric"
Cohesion: 0.06
Nodes (18): PointerLyric, Below, Gap, HalfHeight, Handle, InColumn, Letter, Line (+10 more)

### Community 186 - "DiscordModels.cs"
Cohesion: 0.16
Nodes (8): ChatMessage, DeepLink, HelloInfo, MessageAttachment, MessageAuthor, VoiceChannel, VoiceMember, VoiceState

### Community 188 - "Link"
Cohesion: 0.40
Nodes (5): Link, Mobile, None, Wifi, Wired

### Community 189 - "SoundCloudLike"
Cohesion: 0.29
Nodes (7): SoundCloudLike, Liked, Pending, SignInRequired, TrackButtonMissing, Unknown, Unliked

### Community 195 - "VoiceDeafenIcon"
Cohesion: 0.67
Nodes (3): VoiceDeafenIcon, VoiceMuteIcon, VoiceIcon

## Knowledge Gaps
- **609 isolated node(s):** `MainWindow`, `Content`, `Track`, `Tag`, `Goo` (+604 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 967 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **78 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Window` connect `Window` to `Art`, `Body`, `Eq`, `Glow`, `HeadsetGadget`, `PART_Track`, `PlayerLyricWait`, `PlayerMatrix`, `PlayerStars`, `SeekTrackShape`, `SeekWave`, `ShelfZone`, `MainWindow.BuildWeatherHours`, `Grid`, `WeatherHomeHit`, `Glass`, `Border`, `DrawingContext`, `Digits`, `SoundCloudBrowser`, `Icon`, `Button`, `SoundCloudHeartReveal`, `AreaPicker`, `Path`, `StackPanel`, `VoiceDeafenIcon`, `MessageAvatar`, `Toggle`, `UniformGrid`, `ScaleTransform`, `GradientStop`, `Canvas`, `Ellipse`, `Ring`, `TextBox`, `MainWindow`, `RotateTransform`, `ScrollViewer`, `Segments`, `ToggleButton`, `GapSlider`, `LookRows`, `LyricA`, `NextIcon`, `SeekLines`?**
  _High betweenness centrality (0.184) - this node is a cross-community bridge._
- **What connects `MainWindow`, `Content`, `Track` to the rest of the system?**
  _609 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TrackSnapshot` be split into smaller, more focused modules?**
  _Cohesion score 0.14487179487179488 - nodes in this community are weakly interconnected._
- **Why does `MainWindow` connect `MainWindow` to `Native`, `MainWindow.UpdateTargets`, `Window`, `LyricsNormalizer`, `MainWindow`, `Panel`, `Goo`, `system_windows`, `MediaService`, `RoutedEventArgs`, `Lyric`, `Border`, `FrameworkElement`, `MainWindow.ShowTransient`, `DynamicIsland`, `View`, `TimeSpan`, `Spring.Snap`?**
  _High betweenness centrality (0.163) - this node is a cross-community bridge._
- **Should `Native` be split into smaller, more focused modules?**
  _Cohesion score 0.05306217858812529 - nodes in this community are weakly interconnected._
- **Why does `MainWindow` connect `MainWindow` to `Native`, `MainWindow.BuildWeatherHours`, `system_windows`, `SourceApp`, `Glass`, `MainWindow.StartShapeLoop`, `MainWindow.UpdateMiniIsland`, `Border`, `FrameworkElement`, `MainWindow.SearchWeatherAsync`, `DrawingContext`, `Shelf`, `SoundCloudBrowser`, `Point`, `MainWindow.Notify`, `MainWindow.UpdateTargets`, `Motion.Ms`, `LyricsOffsetStore`, `Grab`, `RoutedEventArgs`, `PlayerLine`, `Gadget`, `PointerLyric`, `LyricsResult`, `LyricsCandidate`, `MainWindow.BuildAddPage`, `Spring.Snap`, `PreserveSig`, `LyricsNormalizer`, `HoverPreview`, `Uri`, `Uri`, `MainWindow.RefreshWeatherCities`, `ChipCorners`, `MainWindow.PublishWeather`, `MainWindow.ShowTransient`, `TimeSpan`, `MainWindow.UpdatePlayerLyrics`, `MainWindow`, `MainWindow`, `CompactLyric`?**
  _High betweenness centrality (0.150) - this node is a cross-community bridge._
- **Should `Window` be split into smaller, more focused modules?**
  _Cohesion score 0.05052631578947368 - nodes in this community are weakly interconnected._