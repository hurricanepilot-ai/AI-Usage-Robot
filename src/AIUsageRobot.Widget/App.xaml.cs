using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AIUsageRobot.Widget;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\AIUsageRobot.Widget.SingleInstance";
    private const string ActivateEventName = "Local\\AIUsageRobot.Widget.Activate";
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUsageRobot", "widget-crash.log");
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _listenerCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        DisableLegacyAutoStart();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try { EventWaitHandle.OpenExisting(ActivateEventName).Set(); } catch { }
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _listenerCancellation = new CancellationTokenSource();
        ListenForActivation(_listenerCancellation.Token);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void DisableLegacyAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("AIUsageRobot", false);
        }
        catch
        {
            // A legacy entry must never prevent a manual launch.
        }
    }

    private void ListenForActivation(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_activateEvent?.WaitOne(500) != true) continue;
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is null) return;
                    MainWindow.Show();
                    MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                });
            }
        }, cancellationToken);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listenerCancellation?.Cancel();
        _activateEvent?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _listenerCancellation?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        try
        {
            var message = $"发生未处理的异常：\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n详细信息已写入 {CrashLogPath}。";
            System.Windows.MessageBox.Show(message, "AIUsageRobot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // A UI failure must never override the original exception path.
        }
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            LogCrash("AppDomain.UnhandledException", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string source, Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(CrashLogPath,
                $"[{DateTimeOffset.UtcNow:O}] {source}: {exception}\n");
        }
        catch
        {
            // Crash logging must never propagate a second exception.
        }
    }
}
