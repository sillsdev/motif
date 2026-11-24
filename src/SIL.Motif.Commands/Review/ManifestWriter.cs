using System.Text;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>Writes the retired and added expected-change manifests from a Reading delta, in the lane format.</summary>
internal static class ManifestWriter
{
    private static readonly string[] Levels = ["entryId", "msaId", "allomorphId"];

    /// <summary>
    /// Writes both files, replacing earlier ones, and summarises each. Known non-words that stopped parsing get no row:
    /// that is the intended result and needs no waiver. Every other measured form with a change gets its full delta.
    /// </summary>
    internal static IReadOnlyList<ReadingManifestSummary> Write(string directory, IReadOnlyList<ReadingDeltaForm> forms)
    {
        Directory.CreateDirectory(directory);
        var included = forms.Where(form => !form.Classes.Contains(ReadingDeltaClasses.Rejected)).ToArray();
        return
        [
            One(directory, ReadingManifestFormat.RetiredFile, "retired", included, form => form.Retired),
            One(directory, ReadingManifestFormat.AddedFile, "added", included, form => form.Added),
        ];
    }

    private static ReadingManifestSummary One(string directory, string file, string kind, IEnumerable<ReadingDeltaForm> forms,
        Func<ReadingDeltaForm, IReadOnlyList<(SIL.Motif.Contract.HandAnalysis.ReadingSignature Signature, long Count)>> pick)
    {
        var text = new StringBuilder();
        long rows = 0, readings = 0;
        var groups = Levels.ToDictionary(level => level,
            _ => new Dictionary<string, (long Readings, HashSet<string> Words)>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var form in forms)
            foreach (var (signature, count) in pick(form))
            {
                text.Append(ReadingManifestFormat.Row(form.Word, signature, count)).Append('\n');
                rows++;
                readings += count;
                foreach (var morpheme in signature.Morphemes)
                    foreach (var (level, id) in new[] { (Levels[0], morpheme.EntryId), (Levels[1], morpheme.MsaId), (Levels[2], morpheme.AllomorphId) })
                    {
                        var group = groups[level];
                        group.TryGetValue(id!, out var found);
                        found.Words ??= new HashSet<string>(StringComparer.Ordinal);
                        found.Words.Add(form.Word);
                        group[id!] = (found.Readings + count, found.Words);
                    }
            }
        var path = Path.GetFullPath(Path.Combine(directory, file));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var byObject = groups.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ReadingDeltaObject>)
            [.. pair.Value.OrderByDescending(item => item.Value.Readings).ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new ReadingDeltaObject(item.Key, item.Value.Readings, item.Value.Words.Count))],
            StringComparer.Ordinal);
        return new ReadingManifestSummary(kind, path, rows, readings, byObject);
    }
}
