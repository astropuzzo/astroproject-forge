using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using AstroForge.App.Services;
using AstroForge.App.ViewModels;
using AstroForge.Core.Equipment;
using AstroForge.Core.Filters;
using AstroForge.CrossPlatform.Controls;

namespace AstroForge.CrossPlatform.ViewModels;

public sealed record FilterChoice(string Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One wheel slot on the instrument screen, with its one-time confirmation when the name says nothing.</summary>
public sealed class FilterSlotRow : BindableBase
{
    private FilterChoice? _choice;
    private readonly bool _english;

    public FilterSlotRow(InstrumentFilter filter, IReadOnlyList<FilterChoice> choices, bool english)
    {
        Filter = filter;
        Choices = choices;
        Bands = SpectrumColors.BandsOf(filter.Identity);
        Glass = SpectrumColors.GlassBrush(Bands, filter.Identity.Kind);
        _choice = filter.Identity.Product is { } product ? choices.FirstOrDefault(choice => choice.Id == product.Id) : null;
        _english = english;
        Usage = $"{UiFormat.Frames(filter.Lights, "Light")} · {UiFormat.Duration(filter.IntegrationSeconds / 3600)} · {UiFormat.Frames(filter.Flats, "Flat")}";
        Source = filter.Identity.Source switch
        {
            FilterMatchSource.UserProfile => english ? "confirmed by you" : "confermato da te",
            FilterMatchSource.CatalogProduct => english ? $"catalogue · {UiFormat.Percent(filter.Identity.Confidence * 100)}" : $"catalogo · {UiFormat.Percent(filter.Identity.Confidence * 100)}",
            FilterMatchSource.GenericName => english ? $"from the name · {UiFormat.Percent(filter.Identity.Confidence * 100)}" : $"dal nome · {UiFormat.Percent(filter.Identity.Confidence * 100)}",
            FilterMatchSource.NoFilter => english ? "colour sensor" : "sensore a colori",
            _ => english ? "to identify" : "da riconoscere"
        };
    }

    public InstrumentFilter Filter { get; }
    public string RawName => Filter.RawName.Length == 0 ? "-" : Filter.RawName;
    public string DisplayName => Filter.Identity.NeedsConfirmation ? "?" : Filter.Identity.DisplayName;
    public string Lines => Filter.Identity.Lines.Count == 0 ? KindLabel : string.Join(" + ", Filter.Identity.Lines.Select(line => $"{SpectrumColors.LineName(line.Name)} {UiFormat.Number(line.WavelengthNm, "0.#")} nm"));
    public string KindLabel => Filter.Identity.Kind switch
    {
        FilterKind.Broadband => _english ? "broadband" : "banda larga",
        FilterKind.LightPollution => _english ? "light pollution" : "anti-inquinamento luminoso",
        _ => ""
    };
    public bool HasLines => Lines.Length > 0;
    public string Usage { get; }
    public string Source { get; }
    public bool NeedsConfirmation => Filter.NeedsConfirmation;
    public bool IsConfirmed => Filter.Identity.Source == FilterMatchSource.UserProfile;
    public IReadOnlyList<Core.Filters.FilterBand> Bands { get; }
    public IBrush Glass { get; }
    public IReadOnlyList<FilterChoice> Choices { get; }
    public FilterChoice? Choice { get => _choice; set { if (Set(ref _choice, value)) Raise(nameof(IsChoiceNew)); } }
    /// <summary>A recognised filter can be changed just like an unknown one: the picker is always there.</summary>
    public bool IsChoiceNew => Choice is not null && Choice.Id != Filter.Identity.Product?.Id;
    public string ChangeHint { get; init; } = "";
    public string ApplyLabel { get; init; } = "";
}

/// <summary>A filter's card on the overview: its hours, its light and how much of it is calibrated.</summary>
public sealed record FilterCard(string Name, string Detail, string Integration, string Lights, string Status, bool Ready, double Percentage, IReadOnlyList<Core.Filters.FilterBand> Bands, IBrush Accent)
{
    public string RawFilter { get; init; } = "";
    public string Hours { get; init; } = "";
    public double Calibrated { get; init; }
    public string CalibratedText { get; init; } = "";
    public string CalibratedDetail { get; init; } = "";
    public bool NeedsConfirmation { get; init; }
}

/// <summary>A camera in the picker: a catalogue entry the user can say the frames really came from.</summary>
public sealed record CameraChoice(string? Id, string Name, CameraSensorType Type)
{
    public override string ToString() => Name;
}

/// <summary>A telescope in the profile picker: a catalogue entry, or the optics the headers name.</summary>
public sealed record TelescopeChoice(string? Id, string Name, double? ApertureMm, double? FocalMm)
{
    public override string ToString() => Name;
}

/// <summary>A reducer option for the chosen optics; factor 1 means none.</summary>
public sealed record ReducerChoice(double Factor, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Avalonia-side state for the overview and instrument screens, derived from the shared MainViewModel
/// so the WPF app is untouched.
/// </summary>
public sealed partial class ObservatoryViewModel : BindableBase
{
    private readonly MainViewModel _main;
    private IReadOnlyList<WheelSlot> _slots = [];
    private IReadOnlyList<NightBar> _nights = [];
    private int _selectedSlot;

    public ObservatoryViewModel(MainViewModel main)
    {
        _main = main;
        Choices = FilterCatalog.Default.Filters
            .OrderBy(filter => filter.Kind switch { FilterKind.Narrowband => 0, FilterKind.Multiband => 1, FilterKind.LightPollution => 2, _ => 3 })
            .ThenBy(filter => filter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(filter => new FilterChoice(filter.Id, filter.DisplayName))
            .ToList();
        main.PropertyChanged += Main_PropertyChanged;
        main.FilterStatistics.CollectionChanged += (_, _) => RebuildOverview();
        main.NightStatistics.CollectionChanged += (_, _) => RebuildOverview();
        Rebuild();
    }

    public IReadOnlyList<FilterChoice> Choices { get; }
    public ObservableCollection<FilterSlotRow> Filters { get; } = [];
    public ObservableCollection<FilterCard> FilterCards { get; } = [];
    public IReadOnlyList<WheelSlot> Slots { get => _slots; private set => Set(ref _slots, value); }
    public IReadOnlyList<NightBar> Nights { get => _nights; private set => Set(ref _nights, value); }

    public int SelectedSlot
    {
        get => _selectedSlot;
        set { if (Set(ref _selectedSlot, value)) { Raise(nameof(SelectedFilter)); RaiseSelectedFilterViews(); } }
    }

    public FilterSlotRow? SelectedFilter => SelectedSlot >= 0 && SelectedSlot < Filters.Count ? Filters[SelectedSlot] : null;

    private bool English => _main.UiLanguage == UiLocalization.English;
    private InstrumentProfile? Instrument => _main.Instrument;

    public bool HasInstrument => Instrument is not null;
    public bool HasNoInstrument => Instrument is null;
    public string CameraName => Instrument?.Camera.DisplayName ?? "-";
    public string CameraType => Instrument?.Camera.Type switch
    {
        CameraSensorType.Mono => "MONO",
        CameraSensorType.Color => English ? "COLOUR" : "COLORE",
        CameraSensorType.Dslr or CameraSensorType.DslrModified => "DSLR",
        _ => "?"
    };
    public string CameraSource => Instrument?.CameraSource switch
    {
        EquipmentSource.User => English ? "your choice" : "scelta tua",
        EquipmentSource.Catalog => English ? $"catalogue · {UiFormat.Percent(Instrument.Camera.Confidence * 100)}" : $"catalogo · {UiFormat.Percent(Instrument.Camera.Confidence * 100)}",
        _ => English ? "from the header" : "dall'header"
    };
    /// <summary>The camera without its maker, for the stepper: "ASI2600MM Pro".</summary>
    public string CameraShortName
    {
        get
        {
            var name = CameraName;
            foreach (var brand in new[] { "ZWO ", "QHY ", "QHYCCD ", "Player One ", "PlayerOne ", "ToupTek ", "SVBONY ", "Atik ", "Moravian ", "Altair " })
                if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) { name = name[brand.Length..]; break; }
            return name;
        }
    }
    public string TelescopeName => Instrument is null ? "-" : Instrument.Telescope.RawName.Length == 0 && Instrument.Telescope.Telescope is null ? (English ? "Optics not in the headers" : "Ottica non indicata negli header") : Instrument.Telescope.DisplayName;
    public string TelescopeDetail => Instrument?.Telescope.Telescope is { } scope
        ? $"Ø {scope.ApertureMm:0} mm · {scope.FocalMm:0} mm · f/{scope.FocalRatio:0.0}" + (Instrument.Telescope.Reducer is { } reducer ? $" → {Instrument.FocalMm:0} mm f/{Instrument.FocalMm / scope.ApertureMm:0.0}" : "")
        : Instrument?.FocalMm is { } focal ? $"{focal:0} mm" : "";
    public string PixelText => Instrument?.PixelUm is { } pixel ? $"{pixel:0.##} µm" + (Instrument.Binning > 1 ? $" · bin {Instrument.Binning}" : "") : "-";
    public string ScaleText => Instrument?.ImageScale is { } scale ? $"{scale:0.00}″/px" : "-";
    public string FieldText => Instrument?.FieldOfView is { } fov ? $"{fov.Width:0.00}° × {fov.Height:0.00}°" : "-";
    public string SensorText => Instrument?.SensorWidthMm is { } w && Instrument.SensorHeightMm is { } h ? $"{w:0.0} × {h:0.0} mm" : "-";
    public double FieldWidth => Instrument?.FieldOfView?.Width ?? 0;
    public double FieldHeight => Instrument?.FieldOfView?.Height ?? 0;
    public int PendingCount => Instrument?.PendingConfirmations ?? 0;
    public bool HasPending => PendingCount > 0;
    public string PendingText => PendingCount switch
    {
        0 => English ? "Every filter identified" : "Tutti i filtri riconosciuti",
        1 => English ? "1 filter to identify" : "1 filtro da riconoscere",
        var count => English ? $"{count} filters to identify" : $"{count} filtri da riconoscere"
    };
    public string WheelCaption => Instrument is null ? "" : English
        ? $"{Filters.Count} filters on {CameraName}. What you confirm is remembered for this camera in every project."
        : $"{Filters.Count} filtri sulla {CameraName}. Quello che confermi resta memorizzato per questa camera in ogni progetto.";

    public void Confirm(FilterSlotRow row)
    {
        if (Instrument is not { } instrument || row.Choice is not { } choice) return;
        var slot = SelectedSlot;
        _main.ConfirmFilter(instrument.CameraKey, row.Filter.RawName, choice.Id);
        SelectedSlot = Math.Min(slot, Filters.Count - 1);
    }

    public void Forget(FilterSlotRow row)
    {
        if (Instrument is not { } instrument) return;
        var slot = SelectedSlot;
        _main.ForgetFilter(instrument.CameraKey, row.Filter.RawName);
        SelectedSlot = Math.Min(slot, Filters.Count - 1);
    }

    private void Main_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Instrument) or nameof(MainViewModel.UiLanguage)) Rebuild();
        else if (e.PropertyName is nameof(MainViewModel.Analysis)) { RebuildOverview(); RefreshSky(); }
        else if (e.PropertyName is nameof(MainViewModel.ShowRealSky)) RefreshSky();
    }

    private void Rebuild()
    {
        Filters.Clear();
        foreach (var filter in Instrument?.Filters ?? [])
            Filters.Add(new FilterSlotRow(filter, Choices, English)
            {
                ChangeHint = filter.NeedsConfirmation
                    ? (English ? "Which filter is it?" : "Che filtro è?")
                    : English ? "Not this one? Pick the right filter." : "Non è questo? Scegli il filtro giusto.",
                ApplyLabel = filter.NeedsConfirmation ? (English ? "Confirm" : "Conferma") : English ? "Use this filter" : "Usa questo filtro"
            });
        Slots = Filters.Select(row => new WheelSlot(row.NeedsConfirmation ? row.RawName : $"{row.RawName} → {row.DisplayName}", row.Glass, row.NeedsConfirmation)).ToList();
        var firstPending = Filters.ToList().FindIndex(row => row.NeedsConfirmation);
        SelectedSlot = firstPending >= 0 ? firstPending : Math.Clamp(SelectedSlot, 0, Math.Max(0, Filters.Count - 1));
        foreach (var name in new[]
                 {
                     nameof(HasInstrument), nameof(HasNoInstrument), nameof(CameraName), nameof(CameraShortName), nameof(CameraType), nameof(CameraSource), nameof(TelescopeName), nameof(TelescopeDetail),
                     nameof(PixelText), nameof(ScaleText), nameof(FieldText), nameof(SensorText), nameof(FieldWidth), nameof(FieldHeight),
                     nameof(PendingCount), nameof(HasPending), nameof(PendingText), nameof(WheelCaption), nameof(SelectedFilter)
                 })
            Raise(name);
        RebuildSetups();
        RebuildInstrumentScreen();
        RebuildOverview();
        RefreshSky();
    }

    private void RebuildOverview()
    {
        FilterCards.Clear();
        var lights = _main.Analysis?.Lights ?? [];
        foreach (var row in _main.FilterStatistics)
        {
            var slot = Filters.FirstOrDefault(filter => string.Equals(filter.Filter.RawName, row.Filter, StringComparison.OrdinalIgnoreCase));
            var identity = slot?.Filter.Identity ?? FilterRecognizer.Recognize(row.Filter);
            var bands = slot?.Bands ?? SpectrumColors.BandsOf(identity);
            var name = identity.NeedsConfirmation ? row.Filter : identity.DisplayName;
            var lines = slot?.Lines ?? string.Join(" + ", identity.Lines.Select(line => SpectrumColors.LineName(line.Name)));
            var width = identity.BandwidthNm is { } nm ? (English ? $"{UiFormat.Number(nm, "0.#")} nm wide" : $"largo {UiFormat.Number(nm, "0.#")} nm") : "";
            var detail = identity.NeedsConfirmation
                ? (English ? "to identify" : "da riconoscere")
                : string.Join(" · ", new[] { lines, width }.Where(part => part.Length > 0));
            if (!identity.NeedsConfirmation && !string.Equals(FilterRecognizer.Normalize(name), FilterRecognizer.Normalize(row.Filter), StringComparison.OrdinalIgnoreCase) && name.Replace("α", "a") != row.Filter)
                detail = $"«{row.Filter}» · {detail}";
            var own = lights.Where(item => string.Equals(item.Light.FilterName.Value, row.Filter, StringComparison.OrdinalIgnoreCase)).ToList();
            var calibrated = own.Count(item => item.Flat.IsAccepted && item.Dark.IsAccepted && item.Bias.IsAccepted);
            var share = own.Count == 0 ? 0 : calibrated / (double)own.Count;
            var open = own.Count - calibrated;
            var hours = _main.NightStatistics.Where(night => string.Equals(night.Filter, row.Filter, StringComparison.OrdinalIgnoreCase)).Sum(night => night.Hours);
            FilterCards.Add(new FilterCard(name, detail, row.Integration, $"{UiFormat.Frames(row.Lights, "Light")} · {NightsLabel(row.Nights)}", row.Status, row.Ready, row.Percentage,
                bands, new SolidColorBrush(SpectrumColors.Glass(bands, identity.Kind)))
            {
                RawFilter = row.Filter,
                Hours = HoursLabel(hours),
                Calibrated = share * 100,
                CalibratedText = UiFormat.Percent(share * 100),
                CalibratedDetail = open == 0 ? (English ? "Calibrated" : "Calibrati") : English ? $"{UiFormat.Frames(open, "Light")} to resolve" : $"{open} Light da risolvere",
                NeedsConfirmation = identity.NeedsConfirmation
            });
        }

        // Built with TryAdd: a duplicate spelling of a filter ("Ha", "HA") must not throw and take the whole analysis down.
        var colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var (card, statistics) in FilterCards.Zip(_main.FilterStatistics)) colours.TryAdd(statistics.Filter, ((SolidColorBrush)card.Accent).Color);
        Nights = _main.NightStatistics
            .GroupBy(row => row.Night)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NightBar(NightLabel(group.Key), group
                .GroupBy(row => row.Filter, StringComparer.OrdinalIgnoreCase)
                .Select(filter => new NightSegment(colours.GetValueOrDefault(filter.Key, Color.Parse("#9DB8FF")), filter.Sum(row => row.Hours)))
                .ToList()))
            .ToList();
    }

    private string NightsLabel(int nights) => nights == 1 ? (English ? "1 night" : "1 notte") : English ? $"{nights} nights" : $"{nights} notti";

    public static string HoursLabel(double hours) => UiFormat.Duration(hours);

    private string NightLabel(string night) =>
        DateTime.TryParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMM", English ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.GetCultureInfo("it-IT"))
            : night;
}
