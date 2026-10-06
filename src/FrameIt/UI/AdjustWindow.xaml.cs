using System.Windows;

namespace FrameIt.UI;

public partial class AdjustWindow : Window
{
    private readonly Action<int, int> _preview;
    private bool _queued;

    public AdjustWindow(Action<int, int> preview)
    {
        InitializeComponent();
        _preview = preview;
        BrightnessSlider.ValueChanged += (_, _) => QueuePreview();
        ContrastSlider.ValueChanged += (_, _) => QueuePreview();
    }

    public int Brightness => (int)Math.Round(BrightnessSlider.Value);

    public int Contrast => (int)Math.Round(ContrastSlider.Value);

    private void QueuePreview()
    {
        BrightnessValue.Text = Brightness.ToString();
        ContrastValue.Text = Contrast.ToString();
        if (_queued)
        {
            return;
        }

        _queued = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                _queued = false;
                _preview(Brightness, Contrast);
            }));
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        BrightnessSlider.Value = 0;
        ContrastSlider.Value = 0;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
