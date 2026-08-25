using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace AIUsageRobot.Widget;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\AIUsageRobot.Widget.SingleInstance";
    private const string ActivateEventName = "Local\\AIUsageRobot.Widget.Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _listenerCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        DisableLegacyAutoStart();
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
}
