using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using AstroForge.App.Services;
using AstroForge.App.ViewModels;
using AstroForge.Core.Analysis;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;
using AstroForge.CrossPlatform.Controls;

namespace AstroForge.CrossPlatform.ViewModels;

public enum NextActionKind { AddSources, Analyze, Busy, Continue, Resolve, NameProject, ChooseDestination, Export, Exporting, OpenPixInsight }

public sealed record CalibrationLine(string Label, string Count, IBrush Dot);

public sealed record CommandItem(string Id, string Title, string Hint);

/// <summary>
/// State of the window around its four steps: which step the project is on, the one button that moves it forward,
/// the project title, and the stack, calibration ring, nights and open choice the steps draw. Derived from the shared view
/// model and refreshed once per dispatcher pass whatever changed.
/// </summary>
public sealed class ShellViewModel : BindableBase
{
    public const int StepCount = 4;
    public const int ImportStep = 0, SetupStep = 1, CalibrationStep = 2, ExportStep = 3;

    private readonly MainViewModel _main;
    private readonly ObservatoryViewModel _observatory;
    private bool _queued;
    private int _step;
    private int _maxReached;
    private bool _wasAnalysed;
    private bool _stayOnce;
    private int _nightIndex;
    private int _palette;
    private string _query = "";
    private int _commandIndex;
    private List<(string Night, double Hours, int Lights)> _nightTotals = [];
    private IReadOnlyList<CommandItem> _commands = [];
    private object? _nightsKey;

