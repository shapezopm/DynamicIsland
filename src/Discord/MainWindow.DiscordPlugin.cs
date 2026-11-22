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
        if (linked != _pluginLinked) _pluginNoticePending = linked && !_discordDemo;
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
        DiscordPluginFeedback.Text = _pluginLinked ? "Плагин подключён"
            : PluginInstaller.FileInstalled ? "Файл в папке Plugins. Включите плагин в BetterDiscord."
            : _pluginDownloadMessage ?? (PluginInstaller.Configured ? "" : "Репозиторий для скачивания не настроен.");
    }

    async void DiscordPluginDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadingDiscordPlugin || !PluginInstaller.Configured) return;

        var dialog = new SaveFileDialog
        {
            FileName = PluginInstaller.FileName,
            Filter = "Плагин BetterDiscord (*.plugin.js)|*.plugin.js",
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
            bool saved = await PluginInstaller.DownloadAsync(dialog.FileName, (done, total) =>
            {
                long percent = done * 100 / total;
                if (percent == lastPercent) return;
                lastPercent = percent;
                DiscordPluginBytes.Text = $"{FormatPluginSize(done)} из {FormatPluginSize(total)}";
                DiscordPluginRing.BeginAnimation(Ring.ProgressProperty, new DoubleAnimation((double)done / total, Ms(200)));
            });
            _pluginDownloadMessage = saved ? "Файл скачан. Переместите его в папку Plugins."
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
