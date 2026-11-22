using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DynamicIsland;

static class SourceApp
{
    const uint GW_OWNER = 4;
    const int SW_RESTORE = 9;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int TitleCapacity = 512, PathCapacity = 1024;
    const int PreparedLifetimeMs = 5000;
    const string AppsFolder = "shell:AppsFolder";

    static readonly string[] BrowserTitleSuffixes =
    [
        " - Google Chrome", " - Chromium", " - Microsoft Edge", " - Brave", " - Opera",
        " - Vivaldi", " — Zen Browser", " — Mozilla Firefox", " - Mozilla Firefox",
        " — LibreWolf", " — Waterfox"
    ];
    static readonly string[] SiteTitleSuffixes =
    [
        " - YouTube", " - YouTube Music", " | Free listening on SoundCloud",
        " | Listen online for free on SoundCloud", " | SoundCloud", " on SoundCloud"
    ];

    static readonly object ExecutableLock = new();
    static readonly object NavigationLock = new();
    static string _namedApp = "", _name = "";
    static string _resolvedApp = "", _executable = "";
    static string _preparedKey = "";
    static long _preparedAt;
    static Task<Destination>? _prepared;
    readonly record struct Destination(IntPtr Window, AutomationElement? Tab, string AppId, bool Browser = false, string? TabName = null);
    readonly record struct MediaWindow(IntPtr Handle, bool MatchesSource, bool Browser);

