namespace SIL.Motif.Commands;

/// <summary>
/// Recognises a WebP, PNG or JPEG picture from its own bytes and reads its size, so a caller (the CLI included) cannot
/// store anything else or lie about the type. It decodes nothing.
/// </summary>
public static class PictureSniffer
{
    /// <summary>The largest side, in pixels, a stored picture may have.</summary>
    public const int MaxSide = 20000;

    /// <summary>The media type and size of a picture, or null when the bytes are not a readable WebP, PNG or JPEG.</summary>
    public static (string MediaType, int Width, int Height)? Read(ReadOnlySpan<byte> bytes) =>
        ReadDeclared(bytes) is { Width: > 0 and <= MaxSide, Height: > 0 and <= MaxSide } found ? found : null;

    /// <summary>
    /// The media type and size a WebP, PNG or JPEG header declares, whatever the size, or null when the bytes are none of them.
    /// A caller that decodes the picture uses it to refuse a huge one before allocating for it.
    /// </summary>
    public static (string MediaType, int Width, int Height)? ReadDeclared(ReadOnlySpan<byte> bytes) =>
        IsPng(bytes) ? ReadPng(bytes) : IsJpeg(bytes) ? ReadJpeg(bytes) : ReadWebp(bytes);

    private static bool IsPng(ReadOnlySpan<byte> b) =>
        b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
        b[12] == 'I' && b[13] == 'H' && b[14] == 'D' && b[15] == 'R';

    // A WebP is a RIFF file whose first chunk is lossy (VP8 ), lossless (VP8L) or extended (VP8X).
    private static (string MediaType, int Width, int Height)? ReadWebp(ReadOnlySpan<byte> b)
    {
        if (b.Length < 30 || !b[..4].SequenceEqual("RIFF"u8) || !b[8..12].SequenceEqual("WEBP"u8)) return null;
        var chunk = b[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
            return ("image/webp", (b[24] | b[25] << 8 | b[26] << 16) + 1, (b[27] | b[28] << 8 | b[29] << 16) + 1);
        if (chunk.SequenceEqual("VP8L"u8) && b[20] == 0x2F)
        {
            var bits = (uint)(b[21] | b[22] << 8 | b[23] << 16) | (uint)b[24] << 24;
            return ("image/webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        return chunk.SequenceEqual("VP8 "u8) && b[23] == 0x9D && b[24] == 0x01 && b[25] == 0x2A
            ? ("image/webp", (b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF)
            : null;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8;

    private static (string MediaType, int Width, int Height)? ReadPng(ReadOnlySpan<byte> b) =>
        ("image/png", (int)ReadUInt32(b, 16), (int)Math.Min(ReadUInt32(b, 20), int.MaxValue));

    private static uint ReadUInt32(ReadOnlySpan<byte> b, int at) =>
        ((uint)b[at] << 24 | (uint)b[at + 1] << 16 | (uint)b[at + 2] << 8 | b[at + 3]) & 0x7FFFFFFF;

    // Walks the marker segments to the first start-of-frame, which holds the height and width.
    private static (string MediaType, int Width, int Height)? ReadJpeg(ReadOnlySpan<byte> b)
    {
        var at = 2;
        while (at + 4 <= b.Length)
        {
            if (b[at] != 0xFF) return null;
            var marker = b[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) { at += 2; continue; }
            var length = b[at + 2] << 8 | b[at + 3];
            if (length < 2) return null;
            var isFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrame)
                return at + 9 <= b.Length
                    ? ("image/jpeg", b[at + 7] << 8 | b[at + 8], b[at + 5] << 8 | b[at + 6])
                    : null;
            at += 2 + length;
        }
        return null;
    }
}
