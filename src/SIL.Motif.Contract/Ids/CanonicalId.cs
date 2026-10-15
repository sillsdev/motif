using System;
using System.Globalization;
using System.Security.Cryptography;

namespace SIL.Motif.Contract.Ids;

/// <summary>
/// The <c>&lt;optional prefix&gt;&lt;22-character unpadded base64url suffix&gt;</c> convention used
/// for <c>proposalId</c>, every <c>operationId</c>, and proposed entity ids — the Change Set
/// contract's "IDs and GUID mapping" rule.
/// </summary>
/// <remarks>
/// Only the trailing 22 characters are structurally enforced: URL-safe base64 alphabet, no
/// padding, decoding to exactly 16 bytes. The prefix is preserved exactly with no normalization
/// and carries no structural meaning.
///
/// An id's identity is the 128 bits its suffix decodes to. <see cref="Equals(CanonicalId)"/>,
/// <see cref="GetHashCode"/>, <c>==</c> and <c>!=</c> compare that identity, so a prefixed and a bare
/// spelling of one GUID are equal, and so are two suffix spellings that differ only in the unused
/// trailing bits. The spelling stays available through <see cref="Value"/> and <see cref="Prefix"/>,
/// and <see cref="SpellingEquals"/> compares it exactly. The type has no natural order; code that
/// sorts ids names <see cref="SpellingOrder"/>.
///
/// This type does not reject a non-canonical (nonzero low-order bit) suffix as long as it decodes
/// to 16 bytes; the contract's four structural rules do not require that. <see cref="Mint"/> and
/// <see cref="FromGuid"/> always produce the canonical zero-bit encoding.
/// </remarks>
public readonly struct CanonicalId : IEquatable<CanonicalId>
{
    public const int SuffixLength = 22;
    public const int ByteLength = 16;

    private readonly string? _suffix;
    private readonly ulong _high;
    private readonly ulong _low;

    private CanonicalId(string prefix, string suffix)
    {
        Prefix = prefix;
        _suffix = suffix;
        var bytes = Base64Url.Decode(suffix);
        _high = ReadUInt64(bytes, 0);
        _low = ReadUInt64(bytes, 8);
    }

    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | bytes[offset + i];
        return value;
    }

    /// <summary>Everything before the final 22 characters, preserved exactly. Empty if none.</summary>
    public string Prefix { get; }

    /// <summary>The structurally-enforced trailing 22-character base64url suffix.</summary>
    public string Suffix => _suffix ?? throw new InvalidOperationException(UninitializedMessage);

    /// <summary>
    /// The suffix this identity's GUID encodes to with its unused trailing bits clear, so every accepted spelling of
    /// one GUID has the same key; sorting by it orders identities the same way however they were spelled.
    /// </summary>
    public string CanonicalSuffix => FromGuid(ToGuid()).Suffix;

    /// <summary>The full textual canonical id: <see cref="Prefix"/> + <see cref="Suffix"/>.</summary>
    public string Value => Prefix + Suffix;

    private bool IsInitialized => _suffix is not null;

    private const string UninitializedMessage =
        "This CanonicalId is the uninitialized default value; it does not represent a valid id.";

    public static CanonicalId Parse(string text)
    {
        if (!TryParse(text, out var id, out var error))
            throw new FormatException(error);
        return id;
    }

    public static bool TryParse(string? text, out CanonicalId id) => TryParse(text, out id, out _);

    public static bool TryParse(string? text, out CanonicalId id, out string? error)
    {
        id = default;

        if (text is null || text.Length == 0)
        {
            error = "A canonical id must not be null or empty.";
            return false;
        }

        if (text.Length < SuffixLength)
        {
            error =
                $"'{text}' is only {text.Length} characters long; a canonical id requires at " +
                $"least the {SuffixLength}-character suffix.";
            return false;
        }

        var prefix = text.Substring(0, text.Length - SuffixLength);
        var suffix = text.Substring(text.Length - SuffixLength);

        byte[] bytes;
        try
        {
            bytes = Base64Url.Decode(suffix);
        }
        catch (FormatException ex)
        {
            error = $"'{text}': suffix '{suffix}' is not valid unpadded URL-safe base64: {ex.Message}";
            return false;
        }

        if (bytes.Length != ByteLength)
        {
            error =
                $"'{text}': suffix '{suffix}' decodes to {bytes.Length} bytes; exactly " +
                $"{ByteLength} bytes (128 bits) are required.";
            return false;
        }

        error = null;
        id = new CanonicalId(prefix, suffix);
        return true;
    }

    /// <summary>Returns a defensive copy of the raw 16 identity bytes.</summary>
    public byte[] ToBytes()
    {
        if (!IsInitialized)
            throw new InvalidOperationException(UninitializedMessage);

        return Base64Url.Decode(Suffix);
    }

    /// <summary>
    /// Converts to a <see cref="Guid"/> using network (big-endian, textual-GUID) byte order:
    /// canonical byte <c>i</c> becomes hex-digit-pair <c>i</c> of the GUID's text form.
    /// Deliberately does not use <c>new Guid(byte[])</c>/<c>Guid.ToByteArray()</c>, whose
    /// historical mixed-endian layout would silently produce a different GUID.
    /// </summary>
    public Guid ToGuid()
    {
        var bytes = ToBytes();
        var hex = BytesToHex(bytes);
        return Guid.ParseExact(hex, "N");
    }

    /// <summary>
    /// Builds a canonical id whose 16 identity bytes are <paramref name="guid"/>'s network-order
    /// (textual-GUID) bytes: hex-digit-pair <c>i</c> of the GUID's text form becomes canonical
    /// byte <c>i</c>. The inverse of <see cref="ToGuid"/>.
    /// </summary>
    public static CanonicalId FromGuid(Guid guid, string prefix = "")
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));

        var hex = guid.ToString("N", CultureInfo.InvariantCulture);
        var bytes = HexToBytes(hex);
        return new CanonicalId(prefix, Base64Url.Encode(bytes));
    }

    /// <summary>
    /// Mints a unique, content-independent, time-ordered id: a 48-bit millisecond Unix timestamp
    /// (big-endian, most significant byte first) followed by 80 cryptographically random bits
    /// (ADR 0004 decision 2).
    /// </summary>
    public static CanonicalId Mint(string prefix = "")
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));

        var bytes = new byte[ByteLength];

        var millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 48-bit big-endian timestamp in bytes[0..5].
        bytes[0] = (byte)((millis >> 40) & 0xFF);
        bytes[1] = (byte)((millis >> 32) & 0xFF);
        bytes[2] = (byte)((millis >> 24) & 0xFF);
        bytes[3] = (byte)((millis >> 16) & 0xFF);
        bytes[4] = (byte)((millis >> 8) & 0xFF);
        bytes[5] = (byte)(millis & 0xFF);

        var random = new byte[10];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }
        Array.Copy(random, 0, bytes, 6, random.Length);

        return new CanonicalId(prefix, Base64Url.Encode(bytes));
    }

    private static string BytesToHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = NibbleToHex(b >> 4);
            chars[i * 2 + 1] = NibbleToHex(b & 0xF);
        }
        return new string(chars);
    }

    private static char NibbleToHex(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    /// <summary>
    /// Compares ids by the GUID they encode, ignoring the optional prefix (design rule 8). Equivalent to the
    /// type's own equality, kept so call sites that spell out the comparer stay valid.
    /// </summary>
    public static System.Collections.Generic.IEqualityComparer<CanonicalId> ByGuid { get; } = new GuidComparer();

    private sealed class GuidComparer : System.Collections.Generic.IEqualityComparer<CanonicalId>
    {
        public bool Equals(CanonicalId x, CanonicalId y) => x.Equals(y);

        public int GetHashCode(CanonicalId id) => id.GetHashCode();
    }

    /// <summary>
    /// Orders ids by byte-ordinal comparison of the UTF-8 encoding of <see cref="Value"/> (prefix included), per
    /// ADR 0007 decision 2. Required wherever order feeds a digest or canonical serialization.
    /// </summary>
    public static System.Collections.Generic.IComparer<CanonicalId> SpellingOrder { get; } =
        System.Collections.Generic.Comparer<CanonicalId>.Create((a, b) => Utf8Ordinal.Compare(a.Value, b.Value));

    /// <summary>True when both ids have exactly the same <see cref="Prefix"/> and <see cref="Suffix"/> text.</summary>
    public bool SpellingEquals(CanonicalId other) =>
        string.Equals(Prefix, other.Prefix, StringComparison.Ordinal) &&
        string.Equals(_suffix, other._suffix, StringComparison.Ordinal);

    /// <summary>True when both ids decode to the same 128 bits, whatever their prefix or suffix spelling.</summary>
    public bool Equals(CanonicalId other) =>
        IsInitialized == other.IsInitialized && _high == other._high && _low == other._low;

    public override bool Equals(object? obj) => obj is CanonicalId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsInitialized, _high, _low);

    public override string ToString() => IsInitialized ? Value : "(uninitialized CanonicalId)";

    public static bool operator ==(CanonicalId left, CanonicalId right) => left.Equals(right);
    public static bool operator !=(CanonicalId left, CanonicalId right) => !left.Equals(right);
}
