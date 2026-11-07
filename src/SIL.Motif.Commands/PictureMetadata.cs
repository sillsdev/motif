namespace SIL.Motif.Commands;

/// <summary>
/// Removes the metadata a picture file carries (camera, GPS, text, timestamps) without decoding it, so a picture
/// attached through the commands keeps none. Pixels and colour data are untouched; bytes it cannot walk are kept as is.
/// </summary>
public static class PictureMetadata
{
    /// <summary>The picture without its metadata; the same bytes when it has none or is not a WebP, PNG or JPEG.</summary>
    public static byte[] Strip(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return PictureSniffer.ReadDeclared(bytes) switch
        {
            ("image/png", _, _) => StripPng(bytes),
            ("image/jpeg", _, _) => StripJpeg(bytes),
            ("image/webp", _, _) => StripWebp(bytes),
            _ => bytes,
        };
    }

    // Keeps APP0 (JFIF); drops APP1 to APP15 (EXIF, XMP, ICC) and comments; the scan on is copied.
    private static byte[] StripJpeg(byte[] b)
    {
        var output = new List<byte>(b.Length) { b[0], b[1] };
        var at = 2;
        while (at + 2 <= b.Length)
        {
            if (b[at] != 0xFF) break;
            var marker = b[at + 1];
            if (marker == 0xFF) { output.Add(0xFF); at++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) { output.Add(0xFF); output.Add(marker); at += 2; continue; }
            if (marker is 0xDA or 0xD9 || at + 4 > b.Length) break;
            var length = b[at + 2] << 8 | b[at + 3];
            if (length < 2 || at + 2 + length > b.Length) break;
            if (marker is not (>= 0xE1 and <= 0xEF or 0xFE))
                for (var i = at; i < at + 2 + length; i++) output.Add(b[i]);
            at += 2 + length;
        }
        for (var i = at; i < b.Length; i++) output.Add(b[i]);
        return output.Count == b.Length ? b : [.. output];
    }

    // Drops the text, EXIF and time chunks; every other chunk, with its checksum, is copied.
    private static byte[] StripPng(byte[] b)
    {
        var output = new List<byte>(b.Length);
        for (var i = 0; i < 8; i++) output.Add(b[i]);
        var at = 8;
        while (at + 12 <= b.Length)
        {
            var length = (long)b[at] << 24 | (long)b[at + 1] << 16 | (long)b[at + 2] << 8 | b[at + 3];
            var end = at + 12 + length;
            if (end > b.Length) break;
            var type = System.Text.Encoding.ASCII.GetString(b, at + 4, 4);
            if (type is not ("tEXt" or "zTXt" or "iTXt" or "eXIf" or "tIME"))
                for (var i = at; i < end; i++) output.Add(b[i]);
            at = (int)end;
        }
        for (var i = at; i < b.Length; i++) output.Add(b[i]);
        return output.Count == b.Length ? b : [.. output];
    }

    // Drops the EXIF and XMP chunks, clears their flags in the extended header and writes the RIFF size again.
    private static byte[] StripWebp(byte[] b)
    {
        var output = new List<byte>(b.Length);
        for (var i = 0; i < 12; i++) output.Add(b[i]);
        var at = 12;
        while (at + 8 <= b.Length)
        {
            var length = (long)(b[at + 4] | b[at + 5] << 8 | b[at + 6] << 16) | (long)b[at + 7] << 24;
            var end = at + 8 + length + (length & 1);
            if (end > b.Length) break;
            var type = System.Text.Encoding.ASCII.GetString(b, at, 4);
            if (type is not ("EXIF" or "XMP "))
            {
                var start = output.Count;
                for (var i = at; i < end; i++) output.Add(b[i]);
                if (type == "VP8X" && length >= 1) output[start + 8] &= 0xF3;
            }
            at = (int)end;
        }
        for (var i = at; i < b.Length; i++) output.Add(b[i]);
        if (output.Count == b.Length)
        {
            // Flags may have been the only change; the size is then already right.
            return [.. output];
        }
        var size = (uint)(output.Count - 8);
        for (var i = 0; i < 4; i++) output[4 + i] = (byte)(size >> (8 * i));
        return [.. output];
    }
}
