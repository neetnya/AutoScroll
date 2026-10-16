using System.Runtime.InteropServices;

namespace AutoScroll;

/// <summary>
/// 原生 Win32 调用：滚轮注入、窗口不激活、拖动、显示器工作区、高速定时器分辨率。
/// </summary>
internal static class NativeMethods
{
    /// <summary>一个滚轮"刻度"的像素当量，向上为正、向下为负。</summary>
    public const int WheelDelta = 120;

    private const int InputMouse = 0;
    private const uint MouseEventWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int type;
        public MouseInput mi;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uMilliseconds);

    /// <summary>提高系统定时器分辨率，仅在高速滚动时使用。</summary>
    public static void BeginHighResolutionPeriod() => timeBeginPeriod(1);

    /// <summary>恢复系统定时器分辨率。</summary>
    public static void EndHighResolutionPeriod() => timeEndPeriod(1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    /// <summary>计算 INPUT 结构体的实际大小（在 32/64 位下正确对齐）。</summary>
    public static int SizeOfInput() => Marshal.SizeOf<Input>();

    /// <summary>
    /// 注入一次鼠标滚轮事件。
    /// </summary>
    /// <param name="direction">+1 向下滚动，-1 向上滚动。</param>
    /// <param name="amplitude">一次注入的刻度数（绝对值），≥1。</param>
    /// <returns>成功注入的事件数；失败返回 0。</returns>
    public static uint SendWheel(int direction, int amplitude)
    {
        // Win32 滚轮 delta：正值=内容向上滚，负值=内容向下滚。
        // 故「向下」需负 delta，「向上」需正 delta。
        int delta = -WheelDelta * Math.Sign(direction) * Math.Max(1, Math.Abs(amplitude));

        var input = new Input
        {
            type = InputMouse,
            mi = new MouseInput
            {
                mouseData = (uint)delta,
                dwFlags = MouseEventWheel,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };

        return SendInput(1, new[] { input }, SizeOfInput());
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int WmDisplayChange = 0x007E;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>让窗口可点、但不成为前台窗口，避免抢走正在阅读的窗口焦点。</summary>
    public static void PreventActivation(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(ex | WsExNoActivate));
    }

    /// <summary>不激活窗口的拖动（替代 WPF DragMove）。</summary>
    public static void DragWithoutActivate(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    /// <summary>取窗口所在显示器的工作区（屏幕像素，不含任务栏）。</summary>
    public static bool TryGetNearestWorkArea(IntPtr hwnd, out ScreenRect work)
    {
        work = default;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        work = new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        return true;
    }

    public readonly struct ScreenRect
    {
        public ScreenRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
    }
}
