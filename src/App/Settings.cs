using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland;

[Flags]
enum Backdrop
{
    Glow = 0,
    Matrix = 1,
    Stars = 2,
    MatrixAndStars = Matrix | Stars,
}

enum LyricChange
{
    Smooth,
    Wave,
    Drum,
}

[Flags]
enum LyricSource
{
    Lrclib = 1,
    Deezer = 2,
    NetEase = 4,
    QqMusic = 8,
    Kugou = 16,
    Paxsenix = 32,
    Genius = 64,
    YouTubeMusic = 128,
    Karalyr = 256,
    BetterLyrics = 512,
    AmllTtmlDb = 1024,
    LrcMux = 2048,
    LiriQo = 4096,
    Usual = Lrclib | Deezer | NetEase | QqMusic | Kugou | YouTubeMusic,
    Default = Lrclib | Deezer | NetEase | QqMusic | YouTubeMusic | AmllTtmlDb | LiriQo,
    All = Usual | Paxsenix | Genius | Karalyr | BetterLyrics | AmllTtmlDb | LrcMux | LiriQo,
}

enum PointerLyricShape
{
    Column,
    Line,
}

[Flags]
enum MiniComponent
{
    None = 0, Hours = 1, Minutes = 2, Seconds = 4, Weather = 8, Weekday = 16,
    All = Hours | Minutes | Seconds | Weather | Weekday,
}

enum Hover
{
    Disc,
    Magnet,
    Flow,
}

enum SeekHover
{
    Magnifier,
    Lift,
    Wave,
}

enum DiscordMusicState
{
    Mini = 0,
    Compact = 1,
    Normal = 2,
}

static partial class Settings
{
    const string Key = @"Software\DynamicIsland";
    const int MinScale = 85, MaxScale = 130, MaxGap = 24;
    const int DefaultScale = 100, DefaultGap = 8;

    static bool _lyrics = ReadSwitch(nameof(Lyrics)), _lyricEffects = ReadSwitch(nameof(LyricEffects));
    static bool _network = ReadSwitch(nameof(Network)), _hideFullscreen = ReadSwitch(nameof(HideFullscreen));
    static bool _rim = ReadSwitch(nameof(Rim)), _appVolume = ReadSwitch(nameof(AppVolume));
    static bool _weather = ReadSwitch(nameof(Weather));
    static bool _shelfEnabled = ReadSwitch(nameof(ShelfEnabled));
    static bool _soundCloudSignInSaved = ReadSwitch(nameof(SoundCloudSignInSaved), false);

    public static bool SoundCloudSignInSaved
    {
        get => _soundCloudSignInSaved;
        set => Write(nameof(SoundCloudSignInSaved), _soundCloudSignInSaved = value);
    }
    static bool _pointerLyric = ReadSwitch(nameof(PointerLyric), false);
    static bool _lyricParting = ReadSwitch(nameof(LyricParting));
    // Текст без таймкодов (обычный): по умолчанию показывается, как и раньше.
    static bool _unsyncedLyrics = ReadSwitch(nameof(UnsyncedLyrics));
    // Experimental, off by default: automatic choice among sync files of one recording, length fitting, uploader credit.
    static readonly bool _lyricsAutoSync = ReadSwitch(nameof(LyricsAutoSync), false);
    public static bool LyricsAutoSync => _lyricsAutoSync;
    static PointerLyricShape _pointerLyricShape = (PointerLyricShape)Math.Clamp(Read(nameof(PointerLyricShape), 0), 0, (int)PointerLyricShape.Line);
    static bool _dots = ReadSwitch(nameof(Dots), false);
    static bool _lineBar = ReadSwitch(nameof(LineBar));
    static bool _seekWave = ReadSwitch(nameof(SeekWave), false);
    // «Под курсором» для полосы-волны: включено по умолчанию, выключается нажатием на «Лупу».
    static bool _seekHoverOn = ReadSwitch(nameof(SeekHoverOn), true);
    static bool _glass = ReadSwitch(nameof(Glass), false);
    static Backdrop _backdrop = (Backdrop)Math.Clamp(Read(nameof(Backdrop), 0), 0, (int)Backdrop.MatrixAndStars);
    static LyricChange _lyricChange = (LyricChange)Math.Clamp(Read(nameof(LyricChange), (int)LyricChange.Wave), 0, (int)LyricChange.Drum);
    // «Эффекты текста»: выключено по умолчанию. Новый ключ: старые номера стилей здесь не совпадают с новыми.
    static LyricFx _textEffect = ReadTextEffect();
    static LyricSource _lyricSources = Known((LyricSource)Read(nameof(LyricSources), (int)LyricSource.Default));
    static Hover _hover = (Hover)Math.Clamp(Read(nameof(Hover), 0), 0, (int)Hover.Flow);
    static SeekHover _seekHover = (SeekHover)Math.Clamp(Read(nameof(SeekHover), 0), 0, (int)SeekHover.Wave);
    static int _scale = Math.Clamp(Read(nameof(Scale), DefaultScale), MinScale, MaxScale);
    static int _gap = Math.Clamp(Read(nameof(Gap), DefaultGap), 0, MaxGap);
    static int _accent = Read(nameof(Accent), 0);
    // Цвет полосы трека: по умолчанию белый; -1 — из обложки.
    static int _seekAccent = Read(nameof(SeekAccent), 0xFFFFFF);
    static int _compactTrackAccent = Read(nameof(CompactTrackAccent), 0xFFFFFF);
    static bool _miniWeatherSeparate = ReadSwitch(nameof(MiniWeatherSeparate), false);
    public static bool MiniWeatherSeparate { get => _miniWeatherSeparate; set => Write(nameof(MiniWeatherSeparate), _miniWeatherSeparate = value); }
    static MiniComponent _miniComponents = (MiniComponent)Read(nameof(MiniComponents), 3) & MiniComponent.All;

