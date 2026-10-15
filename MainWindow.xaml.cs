using System.Windows;
using System.Windows.Input;

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
            ToggleButton.Content = "开始";
        }
        else
        {
            _service.Start();
            ToggleButton.Content = "停止";
        }
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int speed = (int)SpeedSlider.Value;
        _service.SetSpeed(speed);
        SpeedLabel.Text = $"速度 {speed}";
    }

    private void OnAmplitudeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int amplitude = (int)AmplitudeSlider.Value;
        _service.SetAmplitude(amplitude);
        AmplitudeLabel.Text = $"幅度 {amplitude}";
    }

    private void OnDirectionChanged(object sender, RoutedEventArgs e)
    {
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
}
