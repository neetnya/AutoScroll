using System.Windows.Threading;

namespace AutoScroll;

/// <summary>
/// 滚动逻辑核心：维护启停状态、滚动方向、速度档位、滚动幅度，
/// 用 DispatcherTimer 周期性地调用 NativeMethods.SendWheel。
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

    private DispatcherTimer? _timer;
    private int _speed = DefaultSpeed;
    private int _amplitude = DefaultAmplitude;
    private int _direction = DefaultDirection;

    /// <summary>是否正在滚动。</summary>
    public bool IsRunning => _timer is { IsEnabled: true };

    /// <summary>当前速度档位（1~20）。</summary>
    public int Speed => _speed;

    /// <summary>当前幅度（1~20，单次滚动刻度数）。</summary>
    public int Amplitude => _amplitude;

    /// <summary>当前滚动方向：+1 向下，-1 向上。</summary>
    public int Direction => _direction;

    /// <summary>
    /// 由速度档位 s（1~40）计算两次滚轮事件间的间隔毫秒数。
    /// s=21 为中间值 → 200ms（旧速度1）；s=1 最慢 → 2000ms；s=40 最快 → 50ms。
    /// </summary>
    public static int ComputeIntervalMs(int speed)
    {
        int s = ClampSpeed(speed);
        if (s >= 21)
        {
            // 21 → 200ms，40 → 50ms（复用旧的更快区间线性映射）。
            return (int)Math.Round(200.0 - (s - 21) * (150.0 / 19.0));
        }
        // 1 → 2000ms，21 → 200ms（更慢区间线性映射，每档 +90ms）。
        return 200 + (21 - s) * 90;
    }

    /// <summary>钳制速度档位到 [1, 40]。</summary>
    public static int ClampSpeed(int speed) => Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>钳制幅度到 [1, 20]。</summary>
    public static int ClampAmplitude(int amplitude) => Math.Clamp(amplitude, MinAmplitude, MaxAmplitude);

    /// <summary>设置速度档位（运行中即时生效）。</summary>
    public void SetSpeed(int speed)
    {
        _speed = ClampSpeed(speed);
        if (_timer is { IsEnabled: true })
        {
            _timer.Interval = TimeSpan.FromMilliseconds(ComputeIntervalMs(_speed));
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
        if (IsRunning)
        {
            return;
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ComputeIntervalMs(_speed)),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>停止滚动（已停止则无操作）。</summary>
    public void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        NativeMethods.SendWheel(_direction, _amplitude);
    }
}
