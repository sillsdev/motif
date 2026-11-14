using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Reports the homograph numbers a lexeme-form write changes. LibLCM itself renumbers the affected homograph set
/// when an entry gets or changes its lexeme form, exactly as FieldWorks does (<c>LexEntry.UpdateHomographs</c>), so
/// Motif never sets a number; this only reads the numbers before and after so the Dry Run can show them.
/// </summary>
internal sealed class HomographNumbering
{
    private readonly ILexEntry _entry;
    private readonly string? _newText;
    private readonly Dictionary<Guid, int> _before = [];

    private HomographNumbering(ILexEntry entry, string? newText)
    {
        _entry = entry;
        _newText = newText;
    }

    /// <summary>Records the numbers of the entry and of every entry that shares its form now or will share the new form.</summary>
    /// <param name="entry">The entry whose lexeme form is about to be written.</param>
    /// <param name="newText">The form being written, or null when the write only removes the form.</param>
    public static HomographNumbering Before(ILexEntry entry, string? newText)
    {
        var numbering = new HomographNumbering(entry, newText);
        numbering._before[entry.Guid] = entry.HomographNumber;
        foreach (var other in numbering.Candidates())
            numbering._before[other.Guid] = other.HomographNumber;
        return numbering;
    }

    private IEnumerable<ILexEntry> Candidates()
    {
        var repository = _entry.Cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        var keys = new[] { _entry.HomographFormKey, _newText }.Where(key => !string.IsNullOrEmpty(key)).Distinct();
        return keys.SelectMany(key => repository.GetHomographs(key!)).Where(other => other.IsValidObject);
    }

    /// <summary>The Dry Run preview of every number that changed, or null when none did.</summary>
    public JsonElement? After()
    {
        var changes = new List<(ILexEntry Entry, int Before, int After)>();
        var seen = new HashSet<Guid>();
        foreach (var other in Candidates().Prepend(_entry))
        {
            if (!seen.Add(other.Guid)) continue;
            var before = _before.TryGetValue(other.Guid, out var known) ? known : 0;
            if (before != other.HomographNumber) changes.Add((other, before, other.HomographNumber));
        }
        if (changes.Count == 0) return null;
        var ordered = changes.OrderBy(change => change.After == 0 ? int.MaxValue : change.After)
            .ThenBy(change => change.Entry.Guid).ToArray();
        var form = _entry.HomographFormKey;
        return JsonSerializer.SerializeToElement(new
        {
            homographNumbers = ordered.Select(change => new
            {
                entry = CanonicalId.FromGuid(change.Entry.Guid).Value,
                form,
                before = change.Before,
                after = change.After,
                isThisEntry = change.Entry.Guid == _entry.Guid,
            }),
            summary = "Homograph numbers as FieldWorks assigns them for '" + form + "': " + string.Join("; ",
                ordered.Select(change => (change.Entry.Guid == _entry.Guid ? "this entry" : "existing entry " +
                    CanonicalId.FromGuid(change.Entry.Guid).Value) + " " +
                    change.Before.ToString(CultureInfo.InvariantCulture) + " -> " +
                    change.After.ToString(CultureInfo.InvariantCulture))) + ".",
        });
    }
}
