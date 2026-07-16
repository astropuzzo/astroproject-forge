using System.Windows;
using AstroForge.Core.Diagnostics;

namespace AstroForge.App;

public partial class App : Application
{
    private readonly StructuredEventLog _eventLog = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            const string code = "AF-UNHANDLED-001";
            _eventLog.Write("Critical", code, "Eccezione UI non gestita intercettata", args.Exception);
            MessageBox.Show($"[{code}] Si è verificato un errore inatteso. L’evento è stato registrato; il progetto e le immagini originali non sono stati modificati da questa gestione.\n\n{args.Exception.Message}", "AstroProject Forge · recovery", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}

