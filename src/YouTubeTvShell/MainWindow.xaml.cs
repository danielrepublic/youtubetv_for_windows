using Microsoft.UI.Xaml;

namespace YouTubeTvShell;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = "YouTube TV";

        InitializeEscHandling();
        InitializeWindowLifecycle();

        // WinUI3 Window has no Loaded event of its own; the content root
        // (the Grid in MainWindow.xaml) carries WebView2 init instead.
        if (Content is FrameworkElement root)
            root.Loaded += MainWindow_Loaded;
    }
}
