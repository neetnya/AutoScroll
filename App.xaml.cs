using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AutoScroll;

public partial class App : Application
{
    internal const string MutexName = @"Local\AutoScroll.SingleInstance";
    internal const string ShowEventName = @"Local\AutoScroll.ShowExisting";

    private static readonly string ErrorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoScroll",
        "error.log");

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _watchCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);

        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (AbandonedMutexException ex)
        {
            createdNew = true;
            _mutex = ex.Mutex ?? _mutex;
        }

        if (!createdNew)
        {
            try
            {
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // 已有实例但通知失败，仍然避免双开。
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var window = new MainWindow();
        window.Show();
        WatchForRelaunch(window);
    }

    private void WatchForRelaunch(MainWindow window)
    {
        _watchCts = new CancellationTokenSource();
        CancellationToken token = _watchCts.Token;
        EventWaitHandle showEvent = _showEvent!;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (showEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                {
                    window.Dispatcher.BeginInvoke(window.BringOnScreen);
                }
            }
        }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watchCts?.Cancel();
        _showEvent?.Dispose();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 未持有锁时忽略。
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException(ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved();
    }

    private static void LogException(Exception ex)
    {
        try
        {
            string? dir = Path.GetDirectoryName(ErrorLogPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败时不能再抛，否则会递归。
        }
    }
}
