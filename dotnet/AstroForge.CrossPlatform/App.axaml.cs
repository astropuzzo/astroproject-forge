using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AstroForge.Core.Diagnostics;

namespace AstroForge.CrossPlatform;

public sealed partial class App : Application
{
    private readonly StructuredEventLog _eventLog = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            var smokeTest = desktop.Args?.Contains(MainWindow.SmokeTestArgument) == true;
            if (smokeTest)
                window.Opened += async (_, _) => desktop.Shutdown(await window.RunSmokeTestAsync());

            // Recovery for unexpected UI exceptions: log them and keep the session alive.
            // The smoke test lets them crash so CI still fails on startup regressions.
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                const string code = "AF-UNHANDLED-001";
                _eventLog.Write("Critical", code, "Eccezione UI non gestita intercettata", args.Exception);
                if (smokeTest) return;
                args.Handled = true;
                new WindowNotificationManager(window) { Position = NotificationPosition.BottomRight, MaxItems = 3 }.Show(new Notification(
                    "AstroProject Forge · recovery",
                    $"[{code}] Si è verificato un errore inatteso. L’evento è stato registrato; il progetto e le immagini originali non sono stati modificati da questa gestione.\n\n{args.Exception.Message}",
                    NotificationType.Error));
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
