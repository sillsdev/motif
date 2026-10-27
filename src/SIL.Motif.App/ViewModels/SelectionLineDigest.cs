using System.Collections;
using System.Reflection;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A structural 64-bit digest of the immutable records a reader line is built from, so two reads of one line can be
/// compared without keeping the older read. Equal content gives an equal digest; any displayed difference changes it.
/// </summary>
internal static class SelectionLineDigest
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private static readonly Dictionary<Type, PropertyInfo[]> Properties = [];

    /// <summary>Folds each value, in order, into one digest.</summary>
    public static ulong Of(params object?[] values)
    {
        var hash = Offset;
        foreach (var value in values) Add(ref hash, value, 0);
        return hash;
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= Prime;
    }

    private static void Add(ref ulong hash, object? value, int depth)
    {
        if (value is null) { Mix(ref hash, 0x9E37); return; }
        if (depth > 24) return;
        switch (value)
        {
            case Delegate or Type or MemberInfo: return;
            case string text:
                foreach (var character in text) Mix(ref hash, character);
                Mix(ref hash, 0xA5);
                return;
            case IDictionary dictionary:
                Mix(ref hash, 0xD1);
                // Dictionary order is not part of the content, so entries are folded in key order.
                var entries = new List<(ulong Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = Offset;
                    Add(ref key, entry.Key, depth + 1);
                    entries.Add((key, entry.Value));
                }
                foreach (var entry in entries.OrderBy(entry => entry.Key))
                {
                    Mix(ref hash, entry.Key);
                    Add(ref hash, entry.Value, depth + 1);
                }
                return;
            case IEnumerable sequence:
                Mix(ref hash, 0xE2);
                foreach (var item in sequence) Add(ref hash, item, depth + 1);
                Mix(ref hash, 0xE3);
                return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is Guid or decimal or DateTimeOffset or DateTime or TimeSpan)
        {
            foreach (var character in Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                Mix(ref hash, character);
            Mix(ref hash, 0xA6);
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            Add(ref hash, type.GetProperty("Key")!.GetValue(value), depth + 1);
            Add(ref hash, type.GetProperty("Value")!.GetValue(value), depth + 1);
            return;
        }
        PropertyInfo[] properties;
        lock (Properties)
        {
            if (!Properties.TryGetValue(type, out properties!))
                Properties[type] = properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null &&
                        property.Name != "EqualityContract").OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
        }
        Mix(ref hash, (ulong)properties.Length);
        foreach (var property in properties) Add(ref hash, property.GetValue(value), depth + 1);
    }
}
