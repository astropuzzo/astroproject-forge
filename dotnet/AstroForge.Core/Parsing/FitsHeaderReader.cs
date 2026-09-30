using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AstroForge.Core.Parsing;

public static class FitsHeaderReader
{
    private const int BlockSize = 2880;
    private const int CardSize = 80;
    private const int MaxBlocks = 128;
    // Capture software writes one to four header blocks: one read call covers almost every file.
    private const int ChunkSize = 4 * BlockSize;

    /// <summary>Synchronous read for callers that already run on a worker thread, such as the parallel folder scan.</summary>
    public static Dictionary<string, object?> Read(string path, CancellationToken cancellationToken = default)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var header = new HeaderBuilder();
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            var chunk = buffer.AsSpan(0, ChunkSize);
            for (long offset = 0; ; offset += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Fill(handle, chunk, offset);
                if (header.Parse(chunk[..length], offset)) return header.Build();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<Dictionary<string, object?>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new HeaderBuilder();
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            var chunk = buffer.AsMemory(0, ChunkSize);
            for (long offset = 0; ; offset += ChunkSize)
            {
                var length = await FillAsync(handle, chunk, offset, cancellationToken);
                if (header.Parse(chunk.Span[..length], offset)) return header.Build();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int Fill(SafeFileHandle handle, Span<byte> chunk, long offset)
    {
        var length = 0;
        while (length < chunk.Length)
        {
            var read = RandomAccess.Read(handle, chunk[length..], offset + length);
            if (read == 0) break;
            length += read;
        }
        return length;
    }

    private static async ValueTask<int> FillAsync(SafeFileHandle handle, Memory<byte> chunk, long offset, CancellationToken cancellationToken)
    {
        var length = 0;
        while (length < chunk.Length)
        {
            var read = await RandomAccess.ReadAsync(handle, chunk[length..], offset + length, cancellationToken);
            if (read == 0) break;
            length += read;
        }
        return length;
    }

    private sealed class HeaderBuilder
    {
        private readonly Dictionary<string, object?> _headers = new(64, StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _history = [];
        private readonly List<string> _comments = [];
        private readonly char[] _card = new char[CardSize];

        /// <summary>Parses the complete blocks of a chunk; true once the END card is found.</summary>
        public bool Parse(ReadOnlySpan<byte> chunk, long offset)
        {
            for (var start = 0; start < chunk.Length || start == 0; start += BlockSize)
            {
                if ((offset + start) / BlockSize >= MaxBlocks)
                    throw new InvalidDataException("Card END non trovata nell'header FITS.");
                if (chunk.Length - start < BlockSize)
                    throw new EndOfStreamException("Header FITS troncato prima della card END.");
                if (ParseBlock(chunk.Slice(start, BlockSize))) return true;
            }
            return false;
        }

        public Dictionary<string, object?> Build()
        {
            if (_history.Count > 0) _headers["HISTORY"] = _history;
            if (_comments.Count > 0) _headers["COMMENT"] = _comments;
            return _headers;
        }

        private bool ParseBlock(ReadOnlySpan<byte> block)
        {
            for (var offset = 0; offset < BlockSize; offset += CardSize)
            {
                Encoding.ASCII.GetChars(block.Slice(offset, CardSize), _card);
                ReadOnlySpan<char> card = _card;
                var keyword = card[..8].Trim();
                if (keyword.Equals("END", StringComparison.OrdinalIgnoreCase)) return true;
                if (keyword.Equals("HISTORY", StringComparison.OrdinalIgnoreCase))
                {
                    _history.Add(card[8..].Trim().ToString());
                    continue;
                }
                if (keyword.Equals("COMMENT", StringComparison.OrdinalIgnoreCase))
                {
                    _comments.Add(card[8..].Trim().ToString());
                    continue;
                }
                if (keyword.Length == 0 || card[8] != '=' || card[9] != ' ')
                    continue;
                _headers[KeywordName(keyword)] = ParseValue(RemoveComment(card[10..]).Trim());
            }
            return false;
        }
    }

    // The same few dozen keywords repeat in every file of a project: reuse their strings instead of allocating per card.
    private static readonly string[] CommonKeywords =
    [
        "SIMPLE", "BITPIX", "NAXIS", "NAXIS1", "NAXIS2", "NAXIS3", "EXTEND", "BZERO", "BSCALE", "IMAGETYP", "FRAMETYP", "OBSTYPE",
        "EXPOSURE", "EXPTIME", "DATE-LOC", "DATE-OBS", "DATE-AVG", "DATE", "XBINNING", "YBINNING", "GAIN", "OFFSET", "EGAIN",
        "XPIXSZ", "YPIXSZ", "INSTRUME", "SET-TEMP", "CCD-TEMP", "READOUTM", "BAYERPAT", "XBAYROFF", "YBAYROFF", "USBLIMIT",
        "TELESCOP", "FOCALLEN", "FOCRATIO", "APTDIA", "RA", "DEC", "OBJCTRA", "OBJCTDEC", "OBJCTROT", "ROTATOR", "ROTATANG",
        "OBJECT", "FILTER", "FWHEEL", "FOCPOS", "FOCUSPOS", "FOCTEMP", "FOCUSTEM", "AIRMASS", "SITELAT", "SITELONG", "SITEELEV",
        "SWCREATE", "CREATOR", "PIERSIDE", "CENTALT", "CENTAZ", "EQUINOX", "CTYPE1", "CTYPE2", "CRPIX1", "CRPIX2", "CRVAL1", "CRVAL2",
        "CDELT1", "CDELT2", "CD1_1", "CD1_2", "CD2_1", "CD2_2", "MJD-OBS", "JD", "CCDXBIN", "CCDYBIN", "XBIN", "YBIN", "CAMERA"
    ];
    private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup =
        CommonKeywords.ToDictionary(keyword => keyword, StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();

    private static string KeywordName(ReadOnlySpan<char> keyword) =>
        KeywordLookup.TryGetValue(keyword, out var known) ? known : keyword.ToString().ToUpperInvariant();

    internal static object? ParseValue(string text) => ParseValue(text.AsSpan());

    internal static object? ParseValue(ReadOnlySpan<char> text)
    {
        if (text.IsWhiteSpace()) return null;
        if (text[0] == '\'')
        {
            var content = text[1..];
            var end = content.LastIndexOf('\'');
            if (end >= 0) content = content[..end];
            return content.ToString().Replace("''", "'").TrimEnd();
        }
        if (text is "T") return true;
        if (text is "F") return false;
        // FITS allows a D exponent ("1.5D3"); .NET parses only E.
        ReadOnlySpan<char> numeric = text.IndexOfAny('D', 'd') >= 0 ? text.ToString().Replace('D', 'E').Replace('d', 'e') : text;
        if (long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return real;
        return text.Trim().ToString();
    }

    private static ReadOnlySpan<char> RemoveComment(ReadOnlySpan<char> text)
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
