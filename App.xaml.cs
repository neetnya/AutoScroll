using System.Threading;
using System.Windows;

namespace AutoScroll;

public partial class App : Application
{
    internal const string MutexName = @"Local\AutoScroll.SingleInstance";
    internal const string ShowEventName = @"Local\AutoScroll.ShowExisting";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _watchCts;

    protected override void OnStartup(StartupEventArgs e)
    {
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
}
