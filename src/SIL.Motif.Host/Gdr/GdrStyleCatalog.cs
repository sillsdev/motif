using SIL.Motif.Contract.Responses;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.Gdr;

/// <summary>
/// Detached resolved styles for repeated reads of one immutable project state. Never share a catalog between
/// Baselines or with a mutable project. No LibLCM objects are retained; calls are serialized within the catalog.
/// </summary>
public sealed class GdrStyleCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<GdrStyle>> _styles = new(StringComparer.Ordinal);

    internal int ResolutionPasses { get; private set; }

    internal IReadOnlyList<GdrStyle> Read(ProjectStyles styles, SortedDictionary<string, GdrStyleKind> kinds,
        SortedSet<string> tags)
    {
        lock (_gate)
        {
            var key = string.Join('\0', tags);
            if (_styles.TryGetValue(key, out var cached)) return cached;
            var resolved = GdrReader.ReadStyles(styles, kinds, tags);
            ResolutionPasses++;
            _styles.Add(key, resolved);
            return resolved;
        }
    }
}