    public ShellViewModel(MainViewModel main, ObservatoryViewModel observatory)
    {
        _main = main;
        _observatory = observatory;
        main.PropertyChanged += (_, e) => { if (e.PropertyName is not (nameof(MainViewModel.SearchText))) Schedule(); };
        observatory.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(ObservatoryViewModel.PendingCount) or nameof(ObservatoryViewModel.HasInstrument)) Schedule(); };
        foreach (var collection in new INotifyCollectionChanged[] { main.FilterStatistics, main.NightStatistics, main.ReviewQueue, main.SourcePaths, main.MasterLibraries, main.ExportHistory, observatory.FilterCards })
            collection.CollectionChanged += (_, _) => Schedule();
        Refresh();
    }

    public MainViewModel Main => _main;
    public ObservatoryViewModel Observatory => _observatory;

    /// <summary>Raised when the project moves to another step, by the button, the stepper or the analysis finishing.</summary>
    public event EventHandler<int>? StepChanged;

    /// <summary>The step on screen: 0 Import, 1 Setup, 2 Calibration, 3 Export.</summary>
    public int CurrentStep
    {
        get => _step;
        set
        {
            var next = Math.Clamp(value, 0, StepCount - 1);
            if (!Set(ref _step, next)) return;
            _maxReached = Math.Max(_maxReached, next);
            Refresh();
            StepChanged?.Invoke(this, next);
        }
    }

    /// <summary>The next analysis re-reads a project the user was already working on (data added): it does not carry on to another step.</summary>
    public void StayOnStep() => _stayOnce = true;

    public bool CanGoBack => _step > 0;
    public bool IsImportStep => _step == ImportStep;
    public bool IsSetupStep => _step == SetupStep;
    public bool IsCalibrationStep => _step == CalibrationStep;
    public bool IsExportStep => _step == ExportStep;

    /// <summary>A step can be opened once there is something to show for it: the import always, the rest after the analysis.</summary>
    public bool CanOpen(int step) => step <= ImportStep || Analyzed || step <= _step;

    private bool English => _main.UiLanguage == UiLocalization.English;
    private CultureInfo Culture => English ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.GetCultureInfo("it-IT");
    private IReadOnlyList<LightCalibrationAnalysis> Lights => _main.Analysis?.Lights ?? [];
    public bool Analyzed => _main.HasAnalysis && !_main.NeedsReanalysis;
    public bool NeedsAnalysis => !Analyzed;
    public bool HasSources => _main.HasSources;
    public bool NoSources => !_main.HasSources;
    public bool HasLibraries => _main.MasterLibraries.Count > 0;
    public bool NoLibraries => _main.MasterLibraries.Count == 0;

    private void Schedule()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; Refresh(); }, DispatcherPriority.Background);
    }

    // ---- Title bar ----
    public string TargetTitle
    {
        get
        {
            var target = Lights.Select(item => item.Light.ObjectName.Value?.Trim()).Where(name => !string.IsNullOrEmpty(name))
                .GroupBy(name => name!, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(target)) return target;
            if (!string.IsNullOrWhiteSpace(_main.ProjectName)) return _main.ProjectName;
            return English ? "New project" : "Nuovo progetto";
        }
    }

    /// <summary>The channels on hand, in the italic accent next to the target: "Hα · OIII · SII".</summary>
    public string TargetAccent => string.Join(" · ", _observatory.FilterCards.Where(card => !card.NeedsConfirmation).Select(card => ChannelOf(card.Name)).Distinct().Take(4));

    public string StateLabel => StateText.ToUpper(Culture);
    private string StateText => _main.IsScanning ? (English ? "Analysing" : "Analisi in corso")
        : Analyzed ? (English ? "Analysed" : "Analizzato")
        : _main.HasSources ? (English ? "To analyse" : "Da analizzare")
        : (English ? "No data" : "Nessun dato");
    public bool StateLive => Analyzed || _main.IsScanning;
    public string RangeLabel => RangeText.ToUpper(Culture);
    private string RangeText
    {
        get
        {
            if (_main.IsScanning) return $"{_main.Progress:0} %";
            if (_nightTotals.Count == 0) return _main.HasSources ? SourcesLabel : "";
            var dates = _nightTotals.Select(night => ParseNight(night.Night)).Where(date => date is not null).Select(date => date!.Value).ToList();
            var nights = _nightTotals.Count == 1 ? (English ? "1 night" : "1 notte") : English ? $"{_nightTotals.Count} nights" : $"{_nightTotals.Count} notti";
            return dates.Count == 0 ? nights : $"{nights} · {dates.Min().ToString("d MMM", Culture)} – {dates.Max().ToString("d MMM", Culture)}";
        }
    }
    private string SourcesLabel => _main.SourcePaths.Count == 1 ? (English ? "1 source" : "1 sorgente") : English ? $"{_main.SourcePaths.Count} sources" : $"{_main.SourcePaths.Count} sorgenti";
    public bool HasPendingFilters => _observatory.PendingCount > 0;
    public string FilterChip => English ? $"Filters · {_observatory.PendingCount} to confirm" : $"Filtri · {_observatory.PendingCount} da confermare";
    public bool IsScanning => _main.IsScanning;
    public double Progress => _main.Progress;

    // ---- What is new: data added to a project that already had some ----
    public bool HasNovelty => _main.Novelty is not null;
    public string NoveltyTitle => English ? "What is new in the project" : "Novità nel progetto";
    public string NoveltyAction => English ? "Go to export" : "Vai a Esporta";
    public string NoveltyText
    {
        get
        {
            if (_main.Novelty is not { } news) return "";
            var parts = new List<string>();
            if (news.WholeNights > 0) parts.Add(news.WholeNights == 1 ? (English ? "1 new night" : "1 notte nuova") : English ? $"{news.WholeNights} new nights" : $"{news.WholeNights} notti nuove");
            var grown = news.Nights.Where(night => !night.WholeNight).Sum(night => night.Lights);
            if (grown > 0) parts.Add(English ? $"{grown} more Lights on nights already in the project" : $"{grown} Light in più su notti già nel progetto");
            else if (news.NewLights > 0) parts.Add(English ? $"{news.NewLights} Lights" : $"{news.NewLights} Light");
            if (news.NewFilters.Count > 0) parts.Add((English ? "new filter " : "filtro nuovo ") + string.Join(", ", news.NewFilters.Select(DisplayFilter)));
            if (news.NewFlats > 0) parts.Add(English ? $"{news.NewFlats} Flats" : $"{news.NewFlats} Flat");
            parts.Add("+" + ObservatoryViewModel.HoursLabel(news.NewIntegrationSeconds / 3600));
            return string.Join(" · ", parts);
        }
    }
    private bool Updating => _main.HasExportHistory || _main.HasNovelty;

    // ---- The one button that moves the project forward ----
    private bool Exporting => _main.ExportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Preflighting or ExportRunState.Cancelling;
    private bool Exported => _main.ExportState == ExportRunState.Completed;
    private int OpenChoices => _main.ReviewQueue.Count;

    /// <summary>What the main button does on the step on screen. The same button, in the same place, on every step.</summary>
    public NextActionKind NextKind
    {
        get
        {
            if (_main.IsScanning) return NextActionKind.Busy;
            if (Exporting) return NextActionKind.Exporting;
            if (!_main.HasSources) return NextActionKind.AddSources;
            if (!Analyzed) return NextActionKind.Analyze;
            return _step switch
            {
                ImportStep or SetupStep => NextActionKind.Continue,
                CalibrationStep => OpenChoices > 0 ? NextActionKind.Resolve : NextActionKind.Continue,
                _ when OpenChoices > 0 => NextActionKind.Resolve,
                _ when Exported => NextActionKind.OpenPixInsight,
                _ when string.IsNullOrWhiteSpace(_main.ProjectName) => NextActionKind.NameProject,
                _ when string.IsNullOrWhiteSpace(_main.DestinationPath) => NextActionKind.ChooseDestination,
                _ => NextActionKind.Export
            };
        }
    }

    public string NextLabel => NextKind switch
    {
        NextActionKind.Busy => English ? $"Analysing · {_main.Progress:0} %" : $"Analisi · {_main.Progress:0} %",
        NextActionKind.Exporting => English ? $"Exporting · {_main.ExportProgress:0} %" : $"Esportazione · {_main.ExportProgress:0} %",
        NextActionKind.AddSources => English ? "Add captures" : "Aggiungi acquisizioni",
        NextActionKind.Analyze => _main.HasAnalysis ? (English ? "Analyse again" : "Rianalizza") : (English ? "Analyse" : "Analizza"),
        NextActionKind.Continue => English ? "Continue" : "Continua",
        NextActionKind.Resolve => OpenChoices == 1 ? (English ? "Resolve 1 choice" : "Risolvi 1 scelta") : English ? $"Resolve {OpenChoices} choices" : $"Risolvi {OpenChoices} scelte",
        NextActionKind.NameProject => English ? "Name the project" : "Dai un nome al progetto",
        NextActionKind.ChooseDestination => English ? "Choose the destination" : "Scegli la destinazione",
        NextActionKind.Export => _main.HasExportHistory
            ? (ExportSize is { } update ? (English ? $"Update project · {update}" : $"Aggiorna progetto · {update}") : (English ? "Update project" : "Aggiorna progetto"))
            : ExportSize is { } size ? (English ? $"Export {size}" : $"Esporta {size}") : (English ? "Export" : "Esporta"),
        _ => English ? "Open in PixInsight" : "Apri in PixInsight"
    };
    public bool NextEnabled => NextKind is not NextActionKind.Busy;
    // On an update it is the new data that gets copied, not the whole project again.
    private string? ExportSize => _main.Novelty is { NewBytes: > 0 } news && Updating ? HumanSize(news.NewBytes)
        : (_main.BytesToCopy ?? _main.PlannedBytes) is { } bytes and > 0 ? HumanSize(bytes) : null;

    /// <summary>One line under the stepper, in the footer: what this step asks, or what is in the way.</summary>
    public string StepHint
    {
        get
        {
            if (_main.IsScanning) return English ? "Reading the headers…" : "Lettura degli header…";
            var pending = _observatory.PendingCount;
            return _step switch
            {
                ImportStep when !_main.HasSources => English ? "Add the captures to start. The Master Library is optional and remembered for every project." : "Aggiungi le acquisizioni per iniziare. La Master Library è facoltativa e resta salvata per ogni progetto.",
                ImportStep when !Analyzed => English ? "Ready: Forge reads the headers only, your files are never changed." : "Pronto: Forge legge solo gli header, i tuoi file non vengono mai modificati.",
                ImportStep => English ? "Analysed. Next: check that this is your gear." : "Analizzato. Prossimo passo: controlla che sia la tua attrezzatura.",
                SetupStep when !Analyzed => English ? "Analyse the project to see the gear Forge found." : "Analizza il progetto per vedere l'attrezzatura trovata.",
                SetupStep when pending > 0 => English ? $"{pending} filter(s) to identify. Pick them from the catalogue, or continue: the name is kept as written." : $"{pending} filtro/i da riconoscere. Sceglili dal catalogo, oppure continua: il nome resta quello scritto.",
                SetupStep => English ? "Everything recognised. Tap any part to change it if it is not right." : "Tutto riconosciuto. Tocca una voce per cambiarla se non è giusta.",
                CalibrationStep when !Analyzed => English ? "Analyse the project to match Flat, Dark and Bias." : "Analizza il progetto per abbinare Flat, Dark e Bias.",
                CalibrationStep when OpenChoices > 0 => English ? "Some Lights need a choice before the export." : "Alcuni Light richiedono una scelta prima dell'esportazione.",
                CalibrationStep => English ? "Every Light has its Flat, Dark and Bias." : "Ogni Light ha il suo Flat, Dark e Bias.",
                _ when Exported => English ? "Exported. Open PixInsight: WBPP is ready, press Run when you are." : "Esportato. Apri PixInsight: WBPP è pronto, premi Run quando vuoi.",
                _ when OpenChoices > 0 => English ? "Resolve the open calibration choices first." : "Risolvi prima le scelte di calibrazione aperte.",
                _ when Updating && ExportSize is { } size => English ? $"Update: only the {size} of new data are copied and verified; what is already in the project stays as it is." : $"Aggiornamento: si copiano e verificano solo i {size} di dati nuovi; ciò che è già nel progetto resta com'è.",
                _ => English ? "Name the project, choose where it goes, then export. Only new files are copied." : "Dai un nome al progetto, scegli dove va, poi esporta. Si copiano solo i file nuovi."
            };
        }
    }

    // ---- The export page says where it is: before, during and after ----
    public string ExportEyebrow => Exported ? (English ? "STEP 4 OF 4 · DONE" : "PASSO 4 DI 4 · FATTO")
        : Updating ? (English ? "STEP 4 OF 4 · UPDATE" : "PASSO 4 DI 4 · AGGIORNA")
        : English ? "STEP 4 OF 4 · EXPORT" : "PASSO 4 DI 4 · ESPORTA";
    public string ExportTitle => Exported ? (English ? "Ready for PixInsight" : "Pronto per PixInsight")
        : Exporting ? (English ? "Copying and checking…" : "Copia e verifica in corso…")
        : Updating ? (English ? "Add the new data to the project" : "Aggiungi le novità al progetto")
        : English ? "Where do we put it?" : "Dove lo mettiamo?";
    public string ExportSubtitle => Exported
        ? (English ? "The project folder is ready and the WBPP instance carries files, masters and groups. Open it, check Calibration and press Run." : "La cartella di progetto è pronta e l’istanza WBPP porta file, master e gruppi. Aprila, controlla Calibration e premi Run.")
        : Updating ? (_main.Novelty is null
            ? (English ? "This folder already is a project: Forge copies and verifies only the files that are not in it yet." : "Questa cartella è già un progetto: Forge copia e verifica solo i file che non ci sono ancora.")
            : (English ? $"This folder already is a project. New: {NoveltyText}. Forge copies and verifies only those files; the branches that carry them are lit below." : $"Questa cartella è già un progetto. Novità: {NoveltyText}. Forge copia e verifica solo quei file; i rami che li portano sono illuminati qui sotto."))
        : English ? "Forge copies and verifies every file, and next time only adds the new ones." : "Forge copia e verifica ogni file, e la prossima volta aggiunge solo i nuovi.";

    // ---- Stepper ----
    public IReadOnlyList<ShellStep> Steps
    {
        get
        {
            var analysed = Analyzed;
            var calibrated = analysed && _main.Analysis?.Ready == true && OpenChoices == 0;
            var pending = _observatory.PendingCount;
            // Now is the step on screen. Done is a step with nothing left to do that the project has moved past; a step the project
            // has been through and left something open on is Attention. Everything else waits.
            ShellStep Step(int index, string title, string detail, bool complete, bool open) => new(title, detail,
                index == _step ? StepState.Now : complete ? StepState.Done : open && index <= _maxReached ? StepState.Attention : StepState.Pending,
                complete, open && index <= _maxReached, CanOpen(index));

            var importDetail = analysed && _main.Novelty is { } news ? (English ? $"{_main.AnalyzedFileCount} files · +{news.NewFiles} new" : $"{_main.AnalyzedFileCount} file · +{news.NewFiles} nuovi")
                : analysed ? (English ? $"{_main.AnalyzedFileCount} files · {Nights(_nightTotals.Count)}" : $"{_main.AnalyzedFileCount} file · {Nights(_nightTotals.Count)}")
                : _main.HasSources ? (English ? $"{SourcesLabel} · to analyse" : $"{SourcesLabel} · da analizzare")
                : (English ? "captures and masters" : "acquisizioni e master");
            var setupDetail = !analysed ? (English ? "after the analysis" : "dopo l'analisi")
                : _observatory.PendingCount > 0 ? (_observatory.PendingCount == 1 ? (English ? "1 filter to identify" : "1 filtro da riconoscere") : English ? $"{_observatory.PendingCount} filters to identify" : $"{_observatory.PendingCount} filtri da riconoscere")
                : (English ? $"{_observatory.CameraShortName} · {Filters(_observatory.Filters.Count)}" : $"{_observatory.CameraShortName} · {Filters(_observatory.Filters.Count)}");
            var calibrationDetail = !analysed ? (English ? "after the analysis" : "dopo l'analisi")
                : OpenChoices == 0 ? (English ? "all calibrated" : "tutto calibrato")
                : OpenChoices == 1 ? (English ? "1 open choice" : "1 scelta aperta")
                : English ? $"{OpenChoices} open choices" : $"{OpenChoices} scelte aperte";
            var exportDetail = Exported ? (English ? "exported" : "esportato")
                : ExportSize is { } size ? (Updating ? (English ? $"update · {size} new" : $"aggiorna · {size} nuovi") : English ? $"{size} to copy" : $"{size} da copiare")
                : (English ? "project folder" : "cartella di progetto");
            return
            [
                Step(ImportStep, English ? "Import" : "Importa", importDetail, analysed, false),
                Step(SetupStep, English ? "Gear" : "Strumento", setupDetail, analysed && pending == 0 && _maxReached > SetupStep, analysed && pending > 0),
                Step(CalibrationStep, English ? "Calibration" : "Calibrazioni", calibrationDetail, calibrated && _maxReached > CalibrationStep, analysed && !calibrated),
                Step(ExportStep, English ? "Export" : "Esporta", exportDetail, Exported, false)
            ];
        }
    }

    private string Nights(int count) => count == 1 ? (English ? "1 night" : "1 notte") : English ? $"{count} nights" : $"{count} notti";
    private string Filters(int count) => count == 1 ? (English ? "1 filter" : "1 filtro") : English ? $"{count} filters" : $"{count} filtri";

    // ---- Stack preview ----
    public int NightCount => Math.Max(1, _nightTotals.Count);
    public bool HasNights => _nightTotals.Count > 0;
    public int NightIndex
    {
        get => Math.Clamp(_nightIndex, 1, NightCount);
        set { if (Set(ref _nightIndex, Math.Clamp(value, 1, NightCount))) RaiseStack(); }
    }
    private double CumulativeHours(int nights) => _nightTotals.Take(nights).Sum(night => night.Hours);
    public double Snr => HasNights && _nightTotals[0].Hours > 0 ? Math.Sqrt(CumulativeHours(NightIndex) / _nightTotals[0].Hours) : 1;
    public string HoursBig => HasNights ? ((int)Math.Floor(CumulativeHours(NightIndex) + 1e-6)).ToString(CultureInfo.InvariantCulture) : "0";
    public string HoursSmall
    {
        get
        {
            var minutes = (int)Math.Round(CumulativeHours(NightIndex) * 60) % 60;
            return minutes == 0 ? " h" : $" h {minutes:00}";
        }
    }
    public string SnrText => $"×{Snr.ToString("0.0", Culture)}";
    public string NoiseText => Snr <= 1.0001 ? "—" : $"−{(1 - 1 / Snr) * 100:0} %";
    public string LightsText => _nightTotals.Take(NightIndex).Sum(night => night.Lights).ToString("N0", Culture);
    public string NightLabel => !HasNights ? "—"
        : English ? $"{NightIndex} of {NightCount} · {NightShort(_nightTotals[NightIndex - 1].Night)}" : $"{NightIndex} di {NightCount} · {NightShort(_nightTotals[NightIndex - 1].Night)}";
    public int Palette { get => _palette; set { if (Set(ref _palette, Math.Clamp(value, 0, 1))) { Raise(nameof(IsHoo)); Raise(nameof(IsSho)); Raise(nameof(StackEyebrow)); Raise(nameof(StackNote)); } } }
    public bool IsHoo { get => Palette == 0; set { if (value) Palette = 0; } }
    public bool IsSho { get => Palette == 1; set { if (value) Palette = 1; } }
    private IReadOnlyList<string> Channels => _observatory.FilterCards.Where(card => !card.NeedsConfirmation).Select(card => ChannelOf(card.Name)).ToList();
    public bool CanSho => Channels.Contains("SII") && Channels.Contains("Hα");
    public string StackEyebrow => StackEyebrowText.ToUpper(Culture);
    private string StackEyebrowText => English
        ? $"Simulated integration · {(Palette == 1 ? "SHO palette (Hubble)" : "HOO palette")}"
        : $"Integrazione simulata · {(Palette == 1 ? "palette SHO (Hubble)" : "palette HOO")}";
    public string StackNote => Palette == 1
        ? (English ? "SII red, Hα green, OIII blue." : "SII in rosso, Hα in verde, OIII in blu.")
        : (English ? "Noise falls as 1/√t." : "Il rumore cala come 1/√t.");
    public string Seed => TargetTitle;

    // ---- Calibration ring ----
    private int Total => Lights.Count;
    private double Share(Func<LightCalibrationAnalysis, bool> accepted) => Total == 0 ? 0 : Lights.Count(accepted) / (double)Total;
    public double FlatShare => Share(item => item.Flat.IsAccepted);
    public double DarkShare => Share(item => item.Dark.IsAccepted);
    public double BiasShare => Share(item => item.Bias.IsAccepted);
    private int FullyCalibrated => Lights.Count(item => item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted);
    public string CalibratedPercent => Total == 0 ? "—" : $"{FullyCalibrated * 100.0 / Total:0}%";
    public int OpenLights => Total - FullyCalibrated;
    public bool HasOpenLights => OpenLights > 0;
    public IReadOnlyList<CalibrationLine> CalibrationLines
    {
        get
        {
            if (Total == 0) return [];
            var flats = _main.Analysis?.FlatGroups.Count ?? 0;
            var dark = Distinct(Lights.Select(item => item.Dark.Selected?.Frame).Where(frame => frame is not null).Select(frame => $"{frame!.ExposureSeconds.Value:0.#} s {Temperature(frame.EffectiveTemperatureC)}"));
            var bias = Distinct(Lights.Select(item => item.Bias.Selected?.Frame).Where(frame => frame is not null).Select(frame => $"gain {frame!.Gain.Value:0.#}"));
            return
            [
                new(flats == 1 ? "Flat · 1 set" : $"Flat · {flats} set", $"{Lights.Count(item => item.Flat.IsAccepted)}/{Total}", Brush("#3FE0D0")),
                new($"Dark · {dark}", $"{Lights.Count(item => item.Dark.IsAccepted)}/{Total}", Brush("#9DB8FF")),
                new($"Bias · {bias}", $"{Lights.Count(item => item.Bias.IsAccepted)}/{Total}", Brush("#C59BFF"))
            ];
        }
    }
    public string OpenLabel => English ? "To choose" : "Da scegliere";

    // ---- Open choice ----
    public ReviewQueueItem? Choice => _main.ReviewQueue.FirstOrDefault();
    public bool HasChoice => Choice is not null;
    public bool NoChoice => Choice is null && _main.HasAnalysis;
    public bool AllCalibrated => Analyzed && Choice is null;
    public string ChoiceBadge => _main.ReviewQueue.Count == 1 ? (English ? "1 choice" : "1 scelta") : English ? $"{_main.ReviewQueue.Count} choices" : $"{_main.ReviewQueue.Count} scelte";
    public string ChoiceTitle => Choice is { } item ? $"{item.Calibration} · {item.Filter}" : "";
    public string ChoiceSubtitle => Choice is { } item ? (English ? $"night of {NightLong(item.Night)} · {item.FrameCount} subs" : $"notte del {NightLong(item.Night)} · {item.FrameCount} pose") : "";
    public ResolveNode? ChoiceSource => Choice is { } item ? new ResolveNode($"{item.FrameCount} Light {item.Filter}", Signature(item.Frame)) : null;
    public IReadOnlyList<ResolveNode> ChoiceOptions => Choice?.Candidates.Take(3).Select((candidate, index) =>
        new ResolveNode(CandidateName(candidate), index == 0 ? $"{candidate.Compatibility} · {(English ? "recommended" : "consigliato")}" : candidate.Compatibility)).ToList() ?? [];
    public Color ChoiceAccent => Choice is { } item ? ColourOf(item.Filter) : Color.Parse("#9DB8FF");
    public string UseFirstLabel => Choice?.Candidates.ElementAtOrDefault(0) is { } first ? (English ? $"Use {CandidateName(first)}" : $"Usa {CandidateName(first)}") : "";
    public string UseSecondLabel => Choice?.Candidates.ElementAtOrDefault(1) is { } second ? (English ? $"Use {CandidateName(second)}" : $"Usa {CandidateName(second)}") : "";
    public bool HasFirst => Choice?.Candidates.Count > 0;
    public bool HasSecond => Choice?.Candidates.Count > 1;
    public bool NeedsImport => Choice is { CanAssignCandidate: false };
    public string ImportLabel => Choice switch
    {
        { CanImportFlat: true } => English ? "Import Flats" : "Importa Flat",
        { CanAddMasterLibrary: true } => English ? "Add Master Library" : "Aggiungi Master Library",
        { CanEditMetadata: true } => English ? "Fix metadata" : "Correggi metadati",
        _ => English ? "Open Resolve" : "Apri Risolvi"
    };
    public string ChoiceReason => Choice?.Reason ?? "";
    private FilterSlotRow? PendingFilter => _observatory.Filters.FirstOrDefault(row => row.NeedsConfirmation);
    public bool NoChoiceFilter => NoChoice && PendingFilter is not null;
    public bool NoChoiceClean => NoChoice && PendingFilter is null;
    public string PendingFilterTitle => PendingFilter is { } row ? (English ? $"{row.RawName} · unrecognised name" : $"{row.RawName} · nome non riconosciuto") : "";
    public string PendingFilterDetail => PendingFilter is { } row
        ? (English ? $"{row.Filter.Lights} Light · {_observatory.CameraName}. Confirmed once per camera." : $"{row.Filter.Lights} Light · {_observatory.CameraName}. Si conferma una volta per camera.")
        : "";
    public string PendingFilterAction => English ? "Identify in Instrument" : "Riconosci in Strumento";
    public string AllCalibratedLabel => English ? "Every Light has Flat, Dark and Bias." : "Ogni Light ha Flat, Dark e Bias.";

    // ---- Night sky ----
    public IReadOnlyList<SkyNight> SkyNights { get; private set; } = [];

    // ---- Command palette ----
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) { CommandIndex = 0; Raise(nameof(Commands)); } } }
    public int CommandIndex { get => _commandIndex; set => Set(ref _commandIndex, value); }
    public IReadOnlyList<CommandItem> Commands => _commands.Where(item => Query.Length == 0 || item.Title.Contains(Query.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();

    private IReadOnlyList<CommandItem> BuildCommands() =>
    [
        new("next", NextLabel, "↵"),
        new("import", English ? "1 · Import" : "1 · Importa", "Alt 1"),
        new("setup", English ? "2 · Gear" : "2 · Strumento", "Alt 2"),
        new("calibration", English ? "3 · Calibration" : "3 · Calibrazioni", "Alt 3"),
        new("export", English ? "4 · Export and PixInsight" : "4 · Esporta e PixInsight", "Alt 4"),
        new("metadata", English ? "Frames and metadata" : "Frame e metadati", "Alt 5"),
        new("stats", English ? "Statistics" : "Statistiche", "Alt 6"),
        new("quality", English ? "Frame quality" : "Qualità dei frame", "Alt 7"),
        new("masters", "Libreria Master", "Alt 8"),
        new("analyze", English ? "Analyse project" : "Analizza progetto", "Ctrl ↵"),
        new("addFolder", English ? "Import folder" : "Importa cartella", ""),
        new("addLibrary", English ? "Add Master Library" : "Aggiungi Master Library", ""),
        new("new", English ? "New project" : "Nuovo progetto", "Ctrl N"),
        new("open", English ? "Open project" : "Apri progetto", "Ctrl O"),
        new("save", English ? "Save" : "Salva", "Ctrl S"),
        new("saveAs", English ? "Save as" : "Salva con nome", "Ctrl ⇧ S"),
        new("demo", English ? "Open the demo project" : "Apri il progetto demo", ""),
        new("tour", English ? "Guided tour" : "Tour guidato", "F1"),
        new("log", English ? "Diagnostics and log" : "Diagnostica e log", ""),
        new("menu", English ? "Settings" : "Impostazioni", "Ctrl ,"),
        new("updates", English ? "Check for updates" : "Controlla aggiornamenti", "")
    ];

    // ---- Refresh ----
    private void Refresh()
    {
        RebuildNights();
        _commands = BuildCommands();
        if (!CanSho && _palette == 1) _palette = 0;
        // A project without any source has nothing to show on the later steps: it starts again from the import.
        if (!Analyzed && !_main.IsScanning && !_main.HasSources && _step != ImportStep) { CurrentStep = ImportStep; return; }
        // A project without an analysis has been through nothing yet.
        if (!Analyzed && !_main.IsScanning) _maxReached = _step;
        foreach (var property in typeof(ShellViewModel).GetProperties())
            if (property.Name is not (nameof(Query) or nameof(CommandIndex) or nameof(Main) or nameof(Observatory) or nameof(CurrentStep))) Raise(property.Name);

        // The analysis finishing is the end of the import: carry on to the gear on its own.
        var newlyAnalysed = Analyzed && !_wasAnalysed;
        _wasAnalysed = Analyzed;
        if (newlyAnalysed && _step == ImportStep && _nightTotals.Count > 0 && !_stayOnce) CurrentStep = SetupStep;
        if (newlyAnalysed) _stayOnce = false;
    }

    private void RaiseStack()
    {
        foreach (var name in new[] { nameof(NightIndex), nameof(Snr), nameof(HoursBig), nameof(HoursSmall), nameof(SnrText), nameof(NoiseText), nameof(LightsText), nameof(NightLabel) })
            Raise(name);
    }

    private void RebuildNights()
    {
        // The timeline replays its entrance whenever it gets a new list: rebuild only when its inputs changed.
        var key = (_main.Analysis, _main.UiLanguage, _observatory.PendingCount, _observatory.Filters.Count, _main.Novelty);
        if (Equals(key, _nightsKey)) return;
        _nightsKey = key;
        var previousCount = _nightTotals.Count;
        var byNight = Lights
            .GroupBy(item => item.Light.SessionId.Value ?? item.Light.CapturedAt.Value?.ToLocalTime().AddHours(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        _nightTotals = byNight.Select(group => (group.Key, group.Sum(item => item.Light.ExposureSeconds.Value ?? 0) / 3600, group.Count())).ToList();
        if (_nightIndex <= 0 || _nightIndex == previousCount || _nightIndex > _nightTotals.Count) _nightIndex = _nightTotals.Count;

        var site = SkyMath.SiteOf(Lights.Select(item => item.Light));
        SkyNights = byNight.Select(group =>
        {
            var date = ParseNight(group.Key) ?? group.Select(item => item.Light.CapturedAt.Value?.LocalDateTime.AddHours(-12).Date).FirstOrDefault(value => value is not null) ?? DateTime.Today;
            var evening = new DateTime(date.Year, date.Month, date.Day, 18, 0, 0, DateTimeKind.Local);
            var exposures = group.Where(item => item.Light.CapturedAt.Value is not null).Select(item =>
            {
                var local = item.Light.CapturedAt.Value!.Value.ToLocalTime().DateTime;
                var hour = 18 + (local - evening).TotalHours;
                var flagged = !(item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted);
                return new SkyExposure(hour, (item.Light.ExposureSeconds.Value ?? 60) / 3600, ColourOf(item.Light.FilterName.Value), flagged);
            }).Where(exposure => exposure.Start >= NightSky.StartHour - 0.5 && exposure.Start <= NightSky.EndHour).ToList();
            var midnightUtc = evening.AddHours(6).ToUniversalTime();
            var age = SkyMath.MoonAge(midnightUtc);
            var illumination = SkyMath.MoonIllumination(midnightUtc);
            IReadOnlyList<double>? sun = site is { } place
                ? Enumerable.Range(0, 73).Select(step => SkyMath.SunAltitude(evening.AddMinutes(step * 10).ToUniversalTime(), place.Latitude, place.Longitude)).ToList()
                : null;
            // The Moon over the night: from the site in the headers, or a typical northern one with the time zone's longitude.
            var estimated = site is null;
            var (moonLatitude, moonLongitude) = site ?? (45, TimeZoneInfo.Local.GetUtcOffset(evening).TotalHours * 15);
            IReadOnlyList<double> moon = Enumerable.Range(0, 73).Select(step => SkyMath.MoonAltitude(evening.AddMinutes(step * 10).ToUniversalTime(), moonLatitude, moonLongitude)).ToList();
            var parts = group.GroupBy(item => item.Light.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
                .Select(filter => $"{DisplayFilter(filter.Key)} {(filter.Sum(item => item.Light.ExposureSeconds.Value ?? 0) / 3600).ToString("0.0", Culture)} h");
            var open = group.Count(item => !(item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted));
            var fresh = _main.Novelty?.Nights.FirstOrDefault(night => night.Night == group.Key);
            var detail = $"{date.ToString("d MMM", Culture)} · {string.Join(" · ", parts)} · {group.Count()} {(English ? "subs" : "pose")} · {MoonText(illumination, moon, evening, estimated)}"
                         + (fresh is not null ? (fresh.WholeNight ? (English ? " · new night" : " · notte nuova") : English ? $" · +{fresh.Lights} new" : $" · +{fresh.Lights} nuovi") : "")
                         + (open > 0 ? (English ? $" · {open} to resolve" : $" · {open} da risolvere") : "");
            return new SkyNight(date.ToString("d MMM", Culture), detail, exposures, age, illumination, sun, moon, estimated, fresh is not null);
        }).ToList();
    }

    /// <summary>"Moon 85 % · rises 21:40 · sets 05:10": how full it was and when it was up.</summary>
    private string MoonText(double illumination, IReadOnlyList<double> altitudes, DateTime evening, bool estimated)
    {
        string At(int step) => evening.AddMinutes(step * 10).ToString("HH:mm", CultureInfo.InvariantCulture);
        bool Up(int step) => altitudes[step] > SkyMath.MoonRiseAltitude;
        var text = $"{(English ? "Moon" : "Luna")} {illumination * 100:0} %";
        var rise = Enumerable.Range(1, altitudes.Count - 1).Where(step => Up(step) && !Up(step - 1)).Select(step => (int?)step).FirstOrDefault();
        var set = Enumerable.Range(1, altitudes.Count - 1).Where(step => !Up(step) && Up(step - 1)).Select(step => (int?)step).FirstOrDefault();
        if (rise is null && set is null) text += Up(0) ? (English ? " · up all night" : " · alta tutta la notte") : English ? " · below the horizon" : " · sotto l’orizzonte";
        else
        {
            if (rise is { } r) text += English ? $" · rises {At(r)}" : $" · sorge {At(r)}";
            if (set is { } s) text += English ? $" · sets {At(s)}" : $" · tramonta {At(s)}";
        }
        return estimated ? text + (English ? " (estimated)" : " (stima)") : text;
    }

    // ---- Helpers ----
    private Color ColourOf(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return Color.Parse("#C9D3F5");
        var slot = _observatory.Filters.FirstOrDefault(row => string.Equals(row.Filter.RawName, filter, StringComparison.OrdinalIgnoreCase));
        if (slot is not null) return slot.NeedsConfirmation ? Color.Parse("#FFC27A") : SpectrumColors.Glass(slot.Bands, slot.Filter.Identity.Kind);
        var identity = FilterRecognizer.Recognize(filter);
        return identity.NeedsConfirmation ? Color.Parse("#C9D3F5") : SpectrumColors.Glass(SpectrumColors.BandsOf(identity), identity.Kind);
    }

    private string DisplayFilter(string filter)
    {
        var identity = FilterRecognizer.Recognize(filter);
        return identity.NeedsConfirmation ? filter : identity.DisplayName;
    }

    private static string ChannelOf(string name)
    {
        var identity = FilterRecognizer.Recognize(name);
        if (identity.Lines.Count == 1) return identity.Lines[0].Name switch { "Ha" or "H-alpha" => "Hα", var line => line };
        if (identity.Lines.Count > 1) return string.Join("+", identity.Lines.Select(line => line.Name == "Ha" ? "Hα" : line.Name));
        return identity.DisplayName;
    }

    private static string Distinct(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count switch { 0 => "—", 1 => distinct[0], _ => $"{distinct.Count} master" };
    }

    private static string Temperature(double? celsius) => celsius is { } value ? $"{value:0.#} °C".Replace("-", "−") : "";
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private string CandidateName(ReviewCandidateOption candidate) =>
        !string.IsNullOrWhiteSpace(candidate.FlatSetId) ? $"Flat Set {candidate.FlatSetId}".Replace("Flat Set Flat Set", "Flat Set") : Path.GetFileNameWithoutExtension(candidate.FileName);

    private string Signature(FrameMetadata frame)
    {
        if (frame.RotatorAngleDeg.Value is { } angle) return English ? $"rotator {angle.ToString("0.0", Culture)}°" : $"rotatore {angle.ToString("0.0", Culture)}°";
        var parts = new[] { frame.Gain.Value is { } gain ? $"G{gain:0}" : "", Temperature(frame.EffectiveTemperatureC), frame.ExposureSeconds.Value is { } exposure ? $"{exposure:0.#} s" : "" };
        return string.Join(" · ", parts.Where(part => part.Length > 0));
    }

    private static DateTime? ParseNight(string night) =>
        DateTime.TryParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private string NightShort(string night) => ParseNight(night)?.ToString("d MMM", Culture) ?? night;
    private string NightLong(string night) => ParseNight(night)?.ToString("d MMMM", Culture) ?? night;

    private static string HumanSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString(unit < 3 ? "0" : "0.0", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}
