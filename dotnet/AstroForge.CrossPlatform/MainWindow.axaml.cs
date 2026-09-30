using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AstroForge.App.Services;
using AstroForge.App.ViewModels;
using AstroForge.Core.Equipment;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;
using AstroForge.Core.Releases;
using AstroForge.CrossPlatform.Controls;
using AstroForge.CrossPlatform.ViewModels;
using Avalonia.VisualTree;

namespace AstroForge.CrossPlatform;

public sealed partial class MainWindow : Window
{
    private const string RepositoryUrl = "https://github.com/astropuzzo/astroproject-forge";
    private const string GuideUrl = RepositoryUrl + "/wiki";
    private const string IssueUrl = RepositoryUrl + "/issues/new?template=bug_report.yml";
    public const string SmokeTestArgument = "--smoke-test";
    private readonly MainViewModel _viewModel = new();
    private readonly ObservatoryViewModel _observatory;
    private readonly UpdateService _updateService = new();
    private ReleaseArtifact? _availableUpdate;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private CancellationTokenSource? _qualityCancellation;
    private CancellationTokenSource? _previewCancellation;
    private bool _sourcesVisible = true;
    private int _onboardingStep = 1;
    private int _blinkIndex = -1;
    private double _qualityZoom = 1;
    private bool _localizationPending;
    private int _lastWorkspaceIndex;

    private static readonly FilePickerFileType AstroImages = new("Immagini astronomiche")
    {
        Patterns = ["*.fit", "*.fits", "*.fts", "*.xisf", "*.FIT", "*.FITS", "*.FTS", "*.XISF"]
    };

