using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AstroForge.Core.Models;
using AstroForge.Core.Analysis;
using AstroForge.Core.Export;
using AstroForge.Core.Matching;
using AstroForge.Core.Scanning;
using AstroForge.Core.Sessions;
using AstroForge.Core.Validation;
using AstroForge.Core.Wbpp;
using AstroForge.App.Services;

namespace AstroForge.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private readonly ProjectScanner _scanner = new();
    private readonly JsonHeaderCache _headerCache = new();
    private readonly Stack<Action> _undo = new();
    private readonly AppState _state;
    private readonly HashSet<string> _kindOverrides = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<FrameMetadata> _frames = [];
    private ProjectAnalysis? _analysis;
    private ProjectStatistics? _statistics;
    private ProjectPlan? _plan;
    private ProjectTreeNode? _selectedNode;
    private FlatSetOption? _selectedFlatSet;
    private bool _isScanning;
    private bool _showIssuesOnly;
    private string _searchText = "";
    private string _libraryPath = @"E:\immagini\MSTE";
    private string _status = "Aggiungi una cartella N.I.N.A. e avvia la scansione";
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
    private string _currentProjectFile = "";
    private DateTimeOffset _projectCreatedAt = DateTimeOffset.Now;

    public MainViewModel()
    {
        _state = AppStateStore.Load();
        _libraryPath = _state.LibraryPath;
        _destinationPath = _state.DestinationPath;
        _projectName = _state.ProjectName;
        _sessionBoundaryHour = Math.Clamp(_state.SessionBoundaryHour, 0, 23);
        _projectDefaultGain = Input(_state.ProjectDefaultGain);
        _projectDefaultOffset = Input(_state.ProjectDefaultOffset);
        _projectDefaultTemperature = Input(_state.ProjectDefaultTemperatureC);
        _currentProjectFile = _state.LastProjectFile;
        foreach (var path in _state.SourcePaths.Where(Directory.Exists)) SourcePaths.Add(path);
        foreach (var pair in _state.Overrides.Where(pair => pair.Value.Kind is not null)) _kindOverrides.Add(pair.Key);
        ApplyOverridesCommand = new RelayCommand(ApplyOverrides, () => SelectedNode is not null && !IsScanning);
        ApplyLibraryOffsetCommand = new RelayCommand(ApplyLibraryOffset, () => _frames.Any(frame => frame.IsMaster) && !IsScanning);
        ApplyProjectDefaultsCommand = new RelayCommand(ApplyProjectDefaults, () => _frames.Count > 0 && !IsScanning);
        SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsScanning);
        ClearProjectCommand = new RelayCommand(ClearProject, () => (_frames.Count > 0 || _plan is not null) && !IsScanning);
        LinkFlatSetCommand = new RelayCommand(LinkFlatSet, CanLinkFlatSet);
        UnlinkFlatSetCommand = new RelayCommand(UnlinkFlatSet, () => GetManualLinkFrames().Any(frame => frame.Kind == FrameKind.Light && frame.FlatSetId.HasOverride) && !IsScanning);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0 && !IsScanning);
    }

    public ObservableCollection<string> SourcePaths { get; } = [];
    public ObservableCollection<ProjectTreeNode> TreeRoots { get; } = [];
    public ObservableCollection<ProjectTreeNode> PlannedTreeRoots { get; } = [];
    public ObservableCollection<WbppKeywordRow> WbppKeywords { get; } = [];
    public ObservableCollection<string> WbppNotes { get; } = [];
    public ObservableCollection<FlatSetOption> AvailableFlatSets { get; } = [];
    public ObservableCollection<FilterStatsRow> FilterStatistics { get; } = [];
    public ObservableCollection<SessionStatsRow> SessionStatistics { get; } = [];
    public ObservableCollection<NightStatsRow> NightStatistics { get; } = [];
    public ObservableCollection<string> SelectedIssues { get; } = [];
    public RelayCommand ApplyOverridesCommand { get; }
    public RelayCommand ApplyLibraryOffsetCommand { get; }
    public RelayCommand ApplyProjectDefaultsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearProjectCommand { get; }
    public RelayCommand LinkFlatSetCommand { get; }
    public RelayCommand UnlinkFlatSetCommand { get; }
    public RelayCommand UndoCommand { get; }
    public Array FrameKinds => Enum.GetValues<FrameKind>();

    public string LibraryPath { get => _libraryPath; set => Set(ref _libraryPath, value); }
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }
    public string DestinationPath { get => _destinationPath; set => Set(ref _destinationPath, value); }
    public string CurrentProjectFile { get => _currentProjectFile; private set { if (Set(ref _currentProjectFile, value)) Raise(nameof(ProjectDocumentStatus)); } }
    public string ProjectDocumentStatus => string.IsNullOrWhiteSpace(CurrentProjectFile) ? "Progetto non ancora salvato" : Path.GetFileName(CurrentProjectFile);
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
    public bool IsScanning { get => _isScanning; private set { if (Set(ref _isScanning, value)) { ApplyOverridesCommand.RaiseCanExecuteChanged(); ApplyLibraryOffsetCommand.RaiseCanExecuteChanged(); ApplyProjectDefaultsCommand.RaiseCanExecuteChanged(); SaveSettingsCommand.RaiseCanExecuteChanged(); ClearProjectCommand.RaiseCanExecuteChanged(); LinkFlatSetCommand.RaiseCanExecuteChanged(); UnlinkFlatSetCommand.RaiseCanExecuteChanged(); UndoCommand.RaiseCanExecuteChanged(); } } }
    public bool ShowIssuesOnly { get => _showIssuesOnly; set { if (Set(ref _showIssuesOnly, value)) RebuildTree(); } }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) RebuildTree(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public int TotalFiles => _frames.Count;
    public int TotalIssues => _frames.Sum(frame => frame.Issues.Count);
    public int OverrideCount => _frames.Count(frame => HasAnyOverride(frame));
    public int UnresolvedCalibrations => _analysis?.UnresolvedCount ?? 0;
    public bool IsProjectReady => _analysis?.Ready == true;
    public string ReadinessText { get => _readinessText; private set => Set(ref _readinessText, value); }
    public string CalibrationSummary { get => _calibrationSummary; private set => Set(ref _calibrationSummary, value); }
    public string PlanSummary => _plan is null ? "Genera il piano dopo aver risolto le calibrazioni." : $"{_plan.Files.Count} file · {HumanSize(_plan.RequiredBytes)} · {_plan.ProjectRoot}";
    public string TotalIntegrationText => _statistics is null ? "0 h" : FormatHours(_statistics.ExposureSeconds);
    public string StatisticsSummary => _statistics is null ? "Analizza il progetto per calcolare le statistiche." : $"{_statistics.LightCount} Light · {_statistics.FilterCount} filtri · {_statistics.ConfigurationSessionCount} sessioni · {_statistics.NightCount} notti";
    public string StatisticsDateRange => _statistics?.FirstCapture is null ? "Nessun intervallo temporale" : $"{_statistics.FirstCapture.Value.ToLocalTime():dd MMM yyyy} → {_statistics.LastCapture!.Value.ToLocalTime():dd MMM yyyy}";
    public string SelectionTitle => SelectedNode?.Name ?? "Nessuna selezione";
    public string SelectionDetail => SelectedNode is null ? "Seleziona un nodo dell'albero" : $"{SelectedNode.Count} frame · {SelectedNode.IssueCount} segnalazioni";
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

    public void AddSource(string path)
    {
        if (!SourcePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) { SourcePaths.Add(path); SaveState(); }
    }

    public void RemoveSource(string path) { SourcePaths.Remove(path); SaveState(); }

    public void RefreshManualSelection()
    {
        RefreshFlatSetOptions();
        Raise(nameof(ManualLinkSelectionText));
        LinkFlatSetCommand.RaiseCanExecuteChanged();
        UnlinkFlatSetCommand.RaiseCanExecuteChanged();
    }

    public void SaveState()
    {
        _state.SourcePaths = SourcePaths.ToList();
        _state.LibraryPath = LibraryPath;
        _state.DestinationPath = DestinationPath;
        _state.ProjectName = ProjectName;
        _state.SessionBoundaryHour = SessionBoundaryHour;
        _state.ProjectDefaultGain = ParseDefault(ProjectDefaultGain);
        _state.ProjectDefaultOffset = ParseDefault(ProjectDefaultOffset);
        _state.ProjectDefaultTemperatureC = ParseDefault(ProjectDefaultTemperature);
        _state.LastProjectFile = CurrentProjectFile;
        foreach (var frame in _frames)
        {
            if (!HasAnyOverride(frame) && !_kindOverrides.Contains(frame.Path)) { _state.Overrides.Remove(frame.Path); continue; }
            var snapshot = AppStateStore.Snapshot(frame);
            if (!_kindOverrides.Contains(frame.Path)) snapshot.Kind = null;
            _state.Overrides[frame.Path] = snapshot;
        }
        AppStateStore.Save(_state);
        if (!string.IsNullOrWhiteSpace(CurrentProjectFile)) SaveProjectDocument(CurrentProjectFile);
    }

    public void SaveProject(string path)
    {
        CurrentProjectFile = Path.GetFullPath(path);
        SaveState();
        Status = $"Progetto salvato · {Path.GetFileName(CurrentProjectFile)}";
    }

    public async Task LoadProjectAsync(string path)
    {
        var document = ProjectDocumentStore.Load(path);
        CurrentProjectFile = Path.GetFullPath(path);
        _projectCreatedAt = document.CreatedAt;
        SourcePaths.Clear();
        foreach (var source in document.SourcePaths) SourcePaths.Add(source);
        LibraryPath = document.LibraryPath;
        DestinationPath = document.DestinationPath;
        ProjectName = document.ProjectName;
        _sessionBoundaryHour = Math.Clamp(document.SessionBoundaryHour, 0, 23);
        Raise(nameof(SessionBoundaryHour));
        ProjectDefaultGain = Input(document.DefaultGain);
        ProjectDefaultOffset = Input(document.DefaultOffset);
        ProjectDefaultTemperature = Input(document.DefaultTemperatureC);
        _state.Overrides = new(document.Overrides, StringComparer.OrdinalIgnoreCase);
        _kindOverrides.Clear();
        foreach (var pair in _state.Overrides.Where(pair => pair.Value.Kind is not null)) _kindOverrides.Add(pair.Key);
        Status = $"Progetto aperto · {Path.GetFileName(CurrentProjectFile)}";
        if (SourcePaths.Count > 0) await ScanAsync(); else SaveState();
    }

    private void SaveProjectDocument(string path) => ProjectDocumentStore.Save(path, new AstroForgeProjectDocument
    {
        CreatedAt = _projectCreatedAt, ProjectName = ProjectName, SourcePaths = SourcePaths.ToList(), LibraryPath = LibraryPath,
        DestinationPath = DestinationPath, SessionBoundaryHour = SessionBoundaryHour, DefaultGain = ParseDefault(ProjectDefaultGain),
        DefaultOffset = ParseDefault(ProjectDefaultOffset), DefaultTemperatureC = ParseDefault(ProjectDefaultTemperature),
        Overrides = new(_state.Overrides, StringComparer.OrdinalIgnoreCase)
    });

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (SourcePaths.Count == 0) { Status = "Aggiungi almeno una sorgente"; return; }
        IsScanning = true;
        Progress = 0;
        Status = "Lettura header FITS/XISF…";
        try
        {
            var roots = SourcePaths.Concat(Directory.Exists(LibraryPath) ? [LibraryPath] : []);
            var progress = new Progress<ScanProgress>(item =>
            {
                Progress = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
                Status = $"{item.Completed}/{item.Total} · {item.CurrentFile}";
            });
            var sessionSettings = new SessionSettings(TimeZoneInfo.Local, new TimeOnly(SessionBoundaryHour, 0));
            _frames = await _scanner.ScanAsync(roots, sessionSettings, progress, cancellationToken, _headerCache);
            if (Directory.Exists(LibraryPath)) LibraryMetadataResolver.Apply(_frames, LibraryPath);
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
            Status = $"{TotalFiles} file analizzati · {_scanner.LastCacheHits} da cache · {_scanner.LastParsedFiles} letti · {TotalIssues} segnalazioni";
            Raise(nameof(TotalFiles)); Raise(nameof(TotalIssues)); Raise(nameof(OverrideCount));
            ApplyLibraryOffsetCommand.RaiseCanExecuteChanged();
            ApplyProjectDefaultsCommand.RaiseCanExecuteChanged();
            ClearProjectCommand.RaiseCanExecuteChanged();
            SaveState();
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
        _plan = ProjectExporter.BuildPlan(ProjectName, DestinationPath, _analysis);
        BuildPlannedTree();
        Raise(nameof(PlanSummary));
        Status = $"Piano pronto: {_plan.Files.Count} file, {HumanSize(_plan.RequiredBytes)}";
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

    public void ClearHeaderCache()
    {
        _headerCache.Clear();
        Status = "Cache header svuotata · i file verranno riletti alla prossima analisi";
    }

    private void SaveSettings()
    {
        SaveState();
        Status = "Impostazioni, percorsi e override salvati";
    }

    private void ClearProject()
    {
        _frames = [];
        _analysis = null;
        _statistics = null;
        _plan = null;
        _undo.Clear();
        SelectedNode = null;
        TreeRoots.Clear();
        PlannedTreeRoots.Clear();
        WbppKeywords.Clear();
        WbppNotes.Clear();
        AvailableFlatSets.Clear();
        FilterStatistics.Clear();
        SessionStatistics.Clear();
        NightStatistics.Clear();
        SelectedIssues.Clear();
        SearchText = "";
        ShowIssuesOnly = false;
        Progress = 0;
        ReadinessText = "Non analizzato";
        CalibrationSummary = "Seleziona uno o più Light per vedere le calibrazioni assegnate.";
        Status = "Progetto svuotato dalla memoria · nessun file originale è stato cancellato";
        Raise(nameof(TotalFiles)); Raise(nameof(TotalIssues)); Raise(nameof(OverrideCount));
        Raise(nameof(UnresolvedCalibrations)); Raise(nameof(IsProjectReady)); Raise(nameof(PlanSummary));
        Raise(nameof(TotalIntegrationText)); Raise(nameof(StatisticsSummary)); Raise(nameof(StatisticsDateRange));
        ApplyLibraryOffsetCommand.RaiseCanExecuteChanged();
        ApplyProjectDefaultsCommand.RaiseCanExecuteChanged();
        ClearProjectCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
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
        IsScanning = true;
        try
        {
            var progress = new Progress<ExportProgress>(item =>
            {
                Progress = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
                Status = $"Copia verificata {item.Completed}/{item.Total} · {item.CurrentFile}";
            });
            var output = await ProjectExporter.ExecuteAsync(_plan!, progress, cancellationToken);
            Status = $"Progetto verificato: {output}";
            return output;
        }
        finally { IsScanning = false; }
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
    }

    private void AddOpticalSessionTree(FrameMetadata[] visibleFrames)
    {
        if (_analysis is null) return;
        var visible = visibleFrames.Select(frame => frame.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        var visiblePaths = available.Select(frame => frame.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        var unused = available.Where(frame => !usedPaths.Contains(frame.Path)).ToArray();
        if (unused.Length > 0)
        {
            var unusedChildren = new List<ProjectTreeNode>();
            var unusedDarks = unused.Where(frame => frame.Kind == FrameKind.Dark).ToArray();
            var unusedBiases = unused.Where(frame => frame.Kind == FrameKind.Bias).ToArray();
            if (unusedDarks.Length > 0) unusedChildren.Add(GroupNode("unused-darks", "Dark non collegati", "D", unusedDarks, unusedDarks.Select(Leaf)));
            if (unusedBiases.Length > 0) unusedChildren.Add(GroupNode("unused-biases", "Bias non collegati", "B", unusedBiases, unusedBiases.Select(Leaf)));
            sessionNodes.Add(GroupNode("unused-sensor-calibration", "Calibrazioni non collegate", "?", unused, unusedChildren));
        }
        var sessions = GroupNode("sensor-sessions", "Sessioni sensore", "S", available, sessionNodes);
        TreeRoots.Add(Category("no-filter", "Senza filtro", "∅", available, [sessions]));
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
        var libraryRoot = Path.GetFullPath(LibraryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var masters = _frames.Where(frame => frame.IsMaster && Path.GetFullPath(frame.Path).StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase)).ToArray();
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
        Raise(nameof(SelectionTitle)); Raise(nameof(SelectionDetail)); Raise(nameof(ApplyLabel)); Raise(nameof(ManualLinkSelectionText)); Raise(nameof(GainSource)); Raise(nameof(OffsetSource)); Raise(nameof(TemperatureSource)); Raise(nameof(FilterSource)); Raise(nameof(FlatSetSource)); Raise(nameof(SessionSource)); Raise(nameof(RawHeaders));
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
    private static bool HasAnyOverride(FrameMetadata frame) => frame.Gain.HasOverride || frame.Offset.HasOverride || frame.SetTemperatureC.HasOverride || frame.FilterName.HasOverride || frame.FlatSetId.HasOverride || frame.SessionId.HasOverride;

    private void RefreshIntelligence()
    {
        foreach (var frame in _frames)
            frame.Issues.RemoveAll(issue => issue.Code.StartsWith("calibration.", StringComparison.Ordinal));

        _analysis = ProjectAnalyzer.Analyze(_frames);
        RefreshStatistics();
        RefreshFlatSetOptions();
        foreach (var item in _analysis.Lights)
        {
            AddCalibrationIssue(item.Light, "flat", item.Flat);
            AddCalibrationIssue(item.Light, "dark", item.Dark);
            AddCalibrationIssue(item.Light, "bias", item.Bias);
        }

        var recipe = WbppRecipeEngine.Recommend(_analysis);
        WbppKeywords.Clear();
        foreach (var keyword in recipe.Keywords)
            WbppKeywords.Add(new(keyword.Keyword, keyword.Pre ? "ON" : "OFF", keyword.Post ? "ON" : "OFF", keyword.Reason));
        WbppNotes.Clear();
        foreach (var note in recipe.Notes) WbppNotes.Add(note);

        ReadinessText = _analysis.Ready
            ? $"Pronto per WBPP · {_analysis.Lights.Count} Light con Flat, Dark e Bias assegnati"
            : $"Da risolvere · {_analysis.UnresolvedCount} assegnazioni di calibrazione mancanti o ambigue";
        _plan = null;
        PlannedTreeRoots.Clear();
        Raise(nameof(UnresolvedCalibrations));
        Raise(nameof(IsProjectReady));
        Raise(nameof(PlanSummary));
        UpdateCalibrationSummary();
    }

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
            var gain = item.Gains.Count == 0 ? "Gain non disponibile" : $"Gain {string.Join(", ", item.Gains.Select(value => value.ToString("0.###")))}";
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
        var paths = SelectedNode.Frames.Where(frame => frame.Kind == FrameKind.Light).Select(frame => frame.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static string FormatHours(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var hours = (int)Math.Floor(duration.TotalHours);
        return duration.Minutes == 0 ? $"{hours} h" : $"{hours} h {duration.Minutes:00} min";
    }

    private static string TemperatureRange(double? minimum, double? maximum)
    {
        if (minimum is null || maximum is null) return "Temperatura non disponibile";
        return Math.Abs(minimum.Value - maximum.Value) < 0.05 ? $"Temperatura {Signed(minimum.Value)} °C" : $"Temperatura {Signed(minimum.Value)}…{Signed(maximum.Value)} °C";
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
}

public sealed record WbppKeywordRow(string Keyword, string Pre, string Post, string Reason);
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
