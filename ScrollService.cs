using System.Diagnostics;

namespace AutoScroll;

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

    /// <summary>非 null 时表示需要在面板上显示的提示；null 表示清除。</summary>
    public event EventHandler<string?>? AdvisoryChanged;

    private readonly object _gate = new();
    private Thread? _thread;
    private ManualResetEventSlim? _wake;
    private volatile bool _running;
    private volatile int _speed = DefaultSpeed;
    private volatile int _amplitude = DefaultAmplitude;
    private volatile int _direction = DefaultDirection;
    private int _failStreak;

    /// <summary>是否正在滚动。</summary>
    public bool IsRunning => _running;

    /// <summary>当前速度档位（1~40）。</summary>
    public int Speed => _speed;

    /// <summary>当前幅度（1~20，单次滚动刻度数）。</summary>
    public int Amplitude => _amplitude;

    /// <summary>当前滚动方向：+1 向下，-1 向上。</summary>
    public int Direction => _direction;

    /// <summary>
    /// 由速度档位 s（1~40）计算两次滚轮事件间的间隔毫秒数。
    /// s=21 为中间值 → 200ms（旧速度1）；s=1 最慢 → 2000ms；s=40 最快 → 50ms。
    /// 1~20 线性接到 200ms，避免 20→21 出现 90ms 跳变。
    /// </summary>
    public static int ComputeIntervalMs(int speed)
    {
        int s = ClampSpeed(speed);
        if (s >= 21)
        {
            // 21 → 200ms，40 → 50ms。
            return (int)Math.Round(200.0 - (s - 21) * (150.0 / 19.0));
        }

        // 1 → 2000ms，20 → 200ms，与 21 档对齐。
        return (int)Math.Round(2000.0 - (s - 1) * (1800.0 / 19.0));
    }

    /// <summary>钳制速度档位到 [1, 40]。</summary>
    public static int ClampSpeed(int speed) => Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>钳制幅度到 [1, 20]。</summary>
    public static int ClampAmplitude(int amplitude) => Math.Clamp(amplitude, MinAmplitude, MaxAmplitude);

    /// <summary>设置速度档位（运行中即时生效）。</summary>
    public void SetSpeed(int speed)
    {
        _speed = ClampSpeed(speed);
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
            _wake = new ManualResetEventSlim(false);
            _running = true;
            _thread = new Thread(Loop)
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
        ManualResetEventSlim? wake;
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            wake = _wake;
            _wake = null;
            thread = _thread;
            _thread = null;
        }

        wake?.Set();
        thread?.Join(TimeSpan.FromSeconds(1));
        wake?.Dispose();
        _failStreak = 0;
        AdvisoryChanged?.Invoke(this, null);
    }

    private void Loop()
    {
        bool hiRes = false;
        Stopwatch clock = Stopwatch.StartNew();
        long nextMs = 0;
        try
        {
            while (_running)
            {
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
                    if (_wake?.Wait(sleep) == true)
                    {
                        break;
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
        }
    }

    private void ReportSendResult(uint sent)
    {
        if (sent == 0)
        {
            if (Interlocked.Increment(ref _failStreak) == 2)
            {
                AdvisoryChanged?.Invoke(this, "无法滚动该窗口");
            }

            return;
        }

        if (Interlocked.Exchange(ref _failStreak, 0) != 0)
        {
            AdvisoryChanged?.Invoke(this, null);
        }
    }
}