    public MainWindow()
    {
        Motion.SetReduced(_viewModel.ReducedMotion);
        InitializeComponent();
        DataContext = _viewModel;
        _observatory = new ObservatoryViewModel(_viewModel);
        OverviewInstrumentCard.DataContext = _observatory;
        OverviewFilters.DataContext = _observatory;
        InstrumentPanel.DataContext = _observatory;
        _observatory.Filters.CollectionChanged += (_, _) => ScheduleLocalization();
        _viewModel.UiLanguageChanged += (_, _) => { ScheduleLocalization(); Dispatcher.UIThread.Post(RefreshCalibrationVisuals); };
        _blinkTimer.Tick += BlinkTimer_Tick;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.HasSelection)) UpdateInspectorLayout();
            else if (args.PropertyName == nameof(MainViewModel.ReducedMotion)) Motion.SetReduced(_viewModel.ReducedMotion);
            else if (args.PropertyName == nameof(MainViewModel.Analysis)) { RefreshCalibrationVisuals(); PreparePlanPreview(); ScheduleExportVisuals(); }
            else if (args.PropertyName is nameof(MainViewModel.ExportProgress) or nameof(MainViewModel.ExportState)) RefreshExportProgress();
        };
        CalibrationMapView.CellActivated += CalibrationMap_CellActivated;
        _viewModel.PlannedTreeRoots.CollectionChanged += (_, _) => ScheduleExportVisuals();
        SizeChanged += (_, args) => ApplyViewportWidth(args.NewSize.Width);
        KeyDown += Window_KeyDown;
        Opened += async (_, _) =>
        {
            ApplyViewportWidth(ClientSize.Width);
            SelectDensity();
            ScheduleLocalization();
            PlayFirstLight();
            if (!Environment.GetCommandLineArgs().Contains(SmokeTestArgument)) await CheckUpdatesAsync(false);
        };
        Closing += (_, _) =>
        {
            _qualityCancellation?.Cancel();
            _previewCancellation?.Cancel();
            if (WorkspaceGrid.ColumnDefinitions[0].ActualWidth >= 300) _viewModel.SourcePanelWidth = WorkspaceGrid.ColumnDefinitions[0].ActualWidth;
            if (AnalysisGrid.ColumnDefinitions[2].ActualWidth >= 280) _viewModel.InspectorPanelWidth = AnalysisGrid.ColumnDefinitions[2].ActualWidth;
            _viewModel.SaveState();
        };
        ApplyCommandLine();
        ApplyNavigationLabels();
        UpdateInspectorLayout();
    }

    /// <summary>
    /// Opens every workspace once and lets layout, bindings and localization settle, so CI catches
    /// startup and template crashes that a build alone cannot. Returns the process exit code.
    /// </summary>
    public async Task<int> RunSmokeTestAsync()
    {
        try
        {
            for (var index = 0; index < WorkspaceTabs.ItemCount; index++)
            {
                WorkspaceTabs.SelectedIndex = index;
                await Task.Delay(250);
            }
            SettingsPanel.IsVisible = true;
            await Task.Delay(250);
            SettingsPanel.IsVisible = false;
            WorkspaceTabs.SelectedIndex = 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Console.WriteLine($"SMOKE TEST PASSED · {WorkspaceTabs.ItemCount} workspaces opened");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SMOKE TEST FAILED · {exception}");
            return 1;
        }
    }

    private void ApplyNavigationLabels()
    {
        var tabs = WorkspaceTabs.Items.OfType<TabItem>().ToArray();
        if (tabs.Length < 9) return;
        var ordered = new[] { tabs[3], tabs[4], tabs[0], tabs[6], tabs[1], tabs[2], tabs[5], tabs[7], tabs[8] };
        WorkspaceTabs.Items.Clear();
        foreach (var tab in ordered) WorkspaceTabs.Items.Add(tab);
        var labels = new[] { "Panoramica", "Strumento", "1  Progetto", "2  Risolvi", "3  Esporta", "4  WBPP", "Qualità", "Master", "Diagnostica" };
        for (var index = 0; index < labels.Length; index++) ordered[index].Header = labels[index];
    }

    private void ScheduleLocalization()
    {
        if (_localizationPending) return;
        _localizationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _localizationPending = false;
            CanvasText.Language = _viewModel.UiLanguage;
            AvaloniaLocalizationAdapter.Apply(this, _viewModel.UiLanguage);
            InvalidateCanvasText(this);
        }, DispatcherPriority.Background);
    }

    private void SelectDensity()
    {
        DensitySelector.SelectedItem = DensitySelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _viewModel.UiDensity, StringComparison.Ordinal));
    }

    private void DensitySelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DensitySelector.SelectedItem is ComboBoxItem item)
        {
            _viewModel.UiDensity = item.Tag?.ToString() ?? "Comoda";
            _viewModel.SaveState();
        }
    }
    private void UiPreferenceChanged_Click(object? sender, RoutedEventArgs e) => _viewModel.SaveState();

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsPanel.IsVisible)
        {
            SettingsPanel.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            OpenUrl(GuideUrl);
            e.Handled = true;
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (control && e.Key == Key.O)
        {
            OpenProject_Click(sender, e);
            e.Handled = true;
        }
        else if (control && e.Key == Key.N)
        {
            await NewProjectCoreAsync();
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            await SaveProjectAsync(shift);
            e.Handled = true;
        }
        else if (control && e.Key == Key.Enter && _viewModel.CanAnalyzeProject)
        {
            await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());
            e.Handled = true;
        }
        else if (control && e.Key == Key.OemComma)
        {
            SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
            e.Handled = true;
        }
        else if (alt && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            WorkspaceTabs.SelectedIndex = (int)e.Key - (int)Key.D1;
            e.Handled = true;
        }
    }

    private void WorkspaceTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateInspectorLayout();
        // Tab content is created on first visit, after the last translation pass.
        if (IsLoaded) ScheduleLocalization();
        if (Backdrop is null || e.Source != WorkspaceTabs || WorkspaceTabs.SelectedIndex < 0) return;
        var index = WorkspaceTabs.SelectedIndex;
        if (index != _lastWorkspaceIndex) Backdrop.Warp(index > _lastWorkspaceIndex ? 1 : -1);
        _lastWorkspaceIndex = index;
    }

    private void OpenInstrument_Click(object? sender, RoutedEventArgs e) => WorkspaceTabs.SelectedItem = InstrumentTab;

    private void ConfirmFilter_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FilterSlotRow row) _observatory.Confirm(row);
    }

    private void ForgetFilter_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FilterSlotRow row) _observatory.Forget(row);
    }

    private static void InvalidateCanvasText(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
            if (visual is SpectrumBar or FieldOfViewView or NightsTimeline) visual.InvalidateVisual();
    }

    /// <summary>
    /// Opening sequence: the iris opens on the sky, then the interface comes into focus
    /// like a star pulled sharp by the focuser.
    /// </summary>
    private void PlayFirstLight()
    {
        if (Motion.Reduced) return;
        var blur = new BlurEffect { Radius = 18 };
        var scale = new ScaleTransform(1.02, 1.02);
        RootLayout.Opacity = 0;
        RootLayout.Effect = blur;
        RootLayout.RenderTransformOrigin = RelativePoint.Center;
        RootLayout.RenderTransform = scale;
        _ = FirstLight.PlayAsync(async () =>
        {
            await Motion.Tween(this, TimeSpan.FromMilliseconds(950), Motion.EaseOutExpo, t =>
            {
                RootLayout.Opacity = Math.Min(1, t * 1.4);
                blur.Radius = 18 * (1 - t);
                scale.ScaleX = scale.ScaleY = 1.02 - 0.02 * t;
            });
            RootLayout.Effect = null;
            RootLayout.RenderTransform = null;
            RootLayout.Opacity = 1;
        });
    }

    private void UpdateInspectorLayout()
    {
        // TabControl raises SelectionChanged while InitializeComponent is still assigning named controls.
        if (AnalysisGrid is null) return;
        AnalysisGrid.ColumnDefinitions[1].Width = new GridLength(_viewModel.HasSelection ? 5 : 0);
        AnalysisGrid.ColumnDefinitions[2].Width = new GridLength(_viewModel.HasSelection ? _viewModel.InspectorPanelWidth : 0);
    }

    private void ApplyViewportWidth(double width)
    {
        RootLayout.Width = width;
        WorkspaceGrid.Width = width;
        HeaderGrid.Width = Math.Max(760, width - 36);
        if (width < 1200 && _sourcesVisible)
        {
            _sourcesVisible = false;
            SourcesPanel.IsVisible = false;
            WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(0);
            WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(0);
        }
    }

    private void ApplyCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--source" && index + 1 < args.Length) _viewModel.AddSource(args[++index]);
            else if (args[index] == "--library" && index + 1 < args.Length) _viewModel.AddMasterLibrary(args[++index]);
        }
    }

    private async void AddSources_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Aggiungi cartelle FITS/XISF", AllowMultiple = true });
        foreach (var folder in folders) if (folder.TryGetLocalPath() is { } path) _viewModel.AddSource(path);
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importa immagini astronomiche", AllowMultiple = true, FileTypeFilter = [AstroImages] });
        foreach (var file in files) if (file.TryGetLocalPath() is { } path) _viewModel.AddSource(path);
    }

    private void RemoveSource_Click(object? sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is string path) _viewModel.RemoveSource(path);
    }

    private async void AddLibrary_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Aggiungi Master Library", AllowMultiple = true });
        foreach (var folder in folders) if (folder.TryGetLocalPath() is { } path) _viewModel.AddMasterLibrary(path);
    }

    private void RemoveLibrary_Click(object? sender, RoutedEventArgs e) => _viewModel.RemoveSelectedMasterLibrary();
    private void MoveLibraryUp_Click(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(-1);
    private void MoveLibraryDown_Click(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(1);
    private void RefreshLibraries_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshMasterLibraryStates();
    private void ClearAnalysisFilters_Click(object? sender, RoutedEventArgs e) { _viewModel.SearchText = ""; _viewModel.ShowIssuesOnly = false; }
    private void TreeMark_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshManualSelection();

    private void ToggleSources_Click(object? sender, RoutedEventArgs e)
    {
        _sourcesVisible = !_sourcesVisible;
        WorkspaceGrid.ColumnDefinitions[0].Width = _sourcesVisible ? new GridLength(_viewModel.SourcePanelWidth) : new GridLength(0);
        WorkspaceGrid.ColumnDefinitions[1].Width = _sourcesVisible ? new GridLength(5) : new GridLength(0);
        SourcesPanel.IsVisible = _sourcesVisible;
    }

    private void ToggleSettings_Click(object? sender, RoutedEventArgs e) => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    private void OpenOnboarding_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = false;
        _onboardingStep = 1;
        UpdateOnboarding();
        _viewModel.OpenOnboarding();
    }
    private void OpenGuide_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; OpenUrl(GuideUrl); }
    private void OpenRepository_Click(object? sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);
    private void ReportIssue_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; OpenUrl(IssueUrl); }
    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);

    private async Task CheckUpdatesAsync(bool requested)
    {
        try
        {
            UpdateStatus.Text = requested ? "Controllo in corso…" : "";
            var channel = Enum.TryParse<ReleaseChannel>(_viewModel.UpdateChannel, true, out var value) ? value : ReleaseChannel.Stable;
            var decision = await _updateService.CheckChannelAsync(ReleaseIdentity.Version, channel);
            _availableUpdate = decision.IsAvailable ? decision.Manifest.Installer : null;
            UpdateStatus.Text = decision.Reason;
            UpdateButton.Content = decision.IsAvailable ? $"Installa {decision.Manifest.Version}" : "Controlla aggiornamenti";
            if (!requested && decision.IsAvailable)
            {
                SettingsPanel.IsVisible = true;
                UpdateButton.Focus();
            }
        }
        catch (Exception exception)
        {
            if (requested) UpdateStatus.Text = "Impossibile controllare gli aggiornamenti.";
            _viewModel.RecordError("AF-UPDATE-CHECK-001", exception);
        }
    }

    private async void InstallUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckUpdatesAsync(true); return; }
        try
        {
            UpdateButton.IsEnabled = false;
            var destination = Path.Combine(Path.GetTempPath(), _availableUpdate.FileName);
            var progress = new Progress<double>(value =>
            {
                UpdateProgress.Value = value;
                UpdateStatus.Text = $"Download {value:0}%";
            });
            await _updateService.DownloadVerifiedAsync(_availableUpdate, destination, progress);
            UpdateStatus.Text = OperatingSystem.IsMacOS()
                ? "Download completato. Apri il disco e sostituisci l’app in Applicazioni."
                : "Download completato. Segui l’installazione per sostituire la versione corrente.";
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            UpdateStatus.Text = "Aggiornamento non completato.";
            _viewModel.RecordError("AF-UPDATE-INSTALL-001", exception);
        }
        finally { UpdateButton.IsEnabled = true; }
    }
    private void OpenDiagnosticsTab_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; WorkspaceTabs.SelectedItem = DiagnosticsTab; _viewModel.RefreshDiagnostics(); }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OnboardingAddLibrary_Click(object? sender, RoutedEventArgs e) => AddLibrary_Click(sender, e);
    private void OnboardingAddSources_Click(object? sender, RoutedEventArgs e) => AddSources_Click(sender, e);
    private void OnboardingAddFiles_Click(object? sender, RoutedEventArgs e) => AddFiles_Click(sender, e);
    private void OnboardingEnglish_Click(object? sender, RoutedEventArgs e) { _viewModel.UiLanguage = UiLocalization.English; UpdateOnboarding(); }
    private void OnboardingItalian_Click(object? sender, RoutedEventArgs e) { _viewModel.UiLanguage = UiLocalization.Italian; UpdateOnboarding(); }
    private void OnboardingSkip_Click(object? sender, RoutedEventArgs e) => _viewModel.CompleteOnboarding();
    private void OnboardingBack_Click(object? sender, RoutedEventArgs e)
    {
        _onboardingStep = Math.Max(1, _onboardingStep - 1);
        UpdateOnboarding();
    }
    private async void OnboardingNext_Click(object? sender, RoutedEventArgs e)
    {
        if (_onboardingStep == 5)
        {
            _viewModel.CompleteOnboarding();
            if (_viewModel.CanAnalyzeProject)
                await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());
            return;
        }
        _onboardingStep++;
        UpdateOnboarding();
    }

    private void UpdateOnboarding()
    {
        OnboardingStep1.IsVisible = _onboardingStep == 1;
        OnboardingStep2.IsVisible = _onboardingStep == 2;
        OnboardingStep3.IsVisible = _onboardingStep == 3;
        OnboardingStep4.IsVisible = _onboardingStep == 4;
        OnboardingStep5.IsVisible = _onboardingStep == 5;
        OnboardingProgress.Text = $"{_onboardingStep} / 5";
        OnboardingBackButton.IsVisible = _onboardingStep > 1;
        OnboardingNextButton.Content = _onboardingStep switch
        {
            1 => "Continua",
            2 => "Inizia",
            3 when _viewModel.MasterLibraries.Count == 0 => "Continua senza libreria",
            5 when _viewModel.CanAnalyzeProject => "Analizza ora",
            5 => "Vai al progetto",
            _ => "Continua"
        };
        ScheduleLocalization();
    }

    private async void Analyze_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var english = _viewModel.UiLanguage == UiLocalization.English;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = english ? "Open AstroProject Forge project" : "Apri progetto AstroProject Forge", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(english ? "AstroProject Forge project" : "Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && await ConfirmProjectReplacementAsync(opening: true)) await RunAsync("AF-PROJECT-OPEN-001", () => _viewModel.LoadProjectAsync(path));
    }

    private async void SaveProject_Click(object? sender, RoutedEventArgs e) => await SaveProjectAsync(false);
    private async void SaveProjectAs_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; await SaveProjectAsync(true); }
    private async void NewProject_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; await NewProjectCoreAsync(); }

    private async Task NewProjectCoreAsync()
    {
        if (await ConfirmProjectReplacementAsync(opening: false)) _viewModel.NewProject();
    }

    private async Task<bool> ConfirmProjectReplacementAsync(bool opening)
    {
        if (!_viewModel.HasProjectContent) return true;
        var english = _viewModel.UiLanguage == UiLocalization.English;
        var title = english ? (opening ? "Open project" : "New project") : (opening ? "Apri progetto" : "Nuovo progetto");
        var question = english ? (opening ? "Open the selected project?" : "Create a new project?") : (opening ? "Aprire il progetto selezionato?" : "Creare un nuovo progetto?");
        var detail = english
            ? "Unsaved project data will be discarded. Source files and Master Libraries will not be changed."
            : "I dati del progetto non salvati verranno persi. I file sorgente e le Master Library non verranno modificati.";
        var cancel = new Button { Content = english ? "Cancel" : "Annulla", MinWidth = 100 };
        var confirm = new Button { Content = english ? "Continue" : "Continua", MinWidth = 100, Classes = { "primary" } };
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(confirm);
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = question, FontSize = 21, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(actions);
        var dialog = new Window { Title = title, Width = 470, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task SaveProjectAsync(bool saveAs)
    {
        if (!saveAs && !string.IsNullOrWhiteSpace(_viewModel.CurrentProjectFile))
        {
            Try("AF-PROJECT-SAVE-001", () => _viewModel.SaveProject(_viewModel.CurrentProjectFile));
            return;
        }

        var english = _viewModel.UiLanguage == UiLocalization.English;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = english ? "Save AstroProject Forge project" : "Salva progetto AstroProject Forge", SuggestedFileName = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? (english ? "New project.astroforge" : "Nuovo progetto.astroforge") : _viewModel.ProjectName + ".astroforge",
            DefaultExtension = "astroforge", FileTypeChoices = [new FilePickerFileType(english ? "AstroProject Forge project" : "Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
        });
        if (file?.TryGetLocalPath() is { } path) Try("AF-PROJECT-SAVE-001", () => _viewModel.SaveProject(path));
    }

    private async void ChooseDestination_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Destinazione progetto", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _viewModel.DestinationPath = path;
    }

    private void BuildPlan_Click(object? sender, RoutedEventArgs e) => Try("AF-PLAN-001", _viewModel.BuildPlan);
    private async void ExportPreflight_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-EXPORT-PREFLIGHT-001", () => _viewModel.RunExportPreflightAsync());
    private async void Export_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-EXPORT-001", () => _viewModel.ExportAsync());
    private void PauseExport_Click(object? sender, RoutedEventArgs e) => _viewModel.PauseExport();
    private void ResumeExport_Click(object? sender, RoutedEventArgs e) => _viewModel.ResumeExport();
    private void CancelExport_Click(object? sender, RoutedEventArgs e) => _viewModel.CancelExport();

    private async void ExportStatistics_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Esporta statistiche", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) Try("AF-STATS-001", () => _viewModel.ExportStatistics(path));
    }

    private async void AnalyzeQuality_Click(object? sender, RoutedEventArgs e)
    {
        StopBlink();
        _qualityCancellation?.Cancel();
        _qualityCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.AnalyzeQualityAsync(_qualityCancellation.Token);
            await RefreshQualityPreviewAsync(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-001", exception); }
        finally { _qualityCancellation?.Dispose(); _qualityCancellation = null; }
    }

    private async void AnalyzeAllQuality_Click(object? sender, RoutedEventArgs e)
    {
        StopBlink();
        _qualityCancellation?.Cancel();
        _qualityCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.AnalyzeAllQualityAsync(_qualityCancellation.Token);
            await RefreshQualityPreviewAsync(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-ALL-001", exception); }
        finally { _qualityCancellation?.Dispose(); _qualityCancellation = null; }
    }
    private void CancelQuality_Click(object? sender, RoutedEventArgs e) => _qualityCancellation?.Cancel();

    private void ExcludeQualitySuspects_Click(object? sender, RoutedEventArgs e) => _viewModel.ExcludeQualitySuspects();
    private void ExcludeSelectedQuality_Click(object? sender, RoutedEventArgs e) => _viewModel.ExcludeSelectedQualityFrames(QualityGrid.SelectedItems.Cast<QualityFrameRow>().ToArray());
    private void RestoreQuality_Click(object? sender, RoutedEventArgs e) => _viewModel.RestoreAllQualityFrames();

    private async void QualityGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rows = QualityGrid.SelectedItems.Cast<QualityFrameRow>().ToArray();
        _viewModel.SetQualitySelection(rows);
        if (!_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(true);
    }

    private async void QualitySeries_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        StopBlink();
        if (!_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(true);
    }

    private void QualityThreshold_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value") QualityChart?.InvalidateVisual();
    }

    private async void QualityChart_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
        QualityGrid.SelectedItem = _viewModel.SelectedQualityFrame;
        await RefreshQualityPreviewAsync(true);
    }

    private async void QualityPreviewOptions_Click(object? sender, RoutedEventArgs e) => await RefreshQualityPreviewAsync();
    private async void QualityStretch_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value" && IsLoaded) await RefreshQualityPreviewAsync();
    }

    private void Blink_Click(object? sender, RoutedEventArgs e)
    {
        if (_blinkTimer.IsEnabled) { StopBlink(); return; }
        _blinkIndex = -1;
        _blinkTimer.Start();
        BlinkButton.Content = "Ferma Blink";
        BlinkTimer_Tick(this, EventArgs.Empty);
    }
    private void ToggleQualityExclusion_Click(object? sender, RoutedEventArgs e) => _viewModel.ToggleSelectedQualityExclusion();
    private async void QualityFullResolution_Click(object? sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try { await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token, true); SetZoom(1); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-FULLRES-001", exception); }
    }

    private async Task RefreshQualityPreviewAsync(bool fit = false)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token);
            if (fit) ZoomFit();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-PREVIEW-001", exception); }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => SetZoom(_qualityZoom * 1.25);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => SetZoom(_qualityZoom / 1.25);
    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => ZoomFit();
    private void ZoomFit()
    {
        var bitmap = _viewModel.SelectedQualityFrame?.Preview;
        if (bitmap is null || QualityPreviewScroll.Bounds.Width <= 20 || QualityPreviewScroll.Bounds.Height <= 20) return;
        SetZoom(Math.Clamp(Math.Min((QualityPreviewScroll.Bounds.Width - 20) / bitmap.Size.Width, (QualityPreviewScroll.Bounds.Height - 20) / bitmap.Size.Height), .05, 16));
    }
    private void SetZoom(double value)
    {
        _qualityZoom = Math.Clamp(value, .05, 16);
        if (QualityPreviewImage.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = _qualityZoom;
            scale.ScaleY = _qualityZoom;
        }
    }

    private async void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        var rows = QualityGrid.SelectedItems.Cast<QualityFrameRow>().Where(row => row.Preview is not null).ToArray();
        if (rows.Length < 2) rows = _viewModel.QualityFrames.Where(row => row.IsSuspect && row.Preview is not null).ToArray();
        if (rows.Length == 0) { StopBlink(); return; }
        _blinkIndex = (_blinkIndex + 1) % rows.Length;
        QualityGrid.SelectedItem = rows[_blinkIndex];
        _viewModel.SelectedQualityFrame = rows[_blinkIndex];
        await RefreshQualityPreviewAsync();
    }
    private void StopBlink() { _blinkTimer.Stop(); _blinkIndex = -1; if (BlinkButton is not null) BlinkButton.Content = "Avvia Blink"; }

    private void Tree_DoubleTapped(object? sender, TappedEventArgs e) => Reveal(_viewModel.SelectedNode?.Frames.FirstOrDefault()?.Path);
    private void QualityGrid_DoubleTapped(object? sender, TappedEventArgs e) => Reveal(_viewModel.SelectedQualityFrame?.Path);
    private void ReviewQueue_DoubleTapped(object? sender, TappedEventArgs e) => Reveal((sender as ListBox)?.SelectedItem is ReviewQueueItem item ? item.Frame.Path : null);
    private void MasterOrganizer_DoubleTapped(object? sender, TappedEventArgs e) => Reveal((sender as DataGrid)?.SelectedItem is MasterOrganizerItem item ? item.Frame.Path : null);

    /// <summary>Feeds the calibration map and the WBPP flow from the latest analysis.</summary>
    private void RefreshCalibrationVisuals()
    {
        var analysis = _viewModel.Analysis;
        var colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        Color ColourOf(FrameMetadata frame)
        {
            var raw = frame.RawFilterName ?? frame.FilterName.Value ?? "";
            var camera = PhysicalFilterResolver.CameraKey(frame);
            if (colours.TryGetValue($"{camera}|{raw}", out var known)) return known;
            var identity = FilterRecognizer.Recognize(raw.Length == 0 ? null : raw, EquipmentRecognizer.Camera(frame.Camera.Value).IsColor, _viewModel.WheelProfileFor(camera));
            return colours[$"{camera}|{raw}"] = SpectrumColors.Glass(SpectrumColors.BandsOf(identity), identity.Kind);
        }
        CalibrationMapView.Rows = CalibrationRow.Build(analysis, ColourOf);
        PipelineFlowView.Keywords = _viewModel.WbppKeywords.Select(keyword => $"{keyword.Keyword} · PRE {keyword.Pre}").ToList();
        PipelineFlowView.Streams = analysis is null ? [] : analysis.Lights
            .GroupBy(item => item.Light.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
            .Select(group => new PipelineStream(group.Key, ColourOf(group.First().Light), group.Count(), group.Sum(item => item.Light.ExposureSeconds.Value ?? 0)))
            .OrderByDescending(stream => stream.Colour.R - stream.Colour.B)
            .ToList();
    }

    private bool _exportVisualsQueued;

    private void ScheduleExportVisuals()
    {
        if (_exportVisualsQueued) return;
        _exportVisualsQueued = true;
        Dispatcher.UIThread.Post(() => { _exportVisualsQueued = false; RefreshExportVisuals(); });
    }

    /// <summary>A calibrated project with a name and a destination gets its real plan straight away, so Export shows the true folders.</summary>
    private void PreparePlanPreview()
    {
        if (_viewModel.Analysis?.Ready != true || string.IsNullOrWhiteSpace(_viewModel.ProjectName) || string.IsNullOrWhiteSpace(_viewModel.DestinationPath) || _viewModel.HasExportPlan) return;
        try { _viewModel.BuildPlan(); }
        catch (Exception) { /* The Export tab still shows the expected layout. */ }
    }

    private void RefreshExportVisuals()
    {
        var name = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? CanvasText.T("Progetto") : _viewModel.ProjectName;
        var colours = new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);
        Color? FilterColour(IReadOnlyList<FrameMetadata> frames)
        {
            var filters = frames.Select(frame => frame.FilterName.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (frames.Count == 0 || filters.Count != 1 || filters[0] is null) return null;
            if (colours.TryGetValue(filters[0]!, out var known)) return known;
            var frame = frames[0];
            var raw = frame.RawFilterName ?? frame.FilterName.Value ?? "";
            var camera = PhysicalFilterResolver.CameraKey(frame);
            var identity = FilterRecognizer.Recognize(raw, EquipmentRecognizer.Camera(frame.Camera.Value).IsColor, _viewModel.WheelProfileFor(camera));
            return colours[filters[0]!] = SpectrumColors.Glass(SpectrumColors.BandsOf(identity), identity.Kind);
        }

        ExportNode FromPlan(ProjectTreeNode node, int depth) => new(node.Name, node.Count, depth >= 1 && node.Name.StartsWith("FILTER_", StringComparison.Ordinal) || depth >= 2 ? FilterColour(node.Frames) : null,
            depth >= 3 ? [] : node.Children.Where(child => !child.IsLeaf || child.Icon != "·").Select(child => FromPlan(child, depth + 1)).ToList());

        if (_viewModel.PlannedTreeRoots.Count > 0)
        {
            ExportMapView.IsPreview = false;
            ExportMapView.Root = new ExportNode(name, _viewModel.PlannedTreeRoots.Sum(node => node.Count), null, _viewModel.PlannedTreeRoots.Select(node => FromPlan(node, 1)).ToList());
        }
        else if (_viewModel.Analysis is { Lights.Count: > 0 } analysis)
        {
            // Before a plan exists, show the layout the export will follow: filters, then nights.
            ExportNode Branch(string role, IEnumerable<FrameMetadata> frames) => new(role, frames.Count(), null, frames
                .GroupBy(frame => frame.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
                .Select(filter => new ExportNode($"FILTER_{filter.Key}", filter.Count(), FilterColour(filter.ToList()), filter
                    .GroupBy(frame => frame.SessionId.Value ?? "—").OrderBy(night => night.Key, StringComparer.Ordinal)
                    .Select(night => new ExportNode($"NIGHT_{night.Key}", night.Count(), null, [])).ToList()))
                .ToList());
            var lights = analysis.Lights.Select(item => item.Light).ToList();
            var flats = analysis.Lights.SelectMany(item => item.FlatGroup?.Frames ?? []).Distinct().ToList();
            var roles = new List<ExportNode> { Branch("Light", lights) };
            if (flats.Count > 0) roles.Add(Branch("Flat", flats));
            var darks = analysis.Lights.Select(item => item.Dark.Selected?.Frame).OfType<FrameMetadata>().Distinct().Count();
            var biases = analysis.Lights.Select(item => item.Bias.Selected?.Frame).OfType<FrameMetadata>().Distinct().Count();
            if (darks > 0) roles.Add(new ExportNode("Dark", darks, null, []));
            if (biases > 0) roles.Add(new ExportNode("Bias", biases, null, []));
            ExportMapView.IsPreview = true;
            ExportMapView.Root = new ExportNode(name, roles.Sum(role => role.Files), null, roles);
        }
        else ExportMapView.Root = null;
        RefreshExportProgress();
    }

    private void RefreshExportProgress()
    {
        ExportMapView.Progress = _viewModel.ExportProgress / 100;
        ExportMapView.IsRunning = _viewModel.ExportState == ExportRunState.Running;
        ExportPercentText.Text = _viewModel.ExportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Completed or ExportRunState.Cancelled
            ? $"{_viewModel.ExportProgress:0}%" : "";
    }

    private void CalibrationMap_CellActivated(object? sender, (CalibrationRow Row, CalibrationCell Cell) activated)
    {
        var lights = activated.Row.Lights.ToHashSet();
        var item = _viewModel.ReviewQueue.FirstOrDefault(entry => entry.Calibration == activated.Cell.Calibration && entry.Targets.Any(lights.Contains));
        if (item is null) return;
        ReviewList.SelectedItem = item;
        ReviewList.ScrollIntoView(item);
    }

    private void ReviewQueue_SelectionChanged(object? sender, SelectionChangedEventArgs e) => _viewModel.SelectReviewItem((sender as ListBox)?.SelectedItem as ReviewQueueItem);
    private void EditReviewMetadata_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SelectReviewItem((sender as Control)?.DataContext as ReviewQueueItem);
        WorkspaceTabs.SelectedItem = ProjectTab;
        UpdateInspectorLayout();
    }
    private void AssignLight_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Light);
    private void AssignNight_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Night);
    private void AssignSession_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Configuration);

    private async void ScanMasterLibraries_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-SCAN-001", () => _viewModel.ScanMasterLibrariesAsync());
    private async void PreviewMasterOrganizer_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-PREFLIGHT-001", () => _viewModel.PreviewMasterOrganizerAsync());
    private async void OrganizeMasterLibrary_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-ORGANIZE-001", () => _viewModel.OrganizeMasterLibraryAsync());
    private async void RollbackMasterOrganizer_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-ROLLBACK-001", () => _viewModel.RollbackMasterOrganizerAsync());
    private async void ChooseMasterOrganizerDestination_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Destinazione nuova Master Library", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _viewModel.MasterOrganizerDestination = path;
    }

    private void ClearCache_Click(object? sender, RoutedEventArgs e) => _viewModel.ClearHeaderCache();
    private void OpenWbppInstance_Click(object? sender, RoutedEventArgs e) => Try("AF-WBPP-001", () =>
        AstroForge.Core.Wbpp.PixInsightLauncher.Open(_viewModel.GenerateWbppInstance()));
    private void RefreshDiagnostics_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshDiagnostics();
    private async void RestoreRecovery_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-RECOVERY-001", () => _viewModel.RestoreRecoveryAsync());
    private void DiscardRecovery_Click(object? sender, RoutedEventArgs e) => _viewModel.DiscardRecovery();
    private async void ExportSupport_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Esporta diagnostica", SuggestedFileName = $"AstroProjectForge-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip", DefaultExtension = "zip" });
        if (file?.TryGetLocalPath() is { } path) await RunAsync("AF-SUPPORT-001", () => _viewModel.ExportSupportBundleAsync(path));
    }

    private static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var info = new ProcessStartInfo { UseShellExecute = true };
        if (OperatingSystem.IsWindows()) { info.FileName = "explorer.exe"; info.ArgumentList.Add($"/select,{path}"); }
        else if (OperatingSystem.IsMacOS()) { info.FileName = "open"; info.ArgumentList.Add("-R"); info.ArgumentList.Add(path); }
        else { info.FileName = "xdg-open"; info.ArgumentList.Add(Path.GetDirectoryName(path)!); }
        Process.Start(info);
    }

    private async Task RunAsync(string code, Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record(code, exception); }
    }
    private async Task RunAsync(string code, Func<Task<string>> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record(code, exception); }
    }
    private void Try(string code, Action operation) { try { operation(); } catch (Exception exception) { Record(code, exception); } }
    private void Try(string code, Func<string> operation) { try { _ = operation(); } catch (Exception exception) { Record(code, exception); } }
    private void Record(string code, Exception exception) => _viewModel.RecordError(code, exception);
}