    public static MiniComponent MiniComponents
    {
        get => _miniComponents;
        set => Write(nameof(MiniComponents), (int)(_miniComponents = value & MiniComponent.All));
    }

    public static bool Dots
    {
        get => _dots;
        set => Write(nameof(Dots), _dots = value);
    }

    public static bool SeekWave
    {
        get => _seekWave;
        set => Write(nameof(SeekWave), _seekWave = value);
    }

    public static bool SeekHoverOn
    {
        get => _seekHoverOn;
        set => Write(nameof(SeekHoverOn), _seekHoverOn = value);
    }

    public static bool LineBar
    {
        get => _lineBar;
        set => Write(nameof(LineBar), _lineBar = value);
    }

    public static bool Glass
    {
        get => _glass;
        set => Write(nameof(Glass), _glass = value);
    }

    public static Backdrop Backdrop
    {
        get => _backdrop;
        set => Write(nameof(Backdrop), (int)(_backdrop = value));
    }

    public static LyricChange LyricChange
    {
        get => _lyricChange;
        set => Write(nameof(LyricChange), (int)(_lyricChange = value));
    }

    public static LyricSource LyricSources
    {
        get => _lyricSources;
        set => Write(nameof(LyricSources), (int)(_lyricSources = Known(value)));
    }

    public static Hover Hover
    {
        get => _hover;
        set => Write(nameof(Hover), (int)(_hover = value));
    }

    public static SeekHover SeekHover
    {
        get => _seekHover;
        set => Write(nameof(SeekHover), (int)(_seekHover = value));
    }

    public static bool Lyrics
    {
        get => _lyrics;
        set => Write(nameof(Lyrics), _lyrics = value);
    }

    public static bool LyricEffects
    {
        get => _lyricEffects;
        set => Write(nameof(LyricEffects), _lyricEffects = value);
    }

    public static bool PointerLyric
    {
        get => _pointerLyric;
        set => Write(nameof(PointerLyric), _pointerLyric = value);
    }

    public static PointerLyricShape PointerLyricShape
    {
        get => _pointerLyricShape;
        set => Write(nameof(PointerLyricShape), (int)(_pointerLyricShape = value));
    }

    public static bool LyricParting
    {
        get => _lyricParting;
        set => Write(nameof(LyricParting), _lyricParting = value);
    }

    public static LyricFx TextEffect
    {
        get => _textEffect;
        set => Write("TextEffectV2", (int)(_textEffect = value));
    }

    // Текущий ключ хранит номера LyricFx. Если его ещё нет, переносим старый TextEffect:
    // 1 — подъём, 4 — перелив, 7 — свечение; остальные стили удалены и становятся выключено.
    static LyricFx ReadTextEffect()
    {
        int saved = Read("TextEffectV2", -1);
        if (saved >= 0) return (LyricFx)Math.Clamp(saved, 0, (int)LyricFx.Shimmer);
        return Read(nameof(TextEffect), 0) switch
        {
            1 => LyricFx.Lift,
            4 => LyricFx.Shimmer,
            7 => LyricFx.Glow,
            _ => LyricFx.Off,
        };
    }

    public static bool UnsyncedLyrics
    {
        get => _unsyncedLyrics;
        set => Write(nameof(UnsyncedLyrics), _unsyncedLyrics = value);
    }

