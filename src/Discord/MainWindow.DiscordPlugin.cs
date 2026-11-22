using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    bool _downloadingDiscordPlugin, _pluginLinked, _pluginNoticePending;
    string? _pluginDownloadMessage;

    bool VencordPluginConnected => _discord.IsBridgeConnected
        && _discord.PluginVersion.EndsWith("-vencord", StringComparison.Ordinal);

    void TrackDiscordPluginConnection()
    {
        bool linked = _discord.IsBridgeConnected;
        if (linked != _pluginLinked)
        {
            _pluginNoticePending = linked && !_discordDemo;
            if (linked) _pluginDownloadMessage = null;
        }
        _pluginLinked = linked;
        if (_panel == Panel.DiscordPlugin) RefreshDiscordPluginPage();
    }

    void ShowDiscordPluginNotice()
    {
        if (_panel == Panel.DiscordPlugin) RefreshDiscordPluginPage();
        if (!_pluginNoticePending || !_pluginLinked || !DiscordActive || _ringing || _panel != Panel.None
            || _hiddenByFullscreen || _sentAway || _transientView is View.Message or View.Notice) return;

        _pluginNoticePending = false;
        Notify(Glyph.Check, _green, VencordPluginConnected ? "Vencord" : "BetterDiscord", "Плагин подключён");
    }

    void RefreshDiscordPluginPage()
    {
        DiscordPluginDownload.SetVisible(!_downloadingDiscordPlugin);
        DiscordPluginDownload.IsEnabled = PluginInstaller.Configured;
        DiscordPluginLoad.SetVisible(_downloadingDiscordPlugin);
        string? vencordPath = VencordInstallDirectory;
        VencordInstallPath.Text = vencordPath is null ? "Vencord: выбрать исходники" : $"Vencord: {vencordPath}";
        VencordInstallPath.ToolTip = vencordPath ?? "Выбрать папку исходников Vencord";
        BetterDiscordInstallPath.Text = $"BetterDiscord: {PluginInstaller.BetterDiscordPath}";
        BetterDiscordInstallPath.ToolTip = PluginInstaller.BetterDiscordPath;
        DiscordPluginFeedback.Text = _pluginDownloadMessage
            ?? (_pluginLinked ? $"Плагин подключён · {(VencordPluginConnected ? "Vencord" : "BetterDiscord")}"
                : File.Exists(Path.Combine(PluginInstaller.BetterDiscordPath, PluginInstaller.FileName))
                    ? "Плагин BetterDiscord найден. Включите его в клиенте."
                    : PluginInstaller.Configured ? "" : "Репозиторий для скачивания не настроен.");
    }

    static string? VencordInstallDirectory
    {
        get
        {
            string root = Settings.VencordSourceDirectory;
            if (root.Length == 0)
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vencord");
            return IsVencordSource(root) ? Path.Combine(root, "src", "userplugins") : null;
        }
    }

    static bool IsVencordSource(string root) =>
        Directory.Exists(Path.Combine(root, "src", "plugins")) && File.Exists(Path.Combine(root, "pnpm-workspace.yaml"));

    void ChooseVencordFolder_Click(object sender, RoutedEventArgs e) => ChooseVencordFolder();

    bool ChooseVencordFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку исходников Vencord" };
        if (dialog.ShowDialog(this) != true) return false;
        if (!IsVencordSource(dialog.FolderName))
        {
            _pluginDownloadMessage = "Выберите корень исходников Vencord, где есть папка src.";
            RefreshDiscordPluginPage();
            return false;
        }
        Settings.VencordSourceDirectory = dialog.FolderName;
        _pluginDownloadMessage = null;
        RefreshDiscordPluginPage();
        return true;
    }

    void CopyVencordPath_Click(object sender, RoutedEventArgs e)
    {
        if (VencordInstallDirectory is null && !ChooseVencordFolder()) return;
        if (VencordInstallDirectory is { } path) CopyPluginPath(path);
    }

    void CopyBetterDiscordPath_Click(object sender, RoutedEventArgs e) => CopyPluginPath(PluginInstaller.BetterDiscordPath);

    void CopyPluginPath(string path)
    {
        _pluginDownloadMessage = Copy(path) ? "Полный путь скопирован." : "Не удалось скопировать путь.";
        RefreshDiscordPluginPage();
    }

    async void DiscordPluginDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadingDiscordPlugin || !PluginInstaller.Configured) return;

        bool vencord = ReferenceEquals(sender, VencordDownload);
        string assetName = vencord ? PluginInstaller.VencordFileName : PluginInstaller.FileName;
        var dialog = new SaveFileDialog
        {
            FileName = assetName,
            Filter = vencord ? "Архив Vencord (*.zip)|*.zip" : "Плагин BetterDiscord (*.plugin.js)|*.plugin.js",
            InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog(this) != true) return;

        _downloadingDiscordPlugin = true;
        _pluginDownloadMessage = null;
        DiscordPluginRing.BeginAnimation(Ring.ProgressProperty, null);
        DiscordPluginRing.Progress = 0;
        DiscordPluginBytes.Text = "Подключаюсь…";
        RefreshDiscordPluginPage();
        long lastPercent = -1;
        try
        {
            bool saved = await PluginInstaller.DownloadAsync(assetName, dialog.FileName, (done, total) =>
            {
                long percent = done * 100 / total;
                if (percent == lastPercent) return;
                lastPercent = percent;
                DiscordPluginBytes.Text = $"{FormatPluginSize(done)} из {FormatPluginSize(total)}";
                DiscordPluginRing.BeginAnimation(Ring.ProgressProperty, new DoubleAnimation((double)done / total, Ms(200)));
            });
            _pluginDownloadMessage = saved ? (vencord ? "Распакуйте папку из архива в src/userplugins." : "Переместите файл в папку модификации.")
                : "В последнем релизе пока нет файла плагина.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _pluginDownloadMessage = "Репозиторий или публичный релиз пока недоступен.";
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _pluginDownloadMessage = "Не удалось скачать файл. Попробуйте ещё раз.";
        }
        finally
        {
            _downloadingDiscordPlugin = false;
            RefreshDiscordPluginPage();
        }
    }

    static string FormatPluginSize(long bytes) => $"{bytes / 1024d:0.#} КБ";
}
