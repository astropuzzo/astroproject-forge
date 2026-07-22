using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AstroForge.App.ViewModels;

namespace AstroForge.CrossPlatform;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private CancellationTokenSource? _qualityCancellation;
    private CancellationTokenSource? _previewCancellation;
    private bool _sourcesVisible = true;
    private int _blinkIndex = -1;
    private double _qualityZoom = 1;

    private static readonly FilePickerFileType AstroImages = new("Immagini astronomiche")
    {
        Patterns = ["*.fit", "*.fits", "*.fts", "*.xisf", "*.FIT", "*.FITS", "*.FTS", "*.XISF"]
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _blinkTimer.Tick += BlinkTimer_Tick;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.HasSelection)) UpdateInspectorLayout();
        };
        SizeChanged += (_, args) => ApplyViewportWidth(args.NewSize.Width);
        Opened += (_, _) => ApplyViewportWidth(ClientSize.Width);
        Closing += (_, _) =>
        {
            _qualityCancellation?.Cancel();
            _previewCancellation?.Cancel();
            if (WorkspaceGrid.ColumnDefinitions[0].ActualWidth >= 190) _viewModel.SourcePanelWidth = WorkspaceGrid.ColumnDefinitions[0].ActualWidth;
            if (AnalysisGrid.ColumnDefinitions[2].ActualWidth >= 280) _viewModel.InspectorPanelWidth = AnalysisGrid.ColumnDefinitions[2].ActualWidth;
            _viewModel.SaveState();
        };
        ApplyCommandLine();
        UpdateInspectorLayout();
    }

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
        if (width < 1100 && _sourcesVisible)
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
    private void OpenOnboarding_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; _viewModel.OpenOnboarding(); }
    private void CompleteOnboarding_Click(object? sender, RoutedEventArgs e) => _viewModel.CompleteOnboarding();

    private async void Analyze_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri progetto AstroProject Forge", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await RunAsync("AF-PROJECT-OPEN-001", () => _viewModel.LoadProjectAsync(path));
    }

    private async void SaveProject_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salva progetto AstroProject Forge", SuggestedFileName = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? "Nuovo progetto.astroforge" : _viewModel.ProjectName + ".astroforge",
            DefaultExtension = "astroforge", FileTypeChoices = [new FilePickerFileType("Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
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
