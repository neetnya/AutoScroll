using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoScroll;

public partial class MainWindow : Window
{
    private readonly ScrollService _service = new();
    private readonly DispatcherTimer _saveDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
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
        ClampToWorkingArea();
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
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
        SpeedLabel.Text = $"速度 {speed}";
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
            DragMove();
            ClampToWorkingArea();
            SaveSettings();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _saveDebounce.Stop();
        _service.Stop();
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

        if (settings.HasPosition)
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

        SettingsStore.Save(new AppSettings
        {
            Speed = (int)SpeedSlider.Value,
            Amplitude = (int)AmplitudeSlider.Value,
            Direction = DirectionDown.IsChecked == true
                ? ScrollService.DirectionDown
                : ScrollService.DirectionUp,
            Left = Left,
            Top = Top,
            HasPosition = true,
        });
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
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;

        Left = Math.Clamp(Left, workTopLeft.X, Math.Max(workTopLeft.X, workTopLeft.X + areaWidth - width));
        Top = Math.Clamp(Top, workTopLeft.Y, Math.Max(workTopLeft.Y, workTopLeft.Y + areaHeight - height));
    }

    private void SyncRunningChrome()
    {
        bool on = _service.IsRunning;
        ToggleButton.Content = on ? "停止" : "开始";
        ToggleButton.Tag = on ? "On" : "Off";
        StatusLabel.Text = on ? "滚动中" : "已停止";
        StatusLabel.Foreground = on
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("TextPrimary");
        ExtraSettings.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        RootBorder.Background = (Brush)FindResource(on ? "PanelBgRunning" : "PanelBgIdle");
        RootBorder.BorderBrush = (Brush)FindResource(on ? "PanelBorderRunning" : "PanelBorderIdle");
        RootBorder.BorderThickness = new Thickness(on ? 2 : 1);
    }
}
