using System.Collections.ObjectModel;
using Avalonia.Media;
using AstroForge.Core.Equipment;

namespace AstroForge.CrossPlatform.ViewModels;

/// <summary>One rig of the project as a card to pick: a camera on a focal length, how much it shot, and the colour that links it to its frame on the sky.</summary>
public sealed record SetupRow(string Key, string Title, string Camera, string Optics, string Usage, bool IsSelected, IBrush Dot);

/// <summary>A project that mixes rigs (two cameras, or one camera with and without a reducer): each rig has its own camera, optics and filters, and the gear page shows one at a time.</summary>
public sealed partial class ObservatoryViewModel
{
    public ObservableCollection<SetupRow> SetupRows { get; } = [];
    public bool HasSeveralSetups => SetupRows.Count > 1;
    public string SetupHint => English
        ? "This project mixes rigs. Pick one to check its camera, optics and filters; the others stay dashed on the sky."
        : "Il progetto mescola più setup. Scegline uno per controllare camera, ottica e filtri; gli altri restano tratteggiati sul cielo.";

    public void SelectSetup(string key) => _main.SelectSetup(key);

    private void RebuildSetups()
    {
        SetupRows.Clear();
        var setups = _main.Setups;
        if (setups.Count > 1)
        {
            var selected = Instrument?.Setup?.Key;
            var others = 0;
            for (var index = 0; index < setups.Count; index++)
            {
                var setup = setups[index];
                var profile = _main.InstrumentFor(setup);
                var isSelected = setup.Key == selected;
                var colour = isSelected ? SelectedRigColour : RigColour(others++);
                var camera = ShortCamera(profile?.Camera.DisplayName ?? setup.CameraName);
                var scope = profile?.Telescope is { } train && (train.RawName.Length > 0 || train.Telescope is not null) ? train.DisplayName : "";
                var focal = profile?.FocalMm ?? setup.FocalMm;
                var optics = string.Join(" · ", new[] { scope, focal is { } length ? $"{length:0} mm" : "" }.Where(part => part.Length > 0));
                var hours = setup.IntegrationSeconds / 3600;
                var usage = English
                    ? $"{setup.Lights} Light · {hours:0.0} h · {setup.Nights} {(setup.Nights == 1 ? "night" : "nights")}"
                    : $"{setup.Lights} Light · {hours:0.0} h · {setup.Nights} {(setup.Nights == 1 ? "notte" : "notti")}";
                SetupRows.Add(new SetupRow(setup.Key, $"Setup {index + 1}", camera, optics.Length == 0 ? (English ? "optics not in the headers" : "ottica non indicata") : optics, usage, isSelected,
                    new SolidColorBrush(Color.Parse(colour))));
            }
        }
        Raise(nameof(HasSeveralSetups));
        Raise(nameof(SetupHint));
    }

    /// <summary>The colour of the frame being described, the one the sky draws bold.</summary>
    internal const string SelectedRigColour = "#9DB8FF";

    private static string ShortCamera(string name)
    {
        foreach (var brand in new[] { "ZWO ", "QHY ", "QHYCCD ", "Player One ", "PlayerOne ", "ToupTek ", "SVBONY ", "Atik ", "Moravian ", "Altair " })
            if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) return name[brand.Length..];
        return name;
    }
}
