using System.IO;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;
using Hinge.Platform;

namespace Hinge.App;

public partial class App : Application
{
    private Window? _window;
    private AppInstance? _mainInstance;
    public MainWindow? MainWindow => _window as MainWindow;

    public App()
    {
        // The taskbar chooses the grouping and icon identity when the first
        // window is created. Set it before loading app resources or presenting
        // any WinUI UI; setting it later from the notification presenter is too
        // late for the already-created taskbar button.
        Win32NotificationPresenter.InitializeCurrentProcessAppUserModelId();
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
            if (!existingWindow.HandleActivationArguments(args.Arguments))
            {
                existingWindow.Activate();
            }
            return;
        }

        bool startSilently = MainWindow.ShouldStartSilently(args.Arguments);
        _window = new MainWindow();
        if (_window is MainWindow mainWindow && mainWindow.HandleActivationArguments(args.Arguments))
        {
            mainWindow.StartSilentlyToTray();
        }
        else if (startSilently && _window is MainWindow silentWindow)
        {
            silentWindow.StartSilentlyToTray();
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
            var arguments = GetLaunchArguments(args);
            if (!window.HandleActivationArguments(arguments))
            {
                window.RestoreFromTray();
            }
        });
    }

    private static string? GetLaunchArguments(AppActivationArguments activationArguments)
    {
        try
        {
            return activationArguments.Data?
                .GetType()
                .GetProperty("Arguments")?
                .GetValue(activationArguments.Data) as string;
        }
        catch
        {
            return null;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        // A malformed fire-and-forget UI callback must not tear down the
        // resident tray process. The exception is still recorded for later
        // diagnosis, while WinUI is allowed to keep the window alive.
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved();
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
