using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

internal static class TrialCandidateParity
{
    private static readonly ConcurrentDictionary<string, byte> Covered = new(StringComparer.Ordinal);
    private static readonly object CoverageGate = new();
    private static readonly string CoverageRoot = Path.Combine(AppContext.BaseDirectory,
        "trial-candidate-parity-" + Guid.NewGuid().ToString("N"));

    internal static void Verify(LcmCache source, LcmCache candidate, Proposal proposal,
        SIL.Motif.Model.DryRun.DryRun dryRun, string root)
    {
        if (ProjectAppliedLog.TryFindByProposalId(source, proposal.ProposalId.ToGuid(), out _)) return;
        var kinds = proposal.Operations.Select(operation => operation.Kind).Distinct(StringComparer.Ordinal).ToArray();
        var ids = dryRun.ExpectedEffects.Select(effect => effect.CanonicalId.ToGuid())
            .Concat(proposal.Operations.SelectMany(operation => new[] { operation.Target, operation.EntityId })
                .Where(id => id is not null).Select(id => id!.Value.ToGuid())).ToHashSet();
        var loader = new FwDataProjectLoader();
        loader.Save(candidate);
        var baselineIds = ReadObjects(source.ProjectId.Path).Keys.ToHashSet();
        var authoredIds = proposal.Operations.Where(operation => operation.After is not null)
            .SelectMany(operation => AuthoredIds(operation.After!.Value)).ToHashSet();
        ids.UnionWith(authoredIds.Where(id => !baselineIds.Contains(id)));
        var stableIds = baselineIds.Concat(ids).Concat(authoredIds).ToHashSet();
        var expected = ReadObjects(candidate.ProjectId.Path);
        using var applied = new ScratchCacheFactory(loader).CreateFromFileCopy(source.ProjectId.Path,
            Path.Combine(root, "parity-apply"));
        ProposalApplier.Apply(applied, proposal, dryRun.Anchor, "trial-parity");
        loader.Save(applied);
        var actual = ReadObjects(applied.ProjectId.Path);
        var metadata = (IFwMetaDataCacheManaged)source.MetaDataCacheAccessor;
        foreach (var id in ids)
        {
            var candidateData = Normalize(id, expected, stableIds, [], metadata);
            var appliedData = Normalize(id, actual, stableIds, [], metadata);
            Assert.True(XNode.DeepEquals(candidateData, appliedData),
                $"Trial candidate and Apply differ on {id}: {string.Join(", ", kinds)}\n" +
                $"Candidate: {candidateData}\nApply: {appliedData}");
        }
        lock (CoverageGate)
        {
            Directory.CreateDirectory(CoverageRoot);
            File.AppendAllLines(Path.Combine(CoverageRoot, "kinds.txt"),
                kinds.Where(kind => Covered.TryAdd(kind, 0)));
        }
    }

    private static IEnumerable<Guid> AuthoredIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && CanonicalId.TryParse(value.GetString(), out var id))
            yield return id.ToGuid();
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray())
                foreach (var childId in AuthoredIds(child)) yield return childId;
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (CanonicalId.TryParse(property.Name, out var key)) yield return key.ToGuid();
                foreach (var childId in AuthoredIds(property.Value)) yield return childId;
            }
    }

    private static XElement? Normalize(Guid id, IReadOnlyDictionary<Guid, XElement> records,
        IReadOnlySet<Guid> stableIds, HashSet<Guid> visiting, IFwMetaDataCacheManaged metadata)
    {
        if (!records.TryGetValue(id, out var original)) return null;
        if (!visiting.Add(id)) return new XElement("cycle", new XAttribute("class", original.Attribute("class")!.Value));
        var record = new XElement(original);
        record.Elements("DateModified").Remove();
        record.Elements("DateCreated").Remove();
        if ((string?)record.Attribute("class") == "LexDb") record.Elements("Resources").Remove();
        if (!stableIds.Contains(id)) record.Attribute("guid")!.Remove();
        // LibLCM mints segment, bundle and evaluation ids per cache; compare their data and ownership instead.
        foreach (var reference in record.Descendants("objsur").ToArray())
        {
            var referencedId = Guid.Parse(reference.Attribute("guid")!.Value);
            if (stableIds.Contains(referencedId) || !records.ContainsKey(referencedId)) continue;
            reference.ReplaceWith(new XElement("implicit", new XAttribute("t", reference.Attribute("t")!.Value),
                Normalize(referencedId, records, stableIds, visiting, metadata)));
        }
        if (!stableIds.Contains(id) && Guid.TryParse((string?)record.Attribute("ownerguid"), out var ownerId) &&
            records.TryGetValue(ownerId, out var owner))
        {
            var owningField = owner.Elements().Single(field => field.Elements("objsur").Any(reference =>
                (string?)reference.Attribute("t") == "o" && Guid.Parse(reference.Attribute("guid")!.Value) == id));
            record.Add(new XAttribute("implicitOwningField", owningField.Name.LocalName));
            if (!stableIds.Contains(ownerId))
            {
                record.Attribute("ownerguid")!.Remove();
                record.Add(new XElement("implicitOwner", Normalize(ownerId, records, stableIds, visiting, metadata)));
            }
        }
        foreach (var field in record.Elements().Where(field => field.HasElements &&
                     field.Elements().All(child => child.Name == "objsur" || child.Name == "implicit")))
        {
            var name = (string?)field.Attribute("name") ?? field.Name.LocalName;
            var flid = metadata.GetFieldId2(metadata.GetClassId(record.Attribute("class")!.Value), name, true);
            if (flid == 0) continue;
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            if (type is CellarPropertyType.ReferenceCollection or CellarPropertyType.OwningCollection)
                field.ReplaceNodes(field.Elements().OrderBy(child => child.ToString(SaveOptions.DisableFormatting),
                    StringComparer.Ordinal).ToArray());
        }
        visiting.Remove(id);
        return record;
    }

    private static Dictionary<Guid, XElement> ReadObjects(string path)
    {
        var result = new Dictionary<Guid, XElement>();
        using var reader = XmlReader.Create(path, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });
        reader.MoveToContent();
        if (!reader.ReadToDescendant("rt")) return result;
        do
        {
            var record = (XElement)XNode.ReadFrom(reader);
            result[Guid.Parse(record.Attribute("guid")!.Value)] = record;
        } while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "rt");
        return result;
    }
}
