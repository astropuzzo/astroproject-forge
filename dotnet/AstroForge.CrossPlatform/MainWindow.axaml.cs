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
using AstroForge.Core.Releases;

namespace AstroForge.CrossPlatform;

public sealed partial class MainWindow : Window
{
    private const string RepositoryUrl = "https://github.com/astropuzzo/astroproject-forge";
    private const string GuideUrl = RepositoryUrl + "/wiki";
    private const string IssueUrl = RepositoryUrl + "/issues/new?template=bug_report.yml";
    private readonly MainViewModel _viewModel = new();
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

    private static readonly FilePickerFileType AstroImages = new("Immagini astronomiche")
    {
        Patterns = ["*.fit", "*.fits", "*.fts", "*.xisf", "*.FIT", "*.FITS", "*.FTS", "*.XISF"]
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.UiLanguageChanged += (_, _) => ScheduleLocalization();
        _blinkTimer.Tick += BlinkTimer_Tick;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.HasSelection)) UpdateInspectorLayout();
            ScheduleLocalization();
        };
        SizeChanged += (_, args) => ApplyViewportWidth(args.NewSize.Width);
        KeyDown += Window_KeyDown;
        Opened += async (_, _) => { ApplyViewportWidth(ClientSize.Width); SelectDensity(); ScheduleLocalization(); await CheckUpdatesAsync(false); };
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

    private void ApplyNavigationLabels()
    {
        var labels = new[] { "Progetto", "Esportazione", "PixInsight WBPP", "Dati", "Controllo qualità", "Calibrazioni", "Libreria Master", "Diagnostica" };
        var tabs = WorkspaceTabs.Items.OfType<TabItem>().ToArray();
        for (var index = 0; index < Math.Min(labels.Length, tabs.Length); index++) tabs[index].Header = labels[index];
    }

    private void ScheduleLocalization()
    {
        if (_localizationPending) return;
        _localizationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _localizationPending = false;
            AvaloniaLocalizationAdapter.Apply(this, _viewModel.UiLanguage);
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
        else if (alt && e.Key >= Key.D1 && e.Key <= Key.D8)
        {
            WorkspaceTabs.SelectedIndex = (int)e.Key - (int)Key.D1;
            e.Handled = true;
        }
    }

    private void WorkspaceTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ScheduleLocalization();

    private void UpdateInspectorLayout()
    {
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
    private void OpenDiagnosticsTab_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; WorkspaceTabs.SelectedIndex = 7; _viewModel.RefreshDiagnostics(); }
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

    private void ReviewQueue_SelectionChanged(object? sender, SelectionChangedEventArgs e) => _viewModel.SelectReviewItem((sender as ListBox)?.SelectedItem as ReviewQueueItem);
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
