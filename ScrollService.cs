using System.Diagnostics;

namespace AutoScroll;

internal readonly record struct AdvisoryState(string? Message, bool IsError);

/// <summary>
/// 滚动逻辑核心：维护启停状态、滚动方向、速度档位、滚动幅度，
/// 在后台线程按间隔调用 NativeMethods.SendWheel。
/// 速度 = 两次滚轮事件的间隔；幅度 = 单次滚动的刻度数。
/// </summary>
internal sealed class ScrollService
{
    public const int MinSpeed = 1;
    public const int MaxSpeed = 40;
    public const int DefaultSpeed = 22;

    public const int MinAmplitude = 1;
    public const int MaxAmplitude = 20;
    public const int DefaultAmplitude = 5;

    public const int DirectionDown = 1;
    public const int DirectionUp = -1;
    public const int DefaultDirection = DirectionDown;

    /// <summary>面板提示变化（悬停暂停或发送失败）；Message 为 null 表示清除。</summary>
    public event EventHandler<AdvisoryState>? AdvisoryChanged;

    private readonly object _gate = new();
    private Thread? _thread;
    private AutoResetEvent? _signal;
    private IntPtr _ownHwnd;
    private volatile bool _running;
    private volatile int _speed = DefaultSpeed;
    private volatile int _amplitude = DefaultAmplitude;
    private volatile int _direction = DefaultDirection;
    private int _speedChanged;
    private int _failStreak;

    /// <summary>是否正在滚动。</summary>
    public bool IsRunning => _running;

    /// <summary>当前速度档位（1~40）。</summary>
    public int Speed => _speed;

    /// <summary>当前幅度（1~20，单次滚动刻度数）。</summary>
    public int Amplitude => _amplitude;

    /// <summary>当前滚动方向：+1 向下，-1 向上。</summary>
    public int Direction => _direction;

    /// <summary>记录自身面板句柄，用于检测鼠标悬停在自身面板时自动暂停注入。</summary>
    public void SetOwnWindow(IntPtr hwnd)
    {
        _ownHwnd = hwnd;
    }

    /// <summary>
    /// 由速度档位 s（1~40）计算两次滚轮事件间的间隔毫秒数。
    /// s=1 最慢 → 2000ms；s=20 → 290ms；s=21 → 200ms；s=40 最快 → 50ms（无重复档位）。
    /// </summary>
    public static int ComputeIntervalMs(int speed)
    {
        int s = ClampSpeed(speed);
        if (s >= 21)
        {
            // 21 → 200ms，40 → 50ms。
            return (int)Math.Round(200.0 - (s - 21) * (150.0 / 19.0));
        }

        // 1 → 2000ms，20 → 290ms，21 → 200ms（每档均匀递减 90ms）。
        return 2000 - (s - 1) * 90;
    }

    /// <summary>钳制速度档位到 [1, 40]。</summary>
    public static int ClampSpeed(int speed) => Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>钳制幅度到 [1, 20]。</summary>
    public static int ClampAmplitude(int amplitude) => Math.Clamp(amplitude, MinAmplitude, MaxAmplitude);

    /// <summary>设置速度档位（运行中即时唤醒休眠线程生效）。</summary>
    public void SetSpeed(int speed)
    {
        int clamped = ClampSpeed(speed);
        if (_speed == clamped)
        {
            return;
        }

        _speed = clamped;
        Interlocked.Exchange(ref _speedChanged, 1);
        lock (_gate)
        {
            if (_running)
            {
                _signal?.Set();
            }
        }
    }

    /// <summary>设置滚动幅度（运行中即时生效）。</summary>
    public void SetAmplitude(int amplitude)
    {
        _amplitude = ClampAmplitude(amplitude);
    }

    /// <summary>设置方向（+1 向下 / -1 向上）。</summary>
    public void SetDirection(int direction)
    {
        _direction = direction > 0 ? DirectionDown : DirectionUp;
    }

    /// <summary>启动滚动（已启动则无操作）。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _failStreak = 0;
            Interlocked.Exchange(ref _speedChanged, 0);
            var signal = new AutoResetEvent(false);
            _signal = signal;
            _running = true;
            _thread = new Thread(() => Loop(signal))
            {
                IsBackground = true,
                Name = "AutoScroll.Wheel",
            };
            _thread.Start();
        }
    }

    /// <summary>停止滚动（已停止则无操作）。</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _signal?.Set();
            _signal = null;
            thread = _thread;
            _thread = null;
        }

        thread?.Join(TimeSpan.FromSeconds(1));
        Interlocked.Exchange(ref _failStreak, 0);
        AdvisoryChanged?.Invoke(this, new AdvisoryState(null, false));
    }

    private void Loop(AutoResetEvent signal)
    {
        bool hiRes = false;
        bool hoverPaused = false;
        Stopwatch clock = Stopwatch.StartNew();
        long nextMs = 0;

        try
        {
            while (_running)
            {
                // 鼠标位于自身悬浮面板上时自动暂停注入（避免刚点开始或调速时滚到自己）
                if (_ownHwnd != IntPtr.Zero && NativeMethods.IsCursorOverWindow(_ownHwnd))
                {
                    if (!hoverPaused)
                    {
                        hoverPaused = true;
                        Interlocked.Exchange(ref _failStreak, 0);
                        AdvisoryChanged?.Invoke(this, new AdvisoryState("悬停暂停", false));
                    }

                    signal.WaitOne(80);
                    nextMs = clock.ElapsedMilliseconds;
                    continue;
                }

                if (hoverPaused)
                {
                    hoverPaused = false;
                    AdvisoryChanged?.Invoke(this, new AdvisoryState(null, false));
                    nextMs = clock.ElapsedMilliseconds;
                }

                uint sent = NativeMethods.SendWheel(_direction, _amplitude);
                ReportSendResult(sent);

                int interval = ComputeIntervalMs(_speed);
                bool wantHiRes = interval <= 100;
                if (wantHiRes && !hiRes)
                {
                    NativeMethods.BeginHighResolutionPeriod();
                    hiRes = true;
                }
                else if (!wantHiRes && hiRes)
                {
                    NativeMethods.EndHighResolutionPeriod();
                    hiRes = false;
                }

                nextMs += interval;
                long now = clock.ElapsedMilliseconds;
                int sleep = (int)(nextMs - now);
                if (sleep > 0)
                {
                    if (signal.WaitOne(sleep))
                    {
                        if (!_running)
                        {
                            break;
                        }

                        // 速度发生变化：立即重置节拍，避免等待旧的慢速周期
                        if (Interlocked.Exchange(ref _speedChanged, 0) != 0)
                        {
                            nextMs = clock.ElapsedMilliseconds;
                        }
                    }
                }
                else
                {
                    nextMs = now;
                }
            }
        }
        finally
        {
            if (hiRes)
            {
                NativeMethods.EndHighResolutionPeriod();
            }

            signal.Dispose();
        }
    }

    private void ReportSendResult(uint sent)
    {
        if (sent == 0)
        {
            if (Interlocked.Increment(ref _failStreak) == 2)
            {
                AdvisoryChanged?.Invoke(this, new AdvisoryState("无法滚动该窗口", true));
            }

            return;
        }

        if (Interlocked.Exchange(ref _failStreak, 0) != 0)
        {
            AdvisoryChanged?.Invoke(this, new AdvisoryState(null, false));
        }
    }
}
