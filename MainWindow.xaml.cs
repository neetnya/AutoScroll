using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoScroll;

public partial class MainWindow : Window
{
    private const int ToggleHotKeyId = 0x1001;

    private readonly ScrollService _service = new();
    private AdvisoryState _advisory;
    private readonly DispatcherTimer _saveDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };
    private HwndSource? _hwndSource;
    private bool _hotKeyRegistered;
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        _service.AdvisoryChanged += OnAdvisoryChanged;
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveSettings();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        ApplySettings(SettingsStore.Load());
        _loading = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        IntPtr hwnd = _hwndSource.Handle;
        NativeMethods.PreventActivation(hwnd);
        _hwndSource.AddHook(WndProc);
        _service.SetOwnWindow(hwnd);

        _hotKeyRegistered = NativeMethods.RegisterHotKey(
            hwnd,
            ToggleHotKeyId,
            NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
            NativeMethods.VkS);

        HintLabel.Text = _hotKeyRegistered
            ? "滚鼠标所在窗口 · Ctrl+Shift+S 启停"
            : "滚鼠标所在窗口（移到面板自动暂停）";

        ClampToWorkingArea();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // 首次布局完成后 ActualWidth/ActualHeight 已确定，再次确保窗口完全落在工作区内
        ClampToWorkingArea();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return (IntPtr)NativeMethods.MaNoActivate;
        }

        if (msg == NativeMethods.WmHotKey && wParam.ToInt32() == ToggleHotKeyId)
        {
            handled = true;
            ToggleScrolling();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmDisplayChange)
        {
            Dispatcher.BeginInvoke(ClampToWorkingArea);
        }

        return IntPtr.Zero;
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        ToggleScrolling();
    }

    private void ToggleScrolling()
    {
        if (_service.IsRunning)
        {
            _service.Stop();
        }
        else
        {
            _service.Start();
        }

        SyncRunningChrome();
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedLabel is null)
        {
            return;
        }

        int speed = (int)SpeedSlider.Value;
        _service.SetSpeed(speed);
        int intervalMs = ScrollService.ComputeIntervalMs(speed);
        SpeedLabel.Text = $"速度 {speed}（{intervalMs}ms）";
        ScheduleSave();
    }

    private void OnAmplitudeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AmplitudeLabel is null)
        {
            return;
        }

        int amplitude = (int)AmplitudeSlider.Value;
        _service.SetAmplitude(amplitude);
        AmplitudeLabel.Text = $"幅度 {amplitude}";
        ScheduleSave();
    }

    private void OnSliderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is Slider slider)
        {
            double step = e.Delta > 0 ? 1 : -1;
            slider.Value = Math.Clamp(slider.Value + step, slider.Minimum, slider.Maximum);
            e.Handled = true;
        }
    }

    private void OnDirectionChanged(object sender, RoutedEventArgs e)
    {
        if (DirectionDown is null)
        {
            return;
        }

        _service.SetDirection(DirectionDown.IsChecked == true
            ? ScrollService.DirectionDown
            : ScrollService.DirectionUp);
        ScheduleSave();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _service.Stop();
        Close();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            double oldLeft = Left;
            double oldTop = Top;
            NativeMethods.DragWithoutActivate(new WindowInteropHelper(this).Handle);
            ClampToWorkingArea();
            if (Math.Abs(Left - oldLeft) > 0.5 || Math.Abs(Top - oldTop) > 0.5)
            {
                SaveSettings();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _service.AdvisoryChanged -= OnAdvisoryChanged;
        _saveDebounce.Stop();
        _service.Stop();

        if (_hwndSource is not null)
        {
            if (_hotKeyRegistered)
            {
                NativeMethods.UnregisterHotKey(_hwndSource.Handle, ToggleHotKeyId);
                _hotKeyRegistered = false;
            }

            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        SaveSettings();
        base.OnClosed(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(ClampToWorkingArea);
    }

    private void ApplySettings(AppSettings settings)
    {
        SpeedSlider.Value = ScrollService.ClampSpeed(settings.Speed);
        AmplitudeSlider.Value = ScrollService.ClampAmplitude(settings.Amplitude);
        if (settings.Direction < 0)
        {
            DirectionUp.IsChecked = true;
        }
        else
        {
            DirectionDown.IsChecked = true;
        }

        // 确保首次启动即使值等于 XAML 默认值，标签也显示正确毫秒数
        int speed = (int)SpeedSlider.Value;
        _service.SetSpeed(speed);
        SpeedLabel.Text = $"速度 {speed}（{ScrollService.ComputeIntervalMs(speed)}ms）";

        if (settings.HasPosition && double.IsFinite(settings.Left) && double.IsFinite(settings.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.Left;
            Top = settings.Top;
        }
    }

    private void ScheduleSave()
    {
        if (_loading)
        {
            return;
        }

        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        bool validPos = double.IsFinite(Left) && double.IsFinite(Top);
        SettingsStore.Save(new AppSettings
        {
            Speed = (int)SpeedSlider.Value,
            Amplitude = (int)AmplitudeSlider.Value,
            Direction = DirectionDown.IsChecked == true
                ? ScrollService.DirectionDown
                : ScrollService.DirectionUp,
            Left = validPos ? Left : 0,
            Top = validPos ? Top : 0,
            HasPosition = validPos,
        });
    }

    public void BringOnScreen()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        ClampToWorkingArea();
        Topmost = false;
        Topmost = true;
    }

    private void ClampToWorkingArea()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.TryGetNearestWorkArea(hwnd, out NativeMethods.ScreenRect work))
        {
            return;
        }

        Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        Point workTopLeft = fromDevice.Transform(new Point(work.Left, work.Top));
        Point workBottomRight = fromDevice.Transform(new Point(work.Right, work.Bottom));
        double areaWidth = workBottomRight.X - workTopLeft.X;
        double areaHeight = workBottomRight.Y - workTopLeft.Y;

        double width = ActualWidth > 0 ? ActualWidth : (double.IsFinite(Width) && Width > 0 ? Width : 228);
        double height = ActualHeight > 0 ? ActualHeight : (double.IsFinite(Height) && Height > 0 ? Height : 180);

        double curLeft = double.IsFinite(Left) ? Left : workTopLeft.X + (areaWidth - width) / 2;
        double curTop = double.IsFinite(Top) ? Top : workTopLeft.Y + (areaHeight - height) / 2;

        Left = Math.Clamp(curLeft, workTopLeft.X, Math.Max(workTopLeft.X, workTopLeft.X + areaWidth - width));
        Top = Math.Clamp(curTop, workTopLeft.Y, Math.Max(workTopLeft.Y, workTopLeft.Y + areaHeight - height));
    }

    private void OnAdvisoryChanged(object? sender, AdvisoryState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_service.IsRunning && state.Message is not null)
            {
                return;
            }

            _advisory = state;
            SyncStatusText();
        });
    }

    private void SyncStatusText()
    {
        if (_advisory.Message is not null)
        {
            StatusLabel.Text = _advisory.Message;
            StatusLabel.Foreground = (Brush)FindResource(_advisory.IsError ? "CloseHover" : "TextMuted");
            return;
        }

        bool on = _service.IsRunning;
        StatusLabel.Text = on ? "滚动中" : "已停止";
        StatusLabel.Foreground = on
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("TextPrimary");
    }

    private void SyncRunningChrome()
    {
        bool on = _service.IsRunning;
        ToggleButton.Content = on ? "停止" : "开始";
        ToggleButton.Tag = on ? "On" : "Off";
        if (!on)
        {
            _advisory = default;
        }

        SyncStatusText();
        ExtraSettings.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        RootBorder.Background = (Brush)FindResource(on ? "PanelBgRunning" : "PanelBgIdle");
        RootBorder.BorderBrush = (Brush)FindResource(on ? "PanelBorderRunning" : "PanelBorderIdle");
        RootBorder.BorderThickness = new Thickness(on ? 2 : 1);

        // 停止后展开设置区高度变大，布局更新后重新钳制位置防止超出任务栏
        Dispatcher.BeginInvoke(ClampToWorkingArea, DispatcherPriority.Loaded);
    }
}
