using System.Buffers.Binary;
using System.Xml.Linq;

namespace AstroForge.Core.Parsing;

public static class XisfHeaderReader
{
    private static readonly byte[] Signature = "XISF0100"u8.ToArray();

    public static async Task<Dictionary<string, object?>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var preamble = new byte[16];
        await stream.ReadExactlyAsync(preamble, cancellationToken);
        if (!preamble.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Firma XISF 1.0 non valida.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(preamble.AsSpan(8, 4));
        if (length is 0 or > 64 * 1024 * 1024)
            throw new InvalidDataException($"Lunghezza header XISF non valida: {length}.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes), LoadOptions.None);
        var image = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Image")
            ?? throw new InvalidDataException("Nessun elemento Image nell'header XISF.");
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var geometry = ((string?)image.Attribute("geometry"))?.Split(':');
        if (geometry is { Length: >= 2 } && int.TryParse(geometry[0], out var width) && int.TryParse(geometry[1], out var height))
        {
            headers["NAXIS1"] = width;
            headers["NAXIS2"] = height;
        }

        foreach (var keyword in image.Descendants().Where(element => element.Name.LocalName == "FITSKeyword"))
        {
            var name = ((string?)keyword.Attribute("name"))?.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(name) && name is not "HISTORY" and not "COMMENT")
                headers[name] = FitsHeaderReader.ParseValue((string?)keyword.Attribute("value") ?? "");
        }

        var properties = image.Descendants()
            .Where(element => element.Name.LocalName == "Property")
            .Where(element => element.Attribute("id") is not null)
            .ToDictionary(element => (string)element.Attribute("id")!, ParseProperty, StringComparer.OrdinalIgnoreCase);
        Supplement(headers, properties, "Instrument:Camera:Name", "INSTRUME");
        Supplement(headers, properties, "Instrument:Camera:XBinning", "XBINNING");
        Supplement(headers, properties, "Instrument:Camera:YBinning", "YBINNING");
        Supplement(headers, properties, "Instrument:Filter:Name", "FILTER");
        Supplement(headers, properties, "Instrument:FrameExposureTime", "EXPTIME");
        Supplement(headers, properties, "Observation:Time:Start", "DATE-OBS");
        if (!headers.ContainsKey("EGAIN") && properties.TryGetValue("Instrument:Camera:Gain", out var electronGain)) headers["EGAIN"] = electronGain;
        if (!headers.ContainsKey("FOCALLEN") && properties.TryGetValue("Instrument:Telescope:FocalLength", out var focal) && focal is double focalM) headers["FOCALLEN"] = focalM * 1000;
        return headers;
    }

    private static object? ParseProperty(XElement element)
    {
        var raw = (string?)element.Attribute("value") ?? element.Value;
        var type = (string?)element.Attribute("type") ?? "String";
        if (type.StartsWith("Float", StringComparison.OrdinalIgnoreCase) || type.StartsWith("F32") || type.StartsWith("F64"))
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real) ? real : raw;
        if (type.StartsWith("Int", StringComparison.OrdinalIgnoreCase) || type.StartsWith("UInt", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(raw, out var integer) ? integer : raw;
        return raw;
    }

    private static void Supplement(Dictionary<string, object?> headers, Dictionary<string, object?> properties, string property, string keyword)
    {
        if (!headers.ContainsKey(keyword) && properties.TryGetValue(property, out var value) && value is not null && value.ToString()?.Length > 0)
            headers[keyword] = value;
    }
}

