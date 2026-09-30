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

public enum NextActionKind { AddSources, Analyze, Busy, ConfirmFilters, Resolve, Export, Exporting, OpenPixInsight }

public sealed record CalibrationLine(string Label, string Count, IBrush Dot);

public sealed record CommandItem(string Id, string Title, string Hint);

/// <summary>
/// State of the window around the two screens: the four-step constellation, the one next action, the project
/// title, and the overview's stack, calibration ring, nights and open choice. Derived from the shared view model
/// and refreshed once per dispatcher pass whatever changed.
/// </summary>
public sealed class ShellViewModel : BindableBase
{
    private readonly MainViewModel _main;
    private readonly ObservatoryViewModel _observatory;
    private bool _queued;
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
        foreach (var collection in new INotifyCollectionChanged[] { main.FilterStatistics, main.NightStatistics, main.ReviewQueue, main.SourcePaths, main.ExportHistory, observatory.FilterCards })
            collection.CollectionChanged += (_, _) => Schedule();
        Refresh();
    }

    private bool English => _main.UiLanguage == UiLocalization.English;
    private CultureInfo Culture => English ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.GetCultureInfo("it-IT");
    private IReadOnlyList<LightCalibrationAnalysis> Lights => _main.Analysis?.Lights ?? [];
    private bool Analyzed => _main.HasAnalysis && !_main.NeedsReanalysis;

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

    // ---- Next action ----
    public NextActionKind NextKind
    {
        get
        {
            if (_main.IsScanning) return NextActionKind.Busy;
            if (_main.ExportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Preflighting or ExportRunState.Cancelling) return NextActionKind.Exporting;
            if (!_main.HasSources) return NextActionKind.AddSources;
            if (!Analyzed) return NextActionKind.Analyze;
            if (_observatory.PendingCount > 0) return NextActionKind.ConfirmFilters;
            if (_main.ReviewQueue.Count > 0) return NextActionKind.Resolve;
            if (_main.ExportState != ExportRunState.Completed) return NextActionKind.Export;
            return NextActionKind.OpenPixInsight;
        }
    }

    public string NextLabel => NextKind switch
    {
        NextActionKind.Busy => English ? $"Analysing · {_main.Progress:0} %" : $"Analisi · {_main.Progress:0} %",
        NextActionKind.Exporting => English ? "Export in progress" : "Esportazione in corso",
        NextActionKind.AddSources => English ? "Add captures" : "Aggiungi acquisizioni",
        NextActionKind.Analyze => _main.HasAnalysis ? (English ? "Analyse again" : "Rianalizza") : (English ? "Analyse" : "Analizza"),
        NextActionKind.ConfirmFilters => _observatory.PendingCount == 1 ? (English ? "Confirm the filter" : "Conferma il filtro") : English ? $"Confirm {_observatory.PendingCount} filters" : $"Conferma {_observatory.PendingCount} filtri",
        NextActionKind.Resolve => _main.ReviewQueue[0] is { Calibration: "Flat", CanAssignCandidate: true } && _main.ReviewQueue.Count == 1
            ? (English ? "Choose the missing Flat" : "Scegli il Flat mancante")
            : English ? $"Resolve {_main.ReviewQueue.Count}" : $"Risolvi {_main.ReviewQueue.Count}",
        NextActionKind.Export => ExportSize is { } size ? (English ? $"Export {size}" : $"Esporta {size}") : (English ? "Export" : "Esporta"),
        _ => English ? "Open in PixInsight" : "Apri in PixInsight"
    };
    public bool NextEnabled => NextKind is not NextActionKind.Busy;
    private string? ExportSize => (_main.BytesToCopy ?? _main.PlannedBytes) is { } bytes and > 0 ? HumanSize(bytes) : null;

    // ---- Constellation ----
    public IReadOnlyList<ShellStep> Steps
    {
        get
        {
            var projectDone = Analyzed;
            var resolveDone = projectDone && _main.Analysis?.Ready == true && _observatory.PendingCount == 0 && _main.ReviewQueue.Count == 0;
            var exportDone = resolveDone && _main.ExportState == ExportRunState.Completed;
            var states = new[] { projectDone, resolveDone, exportDone, false };
            var now = Array.IndexOf(states, false);
            StepState State(int index) => states[index] ? StepState.Done : index == now ? StepState.Now : StepState.Pending;

            var projectDetail = projectDone ? (English ? $"{_main.AnalyzedFileCount} files analysed" : $"{_main.AnalyzedFileCount} file analizzati")
                : _main.HasSources ? (English ? $"{SourcesLabel} · to analyse" : $"{SourcesLabel} · da analizzare")
                : (English ? "Add captures" : "Aggiungi acquisizioni");
            var choices = _main.ReviewQueue.Count + _observatory.PendingCount;
            var resolveDetail = !projectDone ? (English ? "after the analysis" : "dopo l'analisi")
                : choices == 0 ? (English ? "all calibrated" : "tutto calibrato")
                : _main.ReviewQueue.Count == 0 ? (_observatory.PendingCount == 1 ? (English ? "1 filter to confirm" : "1 filtro da confermare") : English ? $"{choices} filters to confirm" : $"{choices} filtri da confermare")
                : choices == 1 ? (English ? "1 open choice" : "1 scelta in sospeso")
                : English ? $"{choices} open choices" : $"{choices} scelte in sospeso";
            var exportDetail = exportDone ? (English ? "exported" : "esportato")
                : ExportSize is { } size ? (English ? $"{size} to copy" : $"{size} da copiare")
                : (English ? "project folder" : "cartella di progetto");
            var wbppDetail = exportDone ? (English ? "instance ready" : "istanza pronta") : (English ? "after the export" : "dopo l'export");
            return
            [
                new(English ? "Project" : "Progetto", projectDetail, State(0)),
                new(English ? "Resolve" : "Risolvi", resolveDetail, State(1)),
                new(English ? "Export" : "Esporta", exportDetail, State(2)),
                new("PixInsight WBPP", wbppDetail, State(3))
            ];
        }
    }

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
        new("overview", English ? "Overview" : "Panoramica", "Alt 1"),
        new("instrument", English ? "Instrument" : "Strumento", "Alt 2"),
        new("project", English ? "Project · sources and files" : "Progetto · sorgenti e file", "Alt 3"),
        new("resolve", English ? "Resolve calibrations" : "Risolvi calibrazioni", "Alt 4"),
        new("export", English ? "Export" : "Esporta", "Alt 5"),
        new("wbpp", "PixInsight WBPP", "Alt 6"),
        new("stats", English ? "Statistics" : "Statistiche", "Alt 7"),
        new("quality", English ? "Frame quality" : "Qualità dei frame", "Alt 8"),
        new("masters", "Libreria Master", "Alt 9"),
        new("analyze", English ? "Analyse project" : "Analizza progetto", "Ctrl ↵"),
        new("addFolder", English ? "Import folder" : "Importa cartella", ""),
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
        foreach (var property in typeof(ShellViewModel).GetProperties())
            if (property.Name is not (nameof(Query) or nameof(CommandIndex))) Raise(property.Name);
    }

    private void RaiseStack()
    {
        foreach (var name in new[] { nameof(NightIndex), nameof(Snr), nameof(HoursBig), nameof(HoursSmall), nameof(SnrText), nameof(NoiseText), nameof(LightsText), nameof(NightLabel) })
            Raise(name);
    }

    private void RebuildNights()
    {
        // The timeline replays its entrance whenever it gets a new list: rebuild only when its inputs changed.
        var key = (_main.Analysis, _main.UiLanguage, _observatory.PendingCount, _observatory.Filters.Count);
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
            var parts = group.GroupBy(item => item.Light.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
                .Select(filter => $"{DisplayFilter(filter.Key)} {(filter.Sum(item => item.Light.ExposureSeconds.Value ?? 0) / 3600).ToString("0.0", Culture)} h");
            var open = group.Count(item => !(item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted));
            var detail = $"{date.ToString("d MMM", Culture)} · {string.Join(" · ", parts)} · {group.Count()} {(English ? "subs" : "pose")} · {(English ? "Moon" : "Luna")} {illumination * 100:0} %"
                         + (open > 0 ? (English ? $" · {open} to resolve" : $" · {open} da risolvere") : "");
            return new SkyNight(date.ToString("d MMM", Culture), detail, exposures, age, illumination, sun);
        }).ToList();
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
