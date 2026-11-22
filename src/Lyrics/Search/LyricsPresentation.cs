namespace DynamicIsland;

internal sealed record LyricsPresentation(bool ShowArea, bool ShowTimed, bool ShowText, bool IsSearching,
    string StatusText, string Message, string FullText, string Attribution, string? SourceTooltip,
    bool ShowRetry, bool RetryEnabled, bool CanShowFullText, LyricsService.Line[] StableLines)
{
    static readonly LyricsService.Line[] None = [];
    public string RetryTooltip { get; init; } = "Повторить поиск текста";
    // Авторы текста из строк, которые скрыты в тексте (Lyrics by, Композитор и т. п.). Показываются в строке источника.
    public string Credits { get; init; } = "";
    // Any visible document, plain or timed, can be opened in the big editor.
    public bool CanOpenEditor => FullText.Length > 0;

    public static bool IsCurrent(LyricsResult result, TrackSnapshot current) => result.Track?.SameIdentity(current) == true;

    // showUnsynced: false hides text without timings (plain documents) from the island; the sources list still shows them.
    public static LyricsPresentation FromResult(LyricsResult result, DateTimeOffset now, string? preparedFullText = null,
        bool showUnsynced = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool shown = result.Track is not null && result.Status is not (LyricsStatus.Idle or LyricsStatus.Disabled);
        bool searching = shown && result.Status == LyricsStatus.Searching;
        bool timed = result.Status == LyricsStatus.Synced && result.Lines.Length > 0
            && result.Track is { PositionQuality: not PositionQuality.Unknown };
        bool hiddenPlain = !showUnsynced && result.Status == LyricsStatus.Plain;
        bool document = result.Status is LyricsStatus.Synced or LyricsStatus.Plain
            || result.Status == LyricsStatus.AwaitingVerification && result.Candidate?.HasValidSync == true;
        string text = shown && document && !hiddenPlain ? preparedFullText ?? TextForResult(result) : "";
        bool ordinary = result.Status == LyricsStatus.Plain && text.Length > 0 || result.Status == LyricsStatus.AwaitingVerification
            && result.Candidate?.HasValidSync == true && text.Length > 0
            || result.Status == LyricsStatus.Synced && !timed && text.Length > 0;
        bool retry = shown && !searching;
        bool enabled = retry && (result.Status != LyricsStatus.RateLimited || result.RetryAt is null || result.RetryAt <= now);
        bool serverUnavailable = result.ErrorCode is "Http503" or "ProviderCooldown503";
        string status = result.Status switch
        {
            LyricsStatus.Searching => "Ищем текст…",
            LyricsStatus.Synced => !timed ? "Позиция воспроизведения недоступна"
                : result.Track?.PositionQuality == PositionQuality.Estimated ? "Приблизительная синхронизация" : "Текст песни",
            LyricsStatus.Plain => hiddenPlain ? "Без синхронизации · скрыт" : "Без синхронизации",
            LyricsStatus.AwaitingVerification => ordinary ? "Позиция воспроизведения недоступна" : "Не хватает данных для выбора текста",
            LyricsStatus.Instrumental => "Инструментальная запись",
            LyricsStatus.Ambiguous => "Найдено несколько версий",
            LyricsStatus.NotFound => "Текст не найден",
            LyricsStatus.ProviderError => "Не удалось загрузить текст",
            LyricsStatus.RateLimited => serverUnavailable ? "Источник временно недоступен" : "Поиск временно ограничен",
            _ => "",
        };
        string message = result.Status switch
        {
            LyricsStatus.AwaitingVerification when !ordinary => "Нужны более точные данные о песне. Можно повторить поиск.",
            LyricsStatus.Plain when hiddenPlain => "Текст без синхронизации найден. Включите «Без синхронизации» в настройках текста, чтобы его показывать.",
            LyricsStatus.Instrumental => "Для этой записи источник указал инструментальный вариант.",
            LyricsStatus.Ambiguous => "Пока нельзя точно выбрать версию. Можно повторить поиск.",
            LyricsStatus.NotFound => "У источника пока нет подходящего текста.",
            LyricsStatus.ProviderError => ErrorMessage(result.ErrorCode),
            LyricsStatus.RateLimited => enabled ? "Можно повторить поиск текста."
                : serverUnavailable ? "Источник временно недоступен. Повтор станет доступен автоматически."
                    : "Источник ограничил частоту поиска. Повтор станет доступен автоматически.",
            _ => "",
        };
        LyricsCandidate? source = shown && (result.Status is LyricsStatus.Synced or LyricsStatus.Plain or LyricsStatus.Instrumental
            || ordinary) ? result.Candidate : null;
        string attribution = source is not null && !string.IsNullOrWhiteSpace(source.ProviderId)
            ? "Источник: " + source.ProviderId + (string.IsNullOrWhiteSpace(source.Upstream) ? "" : " · " + source.Upstream) : "";
        string? tooltip = source?.RecordId is > 0 ? attribution + " · запись №" + source.RecordId : source?.SourceUrl;
        // Search runs in the background; only an actual document expands the player.
        bool expanded = shown && (timed || ordinary);
        string retryTooltip = expanded ? "Повторить поиск текста" : status;
        if (!enabled && result.Status == LyricsStatus.RateLimited && result.RetryAt is { } deadline)
        {
            DateTimeOffset local = deadline.ToLocalTime();
            string format = local.Date == now.ToLocalTime().Date ? "HH:mm:ss" : "dd.MM.yyyy HH:mm:ss";
            retryTooltip = (serverUnavailable ? "Источник недоступен. Повтор после " : "Повтор доступен после ")
                + local.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
        }
        string credits = expanded ? LyricsContentCleaner.Credits(SourceTextForResult(result)) : "";
        return new(expanded, timed, ordinary, searching, status, message, text, attribution, tooltip,
            retry, enabled, result.Status == LyricsStatus.Synced && timed && text.Length > 0, timed ? result.Lines : None)
        { RetryTooltip = retryTooltip, Credits = credits };
    }

    public static string TextForResult(LyricsResult result)
    {
        if (result.Status == LyricsStatus.Plain) return LyricsContentCleaner.Text(result.Candidate?.PlainLyrics
            ?? (result.Candidate?.HasValidSync == true ? string.Join('\n', result.Candidate.Lines.Select(line => line.Text)) : ""));
        if (result.Status is not (LyricsStatus.Synced or LyricsStatus.AwaitingVerification)) return "";
        LyricsService.Line[] lines = result.Candidate?.HasValidSync == true ? result.Candidate.Lines
            : result.Status == LyricsStatus.Synced && result.Lines.Length > 0 ? result.Lines
            : result.Candidate?.HasValidSync == true ? result.Candidate.Lines : None;
        return lines.Length == 0 ? "" : LyricsContentCleaner.Text(string.Join(Environment.NewLine, lines.Select(line => line.Text)));
    }

    internal static string SourceTextForResult(LyricsResult result) => result.Candidate is not { } candidate ? ""
        : candidate.HasValidSync ? string.Join(Environment.NewLine, candidate.Lines.Select(line => line.Text)) : candidate.PlainLyrics ?? "";

    static string ErrorMessage(string? code) => code switch
    {
        "Http401" or "Http403" => "Источник не разрешил доступ к тексту.",
        "WebAccessBlocked" or "WebNoLinks" or "WebPageUnreadable" => "Не удалось получить текст Genius. Можно повторить поиск позднее.",
        "WebBudgetExceeded" => "Веб-поиск занял слишком много времени. Можно искать через браузер.",
        "NetEaseUnavailable" => "Источник NetEase сейчас недоступен. Можно повторить поиск.",
        "NetEaseTimeout" => "Источник NetEase не ответил вовремя. Можно повторить поиск.",
        "YouTubeMusicUnavailable" => "YouTube Music сейчас недоступен. Можно повторить поиск.",
        "YouTubeMusicTimeout" => "YouTube Music не ответил вовремя. Можно повторить поиск.",
        "MalformedJson" or "MalformedRecord" or "UnexpectedJsonShape" or "UnexpectedContentType" or "PayloadTooLarge"
            => "Не удалось прочитать ответ источника. Попробуйте позднее.",
        "BudgetExceeded" or "RequestTimeout" or "SearchBudgetExceeded" => "Поиск занял слишком много времени. Можно повторить.",
        "NetworkError" => "Не удалось подключиться к источнику. Проверьте интернет.",
        _ => "Источник сейчас недоступен. Попробуйте позднее.",
    };
}
