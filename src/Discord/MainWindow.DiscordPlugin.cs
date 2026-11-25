using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class MainWindow
{
    bool _installingDiscordPlugin, _pluginLinked;
    string? _pluginMessage;
    Panel _discordPluginBackPanel = Panel.Discord;

    bool VencordPluginConnected => _discord.IsBridgeConnected
        && _discord.PluginVersion.EndsWith("-vencord", StringComparison.Ordinal);

    void TrackDiscordPluginConnection()
    {
        bool linked = _discord.IsBridgeConnected;
        if (linked && !_pluginLinked)
        {
            _pluginMessage = null;
            if (!_discordDemo && DiscordActive)
                Notify(Glyph.Check, _green, VencordPluginConnected ? "Vencord" : "BetterDiscord", "Плагин подключён",
                    key: "discord-plugin", isRelevant: () => DiscordActive && _discord.IsBridgeConnected);
        }
        _pluginLinked = linked;
        if (_panel == Panel.DiscordPlugin) RefreshDiscordPluginPage();
    }

    void RefreshDiscordPluginPage()
    {
        bool vencordConnected = VencordPluginConnected;
        bool betterConnected = !_discordDemo && _discord.IsBridgeConnected && !vencordConnected;
        bool betterFound = PluginInstaller.HasBetterDiscord;
        bool betterInstalled = PluginInstaller.HasBetterDiscordPlugin;
        BetterDiscordPluginState.Text = betterConnected ? "Подключён"
            : betterInstalled ? "Файл установлен" : betterFound ? "Нет плагина" : "Мод не найден";
        BetterDiscordPluginDot.Fill = betterConnected ? _green : betterInstalled ? _indigo : betterFound ? _orange : _red;
        BetterDiscordPluginActionText.Text = betterInstalled ? "Открыть папку плагинов" : "Установить плагин";
        BetterDiscordPluginActionIcon.Kind = betterInstalled ? Glyph.Folder : Glyph.Plus;
        BetterDiscordPluginAction.SetVisible(betterFound);
        BetterDiscordPluginAction.IsEnabled = !_installingDiscordPlugin;
        BetterDiscordPluginExport.IsEnabled = !_installingDiscordPlugin;

        bool vencordFound = vencordConnected || PluginInstaller.HasVencord;
        VencordPluginState.Text = vencordConnected ? "Подключён" : vencordFound ? "Не подключён" : "Мод не найден";
        VencordPluginDot.Fill = vencordConnected ? _green : vencordFound ? _orange : _red;
        DiscordPluginFeedback.Text = _pluginMessage
            ?? (betterInstalled && !betterConnected ? "Включите DynamicIslandDiscordBridge в Discord." : "");
        DiscordPluginFeedback.SetVisible(DiscordPluginFeedback.Text.Length > 0);

        DiscordPluginRows.Measure(new Size(DiscordPluginPage.Width, double.PositiveInfinity));
        double height = Math.Ceiling(DiscordPluginRows.DesiredSize.Height);
        if (DiscordPluginPage.Height == height) return;
        DiscordPluginPage.Height = height;
        if (_view == View.DiscordPlugin) UpdateTargets();
    }

    async void BetterDiscordPluginAction_Click(object sender, RoutedEventArgs e)
    {
        if (_installingDiscordPlugin) return;
        if (PluginInstaller.HasBetterDiscordPlugin)
        {
            OpenPluginLocation(PluginInstaller.BetterDiscordPath);
            return;
        }
        await RunPluginActionAsync(PluginInstaller.InstallBetterDiscordAsync, "Включите DynamicIslandDiscordBridge в Discord.");
    }

    void CopyBetterDiscordPluginPath_Click(object sender, RoutedEventArgs e)
    {
        _pluginMessage = Copy(PluginInstaller.BetterDiscordPath) ? "Путь скопирован" : "Не удалось скопировать путь.";
        RefreshDiscordPluginPage();
    }

    void VencordPluginReadme_Click(object sender, RoutedEventArgs e)
    {
        string local = Path.Combine(AppContext.BaseDirectory, "Vencord-README.md");
        if (File.Exists(local))
        {
            OpenPluginLocation(local);
            return;
        }

        string repository = Updater.Repository is { } configured && Updater.IsRepository(configured)
            ? configured : "shapezopm/DynamicIsland";
        OpenPluginLocation($"https://github.com/{repository}/blob/v{Updater.CurrentVersion}/scripts/vencord-setup/README.md");
    }

    void OpenPluginLocation(string location)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(location) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _pluginMessage = "Не удалось открыть. Попробуйте ещё раз.";
            RefreshDiscordPluginPage();
        }
    }

    async void DiscordPluginExport_Click(object sender, RoutedEventArgs e)
    {
        if (_installingDiscordPlugin) return;
        var dialog = new SaveFileDialog
        {
            FileName = PluginInstaller.FileName,
            Filter = "Плагин BetterDiscord (*.plugin.js)|*.plugin.js",
            InitialDirectory = PluginInstaller.HasBetterDiscord ? PluginInstaller.BetterDiscordPath : "",
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunPluginActionAsync(() => PluginInstaller.SaveAsync(dialog.FileName), "Файл сохранён.");
    }

    async Task RunPluginActionAsync(Func<Task> action, string successMessage)
    {
        _installingDiscordPlugin = true;
        _pluginMessage = "Подготавливаю файл…";
        RefreshDiscordPluginPage();
        try
        {
            await action();
            _pluginMessage = successMessage;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _pluginMessage = "Не удалось записать файл. Проверьте папку.";
        }
        finally
        {
            _installingDiscordPlugin = false;
            RefreshDiscordPluginPage();
        }
    }
}
