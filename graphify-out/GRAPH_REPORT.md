# Graph Report - DynamicIsland-1.30.0  (2026-11-23)

## Corpus Check
- 179 files · ~134,545 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 12 file(s) not represented in the graph (top: .otf 4, .ttf 4, (none) 3)

## Summary
- 4203 nodes · 10619 edges · 214 communities (124 shown, 90 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 1034 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- MainWindow
- Native
- IntPtr
- Window
- Glyph
- MainWindow.BuildWeatherHours
- MainWindow.OnLoaded
- ClipboardShelfCapture
- SourceApp
- MediaService
- Glass
- Settings
- MainWindow
- Grid
- ObsService
- TrackSnapshot
- Border
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
- ShelfTile
- Build-TrackIdentityIndex.py
- Icon
- Segments
- Button
- Goo
- Stage
- smtc-players.md
- DynamicIslandDiscordBridge.plugin.js
- Task
- Updater
- Toggle
- CoverFallback
- AreaPicker
- steps
- Point
- Avatar
- LyricsPresentation
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
- LyricsOffsetStore
- ActionPeer
- CoreAudio.cs
- Toggle
- Uri
- WeatherPlace
- SpectrumService
- UniformGrid
- ScaleTransform
- GradientStop
- PlayerBar
- CatalogueLyricsProvider
- LrclibOptions
- LyricsMatcher
- MainWindow.PublishWeather
- system_globalization
- DllImport
- SpacePlaybackHook
- Canvas
- Backdrop
- TimeSpan
- DateTime
- Ellipse
- Puddle
- AudioService
- Geometry
- Ring
- TextBox
- LyricsCache
- MainWindow.ApplyShape
- RotateTransform
- ScrollViewer
- LyricsResult
- TrackShapeBar
- DelayedAction.Start
- Segments
- ToggleButton
- Cover
- LyricsCacheOptions
- LyricChange
- CompactLyric
- ReadOnlySpan
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
- LyricFxPlan.Sample
- DrawingSurfaces
- NetworkService
- Brush
- IMMDevice
- LyricPreview
- SoundCloudBrowser
- Glyph
- FormattedText
- PointerLyric.BuildColumn
- Motion.Ms
- HelloInfo
- Piece
- App
- Grab
- MainWindow
- DurationOrigin
- SoundCloudHeartReveal
- Spring
- AudioService.Sessions
- youtube-preview-media-guard.user.js
- App.Log
- Color
- Gadget
- Path
- PointerLyric
- TurnEase
- Op
- MainWindow.LyricsEditor_TextChanged
- IAudioClient
- DiscordBridgeServer
- Link
- SoundCloudLike
- AdvanceStep
- VoiceDeafenIcon
- Link
- MessageAvatar
- Symbol
- IsFocusUnsettled
- WeatherTyping
- ChipCorners
- Motion.Delayed
- AccentColor
- IsEqVisible

## God Nodes (most connected - your core abstractions)
1. `Window` - 435 edges
2. `MainWindow` - 329 edges
3. `DynamicIsland` - 155 edges
4. `MediaService` - 91 edges
5. `Native` - 89 edges
6. `TrackSnapshot` - 81 edges
7. `LyricsService` - 76 edges
8. `TextBlock` - 73 edges
9. `MainWindow` - 71 edges
10. `Glyph` - 69 edges

## Surprising Connections (you probably didn't know these)
- `Dynamic Island для Vencord` --references--> `DynamicIslandDiscordBridge`  [INFERRED]
  src/Discord/Vencord/dynamicIslandBridge.desktop/README.md → src/Discord/Vencord/dynamicIslandBridge.desktop/bridge.js
- `MainWindow` --inherits--> `Window`  [EXTRACTED]
  src/Discord/MainWindow.Discord.cs → MainWindow.xaml
- `Glass` --references--> `Window`  [EXTRACTED]
  src/UI/Effects/Glass.cs → MainWindow.xaml
- `AreaPicker` --inherits--> `Window`  [EXTRACTED]
  src/Windows/AreaPicker.cs → MainWindow.xaml
- `SoundCloudBrowser` --inherits--> `Window`  [EXTRACTED]
  src/Media/SoundCloudBrowser.cs → MainWindow.xaml

## Import Cycles
- None detected.

## Communities (214 total, 90 thin omitted)

### Community 0 - "MainWindow"
Cohesion: 0.07
Nodes (4): MainWindow, LookOverflow, UpdateNotesOverflow, Size

### Community 1 - "Native"
Cohesion: 0.04
Nodes (22): MaterialgramWindowMode, Native, CursorPosition, DispatcherQueueOptions, DISPLAYMODE, ForegroundWindow, InputMessageSource, IsCtrlDown (+14 more)

### Community 2 - "IntPtr"
Cohesion: 0.12
Nodes (4): TaskbarHiddenOpacity, MonitorInfo, MediaWindow, ICompositorInterop

### Community 3 - "Window"
Cohesion: 0.05
Nodes (75): Content, Tag, AccentText, ArtistBig, BackdropText, BigDate, BigTimerLabel, CompactTrackAccentText (+67 more)

### Community 4 - "Glyph"
Cohesion: 0.03
Nodes (59): Glyph, Back, Bars, Battery, Bell, Bolt, Bulge, Check (+51 more)

### Community 9 - "SourceApp"
Cohesion: 0.09
Nodes (4): SourceApp, Destination, SoundCloudPlayer, WindowProc

### Community 10 - "MediaService"
Cohesion: 0.06
Nodes (24): MediaService, Accent, Album, Art, Artist, DisplayDuration, Duration, HasBarPosition (+16 more)

### Community 11 - "Glass"
Cohesion: 0.13
Nodes (6): Glass, Handle, IsOn, Pane, DesktopComposition, ICompositorDesktopInterop

### Community 12 - "Settings"
Cohesion: 0.10
Nodes (18): Settings, DiscordAvatarCount, DiscordAvatars, DiscordDm, DiscordEnabled, DiscordEveryone, DiscordInGames, DiscordMention (+10 more)

### Community 13 - "MainWindow"
Cohesion: 0.10
Nodes (12): PlayerLine, DueAt, Middle, Row, Y, MainWindow, SeekDisplay, Lines (+4 more)

### Community 14 - "Grid"
Cohesion: 0.04
Nodes (54): ArtSpot, BrightnessView, BubbleBody, BubbleRecord, BubbleTimer, ChargeView, DiscordMessagesPage, DiscordMusicCover (+46 more)

### Community 16 - "ObsService"
Cohesion: 0.11
Nodes (9): ObsService, Busy, Bytes, Elapsed, IsReady, Paused, Recording, Scene (+1 more)

### Community 17 - "TrackSnapshot"
Cohesion: 0.15
Nodes (7): ProviderSearchResult, NeedsRefresh, PreparedLyricsPool, TrackSnapshot, IdentityArtistAliases, IdentityPrimaryArtist, IdentityRequiresDuration

### Community 18 - "Border"
Cohesion: 0.09
Nodes (3): WeatherActionBorder, FocusRing, InvokeAction

### Community 20 - "Border"
Cohesion: 0.06
Nodes (40): ActualHeight, ChipCorners, ToolTip, Visibility, AccentTiles, BackdropTiles, BrightFill, Bubble (+32 more)

### Community 21 - "DrawingContext"
Cohesion: 0.20
Nodes (3): Rect, SeekPreview, Kind

### Community 22 - "Digits"
Cohesion: 0.05
Nodes (37): BigClock, BigTimer, BrightText, BubbleRecordText, BubbleShelfText, BubbleText, BubbleWeatherDegrees, ChargeText (+29 more)

### Community 23 - "Shelf"
Cohesion: 0.07
Nodes (15): Shelf, BITMAP, BITMAPINFOHEADER, IShellItemImageFactory, Item, IsPhoto, IsTextNote, Name (+7 more)

### Community 24 - "DateTime"
Cohesion: 0.06
Nodes (26): WeatherWebData, HourSlot, Sky, Clear, Cloudy, Fog, Overcast, Rain (+18 more)

### Community 26 - "View"
Cohesion: 0.05
Nodes (40): View, Brightness, Charge, Discord, DiscordMessages, DiscordPlugin, DiscordVoice, Focus (+32 more)

### Community 27 - "Settings"
Cohesion: 0.12
Nodes (32): Settings, Accent, AppVolume, Backdrop, CompactTrackAccent, Dots, Gap, Glass (+24 more)

### Community 29 - "LineBar"
Cohesion: 0.11
Nodes (13): LineBar, Column, Center, Left, Raised, Right, Rise, Text (+5 more)

### Community 30 - "LyricsSources.Create"
Cohesion: 0.06
Nodes (19): BetterLyricsProvider, LiriQoProvider, ILyricsProvider, IProgressiveLyricsProvider, LrclibProvider, RequestResult, LrclibRequestCoordinator, LrcMuxProvider (+11 more)

### Community 31 - "Task"
Cohesion: 0.07
Nodes (13): ChatMessage, DeepLink, DiscordUser, MessageAttachment, MessageAuthor, VoiceChannel, VoiceMember, VoiceState (+5 more)

### Community 32 - "Scrubber"
Cohesion: 0.22
Nodes (5): Scrubber, Maximum, Minimum, Step, Value

### Community 34 - "PlaybackClock"
Cohesion: 0.21
Nodes (7): PlaybackClock, HasPosition, IsPlaying, LastTimelineReliable, Position, Rate, Stalled

### Community 35 - "ShelfTile"
Cohesion: 0.28
Nodes (3): ShelfOverflow, ShelfTile, Item

### Community 36 - "Build-TrackIdentityIndex.py"
Cohesion: 0.13
Nodes (4): reach(), key(), musicbrainz_index(), source_index()

### Community 37 - "Icon"
Cohesion: 0.07
Nodes (27): AccentChevron, BackdropChevron, BrightIcon, BubbleWeatherIcon, CompactTrackAccentChevron, FocusIcon, HoverChevron, IdleWeatherIcon (+19 more)

### Community 39 - "Button"
Cohesion: 0.07
Nodes (28): DiscordPluginDownload, DiscordPluginRow, GapRow, LyricsDiagnosticsCopy, LyricsEditorReset, LyricsSourceDiagnosticAction, LyricsSourceEdit, LyricsSourceOriginal (+20 more)

### Community 40 - "Goo"
Cohesion: 0.16
Nodes (4): Goo, BubbleInside, Light, PillInside

### Community 41 - "Stage"
Cohesion: 0.33
Nodes (6): Stage, Failed, Loading, NoPlace, Off, Ready

### Community 42 - "smtc-players.md"
Cohesion: 0.07
Nodes (16): Галерея DynamicIsland, Островок в движении, Экраны, Провайдеры, Тексты песен и источники, Медиаплееры Windows, Dynamic Island для Windows, Запуск и сборка (+8 more)

### Community 43 - "DynamicIslandDiscordBridge.plugin.js"
Cohesion: 0.15
Nodes (25): avatarOf(), connect(), DEFAULTS, discordVersion(), err(), getSettingsPanel(), guard(), later() (+17 more)

### Community 44 - "Task"
Cohesion: 0.09
Nodes (14): LyricsSearchPreference, LyricsSearchPreferences, LyricsService, Candidates, Diagnostics, HasManualSelection, IsOff, IsPending (+6 more)

### Community 47 - "Updater"
Cohesion: 0.06
Nodes (27): Updater, CurrentVersion, DownloadedBytes, ExePath, LatestVersion, Notes, OldPath, Percent (+19 more)

### Community 51 - "steps"
Cohesion: 0.09
Nodes (21): GH_TOKEN, TAG, UPDATE_REPOSITORY, jobs, release, name, on, push (+13 more)

### Community 52 - "Point"
Cohesion: 0.10
Nodes (15): WeatherGesture, Active, Delta, Kind, Motion, City, Close, Days (+7 more)

### Community 53 - "Avatar"
Cohesion: 0.05
Nodes (34): Avatar, Dimmed, Label, Live, Source, Speaking, Status, AvatarStatus (+26 more)

### Community 54 - "LyricsPresentation"
Cohesion: 0.17
Nodes (5): LyricsContentCleaner, LyricsPresentation, CanOpenEditor, Credits, RetryTooltip

### Community 55 - "Equalizer"
Cohesion: 0.11
Nodes (9): BandMeter, Heights, Equalizer, Bars, Dots, Few, Open, Aura (+1 more)

### Community 56 - "LyricsCandidate"
Cohesion: 0.17
Nodes (9): ProviderJson, LyricsDocumentIdentity, LyricsCandidate, HasValidSync, ProviderIdentityVerified, SourceUrl, TimingWarnings, Upstream (+1 more)

### Community 57 - "MainWindow.BuildAddPage"
Cohesion: 0.10
Nodes (9): LyricMask, LyricsPresentationControls, Digits, BlurRadius, RollDown, Text, TravelDistance, WeatherSearchCursor (+1 more)

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
Cohesion: 0.10
Nodes (13): DiscordService, ConnectionMode, DiscordVersion, InCall, IsBridgeConnected, IsRpcConnected, JoinedAt, Linked (+5 more)

### Community 63 - "Spring.Snap"
Cohesion: 0.14
Nodes (3): FrameLoop, Running, RowList

### Community 64 - "LyricSource"
Cohesion: 0.11
Nodes (18): LyricSource, All, AmllTtmlDb, BetterLyrics, Deezer, Default, Genius, Karalyr (+10 more)

### Community 65 - "WaveBar"
Cohesion: 0.14
Nodes (11): SeekHover, Lift, Magnifier, Wave, WaveBar, Amplitude, Energy, Fill (+3 more)

### Community 66 - "PreserveSig"
Cohesion: 0.13
Nodes (3): IAudioEndpointVolume, IAudioSessionControl2, ISimpleAudioVolume

### Community 67 - "Panel"
Cohesion: 0.08
Nodes (24): Panel, Discord, DiscordMessages, DiscordPlugin, DiscordVoice, Look, Lyrics, LyricsManual (+16 more)

### Community 68 - "LyricsNormalizer"
Cohesion: 0.06
Nodes (25): LyricsSecondaryQueries, LyricsArtistAliases, LyricsNormalizer, LyricsQuery, Artist, Title, LyricsQueryKind, ArtistSplit (+17 more)

### Community 69 - "Settings.Read"
Cohesion: 0.17
Nodes (7): Shelf, TimerLastMinutes, TimerPresets, TimerPresetSlot, WeatherCities, WeatherHome, WeatherPlace

### Community 70 - "HoverPreview"
Cohesion: 0.18
Nodes (7): Hover, Disc, Flow, Magnet, HoverPreview, Kind, Waypoint

### Community 73 - "CoreAudio.cs"
Cohesion: 0.26
Nodes (5): Output, IPropertyStore, PropertyKey, PropertyStoreReader, PropVariant

### Community 74 - "Toggle"
Cohesion: 0.08
Nodes (25): AppVolumeSwitch, AutostartSwitch, DiscordDmSwitch, DiscordEnabledSwitch, DiscordEveryoneSwitch, DiscordInGamesSwitch, DiscordMentionSwitch, DiscordMovesSwitch (+17 more)

### Community 75 - "Uri"
Cohesion: 0.16
Nodes (3): SoundCloudWaveform, Profile, TrackData

### Community 76 - "WeatherPlace"
Cohesion: 0.11
Nodes (8): WeatherLocation, WeatherPlace, DisplayName, Valid, WeatherService, Now, PlaceName, State

### Community 77 - "SpectrumService"
Cohesion: 0.14
Nodes (3): IAudioCaptureClient, SpectrumService, Active

### Community 78 - "UniformGrid"
Cohesion: 0.15
Nodes (13): AccentStrip, BackdropStrip, CompactTrackAccentStrip, HoverStrip, LyricChangeStrip, LyricFxStrip, LyricsSourceActions, SeekAccentStrip (+5 more)

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
Cohesion: 0.12
Nodes (6): CatalogueLyricsProvider, ProviderId, Site, Source, LyricsRemoteHttp, YouTubeMusicLyricsProvider

### Community 83 - "LrclibOptions"
Cohesion: 0.20
Nodes (10): LrclibOptions, DefaultCooldown, MaxBodyBytes, MaxCandidates, MaxRequests, RefinePlain, RequestSpacing, RequestTimeout (+2 more)

### Community 86 - "LyricsMatcher"
Cohesion: 0.10
Nodes (11): LyricsIdentityHypothesis, RequiresConfirmation, RequiresDuration, LyricsMatch, LyricsMatcher, Scored, LyricsMatchEvidence, PreparedLyricsCandidate (+3 more)

### Community 87 - "MainWindow.PublishWeather"
Cohesion: 0.18
Nodes (3): WeatherCityView, HomeFirst, WeatherCities

### Community 88 - "system_globalization"
Cohesion: 0.09
Nodes (7): Откуда берутся картинки, Структура DynamicIsland, net8.0-windows10.0.19041.0, Microsoft.Data.Sqlite (8.0.32), Microsoft.Web.WebView2 (1.0.3537.50), ZXing.Net (0.16.11), Microsoft.NET.Sdk

### Community 89 - "DllImport"
Cohesion: 0.26
Nodes (3): QrReader, BITMAPINFOHEADER, Desktop

### Community 90 - "SpacePlaybackHook"
Cohesion: 0.12
Nodes (7): SpacePlaybackGesture, Result, Consume, Pass, Toggle, SpacePlaybackHook, HookProc

### Community 91 - "Canvas"
Cohesion: 0.25
Nodes (8): DiscordMusicLyrics, LyricBox, PlayerLyricLines, PlayerTimedViewport, Shared, WeatherDays, WeatherHours, Canvas

### Community 92 - "Backdrop"
Cohesion: 0.40
Nodes (5): Backdrop, Glow, Matrix, MatrixAndStars, Stars

### Community 93 - "TimeSpan"
Cohesion: 0.15
Nodes (7): LyricTiming, FinalLine, LrcParseResult, TrackDuration, CanMatch, IsKnown, Unknown

### Community 95 - "DateTime"
Cohesion: 0.15
Nodes (9): MiniComponent, All, Hours, Minutes, None, Seconds, Weather, Weekday (+1 more)

### Community 96 - "Ellipse"
Cohesion: 0.20
Nodes (10): BubbleRecordDot, DiscordDot, RecordBigDot, RecordDot, RecordPauseDisc, Ring, TimerDisc, VoiceDeafenDisc (+2 more)

### Community 99 - "AudioService"
Cohesion: 0.27
Nodes (3): AudioService, Device, IAudioMeterInformation

### Community 101 - "Ring"
Cohesion: 0.29
Nodes (7): BigRing, BubbleRing, DiscordPluginRing, LoadingRing, TimerRing, UpdateRing, Ring

### Community 102 - "TextBox"
Cohesion: 0.33
Nodes (6): LyricsEditor, LyricsManualArtist, LyricsManualTitle, LyricsSourceDiagnostics, WeatherSearch, TextBox

### Community 103 - "LyricsCache"
Cohesion: 0.09
Nodes (12): LyricsCache, CacheDocument, ByteSize, CreatedUtc, ExpiresUtc, Format, Key, NormalizationVersion (+4 more)

### Community 105 - "RotateTransform"
Cohesion: 0.40
Nodes (5): BoltTurn, FocusTurn, NoticeTurn, SysTurn, RotateTransform

### Community 106 - "ScrollViewer"
Cohesion: 0.40
Nodes (5): LyricsSourceScroll, PART_ContentHost, PlayerPlainScroll, WeatherCitiesScroll, ScrollViewer

### Community 107 - "LyricsResult"
Cohesion: 0.06
Nodes (30): LyricsDiagnosticEntry, Providers, LyricsDiagnostics, LyricsProviderDiagnostic, LyricsResult, CacheSource, Empty, ErrorCode (+22 more)

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
Cohesion: 0.09
Nodes (13): CompactLyric, Centring, EffectStrength, IsEntered, IslandWidth, Offset, Pose, Progress (+5 more)

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

### Community 145 - "LyricFxPlan.Sample"
Cohesion: 0.14
Nodes (10): LyricFx, Glow, Lift, Off, Shimmer, LyricFxFrame, LyricFxLetter, LyricFxPlan (+2 more)

### Community 146 - "DrawingSurfaces"
Cohesion: 0.11
Nodes (6): DrawingSurfaces, Box, ICompositionDrawingSurfaceInterop, Offset, SetMultithreadProtectedCall, UpdateSubresourceCall

### Community 149 - "NetworkService"
Cohesion: 0.23
Nodes (3): NetworkService, State, Tunnels

### Community 151 - "Brush"
Cohesion: 0.09
Nodes (11): SpectrumLevels, Moved, this[], BackdropPreview, Kind, Star, Shades, StarField (+3 more)

### Community 152 - "IMMDevice"
Cohesion: 0.25
Nodes (4): AudioEndpoint, IMMDevice, IMMDeviceEnumerator, MMDeviceEnumeratorCom

### Community 156 - "Glyph"
Cohesion: 0.18
Nodes (9): RowSlide, Glyph, Center, Effect, Fill, Glow, Index, Shade (+1 more)

### Community 157 - "FormattedText"
Cohesion: 0.18
Nodes (5): Cluster, Pieces, Piece, Glyphs, Index

### Community 159 - "PointerLyric.BuildColumn"
Cohesion: 0.10
Nodes (9): Letter, Line, IsLeaving, LeftAt, Scroll, Seconds, StartedAt, Word (+1 more)

### Community 160 - "Motion.Ms"
Cohesion: 0.12
Nodes (6): Countdown, IsActive, IsRunning, Remaining, RemainingShare, Total

### Community 167 - "App"
Cohesion: 0.25
Nodes (3): App, Application, App

### Community 168 - "Grab"
Cohesion: 0.40
Nodes (5): Grab, Held, Lean, None, Pull

### Community 169 - "MainWindow"
Cohesion: 0.03
Nodes (17): MainWindow, DiscordActive, ShowsVoice, DiscordNormalMusic, WantsDiscordMusic, VencordPluginConnected, IsLyricSettling, ShowShelfCount (+9 more)

### Community 170 - "DurationOrigin"
Cohesion: 0.40
Nodes (5): DurationOrigin, LyricsEstimate, MediaSession, ProgressEstimate, Unknown

### Community 172 - "Spring"
Cohesion: 0.11
Nodes (13): Spring, Damping, Stiffness, Target, Value, Velocity, Blob, At (+5 more)

### Community 175 - "youtube-preview-media-guard.user.js"
Cohesion: 0.53
Nodes (4): get(), isVideoPage(), set(), syncRoute()

### Community 177 - "Color"
Cohesion: 0.09
Nodes (7): LyricFxPaint, Outline, CoverPalette, DotMatrix, Column, Rounding, Shimmer

### Community 178 - "Gadget"
Cohesion: 0.13
Nodes (7): Headset, Gadget, Model, Earbuds, Headphones, Speaker, Turn

### Community 180 - "Path"
Cohesion: 0.25
Nodes (8): PauseIcon, PlayIcon, RecordPauseIcon, RecordResumeIcon, Shadow, TimerPauseIcon, TimerPlayIcon, Path

### Community 181 - "PointerLyric"
Cohesion: 0.10
Nodes (10): PointerLyric, Below, Gap, HalfHeight, Handle, InColumn, Now, Pose (+2 more)

### Community 183 - "Op"
Cohesion: 0.29
Nodes (7): Op, Event, Hello, Identified, Identify, Request, RequestResponse

### Community 186 - "DiscordBridgeServer"
Cohesion: 0.17
Nodes (5): DiscordBridgeServer, Connection, Socket, IsConnected, Port

### Community 188 - "Link"
Cohesion: 0.40
Nodes (5): Link, Mobile, None, Wifi, Wired

### Community 189 - "SoundCloudLike"
Cohesion: 0.29
Nodes (7): SoundCloudLike, Liked, Pending, SignInRequired, TrackButtonMissing, Unknown, Unliked

### Community 195 - "VoiceDeafenIcon"
Cohesion: 0.67
Nodes (3): VoiceDeafenIcon, VoiceMuteIcon, VoiceIcon

### Community 197 - "Link"
Cohesion: 0.40
Nodes (5): Link, Closed, Disabled, Ready, Refused

## Knowledge Gaps
- **608 isolated node(s):** `DiscordActive`, `ShowsVoice`, `VencordPluginConnected`, `SystemSlot`, `LookOverflow` (+603 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 965 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **90 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `MainWindow`, `Native`, `IntPtr`, `Window`, `MainWindow.BuildWeatherHours`, `MainWindow.OnLoaded`, `ClipboardShelfCapture`, `system_windows`, `Glass`, `MainWindow`, `MainWindow.StartShapeLoop`, `ObsService`, `Border`, `FrameworkElement`, `MainWindow.RefreshWeatherCities`, `Shelf`, `Brush`, `DynamicIsland`, `SoundCloudBrowser`, `Spring.Advance`, `Point`, `View`, `MediaService.SendCommandAsync`, `MainWindow.UpdateTargets`, `Grab`, `Spring`, `RoutedEventArgs`, `Updater`, `App.Log`, `Gadget`, `WeatherActionBorder.cs`, `PointerLyric`, `MainWindow.BuildAddPage`, `PreserveSig`, `Panel`, `LyricsNormalizer`, `LyricsOffsetStore`, `Uri`, `WeatherPlace`, `MainWindow.UpdatePlayerLyrics`, `ChipCorners`, `MainWindow.PublishWeather`, `Motion.Delayed`, `system_globalization`, `TimeSpan`, `MainWindow.WireWeatherPane`, `DateTime`, `MainWindow.ApplyShape`, `system_diagnostics`, `DelayedAction.Start`, `CompactLyric`?**
  _High betweenness centrality (0.385) - this node is a cross-community bridge._
- **What connects `DiscordActive`, `ShowsVoice`, `VencordPluginConnected` to the rest of the system?**
  _608 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `MainWindow` be split into smaller, more focused modules?**
  _Cohesion score 0.06901960784313725 - nodes in this community are weakly interconnected._
- **Why does `Window` connect `Window` to `Art`, `Body`, `Eq`, `Glow`, `HeadsetGadget`, `PART_Track`, `PlayerLyricWait`, `PlayerMatrix`, `PlayerStars`, `SeekTrackShape`, `SeekWave`, `ShelfZone`, `MainWindow.BuildWeatherHours`, `Grid`, `WeatherHomeHit`, `Glass`, `Border`, `Digits`, `SoundCloudBrowser`, `Icon`, `Button`, `MainWindow`, `SoundCloudHeartReveal`, `AreaPicker`, `Path`, `MainWindow.BuildAddPage`, `StackPanel`, `VoiceDeafenIcon`, `MessageAvatar`, `Toggle`, `UniformGrid`, `ScaleTransform`, `GradientStop`, `Canvas`, `Ellipse`, `Ring`, `TextBox`, `RotateTransform`, `ScrollViewer`, `Segments`, `ToggleButton`, `GapSlider`, `LookRows`, `LyricA`, `NextIcon`, `SeekLines`?**
  _High betweenness centrality (0.178) - this node is a cross-community bridge._
- **Should `Native` be split into smaller, more focused modules?**
  _Cohesion score 0.043571237636995455 - nodes in this community are weakly interconnected._
- **Why does `DynamicIsland` connect `DynamicIsland` to `system_windows`, `Settings`, `LyricFxPlan.Sample`, `Brush`, `LyricsSources.Create`, `Motion.Ms`, `system_windows_threading`, `smtc-players.md`, `Spring`, `Gadget`, `WeatherActionBorder.cs`, `LyricsPresentation`, `Equalizer`, `IAudioClient`, `system_runtime_interopservices`, `LyricsNormalizer`, `CoreAudio.cs`, `LyricsMatcher`, `system_globalization`, `SpacePlaybackHook`, `TimeSpan`, `LyricsResult`, `system_diagnostics`?**
  _High betweenness centrality (0.134) - this node is a cross-community bridge._
- **Should `IntPtr` be split into smaller, more focused modules?**
  _Cohesion score 0.11904761904761904 - nodes in this community are weakly interconnected._