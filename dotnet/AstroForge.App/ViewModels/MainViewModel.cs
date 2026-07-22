using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Models;
using AstroForge.Core.Analysis;
using AstroForge.Core.Diagnostics;
using AstroForge.Core.Export;
using AstroForge.Core.Matching;
using AstroForge.Core.Scanning;
using AstroForge.Core.Sessions;
using AstroForge.Core.Validation;
using AstroForge.Core.Wbpp;
using AstroForge.Core.IO;
using AstroForge.Core.Quality;
using AstroForge.App.Services;
#if AVALONIA
using Avalonia.Media.Imaging;
using BitmapSource = Avalonia.Media.Imaging.Bitmap;
#else
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif

namespace AstroForge.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private readonly ProjectScanner _scanner = new();
    private readonly JsonHeaderCache _headerCache = new();
    private readonly StructuredEventLog _eventLog = new();
    private readonly RecoveryJournalStore _recoveryJournal = new();
    private readonly Stack<Action> _undo = new();
    private readonly AppState _state;
    private RecoveryJournalEntry<ProjectRecoverySnapshot>? _pendingRecovery;
    private readonly HashSet<string> _kindOverrides = new(PathIdentity.Comparer);
    private IReadOnlyList<FrameMetadata> _frames = [];
    private IReadOnlyList<FrameMetadata> _masterLibraryFrames = [];
    private ProjectAnalysis? _analysis;
    private ProjectStatistics? _statistics;
    private ProjectPlan? _plan;
    private ProjectTreeNode? _selectedNode;
    private FlatSetOption? _selectedFlatSet;
    private bool _isScanning;
    private bool _showIssuesOnly;
    private string _searchText = "";
    private string _libraryPath = "";
    private string _status = "Aggiungi file o cartelle FITS/XISF e avvia l’analisi";
    private double _progress;
    private string _editGain = "";
    private string _editOffset = "";
    private string _editTemperature = "";
    private string _editFilter = "";
    private string _editFlatSet = "";
    private string _editSession = "";
    private FrameKind _editKind;
    private string _initialGain = "";
    private string _initialOffset = "";
    private string _initialTemperature = "";
    private string _initialFilter = "";
    private string _initialFlatSet = "";
    private string _initialSession = "";
    private FrameKind _initialKind;
    private string _projectName = "";
    private string _destinationPath = "";
    private int _sessionBoundaryHour = 12;
    private string _readinessText = "Non analizzato";
    private string _calibrationSummary = "Seleziona uno o più Light per vedere le calibrazioni assegnate.";
    private string _masterLibraryOffset = "51";
    private string _projectDefaultGain = "100";
    private string _projectDefaultOffset = "51";
    private string _projectDefaultTemperature = "";
    private MasterLibraryItem? _selectedMasterLibrary;
    private string _masterOrganizerDestination = "";
    private string _masterOrganizerStatus = "Scansiona le Master Library abilitate per iniziare";
    private string _currentProjectFile = "";
    private DateTimeOffset _projectCreatedAt = DateTimeOffset.Now;
    private string _uiDensity = "Comoda";
    private bool _reducedMotion;
    private bool _checkForUpdates;
    private string _updateChannel = "Beta";
    private string _updateStatus = "Controllo automatico disattivato";
    private bool _showOnboarding;
    private string _diagnosticsSummary = "Nessun evento caricato";
    private bool _awaitingReanalysis;
    private ExportPreflightReport? _exportPreflight;
    private ExportExecutionControl? _exportControl;
    private CancellationTokenSource? _exportCancellation;
    private ExportRunState _exportState;
    private double _exportProgress;
    private string _exportProgressDetail = "L’anteprima è facoltativa; i controlli vengono eseguiti automaticamente durante l’esportazione.";
    private double _exportMarginPercent = 10;
    private double _exportMinimumReserveGiB = 1;
    private double _exportEstimatedThroughputMiBps = 100;
    private readonly HashSet<string> _excludedQualityPaths = new(PathIdentity.Comparer);
    private readonly List<QualityFrameRow> _allQualityFrames = [];
    private QualityFrameRow? _selectedQualityFrame;
    private QualitySeriesRow? _selectedQualitySeries;
    private string _qualityStatus = "Analisi opzionale: i pixel non vengono letti finché non la avvii.";
    private double _qualityProgress;
    private bool _isQualityAnalyzing;
    private double _qualitySigmaThreshold = 3.5;
    private double _qualityStretchStrength = 6;
    private bool _qualityDebayerPreview;
    private bool _qualityShowOnlySuspects;
    private int _qualitySelectedCount;
    private QualityFrameRow? _highResolutionQualityItem;
    private double _sourcePanelWidth = 260;
    private double _inspectorPanelWidth = 390;

    public MainViewModel()
    {
        _state = AppStateStore.Load();
        _libraryPath = _state.LibraryPath;
        var savedLibraries = _state.MasterLibraries.Count > 0 ? _state.MasterLibraries : string.IsNullOrWhiteSpace(_state.LibraryPath) ? [] : [new() { Name = "Libreria principale", Path = _state.LibraryPath, Priority = 1 }];
        foreach (var library in savedLibraries.OrderBy(item => item.Priority)) AddMasterLibraryItem(new(library.Name, library.Path, library.Priority, library.Enabled));
        _destinationPath = _state.DestinationPath;
        _projectName = _state.ProjectName;
        _sessionBoundaryHour = Math.Clamp(_state.SessionBoundaryHour, 0, 23);
        _projectDefaultGain = Input(_state.ProjectDefaultGain);
        _projectDefaultOffset = Input(_state.ProjectDefaultOffset);
        _projectDefaultTemperature = Input(_state.ProjectDefaultTemperatureC);
        _currentProjectFile = _state.LastProjectFile;
        _uiDensity = new[] { "Compatta", "Comoda", "Ampia" }.Contains(_state.UiDensity) ? _state.UiDensity : "Comoda";
        _reducedMotion = _state.ReducedMotion;
        _checkForUpdates = _state.CheckForUpdates;
        _updateChannel = _state.UpdateChannel is "Stable" or "Beta" ? _state.UpdateChannel : "Beta";
        _exportMarginPercent = Math.Clamp(_state.ExportMarginPercent, 0, 100);
        _exportMinimumReserveGiB = Math.Max(0, _state.ExportMinimumReserveGiB);
        _exportEstimatedThroughputMiBps = Math.Max(1, _state.ExportEstimatedThroughputMiBps);
        foreach (var path in _state.ExcludedQualityPaths) _excludedQualityPaths.Add(path);
        _qualitySigmaThreshold = Math.Clamp(_state.QualitySigmaThreshold, 2, 6);
        _qualityStretchStrength = Math.Clamp(_state.QualityStretchStrength, 0, 12);
        _qualityDebayerPreview = _state.QualityDebayerPreview;
        _sourcePanelWidth = Math.Clamp(_state.SourcePanelWidth, 190, 520);
        _inspectorPanelWidth = Math.Clamp(_state.InspectorPanelWidth, 280, 680);
        _updateStatus = _checkForUpdates ? $"Controllo { _updateChannel } attivo · nessun download automatico" : "Controllo automatico disattivato";
        _pendingRecovery = _recoveryJournal.Read<ProjectRecoverySnapshot>();
        _showOnboarding = !_state.HasCompletedOnboarding && _pendingRecovery is null;
        foreach (var path in _state.SourcePaths.Where(path => Directory.Exists(path) || File.Exists(path))) SourcePaths.Add(path);
        if (SourcePaths.Count > 0)
        {
            _readinessText = $"{SourceSummary} · analisi richiesta";
            _status = $"{SourceSummary} · avvia l’analisi per costruire la mappa";
        }
        foreach (var pair in _state.Overrides.Where(pair => pair.Value.Kind is not null)) _kindOverrides.Add(pair.Key);
        ApplyOverridesCommand = new RelayCommand(ApplyOverrides, () => SelectedNode is not null && !IsScanning);
        ApplyLibraryOffsetCommand = new RelayCommand(ApplyLibraryOffset, () => _frames.Any(frame => frame.IsMaster) && !IsScanning);
        ApplyProjectDefaultsCommand = new RelayCommand(ApplyProjectDefaults, () => _frames.Count > 0 && !IsScanning);
        SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsScanning);
        ClearProjectCommand = new RelayCommand(ClearProject, () => (_frames.Count > 0 || _plan is not null) && !IsScanning);
        LinkFlatSetCommand = new RelayCommand(LinkFlatSet, CanLinkFlatSet);
        UnlinkFlatSetCommand = new RelayCommand(UnlinkFlatSet, () => GetManualLinkFrames().Any(frame => frame.Kind == FrameKind.Light && frame.FlatSetId.HasOverride) && !IsScanning);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0 && !IsScanning);
        _eventLog.Write("Information", "AF-APP-START", "Applicazione avviata");
        if (_pendingRecovery is not null)
            _eventLog.Write("Warning", "AF-RECOVERY-AVAILABLE", "Rilevata operazione interrotta recuperabile", operationId: _pendingRecovery.OperationId, operation: _pendingRecovery.Operation);
    }

    public ObservableCollection<string> SourcePaths { get; } = [];
    public ObservableCollection<MasterLibraryItem> MasterLibraries { get; } = [];
    public ObservableCollection<ProjectTreeNode> TreeRoots { get; } = [];
    public ObservableCollection<ProjectTreeNode> PlannedTreeRoots { get; } = [];
    public ObservableCollection<WbppKeywordRow> WbppKeywords { get; } = [];
    public ObservableCollection<string> WbppNotes { get; } = [];
    public ObservableCollection<FlatSetOption> AvailableFlatSets { get; } = [];
    public ObservableCollection<FilterStatsRow> FilterStatistics { get; } = [];
    public ObservableCollection<SessionStatsRow> SessionStatistics { get; } = [];
    public ObservableCollection<NightStatsRow> NightStatistics { get; } = [];
    public ObservableCollection<ReviewQueueItem> ReviewQueue { get; } = [];
    public ObservableCollection<MasterOrganizerItem> MasterOrganizerItems { get; } = [];
    public ObservableCollection<string> SelectedIssues { get; } = [];
    public ObservableCollection<DiagnosticEventRow> DiagnosticEvents { get; } = [];
    public ObservableCollection<ExportPreflightFindingRow> ExportPreflightFindings { get; } = [];
    public ObservableCollection<QualityFrameRow> QualityFrames { get; } = [];
    public ObservableCollection<QualitySeriesRow> QualitySeries { get; } = [];
    public RelayCommand ApplyOverridesCommand { get; }
    public RelayCommand ApplyLibraryOffsetCommand { get; }
    public RelayCommand ApplyProjectDefaultsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearProjectCommand { get; }
    public RelayCommand LinkFlatSetCommand { get; }
    public RelayCommand UnlinkFlatSetCommand { get; }
    public RelayCommand UndoCommand { get; }
    public Array FrameKinds => Enum.GetValues<FrameKind>();

    public string LibraryPath { get => MasterLibraries.FirstOrDefault()?.Path ?? _libraryPath; set { _libraryPath = value; if (!string.IsNullOrWhiteSpace(value) && !MasterLibraries.Any(item => PathIdentity.Equals(item.Path, value))) AddMasterLibrary(value); Raise(); } }
    public MasterLibraryItem? SelectedMasterLibrary { get => _selectedMasterLibrary; set => Set(ref _selectedMasterLibrary, value); }
    public string ProjectName { get => _projectName; set { if (Set(ref _projectName, value)) InvalidateExportPlan(); } }
    public string DestinationPath { get => _destinationPath; set { if (Set(ref _destinationPath, value)) InvalidateExportPlan(); } }
    public string CurrentProjectFile { get => _currentProjectFile; private set { if (Set(ref _currentProjectFile, value)) Raise(nameof(ProjectDocumentStatus)); } }
    public string ProjectDocumentStatus => string.IsNullOrWhiteSpace(CurrentProjectFile) ? "Progetto non ancora salvato" : Path.GetFileName(CurrentProjectFile);
    public string UiDensity { get => _uiDensity; set => Set(ref _uiDensity, value); }
    public bool ReducedMotion { get => _reducedMotion; set => Set(ref _reducedMotion, value); }
    public bool CheckForUpdates { get => _checkForUpdates; set { if (Set(ref _checkForUpdates, value)) UpdateStatus = value ? $"Controllo {UpdateChannel} attivo · nessun download automatico" : "Controllo automatico disattivato"; } }
    public string UpdateChannel { get => _updateChannel; set { var normalized = value == "Stable" ? "Stable" : "Beta"; if (Set(ref _updateChannel, normalized) && CheckForUpdates) UpdateStatus = $"Controllo {normalized} attivo · nessun download automatico"; } }
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }
    public string VersionDisplay => ReleaseIdentity.Display;
    public string ApplicationVersion => ReleaseIdentity.Version;
    public string ReleaseChannel => ReleaseIdentity.Channel;
    public bool ShowOnboarding { get => _showOnboarding; private set => Set(ref _showOnboarding, value); }
    public bool HasRecoverySnapshot => _pendingRecovery is not null;
    public bool CanRunProjectOperations => !IsScanning && !HasRecoverySnapshot;
    public bool CanAnalyzeProject => HasSources && CanRunProjectOperations;
    public bool HasSources => SourcePaths.Count > 0;
    public bool HasAnalysis => _analysis is not null;
    public bool HasVisibleTree => TreeRoots.Count > 0;
    public bool ShowImportPrompt => !HasSources && !HasAnalysis;
    public bool ShowAnalysisPrompt => HasSources && !HasAnalysis;
    public bool ShowNoResultsPrompt => HasAnalysis && TreeRoots.Count == 0;
    public string SourceSummary => SourcePaths.Count == 1 ? "1 sorgente collegata" : $"{SourcePaths.Count} sorgenti collegate";
    public string SourceBreakdown
    {
        get
        {
            var folders = SourcePaths.Count(Directory.Exists);
            var files = SourcePaths.Count(File.Exists);
            var parts = new List<string>();
            if (folders > 0) parts.Add(folders == 1 ? "1 cartella" : $"{folders} cartelle");
            if (files > 0) parts.Add(files == 1 ? "1 file singolo" : $"{files} file singoli");
            return parts.Count == 0 ? "Percorsi non più disponibili" : string.Join(" · ", parts);
        }
    }
    public string AnalysisPromptTitle => _awaitingReanalysis ? "Le sorgenti sono cambiate" : "Sorgenti collegate";
    public string AnalysisPromptDetail => _awaitingReanalysis
        ? "La mappa precedente è stata ritirata per evitare risultati obsoleti. Rianalizza per includere tutti i percorsi correnti."
        : "I percorsi sono pronti. L’analisi leggerà gli header e costruirà filtri, notti e sessioni senza modificare gli originali.";
    public string AnalysisActionLabel => IsScanning ? "ANALISI IN CORSO…" : HasAnalysis || _awaitingReanalysis ? "RIANALIZZA PROGETTO" : "ANALIZZA PROGETTO";
    public string RecoverySummary => _pendingRecovery is null
        ? "Nessun recupero necessario"
        : $"{_pendingRecovery.Operation} interrotta il {_pendingRecovery.StartedAtUtc.ToLocalTime():dd MMM yyyy 'alle' HH:mm}. Puoi ripristinare la fotografia del progetto precedente all’operazione.";
    public string DiagnosticsSummary { get => _diagnosticsSummary; private set => Set(ref _diagnosticsSummary, value); }
    public double ExportMarginPercent { get => _exportMarginPercent; set { if (Set(ref _exportMarginPercent, value)) InvalidateExportPreflight(); } }
    public double ExportMinimumReserveGiB { get => _exportMinimumReserveGiB; set { if (Set(ref _exportMinimumReserveGiB, value)) InvalidateExportPreflight(); } }
    public double ExportEstimatedThroughputMiBps { get => _exportEstimatedThroughputMiBps; set { if (Set(ref _exportEstimatedThroughputMiBps, value)) InvalidateExportPreflight(); } }
    public bool HasExportPlan => _plan is not null;
    public bool HasExportPreflight => _exportPreflight is not null;
    public bool ExportPreflightReady => _exportPreflight?.IsReady == true;
    public bool CanRunExportPreflight => HasExportPlan && !IsScanning && !HasRecoverySnapshot;
    public bool CanStartExport => _analysis?.Ready == true && !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(DestinationPath) && !IsScanning && !HasRecoverySnapshot && _exportState is not (ExportRunState.Running or ExportRunState.Paused or ExportRunState.Cancelling);
    public bool CanPauseExport => _exportState == ExportRunState.Running;
    public bool CanResumeExport => _exportState == ExportRunState.Paused;
    public bool CanCancelExport => _exportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Cancelling;
    public string ExportStateLabel => _exportState switch
    {
        ExportRunState.Preflighting => "PREFLIGHT IN CORSO",
        ExportRunState.Ready => "PRONTO",
        ExportRunState.Blocked => "BLOCCATO",
        ExportRunState.Running => "COPIA + SHA-256",
        ExportRunState.Paused => "IN PAUSA",
        ExportRunState.Cancelling => "ANNULLAMENTO…",
        ExportRunState.Completed => "COMPLETATO",
        ExportRunState.Cancelled => "RIPRENDIBILE",
        ExportRunState.Failed => "ERRORE",
        _ => "NON VERIFICATO"
    };
    public string ExportProgressDetail { get => _exportProgressDetail; private set => Set(ref _exportProgressDetail, value); }
    public string ExportFileSummary => _exportPreflight is null ? "—" : $"{_exportPreflight.TotalFiles} file";
    public string ExportBytesSummary => _exportPreflight is null ? "—" : $"{HumanSize(_exportPreflight.BytesToCopy)} da copiare";
    public string ExportSpaceSummary => _exportPreflight?.AvailableFreeBytes is { } value ? $"{HumanSize(value)} liberi" : "Spazio non disponibile";
    public string ExportEtaSummary => _exportPreflight is null ? "—" : FormatDuration(_exportPreflight.EstimatedDuration);
    public string ExportResumeSummary => _exportPreflight is null || _exportPreflight.ResumeFileCount == 0 ? "Nessuna ripresa" : $"{_exportPreflight.ResumeFileCount} file · {HumanSize(_exportPreflight.ResumeBytes)} riutilizzabili";
    public QualityFrameRow? SelectedQualityFrame { get => _selectedQualityFrame; set => Set(ref _selectedQualityFrame, value); }
    public QualitySeriesRow? SelectedQualitySeries
    {
        get => _selectedQualitySeries;
        set
        {
            if (!Set(ref _selectedQualitySeries, value)) return;
            RebuildQualitySeriesView();
            Raise(nameof(CanRunQualityAnalysis));
        }
    }
    public string QualityStatus { get => _qualityStatus; private set => Set(ref _qualityStatus, value); }
    public double QualityProgress { get => _qualityProgress; private set => Set(ref _qualityProgress, value); }
    public bool IsQualityAnalyzing { get => _isQualityAnalyzing; private set { if (Set(ref _isQualityAnalyzing, value)) { Raise(nameof(CanRunQualityAnalysis)); Raise(nameof(CanChangeQualityExclusions)); Raise(nameof(CanExcludeSelectedQuality)); Raise(nameof(CanExcludeQualitySuspects)); } } }
    public bool CanRunQualityAnalysis => SelectedQualitySeries?.SourceFrames.Any(IsQualityFits) == true && !IsQualityAnalyzing && !IsScanning;
    public bool CanChangeQualityExclusions => !IsQualityAnalyzing && SelectedQualitySeries?.Frames.Count > 0;
    public int QualitySuspectCount => SelectedQualitySeries?.Frames.Count(item => item.IsSuspect) ?? 0;
    public int QualityExcludedCount => SelectedQualitySeries?.Frames.Count(item => item.IsExcluded) ?? 0;
    public int QualityPendingSuspectCount => SelectedQualitySeries?.Frames.Count(item => item.IsSuspect && !item.IsExcluded) ?? 0;
    public bool CanExcludeQualitySuspects => !IsQualityAnalyzing && QualityPendingSuspectCount > 0;
    public string ExcludeQualitySuspectsLabel => QualityPendingSuspectCount > 0 ? $"Escludi {QualityPendingSuspectCount} sospetti" : "Nessun sospetto da escludere";
    public string QualityTableSummary => SelectedQualitySeries is null
        ? "Nessuna serie selezionata"
        : $"{QualityFrames.Count}/{SelectedQualitySeries.AnalyzedCount} mostrati · {QualitySuspectCount} sospetti · {QualityExcludedCount} esclusi";
    public IEnumerable<QualityFrameRow> QualityChartFrames => SelectedQualitySeries?.Frames ?? [];
    public bool QualityShowOnlySuspects
    {
        get => _qualityShowOnlySuspects;
        set { if (Set(ref _qualityShowOnlySuspects, value)) RebuildQualitySeriesView(); }
    }
    public double QualitySigmaThreshold
    {
        get => _qualitySigmaThreshold;
        set
        {
            if (!Set(ref _qualitySigmaThreshold, Math.Clamp(value, 2, 6))) return;
            if (_allQualityFrames.Count > 0)
            {
                ScoreQualityOutliers();
                if (QualityShowOnlySuspects) RebuildQualitySeriesView(); else RefreshQualityCounts();
            }
        }
    }
    public double QualityStretchStrength { get => _qualityStretchStrength; set => Set(ref _qualityStretchStrength, Math.Clamp(value, 0, 12)); }
    public bool QualityDebayerPreview { get => _qualityDebayerPreview; set => Set(ref _qualityDebayerPreview, value); }
    public int QualitySelectedCount { get => _qualitySelectedCount; private set { if (Set(ref _qualitySelectedCount, value)) Raise(nameof(CanExcludeSelectedQuality)); } }
    public bool CanExcludeSelectedQuality => !IsQualityAnalyzing && QualitySelectedCount > 0;
    public double SourcePanelWidth { get => _sourcePanelWidth; set => Set(ref _sourcePanelWidth, Math.Clamp(value, 190, 520)); }
    public double InspectorPanelWidth { get => _inspectorPanelWidth; set => Set(ref _inspectorPanelWidth, Math.Clamp(value, 280, 680)); }
    public void OpenOnboarding() => ShowOnboarding = true;
    public void CompleteOnboarding()
    {
        ShowOnboarding = false;
        _state.HasCompletedOnboarding = true;
        SaveState();
    }
    public int SessionBoundaryHour
    {
        get => _sessionBoundaryHour;
        set
        {
            if (!Set(ref _sessionBoundaryHour, Math.Clamp(value, 0, 23)) || _frames.Count == 0) return;
            var settings = new SessionSettings(TimeZoneInfo.Local, new TimeOnly(_sessionBoundaryHour, 0));
            foreach (var frame in _frames) AstronomicalSessionResolver.Apply(frame, settings);
            RefreshIntelligence();
            RebuildTree();
            Status = $"Sessioni ricalcolate con cambio alle {_sessionBoundaryHour:00}:00 locali";
        }
    }
    public bool IsScanning { get => _isScanning; private set { if (Set(ref _isScanning, value)) { Raise(nameof(CanRunProjectOperations)); Raise(nameof(CanAnalyzeProject)); Raise(nameof(AnalysisActionLabel)); Raise(nameof(CanRunQualityAnalysis)); Raise(nameof(CanChangeQualityExclusions)); RaiseExportProperties(); ApplyOverridesCommand.RaiseCanExecuteChanged(); ApplyLibraryOffsetCommand.RaiseCanExecuteChanged(); ApplyProjectDefaultsCommand.RaiseCanExecuteChanged(); SaveSettingsCommand.RaiseCanExecuteChanged(); ClearProjectCommand.RaiseCanExecuteChanged(); LinkFlatSetCommand.RaiseCanExecuteChanged(); UnlinkFlatSetCommand.RaiseCanExecuteChanged(); UndoCommand.RaiseCanExecuteChanged(); } } }
    public bool ShowIssuesOnly { get => _showIssuesOnly; set { if (Set(ref _showIssuesOnly, value)) RebuildTree(); } }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) RebuildTree(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public double ExportProgress { get => _exportProgress; private set => Set(ref _exportProgress, value); }
    public int TotalFiles => _frames.Count;
    public int TotalIssues => _frames.Sum(frame => frame.Issues.Count);
    public int OverrideCount => _frames.Count(frame => HasAnyOverride(frame));
    public int UnresolvedCalibrations => _analysis?.UnresolvedCount ?? 0;
    public int ReviewQueueCount => ReviewQueue.Count;
    public bool IsProjectReady => _analysis?.Ready == true;
    public string ReadinessText { get => _readinessText; private set => Set(ref _readinessText, value); }
    public string CalibrationSummary { get => _calibrationSummary; private set => Set(ref _calibrationSummary, value); }
    public string PlanSummary => _plan is null ? "Genera il piano dopo aver risolto le calibrazioni." : $"{_plan.Files.Count} file · {HumanSize(_plan.RequiredBytes)} · {_plan.ProjectRoot}";
    public string TotalIntegrationText => _statistics is null ? "0 h" : FormatHours(_statistics.ExposureSeconds);
    public string StatisticsSummary => _statistics is null ? "Analizza il progetto per calcolare le statistiche." : $"{_statistics.LightCount} Light · {_statistics.FilterCount} filtri · {_statistics.ConfigurationSessionCount} sessioni · {_statistics.NightCount} notti";
    public string StatisticsDateRange => _statistics?.FirstCapture is null ? "Nessun intervallo temporale" : $"{_statistics.FirstCapture.Value.ToLocalTime():dd MMM yyyy} → {_statistics.LastCapture!.Value.ToLocalTime():dd MMM yyyy}";
    public string SelectionTitle => SelectedNode?.Name ?? "Nessuna selezione";
    public string SelectionDetail => SelectedNode is null ? "Seleziona un nodo dell'albero" : $"{SelectedNode.Count} frame · {SelectedNode.IssueCount} segnalazioni";
    public bool HasSelection => SelectedNode is not null;
    public string ApplyLabel => SelectedNode is null ? "Applica" : $"Applica a {SelectedNode.Count} frame";
    public string GainSource => AggregateSource(SelectedNode?.Frames, frame => frame.Gain.Source);
    public string OffsetSource => AggregateSource(SelectedNode?.Frames, frame => frame.Offset.Source);
    public string TemperatureSource => AggregateSource(SelectedNode?.Frames, frame => frame.SetTemperatureC.Source);
    public string FilterSource => AggregateSource(SelectedNode?.Frames, frame => frame.FilterName.Source);
    public string FlatSetSource => AggregateSource(SelectedNode?.Frames, frame => frame.FlatSetId.Source);
    public string SessionSource => AggregateSource(SelectedNode?.Frames, frame => frame.SessionId.Source);
    public string RawHeaders => BuildRawHeaders();
    public string ManualLinkSelectionText
    {
        get
        {
            var marked = Descendants(TreeRoots).Where(node => node.IsMarked).ToArray();
            var lights = GetManualLinkFrames().Count(frame => frame.Kind == FrameKind.Light);
            return marked.Length > 0 ? $"{marked.Length} gruppi marcati · {lights} Light" : SelectedNode is null ? "Seleziona o marca le sessioni nell'albero" : $"Selezione corrente · {lights} Light";
        }
    }

    public ProjectTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!Set(ref _selectedNode, value)) return;
            LoadEditor();
            RefreshFlatSetOptions();
            RaiseSelectionProperties();
            ApplyOverridesCommand.RaiseCanExecuteChanged();
            LinkFlatSetCommand.RaiseCanExecuteChanged();
            UnlinkFlatSetCommand.RaiseCanExecuteChanged();
        }
    }

    public FlatSetOption? SelectedFlatSet
    {
        get => _selectedFlatSet;
        set { if (Set(ref _selectedFlatSet, value)) LinkFlatSetCommand.RaiseCanExecuteChanged(); }
    }

    public string EditGain { get => _editGain; set => Set(ref _editGain, value); }
    public string EditOffset { get => _editOffset; set => Set(ref _editOffset, value); }
    public string EditTemperature { get => _editTemperature; set => Set(ref _editTemperature, value); }
    public string EditFilter { get => _editFilter; set => Set(ref _editFilter, value); }
    public string EditFlatSet { get => _editFlatSet; set => Set(ref _editFlatSet, value); }
    public string EditSession { get => _editSession; set => Set(ref _editSession, value); }
    public FrameKind EditKind { get => _editKind; set => Set(ref _editKind, value); }
    public string MasterLibraryOffset { get => _masterLibraryOffset; set => Set(ref _masterLibraryOffset, value); }
    public string ProjectDefaultGain { get => _projectDefaultGain; set => Set(ref _projectDefaultGain, value); }
    public string ProjectDefaultOffset { get => _projectDefaultOffset; set => Set(ref _projectDefaultOffset, value); }
    public string ProjectDefaultTemperature { get => _projectDefaultTemperature; set => Set(ref _projectDefaultTemperature, value); }
    public string MasterOrganizerDestination { get => _masterOrganizerDestination; set { if (Set(ref _masterOrganizerDestination, value)) foreach (var item in MasterOrganizerItems) item.SetPreflight("Non verificato"); } }
    public string MasterOrganizerStatus { get => _masterOrganizerStatus; private set => Set(ref _masterOrganizerStatus, value); }

    public void AddSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = Path.GetFullPath(path);
        if (SourcePaths.Contains(path, PathIdentity.Comparer)) { Status = "Sorgente già collegata"; return; }
        var hadAnalysis = _analysis is not null || _frames.Count > 0;
        SourcePaths.Add(path);
        InvalidateProjectAnalysis(hadAnalysis);
        Status = $"{SourceSummary} · pronta per l’analisi";
        SaveState();
    }

    public void RemoveSource(string path)
    {
        var hadAnalysis = _analysis is not null || _frames.Count > 0;
        if (!SourcePaths.Remove(path)) return;
        InvalidateProjectAnalysis(hadAnalysis);
        Status = HasSources ? $"Sorgente rimossa · {SourceSummary} · rianalizza il progetto" : "Nessuna sorgente collegata";
        SaveState();
    }

    public void AddMasterLibrary(string path)
    {
        path = Path.GetFullPath(path);
        if (MasterLibraries.Any(item => PathIdentity.Equals(item.Path, path))) { SelectedMasterLibrary = MasterLibraries.First(item => PathIdentity.Equals(item.Path, path)); return; }
        var item = new MasterLibraryItem(new DirectoryInfo(path).Name, path, MasterLibraries.Count + 1, true);
        AddMasterLibraryItem(item); SelectedMasterLibrary = item; NormalizeLibraryPriorities();
        InvalidateProjectAnalysis(_analysis is not null || _frames.Count > 0);
        SaveState();
    }

    public void RemoveSelectedMasterLibrary()
    {
        if (SelectedMasterLibrary is null) return;
        SelectedMasterLibrary.PropertyChanged -= MasterLibraryItem_PropertyChanged;
        MasterLibraries.Remove(SelectedMasterLibrary); SelectedMasterLibrary = null; NormalizeLibraryPriorities();
        InvalidateProjectAnalysis(_analysis is not null || _frames.Count > 0);
        SaveState();
        Status = "Libreria rimossa dal progetto · nessun Master è stato cancellato";
    }

    public void MoveSelectedMasterLibrary(int direction)
    {
        if (SelectedMasterLibrary is null) return;
        var index = MasterLibraries.IndexOf(SelectedMasterLibrary); var target = index + direction;
        if (target < 0 || target >= MasterLibraries.Count) return;
        MasterLibraries.Move(index, target); NormalizeLibraryPriorities();
        InvalidateProjectAnalysis(_analysis is not null || _frames.Count > 0);
        SaveState();
    }

    public void RefreshMasterLibraryStates() { foreach (var item in MasterLibraries) item.RefreshState(); Status = $"{MasterLibraries.Count(item => item.IsOnline && item.Enabled)}/{MasterLibraries.Count} librerie disponibili"; }

    private void AddMasterLibraryItem(MasterLibraryItem item)
    {
        item.PropertyChanged += MasterLibraryItem_PropertyChanged;
        MasterLibraries.Add(item);
    }

    private void MasterLibraryItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MasterLibraryItem.Enabled) or nameof(MasterLibraryItem.Path))) return;
        InvalidateProjectAnalysis(_analysis is not null || _frames.Count > 0);
        Status = HasSources ? "Configurazione Master aggiornata · rianalizza il progetto" : "Configurazione Master aggiornata";
        SaveState();
    }

    private void InvalidateProjectAnalysis(bool awaitingReanalysis)
    {
        _frames = [];
        _analysis = null;
        _statistics = null;
        _undo.Clear();
        SelectedNode = null;
        TreeRoots.Clear();
        InvalidateExportPlan();
        WbppKeywords.Clear();
        WbppNotes.Clear();
        AvailableFlatSets.Clear();
        FilterStatistics.Clear();
        SessionStatistics.Clear();
        NightStatistics.Clear();
        ReviewQueue.Clear();
        _allQualityFrames.Clear();
        QualityFrames.Clear();
        QualitySeries.Clear();
        SelectedQualityFrame = null;
        _selectedQualitySeries = null;
        Raise(nameof(SelectedQualitySeries));
        SelectedIssues.Clear();
        _searchText = "";
        Raise(nameof(SearchText));
        _showIssuesOnly = false;
        Raise(nameof(ShowIssuesOnly));
        Progress = 0;
        _awaitingReanalysis = awaitingReanalysis && HasSources;
        ReadinessText = HasSources ? $"{SourceSummary} · analisi richiesta" : "Aggiungi file o cartelle FITS/XISF";
        CalibrationSummary = "Seleziona uno o più Light per vedere le calibrazioni assegnate.";
        Raise(nameof(TotalFiles)); Raise(nameof(TotalIssues)); Raise(nameof(OverrideCount));
        Raise(nameof(UnresolvedCalibrations)); Raise(nameof(IsProjectReady)); Raise(nameof(PlanSummary));
        Raise(nameof(TotalIntegrationText)); Raise(nameof(StatisticsSummary)); Raise(nameof(StatisticsDateRange));
        Raise(nameof(ReviewQueueCount));
        RaiseProjectWorkflowProperties();
        ApplyLibraryOffsetCommand.RaiseCanExecuteChanged();
        ApplyProjectDefaultsCommand.RaiseCanExecuteChanged();
        ClearProjectCommand.RaiseCanExecuteChanged();
        LinkFlatSetCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
    }

    private void RaiseProjectWorkflowProperties()
    {
        Raise(nameof(HasSources));
        Raise(nameof(HasAnalysis));
        Raise(nameof(HasVisibleTree));
        Raise(nameof(ShowImportPrompt));
        Raise(nameof(ShowAnalysisPrompt));
        Raise(nameof(ShowNoResultsPrompt));
        Raise(nameof(CanAnalyzeProject));
        Raise(nameof(SourceSummary));
        Raise(nameof(SourceBreakdown));
        Raise(nameof(AnalysisPromptTitle));
        Raise(nameof(AnalysisPromptDetail));
        Raise(nameof(AnalysisActionLabel));
    }

    private void InvalidateExportPlan()
    {
        if (_exportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Cancelling) return;
        _plan = null;
        PlannedTreeRoots.Clear();
        Raise(nameof(PlanSummary));
        Raise(nameof(HasExportPlan));
        InvalidateExportPreflight();
    }

    private void InvalidateExportPreflight()
    {
        if (_exportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Cancelling) return;
        _exportPreflight = null;
        ExportPreflightFindings.Clear();
        ExportProgress = 0;
        SetExportState(ExportRunState.Idle);
        ExportProgressDetail = HasExportPlan ? "Anteprima pronta · controlli automatici inclusi nell’esportazione." : "Anteprima facoltativa · controlli automatici inclusi nell’esportazione.";
        RaiseExportProperties();
    }

    private void SetExportState(ExportRunState value)
    {
        if (_exportState == value) return;
        _exportState = value;
        RaiseExportProperties();
    }

    private void RaiseExportProperties()
    {
        Raise(nameof(HasExportPlan)); Raise(nameof(HasExportPreflight)); Raise(nameof(ExportPreflightReady));
        Raise(nameof(CanRunExportPreflight)); Raise(nameof(CanStartExport)); Raise(nameof(CanPauseExport));
        Raise(nameof(CanResumeExport)); Raise(nameof(CanCancelExport)); Raise(nameof(ExportStateLabel));
        Raise(nameof(ExportFileSummary)); Raise(nameof(ExportBytesSummary)); Raise(nameof(ExportSpaceSummary));
        Raise(nameof(ExportEtaSummary)); Raise(nameof(ExportResumeSummary));
    }

    private ExportPreflightOptions CurrentExportOptions() => new(
        Math.Clamp(ExportMarginPercent, 0, 100),
        (long)(Math.Max(0, ExportMinimumReserveGiB) * 1024 * 1024 * 1024),
        Math.Max(1, ExportEstimatedThroughputMiBps),
        SourcePaths.Concat(MasterLibraries.Where(item => item.Enabled).Select(item => item.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public async Task OrganizeMasterLibraryAsync(CancellationToken cancellationToken = default)
    {
        var requests = MasterOrganizerRequests();
        using var tracked = BeginTrackedOperation("Organizzazione Master Library", "AF-MASTER-ORGANIZE-START", $"Organizzazione di {requests.Length} Master avviata");
        try
        {
            var plan = await MasterLibraryOrganizer.PlanAsync(requests, MasterOrganizerDestination, cancellationToken);
            ApplyMasterOrganizerPlan(plan);
            var conflicts = plan.Count(item => item.Status != MasterOrganizationPlanStatus.Ready);
            if (conflicts > 0) throw new IOException($"Preflight non superato: risolvi {conflicts} conflitti evidenziati prima di copiare.");
            MasterOrganizerStatus = $"Organizzazione di {requests.Length} Master…";
            var results = await MasterLibraryOrganizer.ExecuteAsync(requests, MasterOrganizerDestination, cancellationToken);
            foreach (var item in MasterOrganizerItems) item.SetPreflight("Copiato e verificato");
            MasterOrganizerStatus = $"Completata · {results.Count} copie verificate · {results.Count(item => item.HeaderStamped)} header aggiornati";
            Status = MasterOrganizerStatus;
            tracked.Complete("AF-MASTER-ORGANIZE-OK", $"Organizzazione completata: {results.Count} copie verificate");
        }
        catch (Exception exception)
        {
            tracked.Fail("AF-MASTER-ORGANIZE-001", "Organizzazione Master Library non completata", exception);
            throw;
        }
    }

    public async Task PreviewMasterOrganizerAsync(CancellationToken cancellationToken = default)
    {
        var plan = await MasterLibraryOrganizer.PlanAsync(MasterOrganizerRequests(), MasterOrganizerDestination, cancellationToken);
        ApplyMasterOrganizerPlan(plan);
        var conflicts = plan.Count(item => item.Status != MasterOrganizationPlanStatus.Ready);
        MasterOrganizerStatus = conflicts == 0 ? $"Preflight superato · {plan.Count} Master pronti" : $"Preflight bloccato · {conflicts} conflitti su {plan.Count} Master";
        Status = MasterOrganizerStatus;
    }

    public async Task RollbackMasterOrganizerAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MasterOrganizerDestination)) throw new InvalidOperationException("Scegli la libreria da ripristinare.");
        using var tracked = BeginTrackedOperation("Rollback Master Library", "AF-MASTER-ROLLBACK-START", "Rollback verificato avviato");
        try
        {
            var removed = await MasterLibraryOrganizer.RollbackAsync(MasterOrganizerDestination, cancellationToken);
            foreach (var item in MasterOrganizerItems) item.SetPreflight("Rollback completato");
            MasterOrganizerStatus = $"Rollback completato · rimosse {removed} copie verificate · originali intatti";
            Status = MasterOrganizerStatus;
            tracked.Complete("AF-MASTER-ROLLBACK-OK", $"Rollback completato: {removed} copie rimosse");
        }
        catch (Exception exception)
        {
            tracked.Fail("AF-MASTER-ROLLBACK-001", "Rollback Master Library non completato", exception);
            throw;
        }
    }

    private MasterOrganizationRequest[] MasterOrganizerRequests()
    {
        if (string.IsNullOrWhiteSpace(MasterOrganizerDestination)) throw new InvalidOperationException("Scegli la destinazione della nuova Master Library.");
        var invalid = MasterOrganizerItems.Where(item => !item.TryRequest(out _)).ToArray();
        if (invalid.Length > 0) throw new InvalidOperationException($"Completa prima i metadati di {invalid.Length} Master evidenziati.");
        return MasterOrganizerItems.Select(item => { item.TryRequest(out var request); return request!; }).ToArray();
    }

    private void ApplyMasterOrganizerPlan(IReadOnlyList<MasterOrganizationPlanItem> plan)
    {
        var bySource = plan.ToDictionary(item => item.Request.Source.Path, PathIdentity.Comparer);
        foreach (var item in MasterOrganizerItems)
            item.SetPreflight(bySource.TryGetValue(item.Frame.Path, out var value) ? value.Status == MasterOrganizationPlanStatus.Ready ? "Pronto" : $"CONFLITTO · {value.Message}" : "Non verificato");
    }

    public async Task ScanMasterLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var libraries = MasterLibraries.Where(item => item.Enabled && item.IsOnline).OrderBy(item => item.Priority).ToArray();
        if (libraries.Length == 0) throw new InvalidOperationException("Aggiungi o abilita almeno una Master Library online.");
        using var tracked = BeginTrackedOperation("Scansione Master Library", "AF-MASTER-SCAN-START", $"Scansione di {libraries.Length} librerie avviata");
        try
        {
            MasterOrganizerStatus = $"Scansione indipendente di {libraries.Length} librerie…";
            var progress = new Progress<ScanProgress>(item => MasterOrganizerStatus = $"Lettura Master {item.Completed}/{item.Total} · {item.CurrentFile}");
            _masterLibraryFrames = await _scanner.ScanAsync(libraries.Select(item => item.Path), new SessionSettings(TimeZoneInfo.Local, new TimeOnly(SessionBoundaryHour, 0)), progress, cancellationToken, _headerCache);
            foreach (var library in libraries) LibraryMetadataResolver.Apply(_masterLibraryFrames, library.Path, library.Priority);
            ProjectMetadataDefaultsResolver.Apply(_masterLibraryFrames, CurrentProjectDefaults());
            RefreshMasterOrganizer(_masterLibraryFrames);
            MasterOrganizerStatus = $"{MasterOrganizerItems.Count} Master · {MasterOrganizerItems.Count(item => item.IsReady)} pronti · {MasterOrganizerItems.Count(item => !item.IsReady)} da completare";
            tracked.Complete("AF-MASTER-SCAN-OK", $"Scansione Master completata: {MasterOrganizerItems.Count} elementi");
        }
        catch (Exception exception)
        {
            tracked.Fail("AF-MASTER-SCAN-001", "Scansione Master Library non completata", exception);
            throw;
        }
    }

    private void NormalizeLibraryPriorities() { for (var index = 0; index < MasterLibraries.Count; index++) MasterLibraries[index].Priority = index + 1; Raise(nameof(LibraryPath)); }

    public void RefreshManualSelection()
    {
        RefreshFlatSetOptions();
        Raise(nameof(ManualLinkSelectionText));
        LinkFlatSetCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
    }

    public void SaveState()
    {
        SyncStateFromViewModel();
        AppStateStore.Save(_state);
        if (!string.IsNullOrWhiteSpace(CurrentProjectFile)) SaveProjectDocument(CurrentProjectFile);
    }

    private void SyncStateFromViewModel()
    {
        _state.SourcePaths = SourcePaths.ToList();
        _state.LibraryPath = LibraryPath;
        _state.MasterLibraries = MasterLibraries.Select(item => item.ToDefinition()).ToList();
        _state.DestinationPath = DestinationPath;
        _state.ProjectName = ProjectName;
        _state.SessionBoundaryHour = SessionBoundaryHour;
        _state.ProjectDefaultGain = ParseDefault(ProjectDefaultGain);
        _state.ProjectDefaultOffset = ParseDefault(ProjectDefaultOffset);
        _state.ProjectDefaultTemperatureC = ParseDefault(ProjectDefaultTemperature);
        _state.LastProjectFile = CurrentProjectFile;
        _state.UiDensity = UiDensity;
        _state.ReducedMotion = ReducedMotion;
        _state.CheckForUpdates = CheckForUpdates;
        _state.UpdateChannel = UpdateChannel;
        _state.ExportMarginPercent = ExportMarginPercent;
        _state.ExportMinimumReserveGiB = ExportMinimumReserveGiB;
        _state.ExportEstimatedThroughputMiBps = ExportEstimatedThroughputMiBps;
        _state.ExcludedQualityPaths = _excludedQualityPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _state.QualitySigmaThreshold = QualitySigmaThreshold;
        _state.QualityStretchStrength = QualityStretchStrength;
        _state.QualityDebayerPreview = QualityDebayerPreview;
        _state.SourcePanelWidth = SourcePanelWidth;
        _state.InspectorPanelWidth = InspectorPanelWidth;
        foreach (var frame in _frames)
        {
            if (!HasAnyOverride(frame) && !_kindOverrides.Contains(frame.Path)) { _state.Overrides.Remove(frame.Path); continue; }
            var snapshot = AppStateStore.Snapshot(frame);
            if (!_kindOverrides.Contains(frame.Path)) snapshot.Kind = null;
            _state.Overrides[frame.Path] = snapshot;
        }
    }

    public void SaveProject(string path)
    {
        CurrentProjectFile = Path.GetFullPath(path);
        SaveState();
        Status = $"Progetto salvato · {Path.GetFileName(CurrentProjectFile)}";
    }

    public async Task LoadProjectAsync(string path)
    {
        if (_pendingRecovery is not null) throw new InvalidOperationException("Ripristina oppure ignora prima il recovery journal mostrato in alto.");
        var document = ProjectDocumentStore.Load(path);
        ApplyProjectDocument(document, Path.GetFullPath(path));
        Status = $"Progetto aperto · {Path.GetFileName(CurrentProjectFile)}";
        if (SourcePaths.Count > 0) await ScanAsync(); else SaveState();
    }

    private void ApplyProjectDocument(AstroForgeProjectDocument document, string projectFile)
    {
        CurrentProjectFile = string.IsNullOrWhiteSpace(projectFile) ? "" : Path.GetFullPath(projectFile);
        _projectCreatedAt = document.CreatedAt;
        SourcePaths.Clear();
        foreach (var source in document.SourcePaths) SourcePaths.Add(source);
        foreach (var library in MasterLibraries) library.PropertyChanged -= MasterLibraryItem_PropertyChanged;
        MasterLibraries.Clear();
        var projectLibraries = document.MasterLibraries.Count > 0 ? document.MasterLibraries : string.IsNullOrWhiteSpace(document.LibraryPath) ? [] : [new() { Name = "Libreria principale", Path = document.LibraryPath, Priority = 1 }];
        foreach (var library in projectLibraries.OrderBy(item => item.Priority)) AddMasterLibraryItem(new(library.Name, library.Path, library.Priority, library.Enabled));
        _libraryPath = document.LibraryPath;
        Raise(nameof(LibraryPath));
        DestinationPath = document.DestinationPath;
        ProjectName = document.ProjectName;
        _sessionBoundaryHour = Math.Clamp(document.SessionBoundaryHour, 0, 23);
        Raise(nameof(SessionBoundaryHour));
        ProjectDefaultGain = Input(document.DefaultGain);
        ProjectDefaultOffset = Input(document.DefaultOffset);
        ProjectDefaultTemperature = Input(document.DefaultTemperatureC);
        _qualitySigmaThreshold = Math.Clamp(document.QualitySigmaThreshold, 2, 6); Raise(nameof(QualitySigmaThreshold));
        _excludedQualityPaths.Clear(); foreach (var excluded in document.ExcludedQualityPaths) _excludedQualityPaths.Add(excluded);
        _state.Overrides = new(document.Overrides, StringComparer.OrdinalIgnoreCase);
        _kindOverrides.Clear();
        foreach (var pair in _state.Overrides.Where(pair => pair.Value.Kind is not null)) _kindOverrides.Add(pair.Key);
        InvalidateProjectAnalysis(false);
    }

    private AstroForgeProjectDocument CreateProjectDocument() => new()
    {
        CreatedAt = _projectCreatedAt, ProjectName = ProjectName, SourcePaths = SourcePaths.ToList(), LibraryPath = LibraryPath, MasterLibraries = MasterLibraries.Select(item => item.ToDefinition()).ToList(),
        DestinationPath = DestinationPath, SessionBoundaryHour = SessionBoundaryHour, DefaultGain = ParseDefault(ProjectDefaultGain),
        DefaultOffset = ParseDefault(ProjectDefaultOffset), DefaultTemperatureC = ParseDefault(ProjectDefaultTemperature),
        QualitySigmaThreshold = QualitySigmaThreshold, ExcludedQualityPaths = _excludedQualityPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
        Overrides = new(_state.Overrides, StringComparer.OrdinalIgnoreCase)
    };

    private void SaveProjectDocument(string path) => ProjectDocumentStore.Save(path, CreateProjectDocument());

    private TrackedOperation BeginTrackedOperation(string operation, string startCode, string message)
    {
        if (_pendingRecovery is not null) throw new InvalidOperationException("Ripristina oppure ignora prima il recovery journal mostrato in alto.");
        SyncStateFromViewModel();
        var snapshot = new ProjectRecoverySnapshot { ProjectFile = CurrentProjectFile, Document = CreateProjectDocument() };
        var journal = _recoveryJournal.Begin(operation, snapshot);
        var log = _eventLog.BeginOperation(operation, startCode, message, journal.OperationId);
        return new(log, _recoveryJournal, journal.OperationId);
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var availableLibraries = MasterLibraries.Where(item => item.Enabled && item.IsOnline).ToArray();
        if (SourcePaths.Count == 0) { Status = "Aggiungi almeno una sorgente FITS/XISF. Il Master Library Lab resta disponibile separatamente."; return; }
        using var tracked = BeginTrackedOperation("Analisi progetto", "AF-SCAN-START", "Analisi progetto avviata");
        IsScanning = true;
        Progress = 0;
        Status = "Lettura header FITS/XISF…";
        try
        {
            var activeLibraries = availableLibraries.OrderBy(item => item.Priority).ToArray();
            var roots = SourcePaths.Concat(activeLibraries.Select(item => item.Path));
            var progress = new Progress<ScanProgress>(item =>
            {
                Progress = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
                Status = $"{item.Completed}/{item.Total} · {item.CurrentFile}";
            });
            var sessionSettings = new SessionSettings(TimeZoneInfo.Local, new TimeOnly(SessionBoundaryHour, 0));
            _frames = await _scanner.ScanAsync(roots, sessionSettings, progress, cancellationToken, _headerCache);
            foreach (var library in activeLibraries) LibraryMetadataResolver.Apply(_frames, library.Path, library.Priority);
            foreach (var frame in _frames)
                if (_state.Overrides.TryGetValue(frame.Path, out var saved)) { AppStateStore.Apply(frame, saved); FrameValidator.Revalidate(frame); }
            var defaultsApplied = ProjectMetadataDefaultsResolver.Apply(_frames, CurrentProjectDefaults());
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                var targets = _frames.Where(frame => frame.Kind == FrameKind.Light).Select(frame => frame.ObjectName.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (targets.Length == 1) ProjectName = targets[0]!;
            }
            if (string.IsNullOrWhiteSpace(DestinationPath) && SourcePaths.Count > 0)
                DestinationPath = Directory.GetParent(SourcePaths[0])?.FullName ?? SourcePaths[0];
            RefreshIntelligence();
            RebuildTree();
            _awaitingReanalysis = false;
            RaiseProjectWorkflowProperties();
            Status = $"{TotalFiles} file analizzati · {_scanner.LastCacheHits} da cache · {_scanner.LastParsedFiles} letti · {TotalIssues} segnalazioni";
            Raise(nameof(TotalFiles)); Raise(nameof(TotalIssues)); Raise(nameof(OverrideCount));
            ApplyLibraryOffsetCommand.RaiseCanExecuteChanged();
            ApplyProjectDefaultsCommand.RaiseCanExecuteChanged();
            ClearProjectCommand.RaiseCanExecuteChanged();
            SaveState();
            tracked.Complete("AF-SCAN-OK", $"Analisi completata: {TotalFiles} file, {TotalIssues} segnalazioni");
        }
        catch (Exception exception)
        {
            tracked.Fail("AF-SCAN-001", "Analisi progetto non completata", exception);
            throw;
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void BuildPlan()
    {
        if (_analysis?.Ready != true) throw new InvalidOperationException("Risolvi prima tutte le calibrazioni evidenziate.");
        if (string.IsNullOrWhiteSpace(ProjectName)) throw new InvalidOperationException("Inserisci il nome del progetto.");
        if (string.IsNullOrWhiteSpace(DestinationPath)) throw new InvalidOperationException("Seleziona la cartella di destinazione.");
        _plan = ProjectExporter.BuildPlan(ProjectName, DestinationPath, _analysis, _excludedQualityPaths);
        BuildPlannedTree();
        InvalidateExportPreflight();
        Raise(nameof(PlanSummary)); Raise(nameof(HasExportPlan)); Raise(nameof(CanRunExportPreflight));
        Status = $"Anteprima pronta: {_plan.Files.Count} file, {HumanSize(_plan.RequiredBytes)} · i controlli verranno eseguiti automaticamente";
    }

    public async Task AnalyzeQualityAsync(CancellationToken cancellationToken = default)
    {
        var series = SelectedQualitySeries ?? throw new InvalidOperationException("Seleziona prima una serie di calibrazione.");
        var lights = series.SourceFrames.Where(IsQualityFits).ToArray();
        if (lights.Length == 0) throw new InvalidOperationException("La serie selezionata non contiene Light FITS supportati.");
        foreach (var previous in series.Frames) _allQualityFrames.Remove(previous);
        series.SetAnalysisResults([]);
        QualityFrames.Clear();
        SelectedQualityFrame = null;
        IsQualityAnalyzing = true;
        QualityProgress = 0;
        QualityStatus = $"{series.DisplayName} · analisi pixel di {lights.Length} Light…";
        var results = new List<QualityFrameRow>(lights.Length);
        try
        {
            for (var index = 0; index < lights.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = lights[index];
                try
                {
                    // Decodifica e misure sono intenzionalmente fuori dal dispatcher WPF:
                    // Blink, annulla e navigazione devono restare reattivi anche su FITS grandi.
                    var metrics = await Task.Run(
                        () => FitsQualityAnalyzer.AnalyzeAsync(frame.Path, cancellationToken),
                        cancellationToken);
                    var row = new QualityFrameRow(frame, metrics, _excludedQualityPaths.Contains(frame.Path), series.ConfigurationSession);
                    results.Add(row); _allQualityFrames.Add(row); QualityFrames.Add(row);
                }
                catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
                {
                    var row = QualityFrameRow.Failed(frame, exception.Message, _excludedQualityPaths.Contains(frame.Path), series.ConfigurationSession);
                    results.Add(row); _allQualityFrames.Add(row); QualityFrames.Add(row);
                }
                QualityProgress = (index + 1) * 100d / lights.Length;
                QualityStatus = $"{index + 1}/{lights.Length} · {frame.FileName}";
            }
            series.SetAnalysisResults(results);
            ScoreQualityOutliers();
            RebuildQualitySeriesView();
            QualityStatus = $"{series.DisplayName} · analisi completata · {results.Count(item => item.Error is null)} misurati · {series.SuspectCount} sospetti";
            Raise(nameof(QualitySuspectCount)); Raise(nameof(QualityExcludedCount)); Raise(nameof(CanChangeQualityExclusions));
        }
        catch (OperationCanceledException)
        {
            series.SetAnalysisResults(results);
            ScoreQualityOutliers();
            RebuildQualitySeriesView();
            QualityStatus = $"{series.DisplayName} · analisi annullata · {results.Count} risultati parziali conservati";
            throw;
        }
        finally { IsQualityAnalyzing = false; }
    }

    public void ExcludeQualitySuspects()
    {
        foreach (var item in SelectedQualitySeries?.Frames.Where(item => item.IsSuspect) ?? []) SetQualityExcluded(item, true);
        SaveState(); InvalidateExportPlan(); RefreshQualityCounts();
    }

    public void SetQualitySelection(IReadOnlyCollection<QualityFrameRow> rows)
    {
        QualitySelectedCount = rows.Count;
        if (rows.Count > 0 && (SelectedQualityFrame is null || !rows.Contains(SelectedQualityFrame))) SelectedQualityFrame = rows.Last();
    }

    public void ExcludeSelectedQualityFrames(IEnumerable<QualityFrameRow> rows)
    {
        foreach (var item in rows) SetQualityExcluded(item, true);
        SaveState(); InvalidateExportPlan(); RefreshQualityCounts();
    }

    public async Task RenderQualityPreviewAsync(QualityFrameRow? item, CancellationToken cancellationToken = default, bool fullResolution = false)
    {
        if (item is null || item.Error is not null) return;
        if (_highResolutionQualityItem is not null && !ReferenceEquals(_highResolutionQualityItem, item))
        {
            _highResolutionQualityItem.ResetPreview(); _highResolutionQualityItem = null;
        }
        var key = $"{QualityDebayerPreview}|{QualityStretchStrength:0.0}|{(fullResolution ? "full" : "screen")}";
        if (item.PreviewKey == key) return;
        var preview = await Task.Run(
            () => FitsQualityAnalyzer.RenderPreviewAsync(item.Path, item.Frame.BayerPattern.Value, QualityDebayerPreview, QualityStretchStrength, cancellationToken, fullResolution ? 2400 : 960),
            cancellationToken);
        item.SetPreview(preview, key);
        if (fullResolution) _highResolutionQualityItem = item;
    }

    public void ToggleSelectedQualityExclusion()
    {
        if (SelectedQualityFrame is null) return;
        SetQualityExcluded(SelectedQualityFrame, !SelectedQualityFrame.IsExcluded);
        SaveState(); InvalidateExportPlan(); RefreshQualityCounts();
    }

    public void RestoreAllQualityFrames()
    {
        foreach (var item in _allQualityFrames) SetQualityExcluded(item, false);
        SaveState(); InvalidateExportPlan(); RefreshQualityCounts();
    }

    private void SetQualityExcluded(QualityFrameRow item, bool excluded)
    {
        item.IsExcluded = excluded;
        if (excluded) _excludedQualityPaths.Add(item.Path); else _excludedQualityPaths.Remove(item.Path);
    }

    private void RefreshQualityCounts()
    {
        foreach (var series in QualitySeries) series.RefreshCounts();
        Raise(nameof(QualitySuspectCount)); Raise(nameof(QualityExcludedCount)); Raise(nameof(QualityPendingSuspectCount));
        Raise(nameof(CanExcludeQualitySuspects)); Raise(nameof(ExcludeQualitySuspectsLabel)); Raise(nameof(QualityTableSummary));
        Raise(nameof(QualityChartFrames));
        QualityStatus = SelectedQualitySeries is null
            ? $"{_allQualityFrames.Count(item => item.Error is null)} misurati"
            : $"{SelectedQualitySeries.DisplayName} · {SelectedQualitySeries.Frames.Count(item => item.Error is null)} misurati · {QualitySuspectCount} sospetti · {QualityExcludedCount} esclusi";
    }

    private void ScoreQualityOutliers()
    {
        foreach (var group in _allQualityFrames.Where(item => item.Error is null).GroupBy(item => $"{item.Filter}|{item.ConfigurationSession}|{item.ExposureSeconds:0.###}", StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToArray();
            if (rows.Length < 5) { foreach (var row in rows) row.SetScore(0, false, "Serie troppo piccola per rilevare outlier"); continue; }
            var fwhm = Robust(rows, item => item.Fwhm);
            var eccentricity = Robust(rows, item => item.Eccentricity);
            var noise = Robust(rows, item => item.Noise);
            var snr = Robust(rows, item => item.Snr);
            var stars = Robust(rows, item => item.StarCount);
            foreach (var row in rows)
            {
                var parts = new[]
                {
                    (Name: "FWHM", Z: PositiveZ(row.Fwhm, fwhm)),
                    (Name: "eccentricità", Z: PositiveZ(row.Eccentricity, eccentricity)),
                    (Name: "rumore", Z: PositiveZ(row.Noise, noise)),
                    (Name: "SNR basso", Z: NegativeZ(row.Snr, snr)),
                    (Name: "poche stelle", Z: NegativeZ(row.StarCount, stars))
                };
                var worst = parts.OrderByDescending(item => item.Z).First();
                var score = parts.Where(item => item.Z > 0).Sum(item => item.Z * item.Z) is var sum ? Math.Sqrt(sum) : 0;
                // One visible rule: the chart marker, orange points and suspect list all use this exact score threshold.
                var suspect = score >= QualitySigmaThreshold;
                row.SetScore(score, suspect, worst.Z >= 2 ? $"Anomalia principale: {worst.Name} ({worst.Z:0.0}σ)" : "Coerente con la serie");
            }
        }
    }

    private void BuildQualitySeriesDefinitions()
    {
        _allQualityFrames.Clear();
        QualityFrames.Clear();
        QualitySeries.Clear();
        if (_analysis is null) return;
        foreach (var group in _analysis.Lights.GroupBy(item => new
                 {
                     Filter = string.IsNullOrWhiteSpace(item.Light.FilterName.Value) ? "Senza filtro" : item.Light.FilterName.Value!.Trim(),
                     Session = item.FlatGroup?.Id ?? "Sessione non risolta"
                 })
                 .OrderBy(group => group.Key.Filter, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(group => group.Key.Session, StringComparer.OrdinalIgnoreCase))
            QualitySeries.Add(new QualitySeriesRow(group.Key.Filter, group.Key.Session, group.Select(item => item.Light).ToArray()));
        SelectedQualitySeries = QualitySeries.FirstOrDefault();
        QualityStatus = QualitySeries.Count == 0
            ? "Nessuna serie Light disponibile nel progetto."
            : $"{QualitySeries.Count} serie disponibili · selezionane una e avvia l’analisi pixel";
        Raise(nameof(CanRunQualityAnalysis));
    }

    private void RebuildQualitySeriesView()
    {
        var previousSelection = SelectedQualityFrame;
        QualityFrames.Clear();
        if (SelectedQualitySeries is not null)
            foreach (var row in SelectedQualitySeries.Frames.Where(row => !QualityShowOnlySuspects || row.IsSuspect)) QualityFrames.Add(row);
        SelectedQualityFrame = previousSelection is not null && QualityFrames.Contains(previousSelection)
            ? previousSelection
            : QualityFrames.OrderByDescending(item => item.OutlierScore).FirstOrDefault();
        QualitySelectedCount = 0;
        if (SelectedQualitySeries is null)
            QualityStatus = "Nessuna serie selezionata.";
        else if (!SelectedQualitySeries.IsAnalyzed)
            QualityStatus = $"{SelectedQualitySeries.DisplayName} · {SelectedQualitySeries.FrameCount} Light in {SelectedQualitySeries.NightCount} notti · non ancora analizzata";
        else
            RefreshQualityCounts();
        Raise(nameof(CanChangeQualityExclusions)); Raise(nameof(CanExcludeQualitySuspects)); Raise(nameof(ExcludeQualitySuspectsLabel));
        Raise(nameof(QualityChartFrames)); Raise(nameof(QualityTableSummary));
    }

    private static bool IsQualityFits(FrameMetadata frame) =>
        frame.Kind == FrameKind.Light && new[] { ".fit", ".fits", ".fts" }.Contains(Path.GetExtension(frame.Path), StringComparer.OrdinalIgnoreCase);

    private static (double Median, double Scale) Robust(QualityFrameRow[] rows, Func<QualityFrameRow, double> selector)
    {
        var values = rows.Select(selector).OrderBy(value => value).ToArray();
        var median = values[values.Length / 2];
        var deviations = values.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToArray();
        return (median, Math.Max(1e-9, deviations[deviations.Length / 2] * 1.4826));
    }
    private static double PositiveZ(double value, (double Median, double Scale) stats) => Math.Max(0, (value - stats.Median) / stats.Scale);
    private static double NegativeZ(double value, (double Median, double Scale) stats) => Math.Max(0, (stats.Median - value) / stats.Scale);

    public async Task RunExportPreflightAsync(CancellationToken cancellationToken = default)
    {
        if (_plan is null) BuildPlan();
        var plan = _plan ?? throw new InvalidOperationException("Impossibile costruire il piano di esportazione.");
        SetExportState(ExportRunState.Preflighting);
        IsScanning = true;
        ExportProgress = 0;
        ExportProgressDetail = "Dry-run: sorgenti, staging, spazio e percorsi…";
        using var operation = _eventLog.BeginOperation("Preflight export", "AF-EXPORT-PREFLIGHT-START", "Dry-run export avviato");
        try
        {
            var report = await ProjectExportPreflight.AnalyzeAsync(plan, CurrentExportOptions(), cancellationToken);
            ApplyExportPreflight(report);
            SetExportState(report.IsReady ? ExportRunState.Ready : ExportRunState.Blocked);
            ExportProgressDetail = report.IsReady
                ? $"Dry-run superato · {report.WarningCount} avvisi · nessun file scritto"
                : $"Export bloccato · {report.ErrorCount} errori · {report.WarningCount} avvisi";
            Status = ExportProgressDetail;
            if (report.IsReady) operation.Complete("AF-EXPORT-PREFLIGHT-OK", ExportProgressDetail);
            else operation.Fail("AF-EXPORT-PREFLIGHT-BLOCKED", ExportProgressDetail, new ExportPreflightException(report));
        }
        catch (Exception exception)
        {
            SetExportState(ExportRunState.Failed);
            ExportProgressDetail = "Preflight non completato";
            operation.Fail("AF-EXPORT-PREFLIGHT-001", "Preflight export non completato", exception);
            throw;
        }
        finally { IsScanning = false; RaiseExportProperties(); }
    }

    private void ApplyExportPreflight(ExportPreflightReport report)
    {
        _exportPreflight = report;
        ExportPreflightFindings.Clear();
        foreach (var finding in report.Findings)
            ExportPreflightFindings.Add(new(finding.Severity.ToString(), finding.Code, finding.Title, finding.Detail, finding.Path ?? ""));
        RaiseExportProperties();
    }

    public string ExportStatistics(string destinationRoot)
    {
        if (_statistics is null) throw new InvalidOperationException("Analizza prima il progetto.");
        var folderName = string.IsNullOrWhiteSpace(ProjectName) ? "AstroForge-Statistics" : $"{ProjectName}-Statistics";
        var output = Path.Combine(destinationRoot, string.Concat(folderName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "project-statistics.json"), JsonSerializer.Serialize(_statistics, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "filters.csv"), CsvFilters(_statistics), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(output, "sessions.csv"), CsvSessions(_statistics), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(output, "nights.csv"), CsvNights(_statistics), new UTF8Encoding(true));
        Status = $"Statistiche esportate: {output}";
        return output;
    }

    public string SupportBundlePreview => string.Join(Environment.NewLine, SupportBundleBuilder.PreviewEntries.Select(entry => $"• {entry}"));

    public void RefreshDiagnostics()
    {
        var events = _eventLog.ReadRecent();
        DiagnosticEvents.Clear();
        foreach (var item in events)
            DiagnosticEvents.Add(new(
                item.Timestamp.ToLocalTime().ToString("dd MMM HH:mm:ss"),
                item.Level,
                item.Code,
                item.Operation ?? "—",
                string.IsNullOrWhiteSpace(item.OperationId) ? "—" : item.OperationId[..Math.Min(8, item.OperationId.Length)],
                item.Message,
                item.ExceptionType ?? ""));
        var errors = events.Count(item => item.Level is "Error" or "Critical");
        var operations = events.Where(item => !string.IsNullOrWhiteSpace(item.OperationId)).Select(item => item.OperationId).Distinct(StringComparer.Ordinal).Count();
        DiagnosticsSummary = $"{events.Count} eventi recenti · {errors} errori · {operations} operazioni correlate";
    }

    public async Task RestoreRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingRecovery is null) return;
        var recovery = _pendingRecovery;
        ApplyProjectDocument(recovery.Snapshot.Document, recovery.Snapshot.ProjectFile);
        _recoveryJournal.Discard();
        _pendingRecovery = null;
        Raise(nameof(HasRecoverySnapshot));
        Raise(nameof(CanRunProjectOperations));
        Raise(nameof(CanAnalyzeProject));
        Raise(nameof(RecoverySummary));
        _eventLog.Write("Information", "AF-RECOVERY-RESTORED", "Fotografia progetto ripristinata", operationId: recovery.OperationId, operation: recovery.Operation);
        Status = "Progetto ripristinato dal recovery journal";
        if (SourcePaths.Count > 0) await ScanAsync(cancellationToken); else SaveState();
    }

    public void DiscardRecovery()
    {
        if (_pendingRecovery is null) return;
        var recovery = _pendingRecovery;
        _recoveryJournal.Discard();
        _pendingRecovery = null;
        Raise(nameof(HasRecoverySnapshot));
        Raise(nameof(CanRunProjectOperations));
        Raise(nameof(CanAnalyzeProject));
        Raise(nameof(RecoverySummary));
        _eventLog.Write("Information", "AF-RECOVERY-DISCARDED", "Fotografia di recupero ignorata", operationId: recovery.OperationId, operation: recovery.Operation);
        Status = "Recovery journal ignorato · progetto corrente invariato";
    }

    public async Task<string> ExportSupportBundleAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        using var operation = _eventLog.BeginOperation("Pacchetto diagnostico", "AF-SUPPORT-START", "Creazione pacchetto diagnostico avviata");
        var allFrames = _frames.Concat(_masterLibraryFrames).DistinctBy(frame => frame.Path, PathIdentity.Comparer).ToArray();
        var issues = allFrames.SelectMany(frame => frame.Issues).GroupBy(issue => new { issue.Code, issue.Severity })
            .Select(group => new SupportIssueSummary(group.Key.Code, group.Key.Severity.ToString(), group.Count())).OrderBy(item => item.Code).ToArray();
        var settings = new Dictionary<string, object?>
        {
            ["sessionBoundaryHour"] = SessionBoundaryHour,
            ["defaultGainConfigured"] = ParseDefault(ProjectDefaultGain).HasValue,
            ["defaultOffsetConfigured"] = ParseDefault(ProjectDefaultOffset).HasValue,
            ["defaultTemperatureConfigured"] = ParseDefault(ProjectDefaultTemperature).HasValue,
            ["uiDensity"] = UiDensity,
            ["reducedMotion"] = ReducedMotion,
            ["sourceCount"] = SourcePaths.Count,
            ["masterLibraryCount"] = MasterLibraries.Count,
            ["enabledMasterLibraryCount"] = MasterLibraries.Count(item => item.Enabled)
        };
        var diagnostics = new Dictionary<string, object?>
        {
            ["frameCount"] = allFrames.Length,
            ["frameKinds"] = allFrames.GroupBy(frame => frame.Kind.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            ["issueCount"] = allFrames.Sum(frame => frame.Issues.Count),
            ["unresolvedCalibrationCount"] = UnresolvedCalibrations,
            ["hasAnalysis"] = _analysis is not null,
            ["hasExportPlan"] = _plan is not null,
            ["recoverySnapshotAvailable"] = HasRecoverySnapshot,
            ["recentCorrelatedOperationCount"] = _eventLog.ReadRecent().Where(item => !string.IsNullOrWhiteSpace(item.OperationId)).Select(item => item.OperationId).Distinct(StringComparer.Ordinal).Count(),
            ["headerCacheHitsLastScan"] = _scanner.LastCacheHits,
            ["headersParsedLastScan"] = _scanner.LastParsedFiles
        };
        var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";
        try
        {
            var result = await SupportBundleBuilder.BuildAsync(new(outputPath, version, settings, diagnostics, issues, _eventLog.Files), cancellationToken);
            operation.Complete("AF-SUPPORT-EXPORTED", $"Pacchetto diagnostico creato con {result.Entries.Count} elementi");
            Status = $"Pacchetto diagnostico creato · {result.Entries.Count} elementi";
            return result.Path;
        }
        catch (Exception exception)
        {
            operation.Fail("AF-SUPPORT-001", "Pacchetto diagnostico non creato", exception);
            throw;
        }
    }

    public void RecordError(string code, Exception exception) => _eventLog.Write("Error", code, "Operazione non completata", exception);

    public void ClearHeaderCache()
    {
        _headerCache.Clear();
        Status = "Cache header svuotata · i file verranno riletti alla prossima analisi";
    }

    public void SelectReviewItem(ReviewQueueItem? item)
    {
        if (item is null) return;
        SelectedNode = Descendants(TreeRoots).FirstOrDefault(node => node.IsLeaf && node.Frames.Any(frame => PathIdentity.Equals(frame.Path, item.Frame.Path)))
            ?? new ProjectTreeNode { Key = $"review:{item.Frame.Path}", Name = item.Frame.FileName, Detail = item.Frame.Path, Icon = "!", Frames = [item.Frame] };
        Status = $"Revisione · {item.Calibration} · {item.Frame.FileName}";
    }

    public void AssignReviewCandidate(ReviewQueueItem? item, ReviewAssignmentScope scope)
    {
        if (item?.SelectedCandidate is null || item.Calibration == "Flat") return;
        var targets = scope switch
        {
            ReviewAssignmentScope.Night => _frames.Where(frame => frame.Kind == FrameKind.Light && NormalizeText(frame.FilterName.Value) == NormalizeText(item.Frame.FilterName.Value) && NormalizeText(frame.SessionId.Value) == NormalizeText(item.Frame.SessionId.Value)).ToArray(),
            ReviewAssignmentScope.Configuration => _frames.Where(frame => CalibrationScopeMatcher.Matches(item.Frame, frame, item.Calibration == "Dark" ? FrameKind.Dark : FrameKind.Bias)).ToArray(),
            _ => [item.Frame]
        };
        var previous = targets.Select(frame => (Frame: frame, Dark: frame.ManualDarkPath.HasOverride ? frame.ManualDarkPath.OverrideValue : null, HasDark: frame.ManualDarkPath.HasOverride, Bias: frame.ManualBiasPath.HasOverride ? frame.ManualBiasPath.OverrideValue : null, HasBias: frame.ManualBiasPath.HasOverride)).ToArray();
        foreach (var frame in targets)
            if (item.Calibration == "Dark") frame.ManualDarkPath.SetOverride(item.SelectedCandidate.Path); else frame.ManualBiasPath.SetOverride(item.SelectedCandidate.Path);
        _undo.Push(() =>
        {
            foreach (var value in previous)
            {
                if (item.Calibration == "Dark") { value.Frame.ManualDarkPath.ClearOverride(); if (value.HasDark) value.Frame.ManualDarkPath.SetOverride(value.Dark); }
                else { value.Frame.ManualBiasPath.ClearOverride(); if (value.HasBias) value.Frame.ManualBiasPath.SetOverride(value.Bias); }
            }
            RefreshIntelligence(); RebuildTree(); SaveState();
        });
        RefreshIntelligence(); RebuildTree(); SaveState(); UndoCommand.RaiseCanExecuteChanged();
        var scopeLabel = scope switch { ReviewAssignmentScope.Night => "nella notte", ReviewAssignmentScope.Configuration => "con la stessa firma tecnica", _ => "selezionato" };
        Status = $"{item.Calibration} assegnato manualmente a {targets.Length} Light {scopeLabel}";
    }

    private void SaveSettings()
    {
        SaveState();
        Status = "Impostazioni, percorsi e override salvati";
    }

    private void ClearProject()
    {
        InvalidateProjectAnalysis(false);
        Status = "Progetto svuotato dalla memoria · nessun file originale è stato cancellato";
    }

    private bool CanLinkFlatSet()
    {
        if (IsScanning || SelectedFlatSet is null) return false;
        var lights = GetManualLinkFrames().Where(frame => frame.Kind == FrameKind.Light).ToArray();
        if (lights.Length == 0) return false;
        var filter = NormalizeText(SelectedFlatSet.Filter);
        return lights.All(light => NormalizeText(light.FilterName.Value) == filter);
    }

    private void LinkFlatSet()
    {
        if (!CanLinkFlatSet() || SelectedFlatSet is null) return;
        var lights = GetManualLinkFrames().Where(frame => frame.Kind == FrameKind.Light).ToArray();
        var flats = SelectedFlatSet.Group.Frames.ToArray();
        var affected = lights.Concat(flats).Distinct().ToArray();
        var undoActions = new List<Action>();
        foreach (var frame in affected) ApplyField(frame.FlatSetId, SelectedFlatSet.LinkId, undoActions);
        _undo.Push(() => { foreach (var action in undoActions.AsEnumerable().Reverse()) action(); RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState(); });
        var linkedName = SelectedFlatSet.Display;
        RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState();
        Status = $"Flat Set collegato a {lights.Length} Light · {linkedName}";
        UndoCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
    }

    private void UnlinkFlatSet()
    {
        var lights = GetManualLinkFrames().Where(frame => frame.Kind == FrameKind.Light && frame.FlatSetId.HasOverride).ToArray();
        if (lights.Length == 0) return;
        var undoActions = new List<Action>();
        foreach (var light in lights)
        {
            var old = light.FlatSetId.OverrideValue;
            undoActions.Add(() => light.FlatSetId.SetOverride(old));
            light.FlatSetId.ClearOverride();
        }
        _undo.Push(() => { foreach (var action in undoActions.AsEnumerable().Reverse()) action(); RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState(); });
        RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState();
        Status = $"Collegamento Flat rimosso da {lights.Length} Light · modalità automatica ripristinata";
        UndoCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        if (_plan is null) BuildPlan();
        var plan = _plan ?? throw new InvalidOperationException("Impossibile costruire il piano di esportazione.");
        using var tracked = BeginTrackedOperation("Esportazione progetto", "AF-EXPORT-START", "Esportazione verificata avviata");
        _exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _exportControl = new();
        SetExportState(ExportRunState.Preflighting);
        IsScanning = true;
        try
        {
            ExportProgressDetail = "Controlli di sicurezza automatici…";
            var report = await ProjectExportPreflight.AnalyzeAsync(plan, CurrentExportOptions(), _exportCancellation.Token);
            ApplyExportPreflight(report);
            if (!report.IsReady) throw new ExportPreflightException(report);
            SetExportState(ExportRunState.Running);
            var progress = new Progress<ExportProgress>(item =>
            {
                ExportProgress = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
                var speed = item.MiBPerSecond <= 0 ? "calcolo velocità" : $"{item.MiBPerSecond:0.0} MiB/s";
                var eta = item.EstimatedRemaining is null ? "ETA —" : $"ETA {FormatDuration(item.EstimatedRemaining.Value)}";
                var mode = item.Resumed ? "riusato" : "verificato";
                ExportProgressDetail = $"{item.Completed}/{item.Total} · {HumanSize(item.BytesCopied)} / {HumanSize(item.TotalBytes)} · {speed} · {eta}";
                Status = $"{mode} · {item.CurrentFile}";
            });
            var output = await ProjectExporter.ExecuteAsync(plan, progress, _exportCancellation.Token, _exportControl, CurrentExportOptions());
            SetExportState(ExportRunState.Completed);
            ExportProgress = 100;
            ExportProgressDetail = $"Copia e verifica completate · {plan.Files.Count} file";
            Status = $"Progetto verificato: {output}";
            tracked.Complete("AF-EXPORT-OK", $"Esportazione verificata completata: {plan.Files.Count} file");
            return output;
        }
        catch (OperationCanceledException) when (_exportCancellation.IsCancellationRequested)
        {
            SetExportState(ExportRunState.Cancelled);
            ExportProgressDetail = "Export annullato · le copie già verificate restano nello staging e saranno riutilizzate";
            Status = ExportProgressDetail;
            tracked.Complete("AF-EXPORT-CANCELLED", "Export annullato in modo riprendibile");
            throw;
        }
        catch (ExportPreflightException exception)
        {
            ApplyExportPreflight(exception.Report);
            SetExportState(ExportRunState.Blocked);
            ExportProgressDetail = $"Export bloccato dal nuovo preflight · {exception.Report.ErrorCount} errori";
            tracked.Fail("AF-EXPORT-PREFLIGHT-CHANGED", "Le condizioni sono cambiate dopo il dry-run", exception);
            throw;
        }
        catch (Exception exception)
        {
            SetExportState(ExportRunState.Failed);
            ExportProgressDetail = "Export interrotto · staging conservato per diagnosi e ripresa";
            tracked.Fail("AF-EXPORT-001", "Esportazione progetto non completata", exception);
            throw;
        }
        finally
        {
            IsScanning = false;
            _exportCancellation.Dispose();
            _exportCancellation = null;
            _exportControl = null;
            RaiseExportProperties();
        }
    }

    public void PauseExport()
    {
        if (_exportState != ExportRunState.Running || _exportControl is null) return;
        _exportControl.Pause();
        SetExportState(ExportRunState.Paused);
        Status = "Export in pausa · nessun file parziale verrà promosso";
    }

    public void ResumeExport()
    {
        if (_exportState != ExportRunState.Paused || _exportControl is null) return;
        _exportControl.Resume();
        SetExportState(ExportRunState.Running);
        Status = "Export ripreso";
    }

    public void CancelExport()
    {
        if (!CanCancelExport || _exportCancellation is null) return;
        SetExportState(ExportRunState.Cancelling);
        _exportControl?.Resume();
        _exportCancellation.Cancel();
        Status = "Annullamento richiesto · chiusura sicura del file corrente";
    }

    private void RebuildTree()
    {
        var selectedFrames = SelectedNode?.Frames;
        IEnumerable<FrameMetadata> query = _frames;
        if (ShowIssuesOnly) query = query.Where(frame => frame.Issues.Count > 0);
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(frame => frame.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                         (frame.ObjectName.Value?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                         (frame.FilterName.Value?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        var frames = query.ToArray();
        TreeRoots.Clear();
        AddOpticalSessionTree(frames);
        AddCalibrationLibraryTree(frames);
        var other = frames.Where(frame => frame.Kind is FrameKind.Unknown or FrameKind.DarkFlat).ToArray();
        if (other.Length > 0) TreeRoots.Add(Category("other", "Altri frame", "?", other, other.Select(Leaf)));
        if (selectedFrames is not null) { LoadEditor(); RaiseSelectionProperties(); }
        Raise(nameof(HasVisibleTree));
        Raise(nameof(ShowNoResultsPrompt));
    }

    private void AddOpticalSessionTree(FrameMetadata[] visibleFrames)
    {
        if (_analysis is null) return;
        var visible = visibleFrames.Select(frame => frame.Path).ToHashSet(PathIdentity.Comparer);
        var lightItems = _analysis.Lights.Where(item => visible.Contains(item.Light.Path)).ToArray();
        var visibleFlatGroups = _analysis.FlatGroups.Where(group => group.Frames.Any(frame => visible.Contains(frame.Path))).ToArray();
        var filterNames = lightItems.Select(item => DisplayFilter(item.Light.FilterName.Value))
            .Concat(visibleFlatGroups.Select(group => DisplayFilter(group.Representative.FilterName.Value)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var filterName in filterNames)
        {
            var items = lightItems.Where(item => DisplayFilter(item.Light.FilterName.Value).Equals(filterName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var usedFlatIds = items.Where(item => item.FlatGroup is not null).Select(item => item.FlatGroup!.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sessions = new List<ProjectTreeNode>();
            var groups = items.GroupBy(item => item.FlatGroup?.Id ?? $"IRRISOLTA-{item.Light.SessionId.Value ?? "UNKNOWN"}")
                .OrderBy(group => group.Min(item => item.Light.CapturedAt.Value ?? DateTimeOffset.MaxValue)).ToArray();
            var sessionIndex = 1;
            foreach (var group in groups)
            {
                var entries = group.ToArray();
                var flatGroup = entries.Select(item => item.FlatGroup).FirstOrDefault(value => value is not null);
                var lights = entries.Select(item => item.Light).ToArray();
                var flats = flatGroup?.Frames.ToArray() ?? [];
                var darks = entries.Select(item => item.Dark.Selected?.Frame).Where(frame => frame is not null).Cast<FrameMetadata>().Distinct().ToArray();
                var biases = entries.Select(item => item.Bias.Selected?.Frame).Where(frame => frame is not null).Cast<FrameMetadata>().Distinct().ToArray();
                var allFrames = lights.Concat(flats).Concat(darks).Concat(biases).Distinct().ToArray();
                var decision = entries.Select(item => item.FlatDecision).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var sessionName = flatGroup is null ? $"Sessione {sessionIndex:00} · Flat da risolvere" : $"Sessione {sessionIndex:00} · {flatGroup.Id}";
                if (!string.IsNullOrWhiteSpace(decision) && decision.Contains("manuale", StringComparison.OrdinalIgnoreCase)) sessionName += " · MANUALE";
                var sessionNode = new ProjectTreeNode { Key = $"optical-session:{filterName}:{group.Key}", Name = sessionName, Detail = decision ?? "", Icon = flatGroup is null ? "!" : "S", Frames = allFrames };

                var nightNodes = lights.GroupBy(light => light.SessionId.Value ?? "Notte non definita").OrderBy(night => night.Key).Select(night =>
                    GroupNode($"night:{filterName}:{group.Key}:{night.Key}", $"Notte · {night.Key}", "N", night,
                        night.GroupBy(SeriesKey).Select(series => GroupNode($"series:{group.Key}:{night.Key}:{series.Key}", series.Key, "≋", series, series.Select(Leaf))))).ToArray();
                if (nightNodes.Length > 0) sessionNode.Children.Add(GroupNode($"nights:{filterName}:{group.Key}", "Notti osservative", "N", lights, nightNodes));
                if (flats.Length > 0) sessionNode.Children.Add(GroupNode($"session-flats:{group.Key}", "Flat della sessione", "F", flats, flats.Select(Leaf)));
                if (darks.Length > 0) sessionNode.Children.Add(GroupNode($"session-darks:{group.Key}", "Dark assegnati · riferimento", "D", darks, darks.Select(Leaf)));
                if (biases.Length > 0) sessionNode.Children.Add(GroupNode($"session-bias:{group.Key}", "Bias assegnati · riferimento", "B", biases, biases.Select(Leaf)));
                sessions.Add(sessionNode);
                sessionIndex++;
            }

            foreach (var unused in visibleFlatGroups.Where(group => DisplayFilter(group.Representative.FilterName.Value).Equals(filterName, StringComparison.OrdinalIgnoreCase) && !usedFlatIds.Contains(group.Id)))
                sessions.Add(GroupNode($"unused-flat:{unused.Id}", $"Flat Set non collegato · {unused.Id}", "?", unused.Frames,
                    [GroupNode($"unused-flat-files:{unused.Id}", "Flat", "F", unused.Frames, unused.Frames.Select(Leaf))]));

            if (sessions.Count == 0) continue;
            var filterFrames = sessions.SelectMany(node => node.Frames).Distinct().ToArray();
            var sessionsContainer = GroupNode($"sessions:{filterName}", "Sessioni di configurazione", "S", filterFrames, sessions);
            TreeRoots.Add(Category($"project-filter:{filterName}", filterName == "Senza filtro" ? "Senza filtro" : $"Filtro · {filterName}", "◐", filterFrames, [sessionsContainer]));
        }
    }

    private void AddCalibrationLibraryTree(FrameMetadata[] frames)
    {
        var available = frames.Where(frame => frame.Kind is FrameKind.Dark or FrameKind.Bias).ToArray();
        if (available.Length == 0) return;
        var visiblePaths = available.Select(frame => frame.Path).ToHashSet(PathIdentity.Comparer);
        var usedPaths = new HashSet<string>(PathIdentity.Comparer);
        var sessionNodes = new List<ProjectTreeNode>();
        if (_analysis is not null)
        {
            var pairs = _analysis.Lights.Select(item => new { Dark = item.Dark.Selected?.Frame, Bias = item.Bias.Selected?.Frame })
                .Where(pair => pair.Dark is not null || pair.Bias is not null)
                .GroupBy(pair => SensorPairKey(pair.Dark, pair.Bias), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            var index = 1;
            foreach (var pair in pairs)
            {
                var darks = pair.Select(value => value.Dark).Where(frame => frame is not null && visiblePaths.Contains(frame.Path)).Cast<FrameMetadata>().Distinct().ToArray();
                var biases = pair.Select(value => value.Bias).Where(frame => frame is not null && visiblePaths.Contains(frame.Path)).Cast<FrameMetadata>().Distinct().ToArray();
                if (darks.Length + biases.Length == 0) continue;
                foreach (var frame in darks.Concat(biases)) usedPaths.Add(frame.Path);
                var children = new List<ProjectTreeNode>();
                if (darks.Length > 0) children.Add(GroupNode($"sensor-dark:{pair.Key}", "Dark", "D", darks, darks.Select(Leaf)));
                if (biases.Length > 0) children.Add(GroupNode($"sensor-bias:{pair.Key}", "Bias", "B", biases, biases.Select(Leaf)));
                var pairFrames = darks.Concat(biases).Distinct().ToArray();
                sessionNodes.Add(GroupNode($"sensor-session:{pair.Key}", $"Sessione sensore {index:00} · {pair.Key}", "S", pairFrames, children));
                index++;
            }
        }
        var used = available.Where(frame => usedPaths.Contains(frame.Path)).ToArray();
        if (used.Length == 0 || sessionNodes.Count == 0) return;
        var sessions = GroupNode("sensor-sessions", "Calibrazioni utilizzate", "S", used, sessionNodes);
        TreeRoots.Add(Category("project-calibration", "Master assegnati al progetto", "◆", used, [sessions]));
    }

    private static string SensorPairKey(FrameMetadata? dark, FrameMetadata? bias)
    {
        var source = dark ?? bias;
        if (source is null) return "Configurazione sconosciuta";
        var gain = source.Gain.Value?.ToString("0.###") ?? "?";
        var offset = source.Offset.Value?.ToString("0.###") ?? "?";
        var temperature = dark?.EffectiveTemperatureC?.ToString("0.###") ?? "n/a";
        return $"G{gain} · O{offset} · T{temperature} °C · {source.Width.Value ?? 0}x{source.Height.Value ?? 0}";
    }

    private static string DisplayFilter(string? value) => string.IsNullOrWhiteSpace(value) ? "Senza filtro" : value.Trim();

    private void AddLightTree(FrameMetadata[] frames)
    {
        if (frames.Length == 0) return;
        var targets = frames.GroupBy(frame => frame.ObjectName.Value ?? "Target non definito").Select(target =>
            GroupNode($"target:{target.Key}", target.Key, "◎", target,
                target.GroupBy(frame => frame.FilterName.Value ?? "Filtro non definito").Select(filter =>
                    GroupNode($"filter:{target.Key}:{filter.Key}", filter.Key, "◐", filter,
                        filter.GroupBy(frame => frame.SessionId.Value ?? "Sessione non definita").Select(session =>
                            GroupNode($"session:{target.Key}:{filter.Key}:{session.Key}", SessionLabel(session), "◷", session,
                                session.GroupBy(SeriesKey).Select(series =>
                                    GroupNode($"series:{series.Key}", series.Key, "≋", series, series.Select(Leaf)))))))));
        TreeRoots.Add(Category("lights", "Light", "L", frames, targets));
    }

    private void AddFlatTree(FrameMetadata[] frames)
    {
        if (frames.Length == 0) return;
        var groups = ProjectAnalyzer.GroupFlats(frames);
        var filters = groups.GroupBy(group => group.Representative.FilterName.Value ?? "Filtro non definito").Select(filter =>
            GroupNode($"flat-filter:{filter.Key}", filter.Key, "◐", filter.SelectMany(group => group.Frames),
                filter.Select(group => GroupNode($"flat-epoch:{group.Id}", $"Flat Epoch · {group.Id}", "E", group.Frames,
                    group.Frames.GroupBy(frame => frame.SessionId.Value ?? "Sessione non definita").Select(session =>
                        GroupNode($"flat-session:{group.Id}:{session.Key}", session.Key, "◷", session, session.Select(Leaf)))))));
        TreeRoots.Add(Category("flats", "Flat", "F", frames, filters));
    }

    private void AddDarkTree(FrameMetadata[] frames)
    {
        if (frames.Length == 0) return;
        var gains = frames.GroupBy(frame => Format(frame.Gain.Value, "Gain non definito", "G"));
        var children = gains.Select(gain => GroupNode($"dark:{gain.Key}", gain.Key, "G", gain,
            gain.GroupBy(frame => Format(frame.EffectiveTemperatureC, "Temperatura non definita", "T", " °C")).Select(temp =>
                GroupNode($"dark:{gain.Key}:{temp.Key}", temp.Key, "T", temp,
                    temp.GroupBy(frame => Format(frame.ExposureSeconds.Value, "Esposizione non definita", "", " s")).Select(exp =>
                        GroupNode($"dark:{gain.Key}:{temp.Key}:{exp.Key}", exp.Key, "◷", exp, exp.Select(Leaf)))))));
        TreeRoots.Add(Category("darks", "Master Dark", "D", frames, children));
    }

    private void AddBiasTree(FrameMetadata[] frames)
    {
        if (frames.Length == 0) return;
        var children = frames.GroupBy(frame => Format(frame.Gain.Value, "Gain non definito", "G")).Select(gain =>
            GroupNode($"bias:{gain.Key}", gain.Key, "G", gain, gain.Select(Leaf)));
        TreeRoots.Add(Category("bias", "Master Bias", "B", frames, children));
    }

    private static ProjectTreeNode Category(string key, string name, string icon, IReadOnlyList<FrameMetadata> frames, IEnumerable<ProjectTreeNode> children)
    {
        var node = new ProjectTreeNode { Key = key, Name = name, Icon = icon, Frames = frames, IsExpanded = true };
        foreach (var child in children) node.Children.Add(child);
        return node;
    }

    private static ProjectTreeNode GroupNode(string key, string name, string icon, IEnumerable<FrameMetadata> source, IEnumerable<ProjectTreeNode> children)
    {
        var frames = source.ToArray();
        var node = new ProjectTreeNode { Key = key, Name = name, Icon = icon, Frames = frames };
        foreach (var child in children) node.Children.Add(child);
        return node;
    }

    private static ProjectTreeNode Leaf(FrameMetadata frame) => new() { Key = frame.Path, Name = frame.FileName, Detail = frame.Path, Icon = frame.Issues.Count > 0 ? "!" : "·", Frames = [frame] };
    private static string SessionLabel(IEnumerable<FrameMetadata> frames)
    {
        var items = frames.ToArray();
        var session = items[0].SessionId.Value ?? "Sessione non definita";
        var flatSets = items.Select(frame => frame.FlatSetId.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return flatSets.Length == 1 ? $"{session} · FLAT {flatSets[0]}" : session;
    }
    private static string SeriesKey(FrameMetadata frame) => $"{Format(frame.ExposureSeconds.Value, "?", "", " s")} · {Format(frame.Gain.Value, "G?", "G")} · {Format(frame.Offset.Value, "O?", "O")} · {Format(frame.EffectiveTemperatureC, "T?", "T", " °C")}";
    private static string Format(double? value, string missing, string prefix, string suffix = "") => value is null ? missing : $"{prefix}{value.Value:0.###}{suffix}";

    private void LoadEditor()
    {
        var frames = SelectedNode?.Frames ?? [];
        _initialGain = EditGain = Aggregate(frames, frame => frame.Gain.Value, value => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "");
        _initialOffset = EditOffset = Aggregate(frames, frame => frame.Offset.Value, value => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "");
        _initialTemperature = EditTemperature = Aggregate(frames, frame => frame.SetTemperatureC.Value, value => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "");
        _initialFilter = EditFilter = Aggregate(frames, frame => frame.FilterName.Value, value => value ?? "");
        _initialFlatSet = EditFlatSet = Aggregate(frames, frame => frame.FlatSetId.Value, value => value ?? "");
        _initialSession = EditSession = Aggregate(frames, frame => frame.SessionId.Value, value => value ?? "");
        _initialKind = EditKind = frames.Select(frame => frame.Kind).Distinct().Take(2).Count() == 1 ? frames[0].Kind : FrameKind.Unknown;
        SelectedIssues.Clear();
        foreach (var issue in frames.SelectMany(frame => frame.Issues.Select(issue => $"{frame.FileName} — {issue.Message}")).Distinct()) SelectedIssues.Add(issue);
        Raise(nameof(EditGain)); Raise(nameof(EditOffset)); Raise(nameof(EditTemperature)); Raise(nameof(EditFilter)); Raise(nameof(EditFlatSet)); Raise(nameof(EditSession)); Raise(nameof(EditKind)); Raise(nameof(RawHeaders));
    }

    private void ApplyOverrides()
    {
        if (SelectedNode is null) return;
        var undoActions = new List<Action>();
        foreach (var frame in SelectedNode.Frames)
        {
            if (EditGain != _initialGain && TryNumber(EditGain, out var gain)) ApplyField(frame.Gain, gain, undoActions);
            if (EditOffset != _initialOffset && TryNumber(EditOffset, out var offset)) ApplyField(frame.Offset, offset, undoActions);
            if (EditTemperature != _initialTemperature && TryNumber(EditTemperature, out var temperature)) ApplyField(frame.SetTemperatureC, temperature, undoActions);
            if (EditFilter != _initialFilter && !IsMixed(EditFilter)) ApplyField(frame.FilterName, string.IsNullOrWhiteSpace(EditFilter) ? null : EditFilter.Trim(), undoActions);
            if (EditFlatSet != _initialFlatSet && !IsMixed(EditFlatSet)) ApplyField(frame.FlatSetId, string.IsNullOrWhiteSpace(EditFlatSet) ? null : EditFlatSet.Trim(), undoActions);
            if (EditSession != _initialSession && !IsMixed(EditSession)) ApplyField(frame.SessionId, string.IsNullOrWhiteSpace(EditSession) ? null : EditSession.Trim(), undoActions);
            if (EditKind != _initialKind)
            {
                var old = frame.Kind;
                var wasManual = _kindOverrides.Contains(frame.Path);
                undoActions.Add(() => { frame.Kind = old; if (wasManual) _kindOverrides.Add(frame.Path); else _kindOverrides.Remove(frame.Path); });
                frame.Kind = EditKind;
                _kindOverrides.Add(frame.Path);
            }
            FrameValidator.Revalidate(frame);
        }
        if (undoActions.Count == 0) { Status = "Nessun valore modificato"; return; }
        var affectedFrames = SelectedNode.Frames.ToArray();
        _undo.Push(() => { foreach (var action in undoActions.AsEnumerable().Reverse()) action(); foreach (var frame in affectedFrames) FrameValidator.Revalidate(frame); RefreshIntelligence(); RebuildTree(); RefreshCounts(); });
        Status = $"Override applicati a {SelectedNode.Count} frame";
        RefreshIntelligence(); RebuildTree(); RefreshCounts(); LoadEditor(); SaveState(); UndoCommand.RaiseCanExecuteChanged();
    }

    private static void ApplyField<T>(MetadataField<T> field, T value, List<Action> undo)
    {
        var hadOverride = field.HasOverride;
        var old = field.OverrideValue;
        undo.Add(() => { if (hadOverride) field.SetOverride(old); else field.ClearOverride(); });
        field.SetOverride(value);
    }

    private void ApplyLibraryOffset()
    {
        if (!TryNumber(MasterLibraryOffset, out var offset)) { Status = "Offset libreria non valido"; return; }
        var roots = MasterLibraries.Where(item => item.Enabled && item.IsOnline).Select(item => Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        var masters = _frames.Where(frame => frame.IsMaster && roots.Any(root => PathIdentity.IsWithin(frame.Path, root))).ToArray();
        if (masters.Length == 0) { Status = "Nessun Master appartenente alla libreria caricata"; return; }
        var undoActions = new List<Action>();
        foreach (var frame in masters) { ApplyField(frame.Offset, offset, undoActions); FrameValidator.Revalidate(frame); }
        _undo.Push(() => { foreach (var action in undoActions.AsEnumerable().Reverse()) action(); foreach (var frame in masters) FrameValidator.Revalidate(frame); RefreshIntelligence(); RebuildTree(); RefreshCounts(); });
        RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState();
        Status = $"Offset {MasterLibraryOffset} applicato a {masters.Length} Master della libreria";
        UndoCommand.RaiseCanExecuteChanged();
    }

    private void ApplyProjectDefaults()
    {
        if (!TryProjectDefaults(out var defaults)) { Status = "Fallback progetto non validi: usa numeri oppure lascia vuoto"; return; }
        var changed = ProjectMetadataDefaultsResolver.Apply(_frames, defaults);
        RefreshIntelligence(); RebuildTree(); RefreshCounts(); SaveState();
        Status = $"Fallback progetto applicati a {changed} frame · valori presenti negli header non modificati";
    }

    private bool TryProjectDefaults(out ProjectMetadataDefaults defaults)
    {
        defaults = new();
        if (!TryNumber(ProjectDefaultGain, out var gain) || !TryNumber(ProjectDefaultOffset, out var offset) || !TryNumber(ProjectDefaultTemperature, out var temperature)) return false;
        defaults = new(gain, offset, temperature);
        return true;
    }

    private ProjectMetadataDefaults CurrentProjectDefaults() => TryProjectDefaults(out var defaults) ? defaults : new(null, null, null);

    private void Undo()
    {
        if (_undo.TryPop(out var action)) action();
        Status = "Ultima modifica annullata";
        SaveState();
        UndoCommand.RaiseCanExecuteChanged();
        LoadEditor();
    }

    private void RefreshCounts() { Raise(nameof(TotalIssues)); Raise(nameof(OverrideCount)); RaiseSelectionProperties(); }
    private void RaiseSelectionProperties()
    {
        UpdateCalibrationSummary();
        Raise(nameof(SelectionTitle)); Raise(nameof(SelectionDetail)); Raise(nameof(HasSelection)); Raise(nameof(ApplyLabel)); Raise(nameof(ManualLinkSelectionText)); Raise(nameof(GainSource)); Raise(nameof(OffsetSource)); Raise(nameof(TemperatureSource)); Raise(nameof(FilterSource)); Raise(nameof(FlatSetSource)); Raise(nameof(SessionSource)); Raise(nameof(RawHeaders));
    }

    private static string Aggregate<T>(IReadOnlyList<FrameMetadata> frames, Func<FrameMetadata, T> selector, Func<T, string> formatter)
    {
        if (frames.Count == 0) return "";
        var values = frames.Select(selector).Distinct().Take(2).ToArray();
        return values.Length == 1 ? formatter(values[0]) : "— Valori misti —";
    }

    private static string AggregateSource(IReadOnlyList<FrameMetadata>? frames, Func<FrameMetadata, MetadataSource> selector)
    {
        if (frames is null || frames.Count == 0) return "—";
        var values = frames.Select(selector).Distinct().Take(2).ToArray();
        return values.Length == 1 ? SourceLabel(values[0]) : "Origini miste";
    }

    private static string SourceLabel(MetadataSource source) => source switch
    {
        MetadataSource.Header => "Header",
        MetadataSource.LibraryPath => "Percorso libreria",
        MetadataSource.Filename => "Nome file",
        MetadataSource.Inferred => "Calcolato",
        MetadataSource.ProjectDefault => "Default progetto",
        MetadataSource.UserOverride => "Override utente",
        _ => "Mancante"
    };

    private string BuildRawHeaders()
    {
        if (SelectedNode?.Frames.Count != 1) return "Seleziona un singolo frame per vedere gli header originali.";
        var builder = new StringBuilder();
        foreach (var pair in SelectedNode.Frames[0].Headers.OrderBy(pair => pair.Key)) builder.AppendLine($"{pair.Key,-12} {pair.Value}");
        return builder.ToString();
    }

    private static bool TryNumber(string text, out double? value)
    {
        value = null;
        if (IsMixed(text)) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var local) || double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out local)) { value = local; return true; }
        return false;
    }

    private static string Input(double? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";
    private static double? ParseDefault(string text) => TryNumber(text, out var value) ? value : null;

    private static bool IsMixed(string value) => value.StartsWith('—');
    private static string NormalizeText(string? value) => string.Join(' ', (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private IReadOnlyList<FrameMetadata> GetManualLinkFrames()
    {
        var marked = Descendants(TreeRoots).Where(node => node.IsMarked).SelectMany(node => node.Frames).Distinct().ToArray();
        return marked.Length > 0 ? marked : SelectedNode?.Frames ?? [];
    }
    private static IEnumerable<ProjectTreeNode> Descendants(IEnumerable<ProjectTreeNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }
    private static bool HasAnyOverride(FrameMetadata frame) => frame.Gain.HasOverride || frame.Offset.HasOverride || frame.SetTemperatureC.HasOverride || frame.FilterName.HasOverride || frame.FlatSetId.HasOverride || frame.SessionId.HasOverride || frame.ManualDarkPath.HasOverride || frame.ManualBiasPath.HasOverride;

    private void RefreshIntelligence()
    {
        foreach (var frame in _frames)
            frame.Issues.RemoveAll(issue => issue.Code.StartsWith("calibration.", StringComparison.Ordinal));

        _analysis = ProjectAnalyzer.Analyze(_frames);
        BuildQualitySeriesDefinitions();
        RefreshStatistics();
        RefreshFlatSetOptions();
        foreach (var item in _analysis.Lights)
        {
            AddCalibrationIssue(item.Light, "flat", item.Flat);
            AddCalibrationIssue(item.Light, "dark", item.Dark);
            AddCalibrationIssue(item.Light, "bias", item.Bias);
        }
        RefreshReviewQueue();

        var recipe = WbppRecipeEngine.Recommend(_analysis);
        WbppKeywords.Clear();
        foreach (var keyword in recipe.Keywords)
            WbppKeywords.Add(new(keyword.Keyword, keyword.Pre ? "ON" : "OFF", keyword.Post ? "ON" : "OFF", keyword.Reason));
        WbppNotes.Clear();
        foreach (var note in recipe.Notes) WbppNotes.Add(note);

        ReadinessText = _analysis.Lights.Count == 0
            ? $"Modalità Master Library · {_frames.Count(frame => frame.IsMaster)} Master analizzati"
            : _analysis.Ready
                ? $"Pronto per WBPP · {_analysis.Lights.Count} Light con Flat, Dark e Bias assegnati"
                : $"Da risolvere · {_analysis.UnresolvedCount} assegnazioni di calibrazione mancanti o ambigue";
        InvalidateExportPlan();
        Raise(nameof(UnresolvedCalibrations));
        Raise(nameof(IsProjectReady));
        Raise(nameof(PlanSummary));
        Raise(nameof(ReviewQueueCount));
        UpdateCalibrationSummary();
    }

    private void RefreshReviewQueue()
    {
        ReviewQueue.Clear();
        if (_analysis is null) return;
        foreach (var item in _analysis.Lights)
        {
            AddReview(item.Light, "Flat", item.Flat);
            AddReview(item.Light, "Dark", item.Dark);
            AddReview(item.Light, "Bias", item.Bias);
        }
        var ordered = ReviewQueue.OrderBy(item => item.Priority).ThenBy(item => item.Filter).ThenBy(item => item.Night).ThenBy(item => item.Frame.FileName).ToArray();
        ReviewQueue.Clear();
        foreach (var item in ordered) ReviewQueue.Add(item);
    }

    private void RefreshMasterOrganizer(IEnumerable<FrameMetadata>? source = null)
    {
        MasterOrganizerItems.Clear();
        foreach (var frame in (source ?? _masterLibraryFrames).Where(frame => frame.IsMaster && frame.Kind is FrameKind.Dark or FrameKind.Bias or FrameKind.DarkFlat).OrderBy(frame => frame.Path))
            MasterOrganizerItems.Add(new(frame));
        var ready = MasterOrganizerItems.Count(item => item.IsReady);
        MasterOrganizerStatus = MasterOrganizerItems.Count == 0 ? "Nessun Master rilevato" : $"{ready}/{MasterOrganizerItems.Count} pronti · {MasterOrganizerItems.Count - ready} richiedono dati";
    }

    private void AddReview(FrameMetadata frame, string calibration, MatchResult result)
    {
        if (result.IsAccepted) return;
        var (priority, state, reason, action) = result.Status switch
        {
            MatchStatus.Ambiguous => (1, "Scelta ambigua", $"{result.Candidates.Count} candidati compatibili hanno la stessa priorità.", $"Confronta i candidati {calibration} e assegna quello corretto al gruppo."),
            MatchStatus.InsufficientMetadata => (0, "Metadati insufficienti", $"Mancano: {string.Join(", ", result.Candidates.FirstOrDefault()?.MissingRequired ?? ["campi richiesti"])}.", "Completa i metadati nell’Inspector e ricalcola il progetto."),
            MatchStatus.Incompatible => (0, "Nessun candidato compatibile", $"{result.Candidates.Count} candidati trovati, ma tutti incompatibili.", $"Controlla libreria e parametri oppure collega un {calibration} valido."),
            _ => (0, "Calibrazione mancante", $"Nessun {calibration} disponibile per questa configurazione.", $"Aggiungi o seleziona una libreria contenente il {calibration} richiesto.")
        };
        var candidates = result.Candidates.Where(candidate => candidate.Compatible)
            .Select(candidate => new ReviewCandidateOption(candidate.Frame.Path, candidate.Frame.FileName, candidate.Score,
                candidate.Exact ? "Compatibilità esatta" : "Entro tolleranza", string.Join(" · ", candidate.Reasons),
                DisplayValue(candidate.Frame.Camera.Value), DisplayValue(candidate.Frame.Gain.Value), DisplayValue(candidate.Frame.Offset.Value),
                DisplayTemperature(candidate.Frame.EffectiveTemperatureC), DisplayExposure(candidate.Frame.ExposureSeconds.Value),
                DisplayBinning(candidate.Frame), DisplayValue(candidate.Frame.ReadoutMode.Value)))
            .ToArray();
        ReviewQueue.Add(new(frame, calibration, state, reason, action, result.Candidates.Count,
            frame.FilterName.Value ?? "Senza filtro", frame.SessionId.Value ?? "Notte non definita", priority, candidates));
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    private static string DisplayValue(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.CurrentCulture) : "—";
    private static string DisplayTemperature(double? value) => value.HasValue ? $"{value.Value:0.#} °C" : "—";
    private static string DisplayExposure(double? value) => value.HasValue ? $"{value.Value:0.###} s" : "—";
    private static string DisplayBinning(FrameMetadata frame) => frame.XBin.Value.HasValue && frame.YBin.Value.HasValue ? $"{frame.XBin.Value}×{frame.YBin.Value}" : "—";

    private void RefreshStatistics()
    {
        FilterStatistics.Clear();
        SessionStatistics.Clear();
        NightStatistics.Clear();
        if (_analysis is null) { _statistics = null; return; }
        _statistics = ProjectStatisticsCalculator.Calculate(_analysis);
        var maximumHours = Math.Max(0.001, _statistics.Filters.Select(item => item.ExposureHours).DefaultIfEmpty().Max());
        foreach (var item in _statistics.Filters)
        {
            var gain = item.Gains.Count == 0 ? "—" : string.Join(", ", item.Gains.Select(value => value.ToString("0.###")));
            var temperature = TemperatureRange(item.MinimumTemperatureC, item.MaximumTemperatureC);
            FilterStatistics.Add(new(item.Filter, FormatHours(item.ExposureSeconds), item.LightCount, item.NightCount, item.ConfigurationSessionCount,
                $"{item.FlatFrameCount} Flat · {item.DarkMasterCount} Dark · {item.BiasMasterCount} Bias", gain, temperature,
                item.Ready ? "Pronto" : $"{item.UnresolvedCalibrationCount} irrisolte", item.Ready, item.ExposureHours / maximumHours * 100));
        }
        var sessionNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _statistics.Sessions)
        {
            sessionNumbers[item.Filter] = sessionNumbers.GetValueOrDefault(item.Filter) + 1;
            var displayName = $"Sessione {sessionNumbers[item.Filter]:00} · {DateRange(item.FirstCapture, item.LastCapture)}";
            SessionStatistics.Add(new(item.Filter, displayName, item.Session, FormatHours(item.ExposureSeconds), item.LightCount, item.NightCount, item.FlatFrameCount,
                item.DarkMasters, item.BiasMasters, item.Ready ? "Pronta" : $"{item.UnresolvedCalibrationCount} irrisolte", item.Ready));
        }
        foreach (var item in _statistics.Nights)
            NightStatistics.Add(new(item.Filter, item.ConfigurationSession, item.Night, FormatHours(item.ExposureSeconds), item.LightCount,
                $"{item.AverageExposureSeconds:0.##} s", TemperatureRange(item.MinimumTemperatureC, item.MaximumTemperatureC), item.IssueCount));
        Raise(nameof(TotalIntegrationText)); Raise(nameof(StatisticsSummary)); Raise(nameof(StatisticsDateRange));
    }

    private void RefreshFlatSetOptions()
    {
        var previous = SelectedFlatSet?.LinkId;
        var manualFrames = GetManualLinkFrames();
        var selectedFilters = manualFrames.Where(frame => frame.Kind == FrameKind.Light)
            .Select(frame => NormalizeText(frame.FilterName.Value)).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedLight = manualFrames.FirstOrDefault(frame => frame.Kind == FrameKind.Light);
        AvailableFlatSets.Clear();
        if (_analysis is null) { SelectedFlatSet = null; return; }
        foreach (var group in _analysis.FlatGroups)
        {
            var representative = group.Representative;
            var filter = representative.FilterName.Value ?? "Filtro sconosciuto";
            if (selectedFilters.Length == 1 && NormalizeText(filter) != selectedFilters[0]) continue;
            if (selectedLight is not null && !CalibrationMatcher.Find(selectedLight, [representative], FrameKind.Flat).Candidates.Any(candidate => candidate.Compatible)) continue;
            var linkId = string.IsNullOrWhiteSpace(representative.FlatSetId.Value) ? group.Id : representative.FlatSetId.Value!;
            var dates = group.Frames.Select(frame => frame.CapturedAt.Value).Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
            var dateLabel = dates.Length == 0 ? "data sconosciuta" : dates[0].ToString("yyyy-MM-dd HH:mm");
            var display = $"{filter} · {dateLabel} · {group.Frames.Count} Flat · {linkId}";
            AvailableFlatSets.Add(new(group, linkId, filter, display));
        }
        SelectedFlatSet = AvailableFlatSets.FirstOrDefault(option => option.LinkId.Equals(previous, StringComparison.OrdinalIgnoreCase));
        LinkFlatSetCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
    }

    private static void AddCalibrationIssue(FrameMetadata light, string role, MatchResult result)
    {
        if (result.IsAccepted) return;
        var detail = result.Status switch
        {
            MatchStatus.Missing => $"nessun {role} disponibile",
            MatchStatus.Ambiguous => $"{result.Candidates.Count} candidati equivalenti",
            MatchStatus.Incompatible => "nessun candidato compatibile",
            MatchStatus.InsufficientMetadata => $"metadati mancanti: {string.Join(", ", result.Candidates.FirstOrDefault()?.MissingRequired ?? [])}",
            _ => result.Status.ToString()
        };
        light.Issues.Add(new($"calibration.{role}", IssueSeverity.Error, $"Assegnazione {role.ToUpperInvariant()} irrisolta: {detail}", role));
    }

    private void UpdateCalibrationSummary()
    {
        if (_analysis is null || SelectedNode is null)
        {
            CalibrationSummary = "Seleziona uno o più Light per vedere le calibrazioni assegnate.";
            return;
        }
        var paths = SelectedNode.Frames.Where(frame => frame.Kind == FrameKind.Light).Select(frame => frame.Path).ToHashSet(PathIdentity.Comparer);
        var items = _analysis.Lights.Where(item => paths.Contains(item.Light.Path)).ToArray();
        if (items.Length == 0)
        {
            CalibrationSummary = "La selezione non contiene Light.";
            return;
        }
        static string Choice(IEnumerable<LightCalibrationAnalysis> source, Func<LightCalibrationAnalysis, MatchResult> selector) =>
            string.Join(", ", source.Select(item => selector(item).Selected?.Frame.FileName ?? $"[{selector(item).Status}]").Distinct(StringComparer.OrdinalIgnoreCase));
        var flatDecision = string.Join("; ", items.Select(item => item.FlatDecision).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        CalibrationSummary = $"Flat: {Choice(items, item => item.Flat)}{(flatDecision.Length > 0 ? $"\n↳ {flatDecision}" : "")}\nDark: {Choice(items, item => item.Dark)}\nBias: {Choice(items, item => item.Bias)}";
    }

    private void BuildPlannedTree()
    {
        PlannedTreeRoots.Clear();
        if (_plan is null) return;
        foreach (var roleGroup in _plan.Files.GroupBy(file => file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]))
        {
            var root = BuildPlanNode(roleGroup.Key, roleGroup.ToArray(), 0);
            root.IsExpanded = true;
            PlannedTreeRoots.Add(root);
        }
    }

    private static ProjectTreeNode BuildPlanNode(string name, PlannedFile[] files, int depth)
    {
        var node = new ProjectTreeNode { Key = $"plan:{depth}:{name}:{files[0].RelativePath}", Name = name, Icon = depth == 0 ? "◆" : "◇", Frames = files.Select(file => file.Frame).Distinct().ToArray() };
        var groups = files.GroupBy(file => file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ElementAtOrDefault(depth + 1));
        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key)) continue;
            var groupFiles = group.ToArray();
            var hasDeeper = groupFiles.Any(file => file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length > depth + 2);
            if (hasDeeper) node.Children.Add(BuildPlanNode(group.Key, groupFiles, depth + 1));
            else foreach (var file in groupFiles)
                node.Children.Add(new ProjectTreeNode { Key = $"planned:{file.RelativePath}", Name = Path.GetFileName(file.RelativePath), Detail = file.RelativePath, Icon = "·", Frames = [file.Frame] });
        }
        return node;
    }

    private static string HumanSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalSeconds < 1) return "< 1 s";
        if (value.TotalMinutes < 1) return $"{Math.Ceiling(value.TotalSeconds):0} s";
        if (value.TotalHours < 1) return $"{(int)value.TotalMinutes} min {value.Seconds:00} s";
        return $"{(int)value.TotalHours} h {value.Minutes:00} min";
    }

    private static string FormatHours(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var hours = (int)Math.Floor(duration.TotalHours);
        return duration.Minutes == 0 ? $"{hours} h" : $"{hours} h {duration.Minutes:00} min";
    }

    private static string TemperatureRange(double? minimum, double? maximum)
    {
        if (minimum is null || maximum is null) return "—";
        return Math.Abs(minimum.Value - maximum.Value) < 0.05 ? $"{Signed(minimum.Value)} °C" : $"{Signed(minimum.Value)}…{Signed(maximum.Value)} °C";
    }

    private static string Signed(double value) => value.ToString("0.#", CultureInfo.CurrentCulture).Replace("-", "−");
    private static string DateRange(DateTimeOffset? first, DateTimeOffset? last)
    {
        if (first is null || last is null) return "date non disponibili";
        var start = first.Value.ToLocalTime(); var end = last.Value.ToLocalTime();
        return start.Date == end.Date ? start.ToString("dd MMM yyyy") : $"{start:dd MMM} – {end:dd MMM yyyy}";
    }

    private static string CsvFilters(ProjectStatistics statistics)
    {
        var builder = new StringBuilder("Filtro;Ore;Light;Notti;Sessioni;Flat;Dark;Bias;Irrisolte\n");
        foreach (var item in statistics.Filters) builder.AppendLine($"{Csv(item.Filter)};{Number(item.ExposureHours)};{item.LightCount};{item.NightCount};{item.ConfigurationSessionCount};{item.FlatFrameCount};{item.DarkMasterCount};{item.BiasMasterCount};{item.UnresolvedCalibrationCount}");
        return builder.ToString();
    }
    private static string CsvSessions(ProjectStatistics statistics)
    {
        var builder = new StringBuilder("Filtro;Sessione;Ore;Light;Notti;Flat;Dark;Bias;Irrisolte\n");
        foreach (var item in statistics.Sessions) builder.AppendLine($"{Csv(item.Filter)};{Csv(item.Session)};{Number(item.ExposureHours)};{item.LightCount};{item.NightCount};{item.FlatFrameCount};{Csv(item.DarkMasters)};{Csv(item.BiasMasters)};{item.UnresolvedCalibrationCount}");
        return builder.ToString();
    }
    private static string CsvNights(ProjectStatistics statistics)
    {
        var builder = new StringBuilder("Filtro;Sessione;Notte;Ore;Light;Esposizione media s;Temperatura min C;Temperatura max C;Avvisi\n");
        foreach (var item in statistics.Nights) builder.AppendLine($"{Csv(item.Filter)};{Csv(item.ConfigurationSession)};{Csv(item.Night)};{Number(item.ExposureHours)};{item.LightCount};{Number(item.AverageExposureSeconds)};{Number(item.MinimumTemperatureC)};{Number(item.MaximumTemperatureC)};{item.IssueCount}");
        return builder.ToString();
    }
    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    private static string Number(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "";

    private sealed class TrackedOperation : IDisposable
    {
        private readonly StructuredLogOperation _log;
        private readonly RecoveryJournalStore _journal;
        private readonly string _operationId;
        private bool _finished;

        public TrackedOperation(StructuredLogOperation log, RecoveryJournalStore journal, string operationId)
        {
            _log = log;
            _journal = journal;
            _operationId = operationId;
        }

        public void Complete(string code, string message)
        {
            if (_finished) return;
            _log.Complete(code, message);
            _journal.Complete(_operationId);
            _finished = true;
        }

        public void Fail(string code, string message, Exception exception)
        {
            if (_finished) return;
            _log.Fail(code, message, exception);
            _journal.Complete(_operationId);
            _finished = true;
        }

        public void Dispose()
        {
            if (_finished) return;
            _log.Dispose();
        }
    }
}

public sealed record WbppKeywordRow(string Keyword, string Pre, string Post, string Reason);
public enum ExportRunState { Idle, Preflighting, Ready, Blocked, Running, Paused, Cancelling, Completed, Cancelled, Failed }
public sealed record ExportPreflightFindingRow(string Severity, string Code, string Title, string Detail, string Path);
public sealed record DiagnosticEventRow(string Timestamp, string Level, string Code, string Operation, string Correlation, string Message, string ExceptionType);
public sealed record FlatSetOption(CalibrationGroup Group, string LinkId, string Filter, string Display)
{
    public override string ToString() => Display;
}
public sealed record FilterStatsRow(string Filter, string Integration, int Lights, int Nights, int Sessions, string Calibration, string Gain, string Temperature, string Status, bool Ready, double Percentage)
{
    public string NightsLabel => Nights == 1 ? "1 notte" : $"{Nights} notti";
    public string SessionsLabel => Sessions == 1 ? "1 sessione configurazione" : $"{Sessions} sessioni configurazione";
}
public sealed record SessionStatsRow(string Filter, string Session, string TechnicalId, string Integration, int Lights, int Nights, int Flats, string DarkMasters, string BiasMasters, string Status, bool Ready)
{
    public string NightsLabel => Nights == 1 ? "1 notte" : $"{Nights} notti";
}
public sealed record NightStatsRow(string Filter, string Session, string Night, string Integration, int Lights, string AverageExposure, string Temperature, int Issues);
public enum ReviewAssignmentScope { Light, Night, Configuration }
public sealed class ReviewQueueItem(FrameMetadata frame, string calibration, string state, string reason, string suggestedAction, int candidateCount, string filter, string night, int priority, IReadOnlyList<ReviewCandidateOption> candidates)
{
    public FrameMetadata Frame { get; } = frame;
    public string Calibration { get; } = calibration;
    public string State { get; } = state;
    public string Reason { get; } = reason;
    public string SuggestedAction { get; } = suggestedAction;
    public int CandidateCount { get; } = candidateCount;
    public string Filter { get; } = filter;
    public string Night { get; } = night;
    public int Priority { get; } = priority;
    public IReadOnlyList<ReviewCandidateOption> Candidates { get; } = candidates;
    public ReviewCandidateOption? SelectedCandidate { get; set; }
    public string Scope => $"{Filter} · {Night}";
    public string CandidateLabel => CandidateCount == 1 ? "1 candidato" : $"{CandidateCount} candidati";
    public bool CanAssignMaster => Calibration is "Dark" or "Bias" && Candidates.Count > 0;
    public string TargetSignature => $"Light · Camera {SignatureValue(Frame.Camera.Value)} · G{SignatureValue(Frame.Gain.Value)} · O{SignatureValue(Frame.Offset.Value)} · {SignatureTemperature(Frame.EffectiveTemperatureC)} · {SignatureBinning(Frame)} · {SignatureValue(Frame.ReadoutMode.Value)}";
    private static string SignatureValue(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    private static string SignatureValue(double? value) => value.HasValue ? value.Value.ToString("0.###") : "—";
    private static string SignatureTemperature(double? value) => value.HasValue ? $"{value.Value:0.#} °C" : "—";
    private static string SignatureBinning(FrameMetadata value) => value.XBin.Value.HasValue && value.YBin.Value.HasValue ? $"{value.XBin.Value}×{value.YBin.Value}" : "—";
}
public sealed record ReviewCandidateOption(string Path, string FileName, int Score, string Compatibility, string Reasons, string Camera, string Gain, string Offset, string Temperature, string Exposure, string Binning, string Readout)
{
    public string Display => $"{FileName} · score {Score} · {Compatibility}";
}

public sealed class QualityFrameRow : BindableBase
{
    private bool _isExcluded; private bool _isSuspect; private double _outlierScore; private string _reason = "In attesa del confronto";
    private BitmapSource? _preview;
    private QualityFrameRow(FrameMetadata frame, QualityMetrics? metrics, string? error, bool excluded, string configurationSession)
    {
        Frame = frame; Metrics = metrics; Error = error; _isExcluded = excluded; ConfigurationSession = configurationSession;
        if (metrics is not null)
        {
            _preview = CreatePlatformBitmap(metrics.PreviewPixels, metrics.PreviewWidth, metrics.PreviewHeight, false); PreviewKey = "analysis";
        }
    }
    public QualityFrameRow(FrameMetadata frame, QualityMetrics metrics, bool excluded, string configurationSession) : this(frame, metrics, null, excluded, configurationSession) { }
    public static QualityFrameRow Failed(FrameMetadata frame, string error, bool excluded, string configurationSession) => new(frame, null, error, excluded, configurationSession);
    public FrameMetadata Frame { get; }
    public QualityMetrics? Metrics { get; }
    public string? Error { get; }
    public BitmapSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    public string PreviewKey { get; private set; } = "";
    public string Path => Frame.Path;
    public string FileName => Frame.FileName;
    public string Filter => Frame.FilterName.Value ?? "Senza filtro";
    public string ConfigurationSession { get; }
    public string Night => Frame.SessionId.Value ?? "—";
    public bool HasBayerPattern => new[] { "RGGB", "BGGR", "GRBG", "GBRG" }.Any(pattern => (Frame.BayerPattern.Value ?? "").Contains(pattern, StringComparison.OrdinalIgnoreCase));
    public double ExposureSeconds => Frame.ExposureSeconds.Value ?? 0;
    public double Fwhm => Metrics?.FwhmPixels ?? 0;
    public double Eccentricity => Metrics?.Eccentricity ?? 0;
    public double Noise => Metrics?.Noise ?? 0;
    public double Snr => Metrics?.Snr ?? 0;
    public int StarCount => Metrics?.StarCount ?? 0;
    public string FwhmText => Error is null ? Fwhm.ToString("0.00") : "—";
    public string EccentricityText => Error is null ? Eccentricity.ToString("0.000") : "—";
    public string NoiseText => Error is null ? Noise.ToString("0.##") : "—";
    public string SnrText => Error is null ? Snr.ToString("0.0") : "—";
    public string StarsText => Error is null ? StarCount.ToString("N0") : "—";
    public bool IsExcluded { get => _isExcluded; set { if (Set(ref _isExcluded, value)) Raise(nameof(State)); } }
    public bool IsSuspect { get => _isSuspect; private set { if (Set(ref _isSuspect, value)) Raise(nameof(State)); } }
    public double OutlierScore { get => _outlierScore; private set => Set(ref _outlierScore, value); }
    public string Reason { get => Error ?? _reason; private set => Set(ref _reason, value); }
    public string State => Error is not null ? "Non analizzato" : IsExcluded ? "Escluso" : IsSuspect ? "Sospetto" : "Valido";
    public void SetScore(double score, bool suspect, string reason) { OutlierScore = score; IsSuspect = suspect; Reason = reason; }
    public void SetPreview(QualityPreview preview, string key)
    {
        Preview = CreatePlatformBitmap(preview.Pixels, preview.Width, preview.Height, preview.IsColor); PreviewKey = key;
    }
    public void ResetPreview()
    {
        if (Metrics is null) return;
        Preview = CreatePlatformBitmap(Metrics.PreviewPixels, Metrics.PreviewWidth, Metrics.PreviewHeight, false); PreviewKey = "analysis";
    }

    private static BitmapSource CreatePlatformBitmap(byte[] pixels, int width, int height, bool color)
    {
#if AVALONIA
        // Avalonia loads the same uncompressed 24-bit BMP on Windows, Linux and macOS.
        // Encoding here keeps pixel storage out of the shared ViewModel contract.
        var rowSize = (width * 3 + 3) & ~3;
        var imageSize = rowSize * height;
        using var stream = new MemoryStream(54 + imageSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + imageSize);
            writer.Write((short)0); writer.Write((short)0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24);
            writer.Write(0); writer.Write(imageSize); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
            var padding = new byte[rowSize - width * 3];
            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++)
                {
                    if (color)
                    {
                        var index = (y * width + x) * 3;
                        writer.Write(pixels[index + 2]); writer.Write(pixels[index + 1]); writer.Write(pixels[index]);
                    }
                    else
                    {
                        var value = pixels[y * width + x];
                        writer.Write(value); writer.Write(value); writer.Write(value);
                    }
                }
                writer.Write(padding);
            }
        }
        stream.Position = 0;
        return new BitmapSource(stream);
#else
        var format = color ? PixelFormats.Rgb24 : PixelFormats.Gray8;
        var stride = width * (color ? 3 : 1);
        var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
#endif
    }
}

public sealed class QualitySeriesRow : BindableBase
{
    private IReadOnlyList<QualityFrameRow> _frames = [];

    public QualitySeriesRow(string filter, string configurationSession, IReadOnlyList<FrameMetadata> sourceFrames)
    {
        Filter = filter;
        ConfigurationSession = configurationSession;
        SourceFrames = sourceFrames;
    }

    public string Filter { get; }
    public string ConfigurationSession { get; }
    public IReadOnlyList<FrameMetadata> SourceFrames { get; }
    public IReadOnlyList<QualityFrameRow> Frames => _frames;
    public int FrameCount => SourceFrames.Count;
    public int AnalyzedCount => Frames.Count;
    public int NightCount => SourceFrames.Select(frame => frame.SessionId.Value ?? "Notte non definita").Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int SuspectCount => Frames.Count(frame => frame.IsSuspect);
    public int ExcludedCount => Frames.Count(frame => frame.IsExcluded);
    public bool IsAnalyzed => Frames.Count > 0;
    public string DisplayName => $"{Filter} · {ConfigurationSession}";
    public string Display => IsAnalyzed
        ? $"{DisplayName}   ·   {AnalyzedCount}/{FrameCount} analizzati · {SuspectCount} sospetti"
        : $"{DisplayName}   ·   {FrameCount} Light / {NightCount} notti · da analizzare";
    public void SetAnalysisResults(IReadOnlyList<QualityFrameRow> frames) { _frames = frames; RefreshCounts(); Raise(nameof(IsAnalyzed)); Raise(nameof(AnalyzedCount)); }
    public void RefreshCounts() { Raise(nameof(SuspectCount)); Raise(nameof(ExcludedCount)); Raise(nameof(Display)); }
}

public sealed class MasterLibraryItem : BindableBase
{
    private string _name; private string _path; private int _priority; private bool _enabled;
    public MasterLibraryItem(string name, string path, int priority, bool enabled) { _name = name; _path = path; _priority = priority; _enabled = enabled; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Path { get => _path; set { if (Set(ref _path, value)) RefreshState(); } }
    public int Priority { get => _priority; set { if (Set(ref _priority, value)) Raise(nameof(PriorityLabel)); } }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Raise(nameof(Status)); } }
    public bool IsOnline => Directory.Exists(Path);
    public string Status => !Enabled ? "Disabilitata" : IsOnline ? "Online" : "Offline";
    public string PriorityLabel => $"Priorità {Priority}";
    public void RefreshState() { Raise(nameof(IsOnline)); Raise(nameof(Status)); }
    public MasterLibraryDefinition ToDefinition() => new() { Name = Name, Path = Path, Priority = Priority, Enabled = Enabled };
}

public sealed class MasterOrganizerItem : BindableBase
{
    private string _camera; private string _gain; private string _offset; private string _temperature; private string _exposure; private string _readoutMode; private string _preflight = "Non verificato";
    public MasterOrganizerItem(FrameMetadata frame)
    {
        Frame = frame; _camera = frame.Camera.Value ?? ""; _gain = Input(frame.Gain.Value); _offset = Input(frame.Offset.Value);
        _temperature = Input(frame.SetTemperatureC.Value); _exposure = Input(frame.ExposureSeconds.Value); _readoutMode = frame.ReadoutMode.Value ?? "Default";
    }
    public FrameMetadata Frame { get; }
    public string FileName => Frame.FileName;
    public string Kind => Frame.Kind.ToString();
    public string Camera { get => _camera; set { if (Set(ref _camera, value)) Changed(); } }
    public string Gain { get => _gain; set { if (Set(ref _gain, value)) Changed(); } }
    public string Offset { get => _offset; set { if (Set(ref _offset, value)) Changed(); } }
    public string Temperature { get => _temperature; set { if (Set(ref _temperature, value)) Changed(); } }
    public string Exposure { get => _exposure; set { if (Set(ref _exposure, value)) Changed(); } }
    public string ReadoutMode { get => _readoutMode; set { if (Set(ref _readoutMode, value)) Changed(); } }
    public string Preflight { get => _preflight; private set => Set(ref _preflight, value); }
    public bool IsReady => TryRequest(out _);
    public string State => IsReady ? "Pronto" : $"Mancano: {string.Join(", ", Missing())}";
    public string SuggestedPath => TryRequest(out var request) ? MasterLibraryOrganizer.RelativePath(Frame, request!.Metadata) : "Completa i campi richiesti";
    public bool TryRequest(out MasterOrganizationRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(Camera) || !Try(Gain, out var gain) || !Try(Offset, out var offset) || !TryOptional(Temperature, out var temperature) || !TryOptional(Exposure, out var exposure)) return false;
        if (Frame.Kind == FrameKind.Dark && (temperature is null || exposure is null)) return false;
        request = new(Frame, new(Camera.Trim(), gain!.Value, offset!.Value, temperature, exposure, string.IsNullOrWhiteSpace(ReadoutMode) ? "Default" : ReadoutMode.Trim())); return true;
    }
    private IEnumerable<string> Missing() { if (string.IsNullOrWhiteSpace(Camera)) yield return "Camera"; if (!Try(Gain, out _)) yield return "Gain"; if (!Try(Offset, out _)) yield return "Offset"; if (Frame.Kind == FrameKind.Dark && !Try(Temperature, out _)) yield return "Temperatura"; if (Frame.Kind == FrameKind.Dark && !Try(Exposure, out _)) yield return "Esposizione"; }
    private void Changed() { Raise(nameof(IsReady)); Raise(nameof(State)); Raise(nameof(SuggestedPath)); Preflight = "Da verificare"; }
    public void SetPreflight(string value) => Preflight = value;
    private static string Input(double? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";
    private static bool Try(string text, out double? value) { value = null; if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) { value = number; return true; } return false; }
    private static bool TryOptional(string text, out double? value) { if (string.IsNullOrWhiteSpace(text)) { value = null; return true; } return Try(text, out value); }
}
