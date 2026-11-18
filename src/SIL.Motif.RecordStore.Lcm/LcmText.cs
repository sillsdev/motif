using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.Motif.RecordStore.Lcm;

internal static class LcmText
{
    public static object Raw(string? value) => new
    {
        text = value,
        utf16le = value is null ? null : Base64Utf16(value)
    };

    private static string Base64Utf16(string value)
    {
        if (BitConverter.IsLittleEndian) return Convert.ToBase64String(MemoryMarshal.AsBytes(value.AsSpan()));
        var bytes = new byte[checked(value.Length * 2)];
        for (var index = 0; index < value.Length; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), value[index]);
        return Convert.ToBase64String(bytes);
    }

    public static string? RestoreRaw(JsonElement value)
    {
        var data = value.GetProperty("utf16le");
        if (data.ValueKind == JsonValueKind.Null) return null;
        var bytes = Convert.FromBase64String(data.GetString()!);
        if (bytes.Length % 2 != 0) throw new InvalidDataException("Odd UTF-16 byte length.");
        return new string(Enumerable.Range(0, bytes.Length / 2).Select(i => (char)(bytes[2 * i] | bytes[2 * i + 1] << 8)).ToArray());
    }

    public static object? Text(LcmCache cache, ITsString? text) => text is null ? null : new
    {
        text = Raw(text.Text),
        runs = Enumerable.Range(0, text.RunCount).Select(index => new
        {
            start = text.get_MinOfRun(index), end = text.get_LimOfRun(index),
            text = Raw(text.get_RunText(index)), props = Properties(cache, text.get_Properties(index))
        }).ToArray()
    };

    public static object? Properties(LcmCache cache, ITsTextProps? properties)
    {
        if (properties is null) return null;
        var integers = new List<object>();
        var strings = new List<object>();
        for (var index = 0; index < properties.IntPropCount; index++)
        {
            var number = properties.GetIntProp(index, out var type, out var variation);
            object value = type is (int)FwTextPropType.ktptWs or (int)FwTextPropType.ktptBaseWs
                ? number > 0 ? cache.ServiceLocator.WritingSystemManager.Get(number).Id : number.ToString(CultureInfo.InvariantCulture)
                : number;
            integers.Add(new { type, variation, value });
        }
        for (var index = 0; index < properties.StrPropCount; index++)
        {
            var value = properties.GetStrProp(index, out var type);
            strings.Add(new { type, value = type == (int)FwTextPropType.ktptWsStyle ? PackedStyle(cache, value) : Raw(value) });
        }
        return new { ints = integers, strings };
    }

    private static string Pair(int value) => new([(char)(value & 65535), (char)((uint)value >> 16)]);

    private static object PackedStyle(LcmCache cache, string raw)
    {
        var decoder = typeof(TsPropsSerializer).GetMethod("DecodeWSStyles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var decoded = (IDictionary)decoder.Invoke(null, [raw])!;
        var positions = new List<(int offset, string tag)>();
        foreach (int handle in decoded.Keys)
        {
            var offset = raw.IndexOf(Pair(handle), StringComparison.Ordinal);
            if (offset < 0 || raw.IndexOf(Pair(handle), offset + 1, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException("Ambiguous packed writing-system handle.");
            positions.Add((offset, cache.ServiceLocator.WritingSystemManager.Get(handle).Id));
        }
        var tags = positions.Select(p => p.tag).Order(StringComparer.Ordinal).ToArray();
        var characters = raw.ToCharArray();
        foreach (var position in positions)
        {
            var pair = Pair(Array.IndexOf(tags, position.tag) + 1);
            characters[position.offset] = pair[0];
            characters[position.offset + 1] = pair[1];
        }
        var normalized = new string(characters);
        return new
        {
            text = normalized,
            utf16le = Base64Utf16(normalized),
            writingSystems = positions.OrderBy(p => p.offset).Select(p => new { p.offset, p.tag }).ToArray()
        };
    }

    public static ITsTextProps? RestoreProperties(LcmCache cache, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var builder = new TsPropsBldr();
        foreach (var property in value.GetProperty("ints").EnumerateArray())
        {
            var stored = property.GetProperty("value");
            var number = stored.ValueKind == JsonValueKind.String
                ? int.TryParse(stored.GetString(), out var special) ? special : cache.ServiceLocator.WritingSystemManager.Get(stored.GetString()).Handle
                : stored.GetInt32();
            builder.SetIntPropValues(property.GetProperty("type").GetInt32(), property.GetProperty("variation").GetInt32(), number);
        }
        foreach (var property in value.GetProperty("strings").EnumerateArray())
        {
            var stored = property.GetProperty("value");
            var raw = RestoreRaw(stored);
            if (raw is not null && stored.TryGetProperty("writingSystems", out var systems))
            {
                var characters = raw.ToCharArray();
                foreach (var system in systems.EnumerateArray())
                {
                    cache.ServiceLocator.WritingSystemManager.GetOrSet(system.GetProperty("tag").GetString(), out var definition);
                    var offset = system.GetProperty("offset").GetInt32();
                    var pair = Pair(definition.Handle);
                    characters[offset] = pair[0];
                    characters[offset + 1] = pair[1];
                }
                raw = new string(characters);
            }
            builder.SetStrPropValue(property.GetProperty("type").GetInt32(), raw);
        }
        return builder.GetTextProps();
    }

    public static ITsString? RestoreText(LcmCache cache, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var builder = new TsStrBldr();
        foreach (var run in value.GetProperty("runs").EnumerateArray())
            builder.Replace(builder.Length, builder.Length, RestoreRaw(run.GetProperty("text")), RestoreProperties(cache, run.GetProperty("props")));
        return builder.GetString();
    }
}
