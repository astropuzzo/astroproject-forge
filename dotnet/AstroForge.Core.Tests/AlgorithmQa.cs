using AstroForge.Core.Analysis;
using AstroForge.Core.Matching;
using AstroForge.Core.Models;
using AstroForge.Core.Parsing;
using AstroForge.Core.Sessions;

/// <summary>Regressions for the 1.14 algorithm review: capture time, nights, statistics and calibration ties.</summary>
internal static class AlgorithmQa
{
    public static void Run()
    {
        CaptureTimeIsUtcUnlessTheSiteClockIsKnown();
        RemoteSiteKeepsItsOwnNight();
        NightsAreCountedOncePerProject();
        ClosestDarkWinsATie();
        Console.WriteLine("PASS: ora di scatto (UTC, orologio del sito), notti di un sito remoto, conteggio notti e spareggio dei Dark verificati.");
    }

    private static void ClosestDarkWinsATie()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var light = Classify(settings, ("DATE-OBS", "2026-08-14T22:00:00Z"));
        FrameMetadata Dark(string name, double temperature, double exposure = 300, bool withOffset = true)
        {
            var frame = Classify(settings, ("IMAGETYP", "Master Dark"), ("SET-TEMP", temperature), ("EXPTIME", exposure));
            if (!withOffset) frame.Offset.SetOriginal(null, MetadataSource.Missing);
            return new FrameMetadata { Path = Path.Combine(Path.GetTempPath(), name), Kind = FrameKind.Dark, IsMaster = true }.CopyFrom(frame);
        }

        var exact = Dark("dark-10.fits", -10);
        var warmer = Dark("dark-08.fits", -8);
        var closest = CalibrationMatcher.Find(light, [warmer, exact], FrameKind.Dark);
        Assert(closest.IsAccepted && closest.Selected?.Frame == exact, $"Tra -10 °C e -8 °C deve vincere il Dark esatto, non {closest.Status}.");

        var colder = Dark("dark-12.fits", -12);
        var symmetric = CalibrationMatcher.Find(light, [warmer, colder], FrameKind.Dark);
        Assert(symmetric.Status == MatchStatus.Ambiguous, "Due Dark alla stessa distanza restano una scelta dell'utente.");

