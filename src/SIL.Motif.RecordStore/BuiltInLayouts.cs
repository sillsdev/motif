using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.RecordStore;

/// <summary>
/// The frozen field-type contract for built-in native fields, generated from pinned LCM metadata
/// (BuiltInLayouts.g.cs). Layouts name this table's version instead of copying its types into every record.
/// </summary>
internal static partial class BuiltInLayouts
{
    private static readonly Lazy<FrozenDictionary<int, int>> Types = new(() => Fields.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split(':')).ToFrozenDictionary(
            pair => int.Parse(pair[0], CultureInfo.InvariantCulture), pair => int.Parse(pair[1], CultureInfo.InvariantCulture)));

    /// <summary>Cellar property types that a stored field may declare.</summary>
    public static readonly FrozenSet<int> KnownTypes = new[] { 1, 2, 5, 6, 8, 9, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 }.ToFrozenSet();

    public static int? TypeOf(int field) => Types.Value.TryGetValue(field, out var type) ? type : null;

    public static int Count => Types.Value.Count;

    /// <summary>SHA-256 of the table text; a test pins it so the contract cannot drift unnoticed.</summary>
    public static string TableDigest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Fields)));
}
