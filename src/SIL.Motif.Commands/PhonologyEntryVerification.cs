using System.Xml;
using SIL.Motif.Contract.Phonology;

namespace SIL.Motif.Commands;

/// <summary>Checks the FieldWorks entry and MSA GUIDs of saved examples against a Baseline's saved project file.</summary>
internal static class PhonologyEntryVerification
{
    internal const int MaxListed = 10;

    /// <summary>The example ids and GUIDs that name no entry, or an MSA the entry does not own.</summary>
    internal sealed record Missing(IReadOnlyList<string> Lines, int Total);

    /// <summary>Whether any example names a FieldWorks entry, the only context kind this check covers.</summary>
    internal static bool HasFieldWorksContext(IReadOnlyList<PhonologyExample> examples) =>
        examples.Any(example => example.Context is PhonologyGenerationContext.FwdataEntry);

    /// <summary>Reads the project file once, without LibLCM, and lists the examples whose GUIDs do not resolve.</summary>
    internal static Missing FindMissing(string fwDataPath, IReadOnlyList<PhonologyExample> examples)
    {
        var wanted = examples.Select(example => (example, fw: example.Context as PhonologyGenerationContext.FwdataEntry))
            .Where(pair => pair.fw is not null).ToArray();
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stemMsaOwners = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var neededEntries = wanted.Select(pair => pair.fw!.EntryGuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var neededMsas = wanted.Select(pair => pair.fw!.MsaGuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true };
        using (var reader = XmlReader.Create(fwDataPath, settings))
        {
            reader.MoveToContent();
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element) { reader.Read(); continue; }
                if (reader.Name == "rt")
                {
                    var guid = reader.GetAttribute("guid");
                    var rtClass = reader.GetAttribute("class");
                    if (guid is not null && rtClass == "LexEntry" && neededEntries.Contains(guid)) entries.Add(guid);
                    else if (guid is not null && rtClass == "MoStemMsa" && neededMsas.Contains(guid))
                        stemMsaOwners[guid] = reader.GetAttribute("ownerguid");
                }
                reader.Skip();
            }
        }

        var lines = new List<string>();
        var total = 0;
        foreach (var (example, fw) in wanted)
        {
            var entryMissing = !entries.Contains(fw!.EntryGuid);
            var msaMissing = !stemMsaOwners.TryGetValue(fw.MsaGuid, out var owner) ||
                !string.Equals(owner, fw.EntryGuid, StringComparison.OrdinalIgnoreCase);
            if (!entryMissing && !msaMissing) continue;
            total++;
            if (lines.Count < MaxListed)
                lines.Add($"{example.Id} (" + (entryMissing ? $"entry {fw.EntryGuid} not found" : "") +
                    (entryMissing && msaMissing ? "; " : "") +
                    (msaMissing ? $"stem MSA {fw.MsaGuid} not found on that entry" : "") + ")");
        }
        return new Missing(lines, total);
    }
}
