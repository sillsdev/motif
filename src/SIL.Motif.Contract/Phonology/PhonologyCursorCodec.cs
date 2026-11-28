using System.Text;
using System.Text.Json;

namespace SIL.Motif.Contract.Phonology;

/// <summary>Writes a phonology page cursor as the opaque string callers pass back and reads one back.</summary>
public static class PhonologyCursorCodec
{
    /// <summary>The opaque cursor for <paramref name="cursor"/>, or null when there is no next page.</summary>
    public static string? Encode<T>(T? cursor) where T : class =>
        cursor is null ? null : Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Whether <paramref name="text"/> is a cursor this API issued; null text is no cursor and succeeds.</summary>
    public static bool TryDecode<T>(string? text, out T? cursor) where T : class
    {
        cursor = null;
        if (text is null) return true;
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            cursor = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
            return cursor is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
