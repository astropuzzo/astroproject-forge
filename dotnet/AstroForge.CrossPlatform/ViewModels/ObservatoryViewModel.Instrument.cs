using System.Globalization;
using Avalonia.Media;
using AstroForge.Core.Equipment;
using AstroForge.Core.Filters;
using AstroForge.CrossPlatform.Controls;

namespace AstroForge.CrossPlatform.ViewModels;

/// <summary>The instrument screen: field of view with and without the reducer, the optical train and the editable profile.</summary>
public sealed partial class ObservatoryViewModel
{
    private static readonly double[] CommonReducers = [0.63, 0.67, 0.7, 0.72, 0.75, 0.77, 0.79, 0.8, 0.85, 0.9];
    private bool _showReduced = true;
    private bool _isEditing;
    private string _cameraText = "";
    private CameraChoice? _selectedCamera;
    private bool _customCameraColor;
    private string _telescopeText = "";
    private TelescopeChoice? _selectedTelescope;
    private ReducerChoice? _selectedReducer;
    private decimal? _pixelValue;
    private decimal? _customFocal;
    private decimal? _customAperture;
    private IReadOnlyList<ReducerChoice> _reducerChoices = [];

    public IReadOnlyList<TelescopeChoice> TelescopeChoices { get; } = EquipmentCatalog.Default.TelescopesByUse
        .Select(item => new TelescopeChoice(item.Id, item.Name, item.ApertureMm, item.FocalMm)).ToList();

    public IReadOnlyList<CameraChoice> CameraChoices { get; } = EquipmentCatalog.Default.Cameras
        .OrderByDescending(item => item.Uses).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .Select(item => new CameraChoice(item.Id, item.Name, item.Type)).ToList();

    private string Number(double value, string format) => value.ToString(format, English ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("it-IT"));

    // ---- Configuration shown in the field of view ----
    private double? ReducerFactorOption => Instrument?.ReducerFactor
        ?? (Instrument?.Telescope.Telescope is { } scope ? EquipmentCatalog.Default.ReducersFor(scope).Select(item => (double?)item.Factor).FirstOrDefault(factor => factor < 1) : null);
    public bool HasReducerOption => ReducerFactorOption is > 0 and < 0.999 && Instrument?.NativeFocalMm is > 0;
    public bool ShowReduced
    {
        get => _showReduced && HasReducerOption;
        set { if (Set(ref _showReduced, value)) RaiseField(); }
    }
    public bool ShowNative { get => !ShowReduced; set => ShowReduced = !value; }
    private double? PreviewFocal => Instrument is not { } instrument ? null
        : HasReducerOption ? instrument.NativeFocalMm * (ShowReduced ? ReducerFactorOption : 1)
        : instrument.FocalMm;
    private double? OtherFocal => HasReducerOption && Instrument is { } instrument ? instrument.NativeFocalMm * (ShowReduced ? 1 : ReducerFactorOption) : null;
    private (double Width, double Height)? PreviewField => PreviewFocal is { } focal ? Instrument?.FieldOfViewAt(focal) : null;
    private (double Width, double Height)? OtherField => OtherFocal is { } focal ? Instrument?.FieldOfViewAt(focal) : null;

