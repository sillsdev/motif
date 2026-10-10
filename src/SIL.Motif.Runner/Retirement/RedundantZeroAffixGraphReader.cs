using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Retirement;

/// <summary>Reads and validates the narrowly supported graph for redundant zero-affix retirement.</summary>
public static class RedundantZeroAffixGraphReader
{
    private static readonly HashSet<string> LoadedZeroMarkers = ["^0", "*0", "&0", "∅"];

    /// <summary>Returns the current owning graph, optional slots, and a digest of its deletion-relevant state.</summary>
    public static RedundantZeroAffixGraph Read(LcmCache cache, CanonicalId entryId)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(entryId.ToGuid(), out var value) || value is not ILexEntry entry)
            throw new InvalidOperationException("Zero-affix retirement requires a live lexical entry.");

        var members = repository.AllInstances().Where(item => item.Guid == entry.Guid || IsOwnedBy(item, entry.Guid))
            .OrderBy(item => item.Guid).ToArray();
        var forms = ValidateShape(cache, entry, members);
        var slots = entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Single().SlotsRC
            .Select(item => new RedundantZeroAffixSlot(CanonicalId.FromGuid(item.Guid), item.Optional))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        if (slots.Any(item => !item.Optional))
            throw new InvalidOperationException("A required affix slot makes this zero morphology meaningful.");
        var references = ReadReferences(cache, members);
        var msaIds = entry.MorphoSyntaxAnalysesOC.Select(item => item.Guid).ToHashSet();
        if (references.Any(item => msaIds.Contains(item.Source.ToGuid()) &&
                (item.Field.Contains("InflectionClass", StringComparison.OrdinalIgnoreCase) ||
                 item.Field.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                 item.Field.Contains("ProdRestrict", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Inflection-class or exception contributions make this zero morphology meaningful.");
        var senseIds = entry.SensesOS.Select(item => item.Guid).ToHashSet();
        var externalSenseReference = references.FirstOrDefault(item => senseIds.Contains(item.Source.ToGuid()) &&
            !members.Any(member => member.Guid == item.Target.ToGuid()));
        if (externalSenseReference is not null && externalSenseReference.Field is not ("PublishIn" or "DoNotPublishIn"))
            throw new InvalidOperationException($"A sense has an external {externalSenseReference.Field} reference to '{externalSenseReference.Target}'.");
        var external = references.Where(item => !members.Any(member => member.Guid == item.Source.ToGuid()))
            .Where(item => !IsOptionalSlotInverse(item, members, slots))
            .ToArray();
        if (external.Length > 0)
        {
            var first = external[0];
            throw new InvalidOperationException($"Object '{first.Target}' has an incoming {first.SourceClass}.{first.Field} reference from outside the entry graph.");
        }

        var memberRows = members.Select(item => new RedundantZeroAffixGraphMember(CanonicalId.FromGuid(item.Guid), item.ClassName,
            item.Owner is null ? null : CanonicalId.FromGuid(item.Owner.Guid))).ToArray();
        var edgeRows = references.Where(item => !(item.Source == CanonicalId.FromGuid(
                entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Single().Guid) && item.Field == "Slots") &&
            !IsOptionalSlotInverse(item, members, slots))
            .OrderBy(item => item.Source.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Field, StringComparer.Ordinal)
            .ThenBy(item => item.Ordinal)
            .ThenBy(item => item.Target.Value, StringComparer.Ordinal).ToArray();
        var primary = entry.LexemeFormOA!;
        var digestPayload = JsonSerializer.Serialize(new
        {
            entry = entryId.Value,
            members = memberRows,
            edges = edgeRows,
            forms,
            morphType = primary.MorphTypeRA?.Guid,
        });
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestPayload)))
            .ToLowerInvariant();
        return new RedundantZeroAffixGraph(entryId, digest, Array.AsReadOnly(memberRows),
            Array.AsReadOnly(slots), Array.AsReadOnly(forms.Select(item => item.WritingSystem).Distinct(StringComparer.Ordinal).ToArray()));
    }

    /// <summary>Checks that every object in the graph has been removed after the owning delete.</summary>
    public static void VerifyDeleted(LcmCache cache, RedundantZeroAffixGraph graph)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var survivors = graph.Members.Where(item => repository.TryGetObject(item.Id.ToGuid(), out _)).ToArray();
        if (survivors.Length > 0)
            throw new InvalidOperationException("LibLCM left one or more owned objects after zero-affix graph deletion.");
    }

    private static GraphForm[] ValidateShape(LcmCache cache, ILexEntry entry, IReadOnlyList<ICmObject> members)
    {
        if (entry.LexemeFormOA is not IMoAffixAllomorph form || form.IsAbstract || entry.AlternateFormsOS.Count != 0)
            throw new InvalidOperationException("Only a primary, ordinary prefix or suffix form with no alternates can be retired.");
        var morphType = form.MorphTypeRA?.Guid;
        if (morphType != MoMorphTypeTags.kguidMorphPrefix && morphType != MoMorphTypeTags.kguidMorphSuffix)
            throw new InvalidOperationException("Only a primary, ordinary prefix or suffix form with no alternates can be retired.");

        var analyses = entry.MorphoSyntaxAnalysesOC.ToArray();
        if (analyses.Length != 1 || analyses[0] is not IMoInflAffMsa msa)
            throw new InvalidOperationException("The entry must contain exactly one inflectional affix analysis.");
        var senses = entry.SensesOS.ToArray();
        if (senses.Length != 1 || senses[0].SensesOS.Count != 0 || HasAuthoredSenseMeaning(senses[0]))
            throw new InvalidOperationException("The entry must have one empty, non-nested sense with no authored gloss or definition.");
        if (msa.InflFeatsOA is not null || msa.FromProdRestrictRC.Count != 0 ||
            form.InflectionClassesRC.Count != 0 || form.MsEnvFeaturesOA is not null ||
            form.PhoneEnvRC.Count != 0 || form.PositionRS.Count != 0 ||
            members.Any(item => item.ClassName.StartsWith("Fs", StringComparison.Ordinal)))
            throw new InvalidOperationException("The affix contributes features, inflection classes, exception features, or conditions.");
        if (members.Any(item => item is not ILexEntry and not ILexSense and not IMoInflAffMsa and not IMoAffixAllomorph))
            throw new InvalidOperationException("The entry graph owns a dependent object outside the supported zero-affix scope.");

        var loadedForms = form.Form.AvailableWritingSystemIds
            .Select(ws => new GraphForm(cache.WritingSystemFactory.GetStrFromWs(ws),
                form.Form.get_String(ws)?.get_NormalizedForm(FwNormalizationMode.knmNFD)?.Text ?? string.Empty))
            .OrderBy(item => item.WritingSystem, StringComparer.Ordinal)
            .ToArray();
        var forms = ReadAuthoredForms(cache, form).OrderBy(item => item.WritingSystem, StringComparer.Ordinal).ToArray();
        if (forms.Length == 0 || loadedForms.Length == 0 || !forms.SequenceEqual(loadedForms) ||
            forms.Any(item => !LoadedZeroMarkers.Contains(item.Text)))
            throw new InvalidOperationException("The primary form must be a saved whole null marker that loads as zero in every writing system.");
        if (members.Count(item => item is IMoAffixAllomorph) != 1)
            throw new InvalidOperationException("The entry graph must contain exactly one ordinary affix realization.");
        if (HasExceptionContribution(cache, msa))
            throw new InvalidOperationException("Exception features make this zero morphology meaningful.");
        return forms;
    }

    private static bool HasAuthoredSenseMeaning(ILexSense sense) =>
        HasAuthoredText(sense.Gloss) || HasAuthoredText(sense.Definition);

    private static bool HasAuthoredText(IMultiAccessorBase value) => value.AvailableWritingSystemIds
        .Any(ws => !string.IsNullOrEmpty(value.get_String(ws)?.Text));

    private static GraphForm[] ReadAuthoredForms(LcmCache cache, IMoAffixAllomorph form)
    {
        var path = cache.ProjectId.Path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("Zero-affix retirement requires the saved project file to verify its authored form.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        var document = XDocument.Load(reader);
        var record = document.Descendants("rt").SingleOrDefault(item =>
            Guid.TryParse((string?)item.Attribute("guid"), out var id) && id == form.Guid);
        var alternatives = record?.Element("Form")?.Elements("AUni").ToArray() ?? [];
        return alternatives.Select(item => new GraphForm((string?)item.Attribute("ws") ?? string.Empty,
            item.Value.Normalize(NormalizationForm.FormD))).ToArray();
    }

    private static bool HasExceptionContribution(LcmCache cache, IMoInflAffMsa msa)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        foreach (var flid in metadata.GetFieldIds().Where(id =>
                     IsSameOrSubclass(metadata, msa.ClassID, metadata.GetOwnClsId(id)) &&
                     metadata.GetFieldName(id).Contains("Exception", StringComparison.OrdinalIgnoreCase)))
        {
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            if (!TryShape(type, out _, out _)) continue;
            var count = type is CellarPropertyType.OwningAtomic or CellarPropertyType.OwningAtom or
                CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceAtom
                ? cache.DomainDataByFlid.get_ObjectProp(msa.Hvo, flid) == 0 ? 0 : 1
                : cache.DomainDataByFlid.get_VecSize(msa.Hvo, flid);
            if (count > 0) return true;
        }
        return false;
    }

    private static IReadOnlyList<GraphEdge> ReadReferences(LcmCache cache, IReadOnlyList<ICmObject> members)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().ToArray();
        var objectsByHvo = objects.ToDictionary(item => item.Hvo);
        var targets = members.ToDictionary(item => item.Hvo);
        var membersByHvo = members.ToDictionary(item => item.Hvo);
        var classIds = metadata.GetClassIds().ToDictionary(metadata.GetClassName);
        var result = new List<GraphEdge>();
        foreach (var flid in metadata.GetFieldIds())
        {
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            if (!TryShape(type, out _, out var atomic) || type is CellarPropertyType.OwningAtomic or
                CellarPropertyType.OwningAtom or CellarPropertyType.OwningCollection or CellarPropertyType.OwningSequence) continue;
            var declaringClass = metadata.GetOwnClsId(flid);
            var destinationName = metadata.GetDstClsName(flid);
            if (!classIds.TryGetValue(destinationName, out var destinationClass)) continue;
            var acceptsTarget = members.Any(item => IsSameOrSubclass(metadata, item.ClassID, destinationClass));
            var name = metadata.GetFieldName(flid);
            var declaringName = metadata.GetOwnClsName(flid);
            var sources = objects.Where(item => IsSameOrSubclass(metadata, item.ClassID, declaringClass) &&
                (membersByHvo.ContainsKey(item.Hvo) || acceptsTarget));
            foreach (var source in sources)
            {
                try
                {
                    if (atomic)
                    {
                        var target = cache.DomainDataByFlid.get_ObjectProp(source.Hvo, flid);
                        AddEdge(target, null);
                        continue;
                    }
                    var count = cache.DomainDataByFlid.get_VecSize(source.Hvo, flid);
                    for (var index = 0; index < count; index++)
                    {
                        var target = cache.DomainDataByFlid.get_VecItem(source.Hvo, flid, index);
                        AddEdge(target, type == CellarPropertyType.ReferenceSequence ? index : null);
                    }

                    void AddEdge(int targetHvo, int? ordinal = null)
                    {
                        if ((IsDerivedAllomorphCollection(declaringName, name) ||
                             IsDerivedLexDbAggregate(declaringName, name)) && !membersByHvo.ContainsKey(source.Hvo)) return;
                        if (membersByHvo.ContainsKey(source.Hvo) && objectsByHvo.TryGetValue(targetHvo, out var resolvedTarget))
                            result.Add(new GraphEdge(CanonicalId.FromGuid(source.Guid), source.ClassName,
                                CanonicalId.FromGuid(resolvedTarget.Guid), name, ordinal));
                        else if (targets.TryGetValue(targetHvo, out resolvedTarget))
                            result.Add(new GraphEdge(CanonicalId.FromGuid(source.Guid), source.ClassName,
                                CanonicalId.FromGuid(resolvedTarget.Guid), name, ordinal));
                    }
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"Could not inspect {metadata.GetOwnClsName(flid)}.{name} while checking incoming references.", error);
                }
            }
        }
        return Array.AsReadOnly(result.Distinct().ToArray());
    }

    private static bool TryShape(CellarPropertyType type, out bool owning, out bool atomic)
    {
        owning = type is CellarPropertyType.OwningAtomic or CellarPropertyType.OwningAtom or
            CellarPropertyType.OwningCollection or CellarPropertyType.OwningSequence;
        atomic = type is CellarPropertyType.OwningAtomic or CellarPropertyType.OwningAtom or
            CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceAtom;
        return owning || type is CellarPropertyType.ReferenceAtomic or CellarPropertyType.ReferenceAtom or
            CellarPropertyType.ReferenceCollection or CellarPropertyType.ReferenceSequence;
    }

    private static bool IsSameOrSubclass(IFwMetaDataCacheManaged metadata, int classId, int expectedBase)
    {
        for (var current = classId; current > 0; current = metadata.GetBaseClsId(current))
            if (current == expectedBase) return true;
        return false;
    }

    private static bool IsOwnedBy(ICmObject item, Guid owner)
    {
        for (var current = item.Owner; current is not null; current = current.Owner)
            if (current.Guid == owner) return true;
        return false;
    }

    private sealed record GraphEdge(CanonicalId Source, string SourceClass, CanonicalId Target, string Field, int? Ordinal);
    private sealed record GraphForm(string WritingSystem, string Text);

    private static bool IsDerivedAllomorphCollection(string declaringClass, string field) =>
        (declaringClass is "LexEntry" or "LexDb") && (field is "AllAllomorphs" or "AllPossibleAllomorphs");

    private static bool IsDerivedLexDbAggregate(string declaringClass, string field) =>
        declaringClass == "LexDb" && (field == "Entries" || field.StartsWith("All", StringComparison.Ordinal));

    private static bool IsOptionalSlotInverse(GraphEdge edge, IReadOnlyList<ICmObject> members,
        IReadOnlyList<RedundantZeroAffixSlot> slots) => edge.SourceClass == "MoInflAffixSlot" &&
        edge.Field == "Affixes" && slots.Any(slot => slot.Optional && slot.Id.ToGuid() == edge.Source.ToGuid()) &&
        members.Any(member => member.Guid == edge.Target.ToGuid());
}

/// <summary>The exact live graph that the scoped owning-delete operation will remove.</summary>
public sealed record RedundantZeroAffixGraph(CanonicalId Entry, string Digest,
    IReadOnlyList<RedundantZeroAffixGraphMember> Members, IReadOnlyList<RedundantZeroAffixSlot> OptionalSlots,
    IReadOnlyList<string> WritingSystems);

/// <summary>One entry-owned object included in the delete scope.</summary>
public sealed record RedundantZeroAffixGraphMember(CanonicalId Id, string ClassName, CanonicalId? Owner);

/// <summary>One optional external slot reference that must be removed before entry deletion.</summary>
public sealed record RedundantZeroAffixSlot(CanonicalId Id, bool Optional);
