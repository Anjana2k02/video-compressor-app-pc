using Microsoft.UI.Xaml;
using VideoOptimizer.Media;

namespace VideoOptimizer.App;

public partial class App : Application
{
    private MainWindow? window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => new FileAppLog().Write("unhandled_exception", e.Exception.GetType().Name);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
