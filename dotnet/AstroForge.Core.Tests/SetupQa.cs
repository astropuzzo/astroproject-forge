using AstroForge.Core.Equipment;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Sessions;

/// <summary>A project that mixes rigs: two cameras, or one camera on two focal lengths. Each rig has its own profile; what is about the camera stays the camera's.</summary>
internal static class SetupQa
{
    public static void Run()
    {
        FrameMetadata Frame(string kind, string camera, double? focal, string filter, int index, string telescope = "")
        {
            var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["IMAGETYP"] = kind, ["INSTRUME"] = camera, ["FILTER"] = filter, ["EXPTIME"] = 300.0, ["GAIN"] = 100.0, ["OFFSET"] = 50.0, ["SET-TEMP"] = -10.0,
                ["XBINNING"] = 1, ["YBINNING"] = 1, ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["XPIXSZ"] = 3.76, ["DATE-OBS"] = $"2026-09-{20 + index % 4:00}T23:{index % 60:00}:00"
            };
            if (focal is { } length) headers["FOCALLEN"] = length;
            if (telescope.Length > 0) headers["TELESCOP"] = telescope;
            return FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"setup_{kind}_{camera}_{index:0000}.fits"), headers, new(TimeZoneInfo.Utc, new TimeOnly(12, 0)));
        }

        // The same camera on 800 mm (10 Lights) and on 560 mm with a reducer (4 Lights, a few mm of difference in the header), and a second camera on 250 mm (6 Lights).
        var frames = new List<FrameMetadata>();
        for (var index = 0; index < 10; index++) frames.Add(Frame("Light", "ZWO ASI2600MC Pro", index % 3 == 0 ? 801 : 800, "HOO", index));
        for (var index = 0; index < 4; index++) frames.Add(Frame("Light", "ZWO ASI2600MC Pro", 560, "L", 20 + index));
        for (var index = 0; index < 6; index++) frames.Add(Frame("Light", "ZWO ASI294MC Pro", 250, "L-Pro", 40 + index, "Samyang 135"));
        frames.Add(Frame("Flat", "ZWO ASI2600MC Pro", 560, "L", 60));
        frames.Add(Frame("Flat", "ZWO ASI2600MC Pro", null, "HOO", 61));

        var setups = InstrumentProfile.Setups(frames);
        Assert(setups.Count == 3, $"Attesi 3 setup, trovati {setups.Count}: {string.Join(" | ", setups.Select(item => $"{item.Key}/{item.FocalMm}"))}.");
        Assert(setups[0].IsPrimary && setups[0].Lights == 10 && setups[0].FocalMm is 800 or 801 && setups[0].Key == setups[0].CameraKey, "Il setup principale è la camera con più Light sulla sua focale più usata e tiene la chiave della camera.");
        Assert(!setups[1].IsPrimary && setups[1].CameraKey == setups[0].CameraKey && setups[1].FocalMm == 560 && setups[1].Key == $"{setups[0].CameraKey}@560" && setups[1].EquipmentKey == setups[1].Key,
            $"La stessa camera su un'altra focale è un altro setup con la sua chiave: {setups[1].Key}.");
        Assert(setups[2].IsPrimary && setups[2].CameraKey != setups[0].CameraKey && setups[2].FocalMm == 250 && setups[2].Lights == 6, "Un'altra camera è un setup a sé.");

        // Each setup has its own train and wheel; the main one is what Build gives with no choice made.
        var main = InstrumentProfile.Build(frames)!;
        Assert(main.Setup == setups[0] && main.FocalMm == 800 && main.Filters.Select(filter => filter.RawName).SequenceEqual(["HOO"]), "Senza scelta il profilo è quello del setup principale.");
        var reduced = InstrumentProfile.Build(frames, setup: setups[1])!;
        Assert(reduced.FocalMm == 560 && reduced.Filters.First().RawName == "L" && reduced.Filters.First().Lights == 4, "Il setup a 560 mm ha la sua focale e il suo filtro.");
        Assert(reduced.Filters.First(filter => filter.RawName == "L").Flats == 1, "Il Flat con la focale di un setup segue quel setup.");
        Assert(reduced.Filters.First(filter => filter.RawName == "HOO").Flats == 1, "Un Flat senza focale appartiene a ogni setup della sua camera.");
        var wide = InstrumentProfile.Build(frames, setup: setups[2])!;
        Assert(wide.FocalMm == 250 && wide.Camera.RawName.Contains("294") && wide.FieldOfView is { } field && field.Width > main.FieldOfView!.Value.Width * 2,
            "L'altra camera ha il suo campo, più largo.");

        // What the user said about the camera applies to its rigs; the optics said for the first rig are never taken for the second one's.
        var asked = new List<string>();
        EquipmentOverride? Override(string key)
        {
            asked.Add(key);
            if (key == setups[0].CameraKey) return new EquipmentOverride { CameraName = "Camera mia", CameraType = CameraSensorType.Color, TelescopeName = "Newton 200", ApertureMm = 200, FocalMm = 800 };
            if (key == setups[1].Key) return new EquipmentOverride { TelescopeName = "Newton 200 con riduttore", ApertureMm = 200, FocalMm = 800, ReducerFactor = 0.7 };
            return null;
        }
        var mainUser = InstrumentProfile.Build(frames, equipmentProfiles: Override)!;
        Assert(mainUser.Override is { TelescopeName: "Newton 200" } && mainUser.Camera.DisplayName == "Camera mia", "Il setup principale usa il profilo della camera, come prima che ci fossero più setup.");
        var secondUser = InstrumentProfile.Build(frames, equipmentProfiles: Override, setup: setups[1])!;
        Assert(secondUser.Override is { TelescopeName: "Newton 200 con riduttore" } && secondUser.Camera.DisplayName == "Camera mia" && secondUser.EquipmentKey == setups[1].Key,
            "Il secondo setup della camera ha la camera dell'utente e la sua ottica.");
        var other = InstrumentProfile.Build(frames, equipmentProfiles: key => key == setups[0].CameraKey ? new EquipmentOverride { TelescopeName = "Newton 200", FocalMm = 800 } : null, setup: setups[1])!;
        Assert(other.Override is null && other.Telescope.RawName != "Newton 200", "L'ottica detta per il primo setup non vale per il secondo.");

        // One rig, or none known, is one setup: nothing changes for the usual project.
        Assert(InstrumentProfile.Setups(frames.Take(10)).Count == 1 && InstrumentProfile.Setups(frames.Take(10).Concat([Frame("Light", "ZWO ASI2600MC Pro", null, "HOO", 90)])).Count == 1, "Un solo setup resta un solo setup, anche con un Light senza focale.");
        Assert(InstrumentProfile.Setups([]).Count == 0 && InstrumentProfile.Build([]) is null, "Senza Light non c'è nessun setup.");

        Console.WriteLine("PASS: setup multipli (camera e focale, ruota e profilo di ciascuno, correzioni dell'utente per camera e per setup) verificati.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
