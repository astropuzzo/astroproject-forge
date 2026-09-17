using System.Text;
using System.Text.Json;
using System.Xml;
using System.Security.Cryptography;
using AstroForge.Core.Export;
using AstroForge.Core.Models;

namespace AstroForge.Core.Wbpp;

public static class WbppInstanceGenerator
{
    public static string Generate(ProjectPlan plan)
    {
        if (!Directory.Exists(plan.ProjectRoot))
            throw new DirectoryNotFoundException("Esporta prima il progetto: l’istanza WBPP usa i percorsi della cartella finale.");

        var output = string.IsNullOrWhiteSpace(plan.PixInsightOutputFolderName)
            ? Path.Combine(plan.ProjectRoot, "PixInsight Output")
            : Path.Combine(plan.ProjectRoot, plan.PixInsightOutputFolderName);
        Directory.CreateDirectory(output);

        var preGroups = plan.Files.Where(file => file.Role != "excluded-light")
            .GroupBy(GroupKey)
            .Select((items, index) => CreateGroup(items.ToArray(), index + 1, plan, 1));
        var postGroups = plan.Files.Where(file => file.Role == "light")
            .GroupBy(PostGroupKey)
            .Select((items, index) => CreateGroup(items.ToArray(), index + 1, plan, 2));
        var groups = preGroups.Concat(postGroups)
            .ToArray();
        var keywords = plan.Recipe.Keywords.Select(item => new { name = item.Keyword, mode = item.Pre ? 1 : 2 }).ToArray();
        var parameters = new Dictionary<string, string>
        {
            ["VERSION"] = "3.0.1",
            ["saveFrameGroups"] = "true",
            ["smartNamingOverride"] = "true",
            ["groupingKeywordsEnabled"] = keywords.Length > 0 ? "true" : "false",
            ["outputDirectory"] = Encode(PathForPixInsight(output)),
            ["usePipelineScript"] = "false",
            ["enableCompactGUI"] = "false",
            ["keywords"] = Encode(JsonSerializer.Serialize(keywords)),
            ["groups"] = Encode(JsonSerializer.Serialize(groups)),
            ["cache_v2"] = Encode("{}")
        };

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        var path = Path.Combine(plan.ProjectRoot, $"{plan.ProjectName}-WBPP.xpsm");
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("xpsm", "http://www.pixinsight.com/xpsm");
        writer.WriteAttributeString("version", "1.0");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteAttributeString("xsi", "schemaLocation", null, "http://www.pixinsight.com/xpsm http://pixinsight.com/xpsm/xpsm-1.0.xsd");
        writer.WriteStartElement("instance");
        writer.WriteAttributeString("class", "Script"); writer.WriteAttributeString("version", "256"); writer.WriteAttributeString("id", "AstroForge_WBPP");
        Element(writer, "parameter", "$PXI_SRCDIR/scripts/BatchPreprocessing/WBPP.js", "filePath");
        Element(writer, "parameter", WbppChecksum(), "md5sum");
        writer.WriteStartElement("table"); writer.WriteAttributeString("id", "parameters"); writer.WriteAttributeString("rows", parameters.Count.ToString());
        foreach (var pair in parameters)
        {
            writer.WriteStartElement("tr"); Element(writer, "td", pair.Key, "id"); Element(writer, "td", pair.Value, "value"); writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
        return path;
    }

    private static object CreateGroup(PlannedFile[] files, int counter, ProjectPlan plan, int mode)
    {
        var first = files[0]; var frame = first.Frame; var type = ImageType(first.Role);
        var keywords = mode == 1 ? Keywords(first, plan) : new Dictionary<string, string>();
        var exposure = frame.ExposureSeconds.Value ?? 0;
        var binning = frame.XBin.Value ?? 1;
        var width = frame.Width.Value ?? 0; var height = frame.Height.Value ?? 0;
        var cfa = !string.IsNullOrWhiteSpace(frame.BayerPattern.Value);
        var filter = frame.FilterName.Value ?? "";
        var items = files.Select(file => CreateItem(file, plan)).ToArray();
        return new
        {
            imageType = type, binning, hasMaster = files.Any(file => file.Frame.IsMaster), exposureTime = exposure, filter,
            exposureTimes = files.Select(file => file.Frame.ExposureSeconds.Value ?? 0).Distinct().ToArray(), optimizeMasterDark = false,
            size = new { width, height }, isCFA = cfa, CFAPattern = CfaPattern(frame.BayerPattern.Value), debayerMethod = 2, keywords, mode,
            fileItems = items, lightOutputPedestalMode = 1, lightOutputPedestal = 0, lightOutputPedestalLimit = 0.0001,
            drizzleData = new { enabled = mode == 2, fast = true, scale = 1, dropShrink = 1, function = 0, gridSize = 16 },
            fastIntegration = new { }, frameFilterConfig = (object?)null, isHidden = false, isActive = true,
            footerLengthForCurrentHeader = 0, forceNoDark = false, forceNoFlat = false,
            id = $"{type}_{binning}_{filter}_{exposure}_{(mode == 2 ? "RGB" : cfa ? "CFA" : "MONO")}_{string.Join('_', keywords.Select(pair => $"{pair.Key}:{pair.Value}"))}_{mode}_{counter}_{width}x{height}",
            fastIntegrationData = new { enabled = false, manuallyChanged = false, saveRegisteredImages = false, weightingScheme = 0 },
            ccData = new { enabled = true, highSigma = 10, CCTemplate = "" }, __counter__ = counter
        };
    }

    private static object CreateItem(PlannedFile file, ProjectPlan plan)
    {
        var frame = file.Frame; var path = PathForPixInsight(Path.Combine(plan.ProjectRoot, file.RelativePath));
        var keywords = Keywords(file, plan); var type = ImageType(file.Role); var binning = frame.XBin.Value ?? 1;
        var width = frame.Width.Value ?? 0; var height = frame.Height.Value ?? 0; var cfa = !string.IsNullOrWhiteSpace(frame.BayerPattern.Value);
        var fileKeywords = new Dictionary<string, string>
        {
            ["IMAGETYP"] = frame.Kind.ToString().ToUpperInvariant(), ["EXPTIME"] = (frame.ExposureSeconds.Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["XBINNING"] = binning.ToString(), ["YBINNING"] = (frame.YBin.Value ?? binning).ToString(), ["FILTER"] = frame.FilterName.Value ?? "",
            ["GAIN"] = (frame.Gain.Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), ["OFFSET"] = (frame.Offset.Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["INSTRUME"] = frame.Camera.Value ?? "", ["BAYERPAT"] = frame.BayerPattern.Value ?? ""
        };
        foreach (var pair in keywords) fileKeywords[pair.Key] = pair.Value;
        return new
        {
            filePath = path, imageType = type, binning, filter = frame.FilterName.Value ?? "", exposureTime = frame.ExposureSeconds.Value ?? 0,
            fileKeywords, enabled = true, size = new { width, height }, matchingSizes = new Dictionary<string, object>(),
            createdWithSmartNamingEnabled = true, solverParams = new { }, isCFA = cfa, isMaster = frame.IsMaster, keywords,
            overscan = new { enabled = false, overscan = Array.Empty<object>(), imageRect = new { x0 = 0, y0 = 0, x1 = 0, y1 = 0, __className__ = "Rect" } },
            processed = new { }, current = new { @default = path }, descriptor = new { }, localNormalizationFile = new { }, drizzleFile = new { },
            isReference = new { @default = false }, __fastIntegration = 0
        };
    }

    private static Dictionary<string, string> Keywords(PlannedFile file, ProjectPlan plan)
    {
        var result = new Dictionary<string, string>();
        foreach (var recommendation in plan.Recipe.Keywords)
        {
            var key = recommendation.Keyword;
            var value = key switch
            {
                "FLATSET" => file.GroupId ?? Segment(file.RelativePath, "FLATSET_"),
                "DARKSET" => Segment(file.RelativePath, "DARKSET_") ?? (file.Role == "dark" ? file.GroupId : null),
                "BIASSET" => Segment(file.RelativePath, "BIASSET_") ?? (file.Role == "bias" ? file.GroupId : null),
                "TARGET" => file.Frame.ObjectName.Value,
                _ => Segment(file.RelativePath, key + "_")
            };
            if (!string.IsNullOrWhiteSpace(value)) result[key] = value;
        }
        return result;
    }

    private static string GroupKey(PlannedFile file) => $"{ImageType(file.Role)}|{file.Frame.IsMaster}|{file.Frame.XBin.Value}|{file.Frame.ExposureSeconds.Value}|{file.Frame.FilterName.Value}|{file.Frame.Width.Value}x{file.Frame.Height.Value}|{file.GroupId}|{file.RelativePath.Split(Path.DirectorySeparatorChar).FirstOrDefault(part => part.StartsWith("DARKSET_", StringComparison.OrdinalIgnoreCase))}|{file.RelativePath.Split(Path.DirectorySeparatorChar).FirstOrDefault(part => part.StartsWith("BIASSET_", StringComparison.OrdinalIgnoreCase))}";
    private static string PostGroupKey(PlannedFile file) => $"{file.Frame.XBin.Value}|{file.Frame.FilterName.Value}|{file.Frame.Width.Value}x{file.Frame.Height.Value}|{file.Frame.BayerPattern.Value}";
    private static int ImageType(string role) => role switch { "bias" => 1, "dark" => 2, "flat" => 3, _ => 4 };
    private static int CfaPattern(string? pattern) => pattern?.ToUpperInvariant() switch { "RGGB" => 0, "BGGR" => 1, "GBRG" => 2, "GRBG" => 3, _ => 0 };
    private static string? Segment(string relativePath, string prefix) => relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault(part => part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string WbppChecksum()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { @"C:\Program Files\PixInsight\src\scripts\BatchPreprocessing\WBPP.js", @"C:\Program Files\PixInsight\scripts\BatchPreprocessing\WBPP.js" }
            : OperatingSystem.IsMacOS()
                ? new[] { "/Applications/PixInsight/PixInsight.app/Contents/Resources/src/scripts/BatchPreprocessing/WBPP.js" }
                : new[] { "/opt/PixInsight/src/scripts/BatchPreprocessing/WBPP.js" };
        var script = candidates.FirstOrDefault(File.Exists);
        return script is null ? "" : Convert.ToHexString(MD5.HashData(File.ReadAllBytes(script))).ToLowerInvariant();
    }
    private static string PathForPixInsight(string path) => Path.GetFullPath(path).Replace('\\', '/');
    private static void Element(XmlWriter writer, string name, string value, string id) { writer.WriteStartElement(name); writer.WriteAttributeString("id", id); writer.WriteString(value); writer.WriteEndElement(); }
}
