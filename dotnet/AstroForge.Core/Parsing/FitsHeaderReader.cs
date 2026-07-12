using System.Globalization;
using System.Text;

namespace AstroForge.Core.Parsing;

public static class FitsHeaderReader
{
    private const int BlockSize = 2880;
    private const int CardSize = 80;

    public static async Task<Dictionary<string, object?>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var history = new List<string>();
        var comments = new List<string>();
        var block = new byte[BlockSize];
        var foundEnd = false;

        for (var blockIndex = 0; blockIndex < 128 && !foundEnd; blockIndex++)
        {
            await stream.ReadExactlyAsync(block, cancellationToken);
            for (var offset = 0; offset < BlockSize; offset += CardSize)
            {
                var card = Encoding.ASCII.GetString(block, offset, CardSize);
                var keyword = card[..8].Trim().ToUpperInvariant();
                if (keyword == "END")
                {
                    foundEnd = true;
                    break;
                }
                if (keyword == "HISTORY")
                {
                    history.Add(card[8..].Trim());
                    continue;
                }
                if (keyword == "COMMENT")
                {
                    comments.Add(card[8..].Trim());
                    continue;
                }
                if (keyword.Length == 0 || card[8..10] != "= ")
                    continue;
                headers[keyword] = ParseValue(RemoveComment(card[10..]).Trim());
            }
        }

        if (!foundEnd)
            throw new InvalidDataException("Card END non trovata nell'header FITS.");
        if (history.Count > 0) headers["HISTORY"] = history;
        if (comments.Count > 0) headers["COMMENT"] = comments;
        return headers;
    }

    internal static object? ParseValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.StartsWith('\''))
        {
            var content = text[1..];
            var end = content.LastIndexOf('\'');
            if (end >= 0) content = content[..end];
            return content.Replace("''", "'").TrimEnd();
        }
        if (text == "T") return true;
        if (text == "F") return false;
        var numeric = text.Replace('D', 'E').Replace('d', 'e');
        if (long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return real;
        return text.Trim();
    }

    private static string RemoveComment(string text)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '\'') { i++; continue; }
                inString = !inString;
            }
            else if (text[i] == '/' && !inString)
                return text[..i];
        }
        return text;
    }
}

