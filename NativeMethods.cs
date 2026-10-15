using System.Runtime.InteropServices;

namespace AutoScroll;

/// <summary>
/// 原生 Win32 调用：通过 SendInput 注入鼠标滚轮事件。
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
}