    public string ReducedLabel => ReducerFactorOption is { } factor ? (English ? $"Reducer {Number(factor, "0.0#")}×" : $"Riduttore {Number(factor, "0.0#")}×") : "";
    public string NativeLabel => English ? "Native" : "Nativo";
    public double PreviewWidth => PreviewField?.Width ?? 0;
    public double PreviewHeight => PreviewField?.Height ?? 0;
    public double OtherWidth => OtherField?.Width ?? 0;
    public double OtherHeight => OtherField?.Height ?? 0;
    public string OtherLabel => OtherFocal is { } focal ? (ShowReduced ? (English ? $"native {focal:0} mm" : $"nativo {focal:0} mm") : (English ? $"with reducer {focal:0} mm" : $"con riduttore {focal:0} mm")) : "";
    public string FovBig => PreviewField is { } fov ? $"{Number(fov.Width, "0.00")}°" : "—";
    public string FovSmall => PreviewField is { } fov ? $"× {Number(fov.Height, "0.00")}°" : "";
    public string FovEyebrow => FovEyebrowText.ToUpperInvariant();
    private string FovEyebrowText => SelectedFilter is { } filter
        ? (English ? $"Field of view · filter {(filter.NeedsConfirmation ? filter.RawName : filter.DisplayName)}" : $"Campo inquadrato · filtro {(filter.NeedsConfirmation ? filter.RawName : filter.DisplayName)}")
        : (English ? "Field of view" : "Campo inquadrato");
    public string FovNote => !HasReducerOption || OtherField is not { } other || PreviewField is not { } fov ? ""
        : ShowReduced
            ? (English ? $"The reducer widens the field {Number(fov.Width / other.Width, "0.00")} times." : $"Il riduttore allarga il campo di {Number(fov.Width / other.Width, "0.00")} volte.")
            : (English ? $"Native: {Number(other.Width / fov.Width, "0.00")}× the detail, smaller field." : $"Nativo: dettaglio {Number(other.Width / fov.Width, "0.00")}×, campo più stretto.");
    public string FocalText => PreviewFocal is { } focal ? $"{focal:0} mm" : "—";
    public string RatioText => PreviewFocal is { } focal && Instrument?.ApertureMm is > 0 ? (English ? $"focal · f/{Number(focal / Instrument.ApertureMm.Value, "0.0")}" : $"focale · f/{Number(focal / Instrument.ApertureMm.Value, "0.0")}") : (English ? "focal length" : "focale");
    public double PreviewScale => PreviewFocal is { } focal ? Instrument?.ImageScaleAt(focal) ?? 0 : 0;
    public string PreviewScaleText => PreviewScale > 0 ? $"{Number(PreviewScale, "0.00")}″/px" : "—";
    public string SamplingVerdict => PreviewScale switch
    {
        <= 0 => "",
        < 1 => English ? "oversampled" : "sovracampionato",
        <= 2 => English ? "well sampled" : "ben campionato",
        _ => English ? "undersampled" : "sottocampionato"
    };
    public string SeeingLabel => English ? "Typical seeing 2–4″" : "Seeing tipico 2–4″";

    // ---- Optical train ----
    public Color BeamColor => SelectedFilter is { } filter
        ? filter.NeedsConfirmation ? Color.Parse("#FFC27A") : SpectrumColors.Glass(filter.Bands, filter.Filter.Identity.Kind)
        : Color.Parse("#EEF2FF");

    public TrainPart? TrainTelescope => Instrument is not { } instrument ? null : new TrainPart(
        instrument.Telescope.RawName.Length == 0 && instrument.Telescope.Telescope is null ? (English ? "Optics not in the headers" : "Ottica non negli header") : instrument.Telescope.Telescope?.Name ?? instrument.Telescope.RawName,
        string.Join(" · ", new[]
        {
            instrument.ApertureMm is > 0 ? $"{instrument.ApertureMm:0} mm" : "",
            instrument.NativeFocalMm is > 0 ? $"{instrument.NativeFocalMm:0} mm" : "",
            instrument.ApertureMm is > 0 && instrument.NativeFocalMm is > 0 ? $"f/{Number(instrument.NativeFocalMm.Value / instrument.ApertureMm.Value, "0.0")}" : ""
        }.Where(part => part.Length > 0)),
        SourceLabel(instrument.TelescopeSource, "TELESCOP", instrument.Telescope.Confidence),
        instrument.TelescopeSource != EquipmentSource.Header || instrument.Telescope.IsRecognized);

    public TrainPart? TrainReducer => Instrument is not { } instrument || !ShowReduced || ReducerFactorOption is not { } factor ? null : new TrainPart(
        instrument.Telescope.Reducer?.Name ?? (English ? $"Reducer {Number(factor, "0.0#")}×" : $"Riduttore {Number(factor, "0.0#")}×"),
        $"{instrument.NativeFocalMm:0} × {Number(factor, "0.0#")} = {instrument.NativeFocalMm * factor:0} mm",
        instrument.FocalSource == EquipmentSource.User ? (English ? "your profile" : "profilo") : "FOCALLEN",
        true);

    public TrainPart? TrainWheel => SelectedFilter is not { } filter ? null : new TrainPart(
        English ? $"Wheel · slot {SelectedSlot + 1}" : $"Ruota · pos. {SelectedSlot + 1}",
        filter.NeedsConfirmation ? (English ? "to identify" : "da riconoscere") : filter.DisplayName,
        "", !filter.NeedsConfirmation);

    public TrainPart? TrainCamera => Instrument is not { } instrument ? null : new TrainPart(
        instrument.Camera.DisplayName,
        string.Join(" · ", new[] { CameraType.ToLowerInvariant(), instrument.Camera.Camera?.Sensor ?? "", instrument.PixelUm is { } pixel ? $"{Number(pixel, "0.##")} µm" : "" }.Where(part => part.Length > 0 && part != "?")),
        SourceLabel(instrument.CameraSource, "INSTRUME", instrument.Camera.Confidence),
        instrument.Camera.IsRecognized);

    private string SourceLabel(EquipmentSource source, string keyword, double confidence) => source switch
    {
        EquipmentSource.User => English ? "your profile" : "profilo",
        EquipmentSource.Catalog => English ? $"{keyword} · catalogue {confidence:P0}" : $"{keyword} · catalogo {confidence:P0}",
        _ => English ? $"{keyword} · header" : $"{keyword} · header"
    };

    // ---- Profile ----
    public bool IsProfileConfirmed => Instrument?.Override is not null;
    public string ProfileBadge => IsProfileConfirmed ? (English ? "Confirmed" : "Confermato") : (English ? "To confirm" : "Da confermare");
    public string OpticsRow => TrainTelescope?.Title ?? "—";
    public string OpticsChip => Instrument is { } instrument ? SourceLabel(instrument.TelescopeSource, "TELESCOP", instrument.Telescope.Confidence) : "";
    public string ReducerRow => Instrument is not { } instrument ? "—"
        : instrument.ReducerFactor is { } factor ? $"{Number(factor, "0.0#")}× · {instrument.FocalMm:0} mm"
        : English ? $"none · {instrument.FocalMm:0} mm" : $"nessuno · {instrument.FocalMm:0} mm";
    public string ReducerChip => Instrument?.FocalSource switch
    {
        EquipmentSource.User => English ? "your profile" : "profilo",
        EquipmentSource.Catalog => English ? "catalogue" : "catalogo",
        null => "",
        _ => "FOCALLEN"
    };
    public string CameraChip => Instrument is { } instrument ? SourceLabel(instrument.CameraSource, "INSTRUME", instrument.Camera.Confidence) : "";
    public string PixelRow => Instrument?.PixelUm is { } pixel ? $"{Number(pixel, "0.##")} µm" + (Instrument.Binning > 1 ? $" · bin {Instrument.Binning}" : "") : "—";
    public string PixelChip => Instrument?.PixelSource switch
    {
        EquipmentSource.User => English ? "your profile" : "profilo",
        EquipmentSource.Catalog => English ? "catalogue" : "catalogo",
        null => "",
        _ => "XPIXSZ"
    };
    public string WheelRow => English
        ? $"{Filters.Count(row => !row.NeedsConfirmation)} of {Filters.Count} filters identified"
        : $"{Filters.Count(row => !row.NeedsConfirmation)} filtri su {Filters.Count} riconosciuti";
    public string WheelChip => PendingCount == 0 ? "OK" : English ? $"{PendingCount} unsure" : $"{PendingCount} dubbi";
    public string WheelTitle => WheelTitleText.ToUpperInvariant();
    private string WheelTitleText => English ? $"Filter wheel · {CameraName}" : $"Ruota portafiltri · {CameraName}";

    // ---- Profile editor ----
    public bool IsEditing { get => _isEditing; set { if (Set(ref _isEditing, value)) Raise(nameof(IsViewing)); } }
    public bool IsViewing => !IsEditing;

    /// <summary>What the headers say the camera is, shown next to the one the user chose when they differ.</summary>
    public bool CameraWasChanged => Instrument is { CameraSource: EquipmentSource.User, DetectedCamera: { } detected }
        && !string.Equals(detected.DisplayName, Instrument.Camera.DisplayName, StringComparison.OrdinalIgnoreCase);
    public string DetectedCameraNote => Instrument?.DetectedCamera is { } detected
        ? (detected.RawName.Length == 0 ? (English ? "headers: no camera named" : "negli header: nessuna camera") : English ? $"headers: “{detected.RawName}”" : $"negli header: «{detected.RawName}»")
        : "";
    public string CameraText
    {
        get => _cameraText;
        set { if (Set(ref _cameraText, value ?? "")) Raise(nameof(IsCustomCamera)); }
    }
    public CameraChoice? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (!Set(ref _selectedCamera, value)) return;
            if (value is not null) CameraText = value.Name;
            Raise(nameof(IsCustomCamera));
        }
    }
    public bool IsCustomCamera => CameraText.Trim().Length > 0 && !string.Equals(SelectedCamera?.Name, CameraText.Trim(), StringComparison.OrdinalIgnoreCase);
    public bool CustomCameraColor { get => _customCameraColor; set { if (Set(ref _customCameraColor, value)) Raise(nameof(CustomCameraMono)); } }
    public bool CustomCameraMono { get => !_customCameraColor; set => CustomCameraColor = !value; }
    public string TelescopeText
    {
        get => _telescopeText;
        set { if (Set(ref _telescopeText, value ?? "")) Raise(nameof(IsCustomTelescope)); }
    }
    public TelescopeChoice? SelectedTelescope
    {
        get => _selectedTelescope;
        set
        {
            if (!Set(ref _selectedTelescope, value)) return;
            if (value is not null) TelescopeText = value.Name;
            RebuildReducerChoices(SelectedReducer?.Factor);
            Raise(nameof(IsCustomTelescope));
        }
    }
    public bool IsCustomTelescope => TelescopeText.Trim().Length > 0 && !string.Equals(SelectedTelescope?.Name, TelescopeText.Trim(), StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<ReducerChoice> ReducerChoices { get => _reducerChoices; private set => Set(ref _reducerChoices, value); }
    public ReducerChoice? SelectedReducer { get => _selectedReducer; set => Set(ref _selectedReducer, value); }
    public decimal? PixelValue { get => _pixelValue; set => Set(ref _pixelValue, value); }
    public decimal? CustomFocal { get => _customFocal; set => Set(ref _customFocal, value); }
    public decimal? CustomAperture { get => _customAperture; set => Set(ref _customAperture, value); }

    public void BeginEdit()
    {
        if (Instrument is not { } instrument) return;
        var machine = instrument.Camera.Camera;
        _selectedCamera = machine is null ? null : CameraChoices.FirstOrDefault(choice => choice.Id == machine.Id);
        Raise(nameof(SelectedCamera));
        CameraText = machine?.Name ?? (instrument.Camera.RawName.Length == 0 ? "" : instrument.Camera.RawName);
        CustomCameraColor = instrument.Camera.IsColor;
        var scope = instrument.Telescope.Telescope;
        _selectedTelescope = scope is null ? null : TelescopeChoices.FirstOrDefault(choice => choice.Id == scope.Id);
        Raise(nameof(SelectedTelescope));
        TelescopeText = scope?.Name ?? instrument.Telescope.RawName;
        CustomFocal = instrument.NativeFocalMm is { } focal ? (decimal)Math.Round(focal) : null;
        CustomAperture = instrument.ApertureMm is { } aperture ? (decimal)Math.Round(aperture) : null;
        PixelValue = instrument.PixelUm is { } pixel ? (decimal)Math.Round(pixel, 2) : null;
        RebuildReducerChoices(instrument.ReducerFactor ?? 1);
        IsEditing = true;
    }

    public void ApplyEdit()
    {
        if (Instrument is not { } instrument) return;
        var catalogPick = SelectedTelescope is not null && !IsCustomTelescope;
        var name = TelescopeText.Trim();
        var (cameraId, cameraName, cameraType) = CameraEdit(instrument);
        _main.SetEquipment(instrument, new EquipmentOverride
        {
            CameraId = cameraId,
            CameraName = cameraName,
            CameraType = cameraType,
            TelescopeId = catalogPick ? SelectedTelescope!.Id : null,
            TelescopeName = !catalogPick && name.Length > 0 ? name : null,
            FocalMm = !catalogPick && CustomFocal is > 0 ? (double)CustomFocal : null,
            ApertureMm = !catalogPick && CustomAperture is > 0 ? (double)CustomAperture : null,
            ReducerFactor = SelectedReducer?.Factor ?? 1,
            PixelUm = PixelValue is > 0 ? (double)PixelValue : null
        });
        IsEditing = false;
    }

    // The camera the user means, or none when it is the one the headers already name (nothing to remember then).
    private (string? Id, string? Name, CameraSensorType? Type) CameraEdit(InstrumentProfile instrument)
    {
        var detected = instrument.DetectedCamera ?? instrument.Camera;
        var text = CameraText.Trim();
        if (SelectedCamera is { } pick && !IsCustomCamera) return pick.Id == detected.Camera?.Id ? (null, null, null) : (pick.Id, null, null);
        if (text.Length == 0 || string.Equals(text, detected.RawName, StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        return (null, text, CustomCameraColor ? CameraSensorType.Color : CameraSensorType.Mono);
    }

    /// <summary>Stores what Forge detected as the user's profile, so the optics stop showing as unconfirmed.</summary>
    public void ConfirmProfile()
    {
        if (Instrument is not { } instrument) return;
        var scope = instrument.Telescope.Telescope;
        _main.SetEquipment(instrument, new EquipmentOverride
        {
            CameraId = instrument.Override?.CameraId,
            CameraName = instrument.Override?.CameraName,
            CameraType = instrument.Override?.CameraType,
            TelescopeId = scope?.Id,
            TelescopeName = scope is null && instrument.Telescope.RawName.Length > 0 ? instrument.Telescope.RawName : null,
            FocalMm = scope is null ? instrument.NativeFocalMm : null,
            ApertureMm = scope is null ? instrument.ApertureMm : null,
            ReducerFactor = instrument.ReducerFactor ?? 1,
            PixelUm = instrument.PixelUm
        });
    }

    public void ResetProfile()
    {
        if (Instrument is { } instrument) _main.ClearEquipment(instrument);
        IsEditing = false;
    }

    private void RebuildReducerChoices(double? keep)
    {
        var factors = new List<(double Factor, string? Name)> { (1, null) };
        var scope = SelectedTelescope?.Id is { } id ? EquipmentCatalog.Default.FindTelescope(id) : null;
        if (scope is not null) factors.AddRange(EquipmentCatalog.Default.ReducersFor(scope).Select(item => (item.Factor, (string?)item.Name)));
        factors.AddRange(CommonReducers.Select(factor => (factor, (string?)null)));
        if (keep is > 0) factors.Add((keep.Value, null));
        ReducerChoices = factors
            .GroupBy(item => Math.Round(item.Factor, 3))
            .Select(group => group.FirstOrDefault(item => item.Name is not null) is { Name: { } named } ? new ReducerChoice(group.Key, $"{named} · {Number(group.Key, "0.0##")}×")
                : new ReducerChoice(group.Key, Math.Abs(group.Key - 1) < 0.001 ? (English ? "None" : "Nessuno") : $"{Number(group.Key, "0.0##")}×"))
            .OrderByDescending(choice => Math.Abs(choice.Factor - 1) < 0.001).ThenBy(choice => choice.Factor)
            .ToList();
        SelectedReducer = ReducerChoices.FirstOrDefault(choice => keep is { } factor && Math.Abs(choice.Factor - factor) < 0.005) ?? ReducerChoices[0];
    }

    private void RaiseSelectedFilterViews()
    {
        foreach (var name in new[] { nameof(BeamColor), nameof(TrainWheel), nameof(FovEyebrow) }) Raise(name);
    }

    private void RaiseField()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowReduced), nameof(ShowNative), nameof(PreviewWidth), nameof(PreviewHeight), nameof(OtherWidth), nameof(OtherHeight), nameof(OtherLabel),
                     nameof(FovBig), nameof(FovSmall), nameof(FovNote), nameof(FocalText), nameof(RatioText), nameof(PreviewScale), nameof(PreviewScaleText),
                     nameof(SamplingVerdict), nameof(TrainReducer)
                 })
            Raise(name);
    }

    private void RebuildInstrumentScreen()
    {
        // The field opens on the train as it was used; the toggle previews the other configuration.
        _showReduced = Instrument?.ReducerFactor is not null;
        IsEditing = false;
        RaiseField();
        foreach (var name in new[]
                 {
                     nameof(HasReducerOption), nameof(ReducedLabel), nameof(NativeLabel), nameof(TrainTelescope), nameof(TrainCamera), nameof(IsProfileConfirmed),
                     nameof(ProfileBadge), nameof(OpticsRow), nameof(OpticsChip), nameof(ReducerRow), nameof(ReducerChip), nameof(CameraChip), nameof(PixelRow),
                     nameof(CameraWasChanged), nameof(DetectedCameraNote),
                     nameof(PixelChip), nameof(WheelRow), nameof(WheelChip), nameof(WheelTitle), nameof(SeeingLabel)
                 })
            Raise(name);
        RaiseSelectedFilterViews();
    }
}
