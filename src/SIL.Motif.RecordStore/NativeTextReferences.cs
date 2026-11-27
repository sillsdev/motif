using System.Globalization;
using System.Text.Json;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.RecordStore;

internal static class NativeTextReferences
{
    public static void Add(GenericRecord source, string key, int type, JsonElement value, string? project,
        List<NativeReference> edges, HashSet<string> missing)
    {
        var field = int.Parse(key, CultureInfo.InvariantCulture);
        if (type == 9)
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("textProps", out var props))
                Properties(props, "props");
        }
        else if (type is 14 or 16)
        {
            if (value.ValueKind != JsonValueKind.Object) { missing.Add("Native multilingual text is malformed."); return; }
            foreach (var alternative in value.EnumerateObject()) Text(alternative.Value, "ws:" + Uri.EscapeDataString(alternative.Name) + "/");
        }
        else Text(value, "");

        void Text(JsonElement text, string prefix)
        {
            if (text.ValueKind == JsonValueKind.Null) return;
            if (text.ValueKind != JsonValueKind.Object || !text.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            { missing.Add("Native rich text is malformed."); return; }
            var ordinal = 0;
            foreach (var run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("props", out var props))
                    missing.Add("Native run properties are unavailable.");
                else Properties(props, prefix + "run:" + (ordinal++).ToString(CultureInfo.InvariantCulture));
            }
        }

        void Properties(JsonElement props, string occurrence)
        {
            if (props.ValueKind == JsonValueKind.Null) return;
            if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("strings", out var strings) || strings.ValueKind != JsonValueKind.Array)
            { missing.Add("Native text properties are malformed."); return; }
            foreach (var property in strings.EnumerateArray())
            {
                if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out var id) ||
                    id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number))
                { missing.Add("Native property identity is unavailable."); continue; }
                if (number != 6) continue;
                if (!property.TryGetProperty("value", out var raw) || !TryRaw(raw, out var data))
                { missing.Add("Native object data is malformed."); continue; }
                if (string.IsNullOrEmpty(data)) continue;
                Guid? target = null;
                switch ((int)data[0])
                {
                    case 3 or 5 or 7 or 8:
                        if (data.Length < 9) { missing.Add("Native GUID object data is truncated."); break; }
                        var bytes = data.Skip(1).Take(8).SelectMany(c => new[] { (byte)c, (byte)(c >> 8) }).ToArray();
                        // FieldWorks object data uses Guid's little-endian fields, unlike the store's GUID columns.
                        target = new Guid(bytes.AsSpan(), bigEndian: false);
                        break;
                    case 4:
                        var url = data[1..];
                        if (!url.StartsWith("silfw://", StringComparison.OrdinalIgnoreCase)) break;
                        if (!SilfwUrl.TryParse(url, out var link) || !link.Guid.HasValue)
                        { missing.Add("Native project link cannot be resolved."); break; }
                        if (link.Database is not null && link.Database != SilfwUrl.CurrentProject && link.Database != project)
                        {
                            if (project is null) missing.Add("Native project-link scope is unavailable.");
                            break;
                        }
                        target = link.Guid.Value;
                        break;
                    default: missing.Add("Native object-data reference encoding is unsupported."); break;
                }
                if (target.HasValue) edges.Add(new NativeReference(source.Id, field, -1, target.Value, false, occurrence));
            }
        }
    }

    internal static bool TryRaw(JsonElement value, out string? text)
    {
        text = null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("utf16le", out var raw)) return false;
        if (raw.ValueKind == JsonValueKind.Null) return true;
        if (raw.ValueKind != JsonValueKind.String) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(raw.GetString()!); }
        catch (FormatException) { return false; }
        if (bytes.Length % 2 != 0) return false;
        text = new string(Enumerable.Range(0, bytes.Length / 2).Select(i => (char)(bytes[i * 2] | bytes[i * 2 + 1] << 8)).ToArray());
        return true;
    }
}
