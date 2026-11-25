using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland;

public partial class App : Application
{
    const string InstanceName = "DynamicIsland.SingleInstance";
    const string LogFileName = "DynamicIsland.log";
    const long LogLimitBytes = 2 * 1024 * 1024;
    static readonly TimeSpan PredecessorExitTimeout = TimeSpan.FromSeconds(10);
    static readonly object LogLock = new();

    Mutex? _mutex;

    static App()
    {
        // Keep tooltip text available to automation peers without opening native popup windows.
        ToolTipEventHandler suppressToolTip = (_, e) => e.Handled = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent, suppressToolTip, true);
        EventManager.RegisterClassHandler(typeof(FrameworkContentElement), ToolTipService.ToolTipOpeningEvent, suppressToolTip, true);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Registered first: an exception on a background thread terminates the process, and without these
        // handlers it would do so without leaving any trace in the log.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write($"FATAL (terminating={args.IsTerminating}) {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };

        _mutex = new Mutex(true, InstanceName, out bool isOnlyInstance);
        if (!isOnlyInstance && e.Args.Contains(Updater.RestartedFlag)) isOnlyInstance = WaitForPredecessor(_mutex);
        if (!isOnlyInstance)
        {
            Shutdown();
            return;
        }
        Updater.CleanUp();

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };
        // A "start" without a matching "exit" in the log means the process was killed (crash, power, task manager).
        Write($"--- start {AppVersion.Product} pid {Environment.ProcessId}");
        Exit += (_, _) => Write($"--- exit pid {Environment.ProcessId}");
        new MainWindow().Show();
    }

    public static void Log(Exception ex) => Write(ex.ToString());

    public static void Log(string text) => Write(text);

    static void Write(string text)
    {
        try
        {
            lock (LogLock)
            {
                string path = Path.Combine(Path.GetTempPath(), LogFileName);
                if (new FileInfo(path) is { Exists: true } file && file.Length > LogLimitBytes) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n");
            }
        }
        catch { }
    }

    static bool WaitForPredecessor(Mutex mutex)
    {
        try { return mutex.WaitOne(PredecessorExitTimeout); }
        catch (AbandonedMutexException) { return true; }
    }
}