        // A library Master without OFFSET scores higher on bonuses, but a complete Dark must not lose to it.
        var incomplete = Dark("library-dark.fits", -10, withOffset: false);
        incomplete.ConfiguredLibraryPriority = 1;
        var complete = CalibrationMatcher.Find(light, [incomplete, exact], FrameKind.Dark);
        Assert(complete.IsAccepted && complete.Selected?.Frame == exact, $"Un Dark completo deve battere un Master senza offset: {complete.Status}.");
        var onlyIncomplete = CalibrationMatcher.Find(light, [incomplete], FrameKind.Dark);
        Assert(onlyIncomplete.Status == MatchStatus.InsufficientMetadata, "Senza offset il Dark resta da completare.");
    }

    private static FrameMetadata CopyFrom(this FrameMetadata target, FrameMetadata source)
    {
        target.Camera.SetOriginal(source.Camera.Value, MetadataSource.Header);
        target.Width.SetOriginal(source.Width.Value, MetadataSource.Header);
        target.Height.SetOriginal(source.Height.Value, MetadataSource.Header);
        target.XBin.SetOriginal(source.XBin.Value, MetadataSource.Header);
        target.YBin.SetOriginal(source.YBin.Value, MetadataSource.Header);
        target.Gain.SetOriginal(source.Gain.Value, MetadataSource.Header);
        target.Offset.SetOriginal(source.Offset.Value, MetadataSource.Header);
        target.SetTemperatureC.SetOriginal(source.SetTemperatureC.Value, MetadataSource.Header);
        target.ExposureSeconds.SetOriginal(source.ExposureSeconds.Value, MetadataSource.Header);
        return target;
    }

    private static void CaptureTimeIsUtcUnlessTheSiteClockIsKnown()
    {
        var rome = TimeZoneInfo.CreateCustomTimeZone("QA+2", TimeSpan.FromHours(2), "QA+2", "QA+2");
        var settings = new SessionSettings(rome, new TimeOnly(12, 0));

        // ASIAIR writes DATE-OBS only, in UTC and without a zone suffix.
        var asiair = Classify(settings, ("DATE-OBS", "2026-08-14T20:31:04.123456"));
        Assert(asiair.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 14, 20, 31, 4, 123, DateTimeKind.Utc).AddTicks(4560), "DATE-OBS senza suffisso deve essere letto come UTC.");
        Assert(asiair.SiteUtcOffset is null && asiair.SessionId.Value == "2026-08-14", "Un frame con il solo DATE-OBS usa il fuso della macchina per la notte.");
        Assert(AstronomicalSessionResolver.LocalTime(asiair, rome) == new DateTime(2026, 8, 14, 22, 31, 4, 123).AddTicks(4560), "L'ora locale di un frame UTC deve seguire il fuso richiesto.");

        // N.I.N.A. writes both: the difference is the site's offset.
        var nina = Classify(settings, ("DATE-LOC", "2026-08-20T22:18:40.000"), ("DATE-OBS", "2026-08-20T20:18:40.000"));
        Assert(nina.SiteUtcOffset == TimeSpan.FromHours(2), $"Offset del sito non ricavato da DATE-LOC e DATE-OBS: {nina.SiteUtcOffset}.");
        Assert(nina.CapturedAt.Value?.DateTime == new DateTime(2026, 8, 20, 22, 18, 40) && nina.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 20, 20, 18, 40), "L'istante e l'ora locale di N.I.N.A. non coincidono con gli header.");

        // Two clocks that disagree by an odd amount are not an offset.
        var skewed = Classify(settings, ("DATE-LOC", "2026-08-20T22:25:40"), ("DATE-OBS", "2026-08-20T20:18:40"));
        Assert(skewed.SiteUtcOffset is null && skewed.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 8, 20, 20, 18, 40), "Orologi incoerenti: deve restare l'istante UTC.");

        var zulu = Classify(settings, ("DATE-OBS", "2026-07-13T22:30:00Z"));
        Assert(zulu.CapturedAt.Value?.UtcDateTime == new DateTime(2026, 7, 13, 22, 30, 0), "Il suffisso Z deve restare UTC.");
    }

    private static void RemoteSiteKeepsItsOwnNight()
    {
        // A remote observatory at UTC+10 read on a machine at UTC+2: local dusk to dawn is 09:00 to 19:00 UTC,
        // which straddles the machine's noon. Every frame must still fall in the site's night of 14 August.
        var machine = new SessionSettings(TimeZoneInfo.CreateCustomTimeZone("QA+2", TimeSpan.FromHours(2), "QA+2", "QA+2"), new TimeOnly(12, 0));
        var dusk = new DateTime(2026, 8, 14, 19, 0, 0);
        var nights = Enumerable.Range(0, 11).Select(hour =>
        {
            var local = dusk.AddHours(hour);
            return Classify(machine, ("DATE-LOC", local.ToString("s")), ("DATE-OBS", local.AddHours(-10).ToString("s"))).SessionId.Value;
        }).Distinct().ToArray();
        Assert(nights is ["2026-08-14"], $"La notte di un sito remoto è stata divisa: {string.Join(", ", nights)}.");
    }

    private static void NightsAreCountedOncePerProject()
    {
        var settings = new SessionSettings(TimeZoneInfo.Utc, new TimeOnly(12, 0));
        var frames = new List<FrameMetadata>();
        foreach (var night in new[] { 14, 15 })
            foreach (var filter in new[] { "Ha", "OIII", "SII" })
                frames.Add(Classify(settings, ("FILTER", filter), ("DATE-OBS", $"2026-08-{night}T22:00:00Z")));
        var statistics = ProjectStatisticsCalculator.Calculate(ProjectAnalyzer.Analyze(frames));
        Assert(statistics.NightCount == 2 && statistics.Nights.Count == 6, $"Due notti con tre filtri devono contare 2 notti, non {statistics.NightCount}.");
        Assert(statistics.Filters.All(filter => filter.NightCount == 2), "Ogni filtro è stato ripreso in due notti.");
    }

    private static FrameMetadata Classify(SessionSettings settings, params (string Key, object Value)[] extra)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGETYP"] = "Light", ["INSTRUME"] = "QA Camera", ["GAIN"] = 100, ["OFFSET"] = 50, ["SET-TEMP"] = -10,
            ["FILTER"] = "Ha", ["EXPTIME"] = 300, ["NAXIS1"] = 6248, ["NAXIS2"] = 4176, ["XBINNING"] = 1, ["YBINNING"] = 1
        };
        foreach (var (key, value) in extra) headers[key] = value;
        return FrameClassifier.Classify(Path.Combine(Path.GetTempPath(), $"qa-{Guid.NewGuid():N}.fits"), headers, settings);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
