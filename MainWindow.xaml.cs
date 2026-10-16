using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoScroll;

public partial class MainWindow : Window
{
    private readonly ScrollService _service = new();

    public MainWindow()
    {
        InitializeComponent();
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
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _service.Stop();
        base.OnClosed(e);
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
