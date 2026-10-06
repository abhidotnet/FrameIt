using System.Windows;
using WpfInput = System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FrameIt.UI;

public partial class ViewerWindow : Window
{
    public ViewerWindow(string imagePath)
    {
        InitializeComponent();
        PathLabel.Text = imagePath;
        CapturedImage.Source = LoadImage(imagePath);
    }

    private static BitmapImage LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void OnKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            Close();
        }
    }
}
