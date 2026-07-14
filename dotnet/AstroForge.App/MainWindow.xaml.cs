using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using AstroForge.App.ViewModels;

namespace AstroForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _sourcesVisible = true;
    private bool _inspectorVisible = true;
    private bool _masterLabActive;
    private int _onboardingStep = 1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += (_, _) => _viewModel.SaveState();
        Loaded += MainWindow_Loaded;
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
        var dialog = new OpenFolderDialog { Title = "Seleziona cartelle contenenti FITS o XISF", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            foreach (var folder in dialog.FolderNames) _viewModel.AddSource(folder);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Importa immagini astronomiche", Filter = "Immagini astronomiche (*.fit;*.fits;*.fts;*.xisf)|*.fit;*.fits;*.fts;*.xisf|Tutti i file (*.*)|*.*", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            foreach (var file in dialog.FileNames) _viewModel.AddSource(file);
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is string path) _viewModel.RemoveSource(path);
    }

    private void ChooseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Aggiungi libreria Master", InitialDirectory = Directory.Exists(_viewModel.LibraryPath) ? _viewModel.LibraryPath : null };
        if (dialog.ShowDialog(this) == true) _viewModel.AddMasterLibrary(dialog.FolderName);
    }

    private void RemoveLibrary_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedMasterLibrary();
    private void MoveLibraryUp_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(-1);
    private void MoveLibraryDown_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(1);
    private void RefreshLibraries_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshMasterLibraryStates();

    private void ToggleSources_Click(object sender, RoutedEventArgs e) { _sourcesVisible = !_sourcesVisible; ApplyResponsiveLayout(); }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e) { _inspectorVisible = !_inspectorVisible; ApplyResponsiveLayout(); }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth < 1080) _sourcesVisible = false;
        if (ActualWidth < 980) _inspectorVisible = false;
        ApplyResponsiveLayout();
    }
    private void ApplyResponsiveLayout()
    {
        var compact = ActualWidth < 1250;
        var narrowHeader = ActualWidth < 1080;
        SourcesColumn.Width = _sourcesVisible ? new GridLength(compact ? 220 : 270) : new GridLength(0);
        InspectorColumn.Width = _inspectorVisible && !_masterLabActive ? new GridLength(compact ? 330 : 430) : new GridLength(0);
        BrandText.Visibility = narrowHeader ? Visibility.Collapsed : Visibility.Visible;
        ProjectStatusBadge.Visibility = ActualWidth < 1380 ? Visibility.Collapsed : Visibility.Visible;
        SourceToggleButton.Content = narrowHeader ? "☰" : "☰  Sorgenti";
        SourceToggleButton.Width = narrowHeader ? 44 : double.NaN;
        SourceToggleButton.Padding = narrowHeader ? new Thickness(0) : new Thickness(13, 0, 13, 0);
        InspectorToggleButton.Content = narrowHeader ? "◫" : "Inspector  ◫";
        InspectorToggleButton.Width = narrowHeader ? 44 : double.NaN;
        InspectorToggleButton.Padding = narrowHeader ? new Thickness(0) : new Thickness(13, 0, 13, 0);
        OpenProjectButton.Content = narrowHeader ? "Apri" : "Apri progetto";
        SaveProjectButton.Content = narrowHeader ? "Salva" : "Salva progetto";
    }

    private void More_Click(object sender, RoutedEventArgs e) { SettingsPopup.IsOpen = false; MorePopup.IsOpen = !MorePopup.IsOpen; }
    private void CloseMore_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = false;
    private void Settings_Click(object sender, RoutedEventArgs e) { MorePopup.IsOpen = false; SettingsPopup.IsOpen = !SettingsPopup.IsOpen; }
    private void DensitySelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (DensitySelector.SelectedItem is System.Windows.Controls.ComboBoxItem item) _viewModel.UiDensity = item.Content?.ToString() ?? "Comoda";
        ApplyUiPreferences();
    }
    private void ReducedMotion_Click(object sender, RoutedEventArgs e) => ApplyUiPreferences();
    private void SaveUiPreferences_Click(object sender, RoutedEventArgs e) { _viewModel.SaveState(); SettingsPopup.IsOpen = false; }
    private void ReopenOnboarding_Click(object sender, RoutedEventArgs e) { SettingsPopup.IsOpen = false; _onboardingStep = 1; _viewModel.OpenOnboarding(); UpdateOnboarding(); }
    private void OnboardingChooseLibrary_Click(object sender, RoutedEventArgs e) => ChooseLibrary_Click(sender, e);
    private void OnboardingAddSource_Click(object sender, RoutedEventArgs e) => AddSource_Click(sender, e);
    private void OnboardingAddFiles_Click(object sender, RoutedEventArgs e) => AddFiles_Click(sender, e);
    private void OnboardingSkip_Click(object sender, RoutedEventArgs e) => _viewModel.CompleteOnboarding();
    private void OnboardingBack_Click(object sender, RoutedEventArgs e) { _onboardingStep = Math.Max(1, _onboardingStep - 1); UpdateOnboarding(); }
    private void OnboardingNext_Click(object sender, RoutedEventArgs e)
    {
        if (_onboardingStep == 4) { _viewModel.CompleteOnboarding(); return; }
        _onboardingStep++;
        UpdateOnboarding();
    }
    private void UpdateOnboarding()
    {
        OnboardingStep1.Visibility = _onboardingStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStep2.Visibility = _onboardingStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStep3.Visibility = _onboardingStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingStep4.Visibility = _onboardingStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingProgress.Text = $"{_onboardingStep} / 4";
        OnboardingBackButton.Visibility = _onboardingStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingNextButton.Content = _onboardingStep switch { 1 => "Inizia", 4 => "Vai al progetto", _ => "Continua" };
    }
    private void ApplyUiPreferences()
    {
        var density = _viewModel.UiDensity;
        Application.Current.Resources["ControlPadding"] = density switch { "Compatta" => new Thickness(12, 6, 12, 6), "Ampia" => new Thickness(18, 11, 18, 11), _ => new Thickness(15, 9, 15, 9) };
        Application.Current.Resources["ControlMinHeight"] = density switch { "Compatta" => 34d, "Ampia" => 46d, _ => 40d };
        Application.Current.Resources["ToolbarHeight"] = density switch { "Compatta" => 38d, "Ampia" => 46d, _ => 42d };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyUiPreferences();
        if (_viewModel.ReducedMotion) return;
        RootSurface.Opacity = 0;
        if (RootSurface.RenderTransform is System.Windows.Media.TranslateTransform transform) transform.Y = 8;
        RootSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (RootSurface.RenderTransform is System.Windows.Media.TranslateTransform translate)
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void WorkspaceTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source != WorkspaceTabs) return;
        _masterLabActive = (WorkspaceTabs.SelectedItem as System.Windows.Controls.TabItem)?.Header?.ToString()?.Contains("MASTER LIBRARY", StringComparison.OrdinalIgnoreCase) == true;
        ApplyResponsiveLayout();
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
        MorePopup.IsOpen = false;
        if (MessageBox.Show(this, "Svuotare la cache degli header? Le immagini originali non verranno modificate.", "Pulisci cache", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _viewModel.ClearHeaderCache();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ScanAsync(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Scansione non completata", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _viewModel.SelectedNode = e.NewValue as ProjectTreeNode;

    private void Tree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Reveal((_viewModel.SelectedNode?.Frames.FirstOrDefault())?.Path);
    private void ReviewQueue_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Reveal(((sender as System.Windows.Controls.ListBox)?.SelectedItem as ReviewQueueItem)?.Frame.Path);
    private void MasterOrganizerGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Reveal((MasterOrganizerGrid.SelectedItem as MasterOrganizerItem)?.Frame.Path);

    private static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void TreeMark_Changed(object sender, RoutedEventArgs e) => _viewModel.RefreshManualSelection();

    private void ReviewQueue_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        _viewModel.SelectReviewItem((sender as System.Windows.Controls.ListBox)?.SelectedItem as ReviewQueueItem);

    private void AssignCandidate_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AssignReviewCandidate((sender as FrameworkElement)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Light);

    private void AssignCandidateGroup_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AssignReviewCandidate((sender as FrameworkElement)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Night);

    private void AssignCandidateSignature_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AssignReviewCandidate((sender as FrameworkElement)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Configuration);

    private void ChooseMasterOrganizerDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Destinazione della nuova Master Library" };
        if (dialog.ShowDialog(this) == true) _viewModel.MasterOrganizerDestination = dialog.FolderName;
    }

    private async void OrganizeMasterLibrary_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.OrganizeMasterLibraryAsync(); MessageBox.Show(this, _viewModel.MasterOrganizerStatus, "Master Library completata", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Organizzazione non completata", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ScanMasterLibraries_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ScanMasterLibrariesAsync(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Scansione librerie non completata", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
