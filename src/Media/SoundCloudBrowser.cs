using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DynamicIsland;

sealed class SoundCloudBrowser : Window
{
    internal static readonly string ProfileFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "SoundCloudBrowser");

    readonly WebView2 _browser = new();
    readonly TextBlock _address = new()
    {
        Padding = new Thickness(12, 8, 12, 8),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    readonly Task _ready;
    bool _closed;
    readonly DispatcherTimer _signInCheck = new() { Interval = TimeSpan.FromMilliseconds(650) };
    bool _checkingSignIn;

    public event Action? SignedIn;

    public SoundCloudBrowser()
    {
        Title = "SoundCloud — DynamicIsland";
        Width = 960;
        Height = 750;
        MinWidth = 420;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var content = new DockPanel();
        DockPanel.SetDock(_address, Dock.Top);
        content.Children.Add(_address);
        content.Children.Add(_browser);
        Content = content;
        _ready = InitializeAsync();
        _signInCheck.Tick += CheckSignIn;
        Closed += (_, _) =>
        {
            _closed = true;
            _signInCheck.Stop();
            _browser.Dispose();
        };
    }

    public void WatchSignIn() => _signInCheck.Start();

    async void CheckSignIn(object? sender, EventArgs e)
    {
        if (_checkingSignIn || _closed || !_ready.IsCompletedSuccessfully
            || !Uri.TryCreate(_browser.CoreWebView2.Source, UriKind.Absolute, out var source)
            || source.Scheme != Uri.UriSchemeHttps
            || source.Host is not ("soundcloud.com" or "www.soundcloud.com")) return;
        _checkingSignIn = true;
        try
        {
            string signedIn = await _browser.ExecuteScriptAsync("""
                ['soundcloud.com', 'www.soundcloud.com'].includes(location.hostname)
                && !!document.querySelector('.header__userNavUsernameButton')
                && !document.querySelector('#challenge-running, #challenge-stage, iframe[src*="challenges.cloudflare.com"]')
                """);
            if (!_closed && _signInCheck.IsEnabled && signedIn == "true")
            {
                _signInCheck.Stop();
                SignedIn?.Invoke();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!_closed) App.Log(ex);
        }
        finally { _checkingSignIn = false; }
    }

    async Task InitializeAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: ProfileFolder,
            options: new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount = false });
        if (_closed) return;
        if (!Path.GetFullPath(environment.UserDataFolder).Equals(Path.GetFullPath(ProfileFolder), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SoundCloud browser requires its own profile.");
        await _browser.EnsureCoreWebView2Async(environment);
        if (_closed) return;
        _browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
        _browser.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
                args.Cancel = true;
            else _address.Text = address.GetLeftPart(UriPartial.Authority);
        };
    }

    public async Task NavigateAsync(Uri address)
    {
        await _ready;
        if (!_closed) _browser.CoreWebView2.Navigate(address.AbsoluteUri);
    }
}
