using System.IO;
using System.Windows;
using Microsoft.Win32;
using AstroForge.App.ViewModels;

namespace AstroForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += (_, _) => _viewModel.SaveState();
        ApplyCommandLine();
    }

    private void ApplyCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--source" && index + 1 < args.Length) _viewModel.AddSource(args[++index]);
            else if (args[index] == "--library" && index + 1 < args.Length) _viewModel.LibraryPath = args[++index];
        }
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Seleziona cartella N.I.N.A.", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            foreach (var folder in dialog.FolderNames) _viewModel.AddSource(folder);
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is string path) _viewModel.RemoveSource(path);
    }

    private void ChooseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Seleziona libreria Master", InitialDirectory = Directory.Exists(_viewModel.LibraryPath) ? _viewModel.LibraryPath : null };
        if (dialog.ShowDialog(this) == true) _viewModel.LibraryPath = dialog.FolderName;
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Seleziona destinazione progetto", InitialDirectory = Directory.Exists(_viewModel.DestinationPath) ? _viewModel.DestinationPath : null };
        if (dialog.ShowDialog(this) == true) _viewModel.DestinationPath = dialog.FolderName;
    }

    private void BuildPlan_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.BuildPlan(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Anteprima non disponibile", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _viewModel.ExportAsync();
            MessageBox.Show(this, $"Progetto creato e verificato.\n\n{path}", "Esportazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Esportazione non completata", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportStatistics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog { Title = "Scegli dove esportare le statistiche", InitialDirectory = Directory.Exists(_viewModel.DestinationPath) ? _viewModel.DestinationPath : null };
            if (dialog.ShowDialog(this) != true) return;
            var path = _viewModel.ExportStatistics(dialog.FolderName);
            MessageBox.Show(this, $"Statistiche CSV e JSON esportate.\n\n{path}", "Dati esportati", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Esportazione statistiche non completata", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "Apri progetto AstroProject Forge", Filter = "Progetto AstroProject Forge (*.astroforge)|*.astroforge" };
            if (dialog.ShowDialog(this) == true) await _viewModel.LoadProjectAsync(dialog.FileName);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Progetto non aperto", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Title = "Salva progetto AstroProject Forge", Filter = "Progetto AstroProject Forge (*.astroforge)|*.astroforge", AddExtension = true, DefaultExt = ".astroforge", FileName = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? "Nuovo progetto" : _viewModel.ProjectName };
            if (dialog.ShowDialog(this) == true) _viewModel.SaveProject(dialog.FileName);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Progetto non salvato", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Svuotare la cache degli header? Le immagini originali non verranno modificate.", "Pulisci cache", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _viewModel.ClearHeaderCache();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ScanAsync(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Scansione non completata", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _viewModel.SelectedNode = e.NewValue as ProjectTreeNode;

    private void TreeMark_Changed(object sender, RoutedEventArgs e) => _viewModel.RefreshManualSelection();

    private void ReviewQueue_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        _viewModel.SelectReviewItem((sender as System.Windows.Controls.ListBox)?.SelectedItem as ReviewQueueItem);

    private void AssignCandidate_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AssignReviewCandidate((sender as FrameworkElement)?.DataContext as ReviewQueueItem, false);

    private void AssignCandidateGroup_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AssignReviewCandidate((sender as FrameworkElement)?.DataContext as ReviewQueueItem, true);
}
