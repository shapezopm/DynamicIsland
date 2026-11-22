namespace DynamicIsland;

static partial class Settings
{
    const int MaxAvatarChoice = 3, MaxMessageSecondsChoice = 2;
    static readonly int[] AvatarCounts = [3, 4, 5, 6];
    static readonly double[] MessageSeconds = [3.5, 6, 10];

    static bool _discordEnabled = ReadSwitch(nameof(DiscordEnabled), false);
    static bool _discordMusicBubble = ReadSwitch(nameof(DiscordMusicBubble)), _discordMusicMini = ReadSwitch(nameof(DiscordMusicMini), false);
    static int _discordMusicState = Math.Clamp(Read(nameof(DiscordMusicState), _discordMusicMini ? (int)DiscordMusicState.Mini : (int)DiscordMusicState.Compact), 0, 2);
    static bool _discordVoice = ReadSwitch(nameof(DiscordVoice)), _discordSpeaking = ReadSwitch(nameof(DiscordSpeaking));
    static bool _discordMoves = ReadSwitch(nameof(DiscordMoves), false), _discordInGames = ReadSwitch(nameof(DiscordInGames));
    static bool _discordDm = ReadSwitch(nameof(DiscordDm)), _discordMention = ReadSwitch(nameof(DiscordMention));
    static bool _discordEveryone = ReadSwitch(nameof(DiscordEveryone), false), _discordPreview = ReadSwitch(nameof(DiscordPreview));
    static bool _discordQuietFocused = ReadSwitch(nameof(DiscordQuietFocused));
    static int _discordAvatars = Math.Clamp(Read(nameof(DiscordAvatars), 1), 0, MaxAvatarChoice);
    static int _discordMessageTime = Math.Clamp(Read(nameof(DiscordMessageTime), 1), 0, MaxMessageSecondsChoice);

    /// <summary>Интеграция с Discord: включается в меню Discord.</summary>
    public static bool DiscordEnabled
    {
        get => _discordEnabled;
        set => Write(nameof(DiscordEnabled), _discordEnabled = value);
    }

    /// <summary>Во время звонка музыка висит отдельным пузырём рядом с островом.</summary>
    public static bool DiscordMusicBubble
    {
        get => _discordMusicBubble;
        set => Write(nameof(DiscordMusicBubble), _discordMusicBubble = value);
    }

    public static DiscordMusicState DiscordMusicState
    {
        get => (DiscordMusicState)_discordMusicState;
        set
        {
            Write(nameof(DiscordMusicState), _discordMusicState = (int)value);
            _discordMusicMini = value == DiscordMusicState.Mini;
            Write(nameof(DiscordMusicMini), _discordMusicMini);
        }
    }

    /// <summary>Пузырь музыки сжат в маленький кружок с обложкой: его перетаскивают на остров, чтобы сжать или развернуть.</summary>
    public static bool DiscordMusicMini
    {
        get => _discordMusicState == (int)DiscordMusicState.Mini;
        set
        {
            _discordMusicMini = value;
            _discordMusicState = value ? (int)DiscordMusicState.Mini : (int)DiscordMusicState.Compact;
            Write(nameof(DiscordMusicMini), value);
            Write(nameof(DiscordMusicState), _discordMusicState);
        }
    }

    /// <summary>Остров показывает голосовой канал, пока вы в звонке.</summary>
    public static bool DiscordVoice
    {
        get => _discordVoice;
        set => Write(nameof(DiscordVoice), _discordVoice = value);
    }

    public static bool DiscordSpeaking
    {
        get => _discordSpeaking;
        set => Write(nameof(DiscordSpeaking), _discordSpeaking = value);
    }

    /// <summary>Короткое уведомление, когда кто-то заходит в канал или выходит из него.</summary>
    public static bool DiscordMoves
    {
        get => _discordMoves;
        set => Write(nameof(DiscordMoves), _discordMoves = value);
    }

    /// <summary>Не прятать остров, пока в звонке открыта игра на весь экран.</summary>
    public static bool DiscordInGames
    {
        get => _discordInGames;
        set => Write(nameof(DiscordInGames), _discordInGames = value);
    }

    public static bool DiscordDm
    {
        get => _discordDm;
        set => Write(nameof(DiscordDm), _discordDm = value);
    }

    public static bool DiscordMention
    {
        get => _discordMention;
        set => Write(nameof(DiscordMention), _discordMention = value);
    }

    public static bool DiscordEveryone
    {
        get => _discordEveryone;
        set => Write(nameof(DiscordEveryone), _discordEveryone = value);
    }

    public static bool DiscordPreview
    {
        get => _discordPreview;
        set => Write(nameof(DiscordPreview), _discordPreview = value);
    }

    /// <summary>Молчать о сообщениях, пока открыт тот самый чат в окне Discord.</summary>
    public static bool DiscordQuietFocused
    {
        get => _discordQuietFocused;
        set => Write(nameof(DiscordQuietFocused), _discordQuietFocused = value);
    }

    /// <summary>Номер варианта «сколько аватарок в свёрнутом острове».</summary>
    public static int DiscordAvatars
    {
        get => _discordAvatars;
        set => Write(nameof(DiscordAvatars), _discordAvatars = Math.Clamp(value, 0, MaxAvatarChoice));
    }

    public static int DiscordAvatarCount => AvatarCounts[_discordAvatars];

    /// <summary>Номер варианта «сколько секунд висит сообщение».</summary>
    public static int DiscordMessageTime
    {
        get => _discordMessageTime;
        set => Write(nameof(DiscordMessageTime), _discordMessageTime = Math.Clamp(value, 0, MaxMessageSecondsChoice));
    }

    public static double DiscordMessageSeconds => MessageSeconds[_discordMessageTime];

}
