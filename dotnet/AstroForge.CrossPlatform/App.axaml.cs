using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AstroForge.CrossPlatform;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (desktop.Args?.Contains(MainWindow.SmokeTestArgument) == true)
                window.Opened += async (_, _) => desktop.Shutdown(await window.RunSmokeTestAsync());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
