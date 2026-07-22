using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using AstroForge.App.ViewModels;
using AstroForge.App.Services;
using AstroForge.Core.Releases;

namespace AstroForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _sourcesVisible = true;
    private bool _inspectorVisible = true;
    private bool _inspectorContextAvailable = true;
    private int _onboardingStep = 1;
    private readonly UpdateService _updateService = new();
    private ReleaseManifest? _availableUpdate;
    private CancellationTokenSource? _qualityCancellation;
    private CancellationTokenSource? _previewCancellation;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private int _blinkIndex;
    private double _sourcePanelWidth = 260;
    private double _inspectorPanelWidth = 390;
    private double _qualityZoom = 1;
    private bool _qualityPreviewPanning;
    private Point _qualityPanStart;
    private double _qualityPanHorizontal;
    private double _qualityPanVertical;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _sourcePanelWidth = _viewModel.SourcePanelWidth;
        _inspectorPanelWidth = _viewModel.InspectorPanelWidth;
        Closing += (_, _) => { _qualityCancellation?.Cancel(); _previewCancellation?.Cancel(); _viewModel.SaveState(); };
        Loaded += MainWindow_Loaded;
        _blinkTimer.Tick += BlinkTimer_Tick;
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

    private void ClearAnalysisFilters_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchText = "";
        _viewModel.ShowIssuesOnly = false;
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
        var narrowHeader = ActualWidth < 1120;
        var showInspector = _inspectorVisible && _inspectorContextAvailable;
        SourcesColumn.MinWidth = _sourcesVisible ? 190 : 0;
        SourcesColumn.Width = _sourcesVisible ? new GridLength(Math.Clamp(_sourcePanelWidth, 190, 520)) : new GridLength(0);
        SourceSplitterColumn.Width = _sourcesVisible ? new GridLength(5) : new GridLength(0);
        SourceSplitter.Visibility = _sourcesVisible ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.MinWidth = showInspector ? 280 : 0;
        InspectorColumn.Width = showInspector ? new GridLength(Math.Clamp(_inspectorPanelWidth, 280, 680)) : new GridLength(0);
        InspectorSplitterColumn.Width = showInspector ? new GridLength(5) : new GridLength(0);
        InspectorSplitter.Visibility = showInspector ? Visibility.Visible : Visibility.Collapsed;
        InspectorToggleButton.Visibility = _inspectorContextAvailable ? Visibility.Visible : Visibility.Collapsed;
        BrandText.Visibility = narrowHeader ? Visibility.Collapsed : Visibility.Visible;
        ProjectStatusBadge.Visibility = ActualWidth < 1420 ? Visibility.Collapsed : Visibility.Visible;
        SourceToggleButton.Content = narrowHeader ? "☰" : "Sorgenti";
        SourceToggleButton.Width = narrowHeader ? 44 : double.NaN;
        SourceToggleButton.Padding = narrowHeader ? new Thickness(0) : new Thickness(13, 0, 13, 0);
        InspectorToggleButton.Content = narrowHeader ? "◫" : "Inspector";
        InspectorToggleButton.Width = narrowHeader ? 44 : double.NaN;
        InspectorToggleButton.Padding = narrowHeader ? new Thickness(0) : new Thickness(13, 0, 13, 0);
        OpenProjectButton.Content = "Apri";
        SaveProjectButton.Content = "Salva";
        SettingsButton.Content = narrowHeader ? "⚙" : "Impostazioni";
        SettingsButton.Width = narrowHeader ? 44 : double.NaN;
        SettingsButton.Padding = narrowHeader ? new Thickness(0) : new Thickness(13, 0, 13, 0);
    }

    private void PanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_sourcesVisible && SourcesColumn.ActualWidth >= 190) _sourcePanelWidth = SourcesColumn.ActualWidth;
        if (_inspectorVisible && _inspectorContextAvailable && InspectorColumn.ActualWidth >= 280) _inspectorPanelWidth = InspectorColumn.ActualWidth;
        _viewModel.SourcePanelWidth = _sourcePanelWidth;
        _viewModel.InspectorPanelWidth = _inspectorPanelWidth;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void More_Click(object sender, RoutedEventArgs e) { SettingsPopup.IsOpen = false; MorePopup.IsOpen = !MorePopup.IsOpen; }
    private void CloseMore_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = false;
    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        _viewModel.RefreshDiagnostics();
        DiagnosticsOverlay.Visibility = Visibility.Visible;
    }
    private void CloseDiagnostics_Click(object sender, RoutedEventArgs e) => DiagnosticsOverlay.Visibility = Visibility.Collapsed;
    private void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshDiagnostics();
    private void Settings_Click(object sender, RoutedEventArgs e) { MorePopup.IsOpen = false; SettingsPopup.IsOpen = !SettingsPopup.IsOpen; }
    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        SettingsPopup.IsOpen = false;
        AboutOverlay.Visibility = Visibility.Visible;
    }
    private void CloseAbout_Click(object sender, RoutedEventArgs e) => AboutOverlay.Visibility = Visibility.Collapsed;
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool interactive)
    {
        try
        {
            _viewModel.UpdateStatus = $"Controllo canale {_viewModel.UpdateChannel}…";
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            var channel = Enum.Parse<ReleaseChannel>(_viewModel.UpdateChannel, true);
            var decision = await _updateService.CheckAsync(UpdateService.FeedUri(channel), ReleaseIdentity.Version, channel);
            _viewModel.UpdateStatus = decision.Reason;
            _availableUpdate = decision.IsAvailable ? decision.Manifest : null;
            DownloadUpdateButton.Visibility = decision.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (interactive && !decision.IsAvailable)
                MessageBox.Show(this, decision.Reason, "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (HttpRequestException)
        {
            _availableUpdate = null;
            _viewModel.UpdateStatus = "Feed non raggiungibile o canale non ancora pubblicato";
            if (interactive) MessageBox.Show(this, "Il canale selezionato non è raggiungibile o non è ancora stato pubblicato. Nessun file è stato scaricato.", "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _availableUpdate = null;
            _viewModel.UpdateStatus = "Manifest rifiutato: integrità o formato non validi";
            if (interactive) ShowError("AF-UPDATE-001", "Controllo aggiornamenti non completato", exception, MessageBoxImage.Warning);
        }
    }
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        var artifact = _availableUpdate.Installer;
        var dialog = new SaveFileDialog { Title = "Salva installer verificato", FileName = artifact.FileName, Filter = "Installer Windows (*.exe)|*.exe", AddExtension = true, DefaultExt = ".exe" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _viewModel.UpdateStatus = "Download e verifica SHA-256…";
            var progress = new Progress<double>(value => _viewModel.UpdateStatus = $"Download verificato · {value:0}%");
            var path = await _updateService.DownloadVerifiedAsync(artifact, dialog.FileName, progress);
            _viewModel.UpdateStatus = "Installer verificato e pronto · avvio manuale";
            MessageBox.Show(this, $"Installer scaricato e verificato.\n\n{path}\n\nForge non lo avvierà automaticamente: chiudi il progetto e avvialo quando vuoi procedere.", "Aggiornamento verificato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { ShowError("AF-UPDATE-002", "Download rifiutato", exception, MessageBoxImage.Error); }
    }
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyUiPreferences();
        UpdateWorkspaceContext();
        if (!_viewModel.ReducedMotion)
        {
            RootSurface.Opacity = 0;
            if (RootSurface.RenderTransform is System.Windows.Media.TranslateTransform transform) transform.Y = 8;
            RootSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            if (RootSurface.RenderTransform is System.Windows.Media.TranslateTransform translate)
                translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        if (_viewModel.CheckForUpdates) await CheckUpdatesAsync(false);
    }

    private void WorkspaceTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateWorkspaceContext();
            AnimateWorkspaceTransition();
        }), DispatcherPriority.Loaded);
    }

    private void UpdateWorkspaceContext()
    {
        var selectedTab = WorkspaceTabs.SelectedItem as System.Windows.Controls.TabItem;
        _inspectorContextAvailable = string.Equals(selectedTab?.Tag?.ToString(), "Inspector", StringComparison.Ordinal);
        if (!string.Equals(selectedTab?.Header?.ToString(), "Qualità", StringComparison.Ordinal)) StopBlink();
        ApplyResponsiveLayout();
    }

    private void AnimateWorkspaceTransition()
    {
        if (_viewModel.ReducedMotion || WorkspaceTabs.SelectedContent is not FrameworkElement content) return;
        var translate = content.RenderTransform as System.Windows.Media.TranslateTransform ?? new System.Windows.Media.TranslateTransform();
        content.RenderTransform = translate;
        content.Opacity = 0;
        translate.Y = 7;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Seleziona destinazione progetto", InitialDirectory = Directory.Exists(_viewModel.DestinationPath) ? _viewModel.DestinationPath : null };
        if (dialog.ShowDialog(this) == true) _viewModel.DestinationPath = dialog.FolderName;
    }

    private void BuildPlan_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.BuildPlan(); }
        catch (Exception exception) { ShowError("AF-PLAN-001", "Anteprima non disponibile", exception, MessageBoxImage.Warning); }
    }

    private async void ExportPreflight_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.RunExportPreflightAsync(); }
        catch (Exception exception) { ShowError("AF-EXPORT-PREFLIGHT-001", "Preflight non completato", exception, MessageBoxImage.Warning); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _viewModel.ExportAsync();
            MessageBox.Show(this, $"Progetto creato e verificato.\n\n{path}", "Esportazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowError("AF-EXPORT-001", "Esportazione non completata", exception, MessageBoxImage.Error); }
    }

    private void PauseExport_Click(object sender, RoutedEventArgs e) => _viewModel.PauseExport();
    private void ResumeExport_Click(object sender, RoutedEventArgs e) => _viewModel.ResumeExport();
    private void CancelExport_Click(object sender, RoutedEventArgs e) => _viewModel.CancelExport();

    private async void AnalyzeQuality_Click(object sender, RoutedEventArgs e)
    {
        StopBlink();
        _qualityCancellation?.Dispose();
        _qualityCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.AnalyzeQualityAsync(_qualityCancellation.Token);
            QualityChart.Refresh();
            await RefreshQualityPreviewAsync(fitToViewport: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowError("AF-QUALITY-001", "Analisi qualità non completata", exception, MessageBoxImage.Warning); }
        finally { _qualityCancellation.Dispose(); _qualityCancellation = null; }
    }
    private void CancelQuality_Click(object sender, RoutedEventArgs e) => _qualityCancellation?.Cancel();
    private void ExcludeQualitySuspects_Click(object sender, RoutedEventArgs e) => _viewModel.ExcludeQualitySuspects();
    private void ExcludeSelectedQuality_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ExcludeSelectedQualityFrames(QualityGrid.SelectedItems.OfType<QualityFrameRow>().ToArray());
    private void RestoreQuality_Click(object sender, RoutedEventArgs e) => _viewModel.RestoreAllQualityFrames();
    private void ToggleQualityExclusion_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleSelectedQualityExclusion();
    private void BlinkQuality_Click(object sender, RoutedEventArgs e)
    {
        if (_blinkTimer.IsEnabled) { StopBlink(); return; }
        if (_viewModel.QualityFrames.Count == 0) return;
        _blinkIndex = -1; _blinkTimer.Start(); BlinkButton.Content = "Ferma Blink"; BlinkTimer_Tick(this, EventArgs.Empty);
    }
    private async void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        var selected = QualityGrid.SelectedItems.OfType<QualityFrameRow>().Where(item => item.Preview is not null).ToArray();
        var rows = selected.Length > 1 ? selected : _viewModel.QualityFrames.Where(item => item.IsSuspect && item.Preview is not null).ToArray();
        if (rows.Length == 0) rows = _viewModel.QualityFrames.Where(item => item.Preview is not null).ToArray();
        if (rows.Length == 0) { StopBlink(); return; }
        _blinkIndex = (_blinkIndex + 1) % rows.Length;
        _viewModel.SelectedQualityFrame = rows[_blinkIndex];
        QualityGrid.ScrollIntoView(rows[_blinkIndex]);
        await RefreshQualityPreviewAsync();
    }
    private void StopBlink() { _blinkTimer.Stop(); if (BlinkButton is not null) BlinkButton.Content = "Avvia Blink"; }

    private async void QualityGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var rows = QualityGrid.SelectedItems.OfType<QualityFrameRow>().ToArray();
        _viewModel.SetQualitySelection(rows);
        QualityChart?.Refresh();
        if (!_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(fitToViewport: true);
    }

    private void QualityGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Reveal(_viewModel.SelectedQualityFrame?.Path);
    private void QualityThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => QualityChart?.Refresh();
    private async void QualitySeries_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        StopBlink();
        QualityGrid?.Items.SortDescriptions.Clear();
        QualityChart?.Refresh();
        if (IsLoaded && !_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(fitToViewport: true);
    }
    private void QualityChart_FrameSelected(object? sender, QualityFrameRow row)
    {
        QualityGrid.SelectedItems.Clear(); QualityGrid.SelectedItem = row; QualityGrid.ScrollIntoView(row);
    }
    private async void QualityPreviewOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await RefreshQualityPreviewAsync(180, true);
    }
    private async void QualityStretch_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        await RefreshQualityPreviewAsync(180, true);
    }

    private async Task RefreshQualityPreviewAsync(int debounceMilliseconds = 0, bool fitToViewport = false)
    {
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try
        {
            if (debounceMilliseconds > 0) await Task.Delay(debounceMilliseconds, cancellation.Token);
            await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token);
            if (fitToViewport) { QualityPreviewImage.UpdateLayout(); FitQualityPreview(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _viewModel.RecordError("AF-QUALITY-PREVIEW-001", exception); }
        finally { if (ReferenceEquals(_previewCancellation, cancellation)) { _previewCancellation.Dispose(); _previewCancellation = null; } }
    }

    private void QualityZoomIn_Click(object sender, RoutedEventArgs e) => SetQualityZoom(_qualityZoom * 1.25);
    private void QualityZoomOut_Click(object sender, RoutedEventArgs e) => SetQualityZoom(_qualityZoom / 1.25);
    private void QualityZoomFit_Click(object sender, RoutedEventArgs e) => FitQualityPreview();
    private async void QualityFullResolution_Click(object sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token, true);
            SetQualityZoom(1);
            QualityPreviewScroll.ScrollToHome();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _viewModel.RecordError("AF-QUALITY-FULLRES-001", exception); }
        finally { if (ReferenceEquals(_previewCancellation, cancellation)) { _previewCancellation.Dispose(); _previewCancellation = null; } }
    }
    private void QualityPreview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var anchor = e.GetPosition(QualityPreviewScroll);
        SetQualityZoom(_qualityZoom * (e.Delta > 0 ? 1.18 : 1 / 1.18), anchor);
        e.Handled = true;
    }
    private void QualityPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _qualityPreviewPanning = true;
        _qualityPanStart = e.GetPosition(QualityPreviewScroll);
        _qualityPanHorizontal = QualityPreviewScroll.HorizontalOffset;
        _qualityPanVertical = QualityPreviewScroll.VerticalOffset;
        QualityPreviewImage.CaptureMouse();
        e.Handled = true;
    }
    private void QualityPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_qualityPreviewPanning || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(QualityPreviewScroll);
        QualityPreviewScroll.ScrollToHorizontalOffset(_qualityPanHorizontal - (current.X - _qualityPanStart.X));
        QualityPreviewScroll.ScrollToVerticalOffset(_qualityPanVertical - (current.Y - _qualityPanStart.Y));
    }
    private void QualityPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _qualityPreviewPanning = false;
        QualityPreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void FitQualityPreview()
    {
        if (_viewModel.SelectedQualityFrame?.Preview is not System.Windows.Media.Imaging.BitmapSource bitmap || QualityPreviewScroll.ViewportWidth <= 0 || QualityPreviewScroll.ViewportHeight <= 0) return;
        var fit = Math.Min((QualityPreviewScroll.ViewportWidth - 10) / bitmap.PixelWidth, (QualityPreviewScroll.ViewportHeight - 10) / bitmap.PixelHeight);
        SetQualityZoom(Math.Clamp(fit, 0.05, 16));
        QualityPreviewScroll.ScrollToHome();
    }
    private void SetQualityZoom(double value, Point? anchor = null)
    {
        var old = _qualityZoom;
        var next = Math.Clamp(value, 0.05, 16);
        if (Math.Abs(next - old) < 0.0001) return;
        var point = anchor ?? new Point(QualityPreviewScroll.ViewportWidth / 2, QualityPreviewScroll.ViewportHeight / 2);
        var horizontal = QualityPreviewScroll.HorizontalOffset;
        var vertical = QualityPreviewScroll.VerticalOffset;
        _qualityZoom = next;
        QualityPreviewScale.ScaleX = next; QualityPreviewScale.ScaleY = next;
        QualityZoomText.Text = $"{next * 100:0}%";
        QualityPreviewImage.UpdateLayout();
        var factor = next / old;
        QualityPreviewScroll.ScrollToHorizontalOffset((horizontal + point.X) * factor - point.X);
        QualityPreviewScroll.ScrollToVerticalOffset((vertical + point.Y) * factor - point.Y);
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
        catch (Exception exception) { ShowError("AF-STATS-001", "Esportazione statistiche non completata", exception, MessageBoxImage.Warning); }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "Apri progetto AstroProject Forge", Filter = "Progetto AstroProject Forge (*.astroforge)|*.astroforge" };
            if (dialog.ShowDialog(this) == true) await _viewModel.LoadProjectAsync(dialog.FileName);
        }
        catch (Exception exception) { ShowError("AF-PROJECT-OPEN-001", "Progetto non aperto", exception, MessageBoxImage.Error); }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Title = "Salva progetto AstroProject Forge", Filter = "Progetto AstroProject Forge (*.astroforge)|*.astroforge", AddExtension = true, DefaultExt = ".astroforge", FileName = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? "Nuovo progetto" : _viewModel.ProjectName };
            if (dialog.ShowDialog(this) == true) _viewModel.SaveProject(dialog.FileName);
        }
        catch (Exception exception) { ShowError("AF-PROJECT-SAVE-001", "Progetto non salvato", exception, MessageBoxImage.Error); }
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        if (MessageBox.Show(this, "Svuotare la cache degli header? Le immagini originali non verranno modificate.", "Pulisci cache", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _viewModel.ClearHeaderCache();
    }

    private async void ExportSupportBundle_Click(object sender, RoutedEventArgs e) => await ExportSupportBundleCoreAsync();

    private async void ExportSupportFromDiagnostics_Click(object sender, RoutedEventArgs e) => await ExportSupportBundleCoreAsync();

    private async Task ExportSupportBundleCoreAsync()
    {
        MorePopup.IsOpen = false;
        var preview = "Il pacchetto verrà creato soltanto sul computer e conterrà esattamente:\n\n" + _viewModel.SupportBundlePreview + "\n\nNon include FITS/XISF, pixel, target, coordinate, nomi file, percorsi o header grezzi. Continuare?";
        if (MessageBox.Show(this, preview, "Anteprima pacchetto diagnostico", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog { Title = "Salva pacchetto diagnostico", Filter = "Pacchetto diagnostico ZIP (*.zip)|*.zip", AddExtension = true, DefaultExt = ".zip", FileName = $"AstroProjectForge-Support-{DateTime.Now:yyyyMMdd-HHmmss}" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = await _viewModel.ExportSupportBundleAsync(dialog.FileName);
            MessageBox.Show(this, $"Pacchetto creato localmente:\n{path}\n\nNessun dato è stato inviato.", "Diagnostica esportata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { ShowError("AF-SUPPORT-001", "Pacchetto diagnostico non creato", exception, MessageBoxImage.Error); }
    }

    private async void RestoreRecovery_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.RestoreRecoveryAsync(); }
        catch (Exception exception) { ShowError("AF-RECOVERY-001", "Progetto non ripristinato", exception, MessageBoxImage.Error); }
    }

    private void DiscardRecovery_Click(object sender, RoutedEventArgs e) => _viewModel.DiscardRecovery();

    private void ShowError(string code, string title, Exception exception, MessageBoxImage icon)
    {
        _viewModel.RecordError(code, exception);
        MessageBox.Show(this, $"[{code}] {exception.Message}\n\nIl codice è stato registrato nella diagnostica locale.", title, MessageBoxButton.OK, icon);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ScanAsync(); }
        catch (Exception exception) { ShowError("AF-SCAN-001", "Scansione non completata", exception, MessageBoxImage.Error); }
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
        catch (Exception exception) { ShowError("AF-MASTER-ORGANIZE-001", "Organizzazione non completata", exception, MessageBoxImage.Warning); }
    }

    private async void PreviewMasterOrganizer_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.PreviewMasterOrganizerAsync(); }
        catch (Exception exception) { ShowError("AF-MASTER-PREFLIGHT-001", "Preflight non completato", exception, MessageBoxImage.Warning); }
    }

    private async void RollbackMasterOrganizer_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Annullare l’ultimo batch? Verranno rimosse soltanto le copie elencate nel manifest e solo se non sono state modificate. Gli originali resteranno intatti.", "Annulla ultimo batch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _viewModel.RollbackMasterOrganizerAsync(); MessageBox.Show(this, _viewModel.MasterOrganizerStatus, "Batch annullato", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { ShowError("AF-MASTER-ROLLBACK-001", "Impossibile annullare il batch", exception, MessageBoxImage.Warning); }
    }

    private async void ScanMasterLibraries_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ScanMasterLibrariesAsync(); }
        catch (Exception exception) { ShowError("AF-MASTER-SCAN-001", "Scansione librerie non completata", exception, MessageBoxImage.Warning); }
    }
}
