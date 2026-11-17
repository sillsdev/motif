using System;
using System.Collections.Generic;
using System.Globalization;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.CommandLine;

internal sealed class PictureMapper : IObjectMapper
{
    readonly Dictionary<(Guid?, string?, string?), string> _data = new();
    readonly Dictionary<string, MarkdownObject> _pictures = new(StringComparer.Ordinal);
    readonly string _prefix = "\0fwmd-picture-" + Guid.NewGuid().ToString("N") + "-";

    public MarkdownObject? Describe(string objData) => _pictures.TryGetValue(objData, out var picture)
        ? picture : DefaultObjectMapper.Instance.Describe(objData);

    public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
    {
        if (target.Kind != MarkdownObjectKind.Picture)
            return DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out problem);
        var key = (target.Guid, target.Path, target.Caption);
        if (!_data.TryGetValue(key, out var stored))
        {
            stored = _prefix + _data.Count.ToString(CultureInfo.InvariantCulture);
            _data.Add(key, stored);
            _pictures.Add(stored, target);
        }
        objData = stored;
        problem = null;
        return true;
    }
}