    delegate bool WindowProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(WindowProc proc, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint relation);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromEvent(IntPtr hwnd, uint objectId, uint childId,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible,
        [MarshalAs(UnmanagedType.Struct)] out object child);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder id);

    public static void Prepare(string appId, string title, string artist)
    {
        if (appId.Length == 0 || title.Length == 0) return;
        _ = PreparedDestination(appId, title, artist, out _);
    }

    static Task<Destination> PreparedDestination(string appId, string title, string artist, out bool reused)
    {
        string key = string.Join('\u001f', appId, title, artist);
        lock (NavigationLock)
        {
            long now = Environment.TickCount64;
            reused = _preparedKey == key && _prepared is { IsFaulted: false } && now - _preparedAt <= PreparedLifetimeMs;
            if (!reused)
            {
                _preparedKey = key;
                _preparedAt = now;
                _prepared = Task.Run(() => FindDestination(appId, title, artist));
            }
            return _prepared!;
        }
    }

    public static async Task BringToFrontAsync(string appId, string title, string artist = "")
    {
        if (appId.Length == 0) return;
        Task<Destination> prepared = PreparedDestination(appId, title, artist, out bool reused);
        Destination destination = await Task.Run(async () =>
        {
            Destination candidate = await prepared.ConfigureAwait(false);
            if (await ConfirmDestinationAsync(candidate, title, artist).ConfigureAwait(false) || !reused) return candidate;

            // The foreground tab, window, or accessibility tree can change after prefetch.
            lock (NavigationLock)
                if (_prepared == prepared) _prepared = null;
            candidate = await PreparedDestination(appId, title, artist, out _).ConfigureAwait(false);
            await ConfirmDestinationAsync(candidate, title, artist).ConfigureAwait(false);
            return candidate;
        });
        ActivateWindow(destination);
    }

    static Destination FindDestination(string appId, string title, string artist)
    {
        string executable = ResolveExecutable(appId);
        var windows = new List<MediaWindow>();
        var processes = new Dictionary<uint, (bool Source, bool Browser)>();
        var text = new StringBuilder(TitleCapacity);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
            if (Native.IsToolWindow(hwnd)) return true;
            if (GetWindowText(hwnd, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(hwnd, out uint process);
            if (!processes.TryGetValue(process, out var identity))
            {
                identity = (IsProcessOf(process, appId, executable), IsBrowserProcess(process));
                processes[process] = identity;
            }
            if (identity.Source || identity.Browser) windows.Add(new(hwnd, identity.Source, identity.Browser));
            return true;
        }, IntPtr.Zero);

        MediaWindow[] native = windows.Where(w => w.MatchesSource && !w.Browser).ToArray();
        if (native.Length > 0)
        {
            IntPtr titled = native.FirstOrDefault(w => title.Length > 0 &&
                WindowTitle(w.Handle).Contains(title, StringComparison.OrdinalIgnoreCase)).Handle;
            return new Destination(titled != IntPtr.Zero ? titled : native[0].Handle, null, appId);
        }

        MediaWindow[] owned = windows.Where(w => w.MatchesSource).ToArray();
        // Some browsers use an opaque session id. Widen the search only if there is no resolved owner.
        IntPtr[] browsers = (owned.Length > 0 ? owned.AsEnumerable() : windows).Select(w => w.Handle).ToArray();
        IntPtr[] active = browsers.Where(w => TabTitleScore(WindowTitle(w), title, artist) > 0).ToArray();
        if (active.Length == 1) return new Destination(active[0], null, appId, Browser: true);

        var tabs = browsers.SelectMany(window => ReadBrowserTabs(window, appId)).ToArray();
        var titledTab = FindBrowserTabByTitle(tabs, title, artist);
        if (titledTab.Tab != null) return titledTab;
        var contentTabs = tabs.GroupBy(tab => tab.Window)
            .Select(group => FindTabByTrack(group.Key, group, title, artist))
            .Where(candidate => candidate.Tab != null).Take(2).ToArray();
        if (contentTabs.Length == 1) return contentTabs[0];

        if (owned.Length > 0) return new Destination(owned[0].Handle, null, appId, Browser: true);
        if (browsers.Length == 0) return new Destination(FindWindowByTitle(title), null, appId);
        return new Destination(IntPtr.Zero, null, appId);
    }

    static async Task<bool> ConfirmDestinationAsync(Destination destination, string title, string artist)
    {
        if (!IsWindow(destination.Window)) return false;
        if (!destination.Browser || TabTitleScore(WindowTitle(destination.Window), title, artist) > 0) return true;
        if (destination.Tab is not { } tab) return false;
        try
        {
            if (!SameName(tab.Current.Name, destination.TabName ?? "")) return false;
            SelectionItemPattern? item = null;
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selection))
            {
                item = (SelectionItemPattern)selection;
                if (item.Current.IsSelected) return true;
                item.Select();
            }
            else if (tab.TryGetCurrentPattern(InvokePattern.Pattern, out object invoke))
                ((InvokePattern)invoke).Invoke();
            else return false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (item?.Current.IsSelected == true || TabTitleScore(WindowTitle(destination.Window), destination.TabName!, "") > 0)
                    return true;
                if (attempt < 3) await Task.Delay(25).ConfigureAwait(false);
            }
        }
        catch (Exception) { /* Browser closed or replaced the tab element. */ }
        return false;
    }

    static void ActivateWindow(Destination destination)
    {
        IntPtr window = destination.Window;
        if (window == IntPtr.Zero) { Launch(destination.AppId); return; }
        if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
        SetForegroundWindow(window);
    }

    static IntPtr FindWindowByTitle(string title)
    {
        if (title.Length == 0) return IntPtr.Zero;
        IntPtr match = IntPtr.Zero;
        var text = new StringBuilder(TitleCapacity);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || Native.IsToolWindow(hwnd)) return true;
            if (GetWindowText(hwnd, text.Clear(), text.Capacity) == 0) return true;
            if (!text.ToString().Contains(title, StringComparison.OrdinalIgnoreCase)) return true;
            match = hwnd;
            return false;
        }, IntPtr.Zero);
        return match;
    }

    static string WindowTitle(IntPtr window)
    {
        var text = new StringBuilder(TitleCapacity);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    static string RemoveTitleSuffix(string title, string[] suffixes)
    {
        foreach (string suffix in suffixes)
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return title[..^suffix.Length].TrimEnd();
        return title;
    }

    internal static int TabTitleScore(string label, string title, string artist)
    {
        title = title.Trim();
        if (title.Length == 0) return 0;
        label = label.Trim();
        if (SameName(label, title)) return 3;
        string labelWithoutBrowser = RemoveTitleSuffix(label, BrowserTitleSuffixes);
        if (SameName(labelWithoutBrowser, title)) return 3;

        string track = RemoveTitleSuffix(labelWithoutBrowser, SiteTitleSuffixes);
        if (SameName(track, title)) return 2;

        // SoundCloud's global player sets "<track> by <artist>" on pages such as Library.
        // Track pages can also use "Stream <track> by <artist> … on SoundCloud".
        artist = artist.Trim();
        if (artist.Length == 0) return 0;
        string creditedTitle = $"{title} by {artist}";
        if (SameName(track, creditedTitle)) return 1;
        return labelWithoutBrowser.EndsWith(" on SoundCloud", StringComparison.OrdinalIgnoreCase)
            && track.StartsWith("Stream ", StringComparison.OrdinalIgnoreCase)
            && SameName(track[7..], creditedTitle) ? 1 : 0;
    }

    static Destination FindBrowserTabByTitle(IEnumerable<Destination> tabs, string title, string artist)
    {
        Destination match = default;
        int best = 0;
        bool ambiguous = false;
        foreach (Destination tab in tabs)
        {
            int score = TabTitleScore(tab.TabName!, title, artist);
            if (score == 0 || score < best) continue;
            ambiguous = score == best;
            if (ambiguous) continue;
            best = score;
            match = tab;
        }
        return ambiguous ? default : match;
    }

    static List<Destination> ReadBrowserTabs(IntPtr window, string appId)
    {
        var result = new List<Destination>();
        try
        {
            var tabs = AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            foreach (AutomationElement tab in tabs)
            {
                string name = tab.Current.Name;
                if (name.Length == 0 || IsInsideDocument(tab)) continue;
                result.Add(new Destination(window, tab, appId, Browser: true, TabName: name));
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (ArgumentException) { }
        return result;
    }

    static bool IsBrowserProcess(uint process)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var text = new StringBuilder(PathCapacity);
            int size = text.Capacity;
            if (!QueryFullProcessImageName(handle, 0, text, ref size)) return false;
            return IsBrowserExecutable(Path.GetFileName(text.ToString()));
        }
        finally { CloseHandle(handle); }
    }

    internal static bool IsBrowserExecutable(string name) => name.ToLowerInvariant() is
        "zen.exe" or "firefox.exe" or "chrome.exe" or "msedge.exe" or "brave.exe"
        or "opera.exe" or "vivaldi.exe" or "chromium.exe" or "librewolf.exe"
        or "waterfox.exe" or "opera_gx.exe";

    static Destination FindTabByTrack(IntPtr window, IEnumerable<Destination> tabs, string title, string artist)
    {
        if (title.Length < 3) return default;
        try
        {
            var byName = tabs.ToLookup(tab => tab.TabName!, StringComparer.OrdinalIgnoreCase);
            AutomationElement root = AutomationElement.FromHandle(window);
            var documents = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            Destination result = default, vkArtistMatch = default;
            bool ambiguousVkArtist = false;
            foreach (AutomationElement document in documents)
            {
                var matchingTabs = byName[document.Current.Name].Take(2).ToArray();
                if (matchingTabs.Length != 1) continue;
                var content = document.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, title, PropertyConditionFlags.IgnoreCase));
                bool hasArtist = artist.Length > 0 && document.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, artist, PropertyConditionFlags.IgnoreCase)) != null;
                if (content != null && (artist.Length == 0 || hasArtist))
                {
                    if (result.Tab != null) return default;
                    result = matchingTabs[0];
                    continue;
                }
                // VK leaves the browser tab titled "Главная" and may omit the playing song from
                // the accessibility tree while backgrounded. Its artist and site label remain visible.
                if (!hasArtist || document.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "VK Музыка", PropertyConditionFlags.IgnoreCase)) == null)
                    continue;
                if (vkArtistMatch.Tab != null) ambiguousVkArtist = true;
                else vkArtistMatch = matchingTabs[0];
            }
            return result.Tab != null || ambiguousVkArtist ? result : vkArtistMatch;
        }
        catch (Exception) { return default; }
    }

    static bool IsInsideDocument(AutomationElement element)
    {
        for (AutomationElement? parent = TreeWalker.RawViewWalker.GetParent(element);
             parent != null; parent = TreeWalker.RawViewWalker.GetParent(parent))
            if (parent.Current.ControlType == ControlType.Document) return true;
        return false;
    }

    internal static Uri? FindSoundCloudTrack(TrackSnapshot track, CancellationToken token, bool allowOtherBrowser = false)
    {
        var documentsCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        var hyperlink = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink);
        var currentTrack = new AndCondition(hyperlink,
            new PropertyCondition(AutomationElement.NameProperty, "Current track: " + track.Title, PropertyConditionFlags.IgnoreCase));
        var trackTitle = new AndCondition(hyperlink,
            new PropertyCondition(AutomationElement.NameProperty, track.Title, PropertyConditionFlags.IgnoreCase));
        var possible = new HashSet<Uri>();
        var currentBadgeTracks = new HashSet<Uri>();

        bool detached = allowOtherBrowser && !IsBrowserExecutable(track.SourceApp);
        foreach (IntPtr window in SoundCloudWindows(track, detached))
        {
            token.ThrowIfCancellationRequested();
            GetWindowThreadProcessId(window, out uint process);
            if (!IsBrowserProcess(process)) continue;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                var documents = root.FindAll(TreeScope.Descendants, documentsCondition);
                foreach (AutomationElement document in documents.Cast<AutomationElement>().Take(48))
                {
                    token.ThrowIfCancellationRequested();
                    string? page = AccessibleValue(document);
                    if (!Uri.TryCreate(page, UriKind.Absolute, out Uri? address) || !SoundCloudWaveform.IsSite(address)) continue;
                    var playerLinks = document.FindAll(TreeScope.Descendants, hyperlink).Cast<AutomationElement>()
                        .Where(link => link.Current.ClassName.Contains("playbackSoundBadge__", StringComparison.Ordinal))
                        .ToArray();
                    var badgeTitle = playerLinks.FirstOrDefault(link => link.Current.ClassName.Contains("playbackSoundBadge__titleLink", StringComparison.Ordinal));
                    var badgeArtist = playerLinks.FirstOrDefault(link => link.Current.ClassName.Contains("playbackSoundBadge__lightLink", StringComparison.Ordinal));
                    if (badgeTitle is not null && badgeArtist is not null
                        && SameName(badgeTitle.Current.Name.Trim().Replace("Current track: ", "", StringComparison.OrdinalIgnoreCase), track.Title.Trim())
                        && MatchesSoundCloudArtist(badgeArtist.Current.Name, track.Artist)
                        && SoundCloudWaveform.TryTrackAddress(AccessibleValue(badgeTitle), out Uri? badgeTrack))
                    {
                        if (!allowOtherBrowser) return badgeTrack;
                        currentBadgeTracks.Add(badgeTrack!);
                    }
                    if (allowOtherBrowser)
                    {
                        if (TabTitleScore(document.Current.Name, track.Title, track.Artist) == 1
                            && SoundCloudWaveform.TryTrackAddress(page, out Uri? matchingPage)) possible.Add(matchingPage!);
                        continue;
                    }
                    AutomationElement? current = document.FindFirst(TreeScope.Descendants, currentTrack);
                    if (current != null && SoundCloudWaveform.TryTrackAddress(AccessibleValue(current), out Uri? playing))
                        return playing;
                    // The player's accessible title can lag behind the system media session.
                    // A matching track link on the feed is still usable after metadata verification.
                    var links = document.FindAll(TreeScope.Descendants, trackTitle);
                    bool foundLink = false;
                    foreach (AutomationElement link in links.Cast<AutomationElement>().Take(16))
                    {
                        if (!SoundCloudWaveform.TryTrackAddress(AccessibleValue(link), out Uri? item)) continue;
                        possible.Add(item!);
                        foundLink = true;
                    }
                    if (!foundLink && TabTitleScore(document.Current.Name, track.Title, track.Artist) > 0
                        && SoundCloudWaveform.TryTrackAddress(page, out Uri? ownPage)) possible.Add(ownPage!);
                }
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            catch (ArgumentException) { }
        }
        if (currentBadgeTracks.Count > 0)
            return currentBadgeTracks.Count == 1 ? currentBadgeTracks.Single() : null;
        return possible.Count == 1 ? possible.Single() : null;
    }

    internal enum SoundCloudLike { Unknown, Unliked, Liked, Pending, SignInRequired, TrackButtonMissing }

    internal readonly record struct SoundCloudPlayer(Uri Track, SoundCloudLike State);

    internal static SoundCloudPlayer? ReadSoundCloudPlayer(TrackSnapshot track, Uri? requestedTrack = null, SoundCloudLike? desiredState = null)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)) return null;
        var matches = new Dictionary<string, (IntPtr Window, Uri Track, AutomationElement Document, AutomationElement Title, AutomationElement Button)>();
        var documents = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        var links = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink);
        var buttons = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        foreach (IntPtr window in Windows(track.SourceApp).Where(IsWindowVisible).Take(8))
        {
            GetWindowThreadProcessId(window, out uint process);
            if (!IsBrowserProcess(process)) continue;
            try
            {
                var root = AutomationElement.FromHandle(window);
                foreach (AutomationElement document in root.FindAll(TreeScope.Descendants, documents).Cast<AutomationElement>().Take(48))
                {
                    if (!Uri.TryCreate(AccessibleValue(document), UriKind.Absolute, out var page) || !SoundCloudWaveform.IsSite(page))
                        continue;
                    foreach (AutomationElement title in document.FindAll(TreeScope.Descendants, links))
                    {
                        if (!HasClass(title, "playbackSoundBadge__titleLink") ||
                            !SameName(title.Current.Name.Replace("Current track: ", "", StringComparison.OrdinalIgnoreCase).Trim(), track.Title.Trim()) ||
                            !SoundCloudWaveform.TryTrackAddress(AccessibleValue(title), out var address) ||
                            (requestedTrack is not null && requestedTrack != address)) continue;

                        var badge = TreeWalker.RawViewWalker.GetParent(title);
                        while (badge is not null && !badge.Equals(document) && !HasClass(badge, "playbackSoundBadge"))
                            badge = TreeWalker.RawViewWalker.GetParent(badge);
                        if (badge is null || badge.Equals(document)) continue;
                        var artist = badge.FindAll(TreeScope.Descendants, links).Cast<AutomationElement>()
                            .FirstOrDefault(link => HasClass(link, "playbackSoundBadge__lightLink"));
                        if (artist is null || !MatchesSoundCloudArtist(artist.Current.Name, track.Artist)) continue;
                        var like = badge.FindAll(TreeScope.Descendants, buttons).Cast<AutomationElement>()
                            .Where(button => HasClass(button, "playbackSoundBadge__like")).ToArray();
                        if (like.Length != 1) continue;
                        matches.TryAdd(string.Join(",", like[0].GetRuntimeId()), (window, address!, document, title, like[0]));
                    }
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException or InvalidOperationException) { }
        }
        if (matches.Count != 1) return null;

        var player = matches.Values.Single();
        try
        {
            var account = player.Document.FindAll(TreeScope.Descendants, buttons).Cast<AutomationElement>()
                .Any(button => HasClass(button, "header__userNavUsernameButton"));
            if (!account) return new(player.Track, SoundCloudLike.SignInRequired);
            SoundCloudLike state = SoundCloudButtonState(player.Button.Current.Name, HasClass(player.Button, "sc-button-selected"));
            if (!ShouldChangeSoundCloudLike(state, desiredState)) return new(player.Track, state);

            if (!player.Button.Current.IsEnabled
                || SoundCloudButtonState(player.Button.Current.Name, HasClass(player.Button, "sc-button-selected")) != state
                || !SoundCloudWaveform.TryTrackAddress(AccessibleValue(player.Title), out var current) || current != player.Track
                || !TryInvokeSoundCloudButton(player.Button, player.Window))
                return new(player.Track, SoundCloudLike.TrackButtonMissing);
            return new(player.Track, SoundCloudLike.Pending);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return new(player.Track, SoundCloudLike.TrackButtonMissing);
        }
    }

    static bool TryInvokeSoundCloudButton(AutomationElement button, IntPtr browser)
    {
        if (Native.IsGeckoWindow(browser))
        {
            // Gecko appends its MSAA child ID to its UI Automation runtime ID.
            // Calling the matching MSAA object's default action keeps Zen in the background.
            const uint clientObjectId = unchecked((uint)-4);
            const int pushButtonRole = 0x2B;
            int[] runtimeId = button.GetRuntimeId();
            if (runtimeId.Length > 1 && runtimeId[^1] < 0
                && AccessibleObjectFromEvent(browser, clientObjectId, unchecked((uint)runtimeId[^1]), out var accessible, out var child) == 0
                && accessible is not null && accessible.get_accRole(child) is pushButtonRole
                && accessible.get_accName(child) == button.Current.Name)
            {
                accessible.accDoDefaultAction(child);
                return true;
            }
        }

        // UI Automation Invoke can activate a browser window.
        if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out object action) || Native.ForegroundWindow != browser)
            return false;
        ((InvokePattern)action).Invoke();
        return true;
    }

    static bool HasClass(AutomationElement element, string cssClass) =>
        element.Current.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(cssClass, StringComparer.Ordinal);

    internal static bool ShouldChangeSoundCloudLike(SoundCloudLike current, SoundCloudLike? desired) =>
        (current, desired) is (SoundCloudLike.Unliked, SoundCloudLike.Liked) or (SoundCloudLike.Liked, SoundCloudLike.Unliked);

    internal static SoundCloudLike SoundCloudButtonState(string label, bool selected) =>
        selected || label.Trim() is "Unlike" or "Liked" or "Убрать лайк"
            ? SoundCloudLike.Liked
            : label.Trim() is "Like" or "Нравится" ? SoundCloudLike.Unliked : SoundCloudLike.Unknown;

    internal static bool MatchesSoundCloudArtist(string visible, string mediaArtist)
    {
        string artist = mediaArtist.Trim();
        return artist.Length > 0 && (SameName(visible.Trim(), artist)
            || visible.Split(',', StringSplitOptions.TrimEntries).Any(name => SameName(name, artist)));
    }

    static IEnumerable<IntPtr> SoundCloudWindows(TrackSnapshot track, bool detached)
    {
        if (!detached) return Windows(track.SourceApp).Where(IsWindowVisible).Take(8);

        var windows = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && GetWindow(window, GW_OWNER) == IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out uint process);
                if (IsBrowserProcess(process)) windows.Add(window);
            }
            return windows.Count < 8;
        }, IntPtr.Zero);
        return windows;
    }

    static string? AccessibleValue(AutomationElement element)
        => element.GetCurrentPropertyValue(ValuePattern.ValueProperty, ignoreDefaultValue: true) as string;

    public static string Name(string appId)
    {
        if (appId != _namedApp) (_namedApp, _name) = (appId, ResolveName(appId));
        return _name;
    }

    public static bool OwnsProcess(string appId, uint process)
    {
        if (appId.Length == 0) return false;
        string executable;
        lock (ExecutableLock)
        {
            if (appId != _resolvedApp) (_resolvedApp, _executable) = (appId, ResolveExecutable(appId));
            executable = _executable;
        }
        return IsProcessOf(process, appId, executable);
    }

    public static List<IntPtr> Windows(string appId)
    {
        var found = new List<IntPtr>();
        if (appId.Length == 0) return found;
        EnumWindows((hwnd, _) =>
        {
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
            GetWindowThreadProcessId(hwnd, out uint process);
            if (OwnsProcess(appId, process)) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static dynamic? StartMenuEntry(string appId)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        return shell.NameSpace(AppsFolder)?.ParseName(appId);
    }

    static string ResolveName(string appId)
    {
        if (appId.Length == 0) return "";
        try
        {
            if (StartMenuEntry(appId)?.Name is string name && name.Length > 0) return name;
        }
        catch (Exception failure) { App.Log(failure); }
        return appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(appId) : "";
    }

    static string ResolveExecutable(string appId)
    {
        try
        {
            return StartMenuEntry(appId)?.ExtendedProperty("System.Link.TargetParsingPath") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    static bool IsProcessOf(uint process, string appId, string executable)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var text = new StringBuilder(PathCapacity);
            int size = text.Capacity;
            if (QueryFullProcessImageName(handle, 0, text, ref size))
            {
                string path = text.ToString();
                if (SameName(path, executable) || SameName(Path.GetFileName(path), appId)) return true;
            }
            size = text.Capacity;
            return GetApplicationUserModelId(handle, ref size, text.Clear()) == 0 && SameName(text.ToString(), appId);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static void Launch(string appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppsFolder + @"\" + appId) { UseShellExecute = true })?.Dispose();
            return;
        }
        catch (Win32Exception) { }

        try
        {
            foreach (Process running in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appId)))
            {
                if (running.MainModule?.FileName is not { } path) continue;
                Process.Start(path).Dispose();
                return;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