    public static bool Rim
    {
        get => _rim;
        set => Write(nameof(Rim), _rim = value);
    }

    public static bool AppVolume
    {
        get => _appVolume;
        set => Write(nameof(AppVolume), _appVolume = value);
    }

    public static bool Network
    {
        get => _network;
        set => Write(nameof(Network), _network = value);
    }

    public static bool Weather
    {
        get => _weather;
        set => Write(nameof(Weather), _weather = value);
    }

    public static bool ShelfEnabled
    {
        get => _shelfEnabled;
        set => Write(nameof(ShelfEnabled), _shelfEnabled = value);
    }
    public static bool HideFullscreen
    {
        get => _hideFullscreen;
        set => Write(nameof(HideFullscreen), _hideFullscreen = value);
    }

    public static int Scale
    {
        get => _scale;
        set => Write(nameof(Scale), _scale = Math.Clamp(value, MinScale, MaxScale));
    }

    public static int Gap
    {
        get => _gap;
        set => Write(nameof(Gap), _gap = Math.Clamp(value, 0, MaxGap));
    }

    public static Color? Accent
    {
        get => _accent == 0 ? null : Color.FromRgb((byte)(_accent >> 16), (byte)(_accent >> 8), (byte)_accent);
        set => Write(nameof(Accent), _accent = value is { } c ? c.R << 16 | c.G << 8 | c.B : 0);
    }

    // Цвет полосы трека: null — из обложки, иначе выбранный цвет.
    public static Color? SeekAccent
    {
        get => _seekAccent < 0 ? null : Color.FromRgb((byte)(_seekAccent >> 16), (byte)(_seekAccent >> 8), (byte)_seekAccent);
        set => Write(nameof(SeekAccent), _seekAccent = value is { } c ? c.R << 16 | c.G << 8 | c.B : -1);
    }

    public static Color? CompactTrackAccent
    {
        get => _compactTrackAccent < 0 ? null : Color.FromRgb((byte)(_compactTrackAccent >> 16), (byte)(_compactTrackAccent >> 8), (byte)_compactTrackAccent);
        set => Write(nameof(CompactTrackAccent), _compactTrackAccent = value is { } c ? c.R << 16 | c.G << 8 | c.B : -1);
    }

    public static string[] Shelf
    {
        get => Read<string[]>(nameof(Shelf), []);
        set => Write(nameof(Shelf), value, RegistryValueKind.MultiString);
    }

    public static string WeatherPlace
    {
        get => Read<string>(nameof(WeatherPlace), "");
        set => Write(nameof(WeatherPlace), value, RegistryValueKind.String);
    }

    public static string WeatherHome
    {
        get => Read<string>(nameof(WeatherHome), "");
        set => Write(nameof(WeatherHome), value, RegistryValueKind.String);
    }

    public static string WeatherCities
    {
        get => Read<string>(nameof(WeatherCities), "");
        set => Write(nameof(WeatherCities), value, RegistryValueKind.String);
    }

    internal static LyricSource Known(LyricSource sources) => (sources & LyricSource.All) == 0 ? LyricSource.Default : sources & LyricSource.All;


    public static int[] TimerPresets
    {
        get => ParsePresets(Read("TimerPresets", "5,10,15,25,45"));
        set => Write("TimerPresets", string.Join(",", value), RegistryValueKind.String);
    }

    public static int TimerPresetSlot
    {
        get => Math.Clamp(Read("TimerPresetSlot", 0), 0, 4);
        set => Write("TimerPresetSlot", Math.Clamp(value, 0, 4));
    }

    public static int TimerLastMinutes
    {
        get => Math.Clamp(Read("TimerLastMinutes", 25), 1, 99);
        set => Write("TimerLastMinutes", Math.Clamp(value, 1, 99));
    }

    static int[] ParsePresets(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 5) return [5, 10, 15, 25, 45];
        var result = new int[5];
        for (int i = 0; i < 5; i++)
        {
            if (!int.TryParse(parts[i], out int minutes) || minutes < 1 || minutes > 99) return [5, 10, 15, 25, 45];
            result[i] = minutes;
        }
        return result;
    }

    static bool ReadSwitch(string name, bool fallback = true) => Read(name, fallback ? 1 : 0) != 0;

    static T Read<T>(string name, T fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is T value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    static void Write(string name, bool value) => Write(name, value ? 1 : 0);

    static void Write(string name, int value) => Write(name, value, RegistryValueKind.DWord);

    static void Write(string name, object value, RegistryValueKind kind)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, value, kind);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
