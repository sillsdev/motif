namespace SIL.Motif.Commands;

/// <summary>
/// Recognises a PNG or JPEG picture from its own bytes and reads its size, so a caller (the CLI included) cannot
/// store anything else or lie about the type. It decodes nothing.
/// </summary>
public static class PictureSniffer
{
    /// <summary>The largest side, in pixels, a stored picture may have.</summary>
    public const int MaxSide = 20000;

    /// <summary>The media type and size of a picture, or null when the bytes are not a readable PNG or JPEG.</summary>
    public static (string MediaType, int Width, int Height)? Read(ReadOnlySpan<byte> bytes) =>
        ReadDeclared(bytes) is { Width: > 0 and <= MaxSide, Height: > 0 and <= MaxSide } found ? found : null;

    /// <summary>
    /// The media type and size a PNG or JPEG header declares, whatever the size, or null when the bytes are neither.
    /// A caller that decodes the picture uses it to refuse a huge one before allocating for it.
    /// </summary>
    public static (string MediaType, int Width, int Height)? ReadDeclared(ReadOnlySpan<byte> bytes) =>
        IsPng(bytes) ? ReadPng(bytes) : IsJpeg(bytes) ? ReadJpeg(bytes) : null;

    private static bool IsPng(ReadOnlySpan<byte> b) =>
        b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
        b[12] == 'I' && b[13] == 'H' && b[14] == 'D' && b[15] == 'R';

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
