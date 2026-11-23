using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SIL.Motif.RecordStore;

/// <summary>
/// Order-independent digest of a multiset of byte items: the sum of their SHA-256 hashes modulo 2^256, plus a count.
/// Adding or removing one item is O(1), so a one-record edit can update a published digest without rescanning the store.
/// It detects corruption and divergence; it is not a keyed or adversary-proof authenticator.
/// </summary>
internal readonly record struct SetDigest(UInt128 Low, UInt128 High, long Count)
{
    public SetDigest Add(byte[] item) => Add(item.AsSpan());

    public SetDigest Add(ReadOnlySpan<byte> item)
    {
        var (low, high) = Hash(item);
        var sum = Low + low;
        return new(sum, High + high + (sum < Low ? UInt128.One : UInt128.Zero), Count + 1);
    }

    public SetDigest Remove(byte[] item) => Remove(item.AsSpan());

    public SetDigest Remove(ReadOnlySpan<byte> item)
    {
        var (low, high) = Hash(item);
        return new(Low - low, High - high - (Low < low ? UInt128.One : UInt128.Zero), Count - 1);
    }

    /// <summary>The digest of the union of two multisets: partial digests of disjoint parts add up exactly.</summary>
    public SetDigest Combine(SetDigest other)
    {
        var sum = Low + other.Low;
        return new(sum, High + other.High + (sum < Low ? UInt128.One : UInt128.Zero), Count + other.Count);
    }

    public string Final(string domain)
    {
        var bytes = new byte[32 + 8];
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(0, 16), Low);
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(16, 16), High);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32, 8), Count);
        return Convert.ToHexStringLower(SHA256.HashData([..System.Text.Encoding.UTF8.GetBytes(domain), 0, ..bytes]));
    }

    /// <summary>The exact accumulator as text, so a stored digest can be extended later; <see cref="Final"/> is one way.</summary>
    public string ToText() => $"{Low:x32}:{High:x32}:{Count}";

    public static bool TryParse(string? text, out SetDigest digest)
    {
        digest = default;
        var parts = text?.Split(':');
        if (parts is not { Length: 3 } || parts[0].Length != 32 || parts[1].Length != 32) return false;
        if (!UInt128.TryParse(parts[0], System.Globalization.NumberStyles.AllowHexSpecifier, null, out var low) ||
            !UInt128.TryParse(parts[1], System.Globalization.NumberStyles.AllowHexSpecifier, null, out var high) ||
            !long.TryParse(parts[2], System.Globalization.NumberStyles.None, null, out var count)) return false;
        digest = new(low, high, count);
        return true;
    }

    private static (UInt128, UInt128) Hash(ReadOnlySpan<byte> item)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(item, hash);
        return (BinaryPrimitives.ReadUInt128LittleEndian(hash[..16]), BinaryPrimitives.ReadUInt128LittleEndian(hash[16..]));
    }
}
