using System.IO;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;

namespace Hinge.App;

public partial class App : Application
{
    private Window? _window;
    private AppInstance? _mainInstance;
    public MainWindow? MainWindow => _window as MainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instance = AppInstance.FindOrRegisterForKey("Hinge.Main");
        if (!instance.IsCurrent)
        {
            try
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activationArgs != null)
                {
                    await instance.RedirectActivationToAsync(activationArgs);
                }
            }
            finally
            {
                Environment.Exit(0);
            }

            return;
        }

        _mainInstance = instance;
        _mainInstance.Activated += MainInstance_Activated;

        if (_window is MainWindow existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        bool startSilently = MainWindow.ShouldStartSilently(args.Arguments);
        _window = new MainWindow();
        if (startSilently && _window is MainWindow mainWindow)
        {
            mainWindow.StartSilentlyToTray();
        }
        else
        {
            _window.Activate();
        }
    }

    private void MainInstance_Activated(object? sender, AppActivationArguments args)
    {
        if (_window is not MainWindow window) return;

        window.DispatcherQueue.TryEnqueue(() =>
        {
            window.RestoreFromTray();
        });
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    private static void LogException(Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hinge");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
            // Crash logging must never mask the original exception.
        }
    }
}
