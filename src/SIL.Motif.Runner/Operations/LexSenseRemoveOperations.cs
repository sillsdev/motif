using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kind that deletes one sense that carries content, with its sub-senses and examples.</summary>
public static class LexSenseRemoveOperationKinds
{
    /// <summary>Deletes the targeted top-level sense or sub-sense, and the analyses only it used.</summary>
    public const string Remove = "lexical/lexSense/remove";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Remove);
        OperationHandlerRegistry.Register(Remove, LexSenseRemoveHandler.Instance);
        OperationSlotRegistry.RegisterMany(Remove, operation =>
        {
            var sense = OperationSlotRegistry.Target(operation);
            var payload = LexSenseRemovePayload.Parse(OperationSlotRegistry.After(operation));
            var member = OperationSlotRegistry.MemberKey(sense);
            var slots = new List<OperationSlot>
            {
                new(sense, null),
                new(payload.Owner, payload.TopLevel ? SnapshotFields.LexEntrySenses : SnapshotFields.LexSenseSubsenses, member),
            };
            if (payload.TopLevel)
                foreach (var msa in payload.Msas)
                {
                    slots.Add(new OperationSlot(msa, null));
                    slots.Add(new OperationSlot(payload.Owner, SnapshotFields.LexEntryMorphoSyntaxAnalyses,
                        OperationSlotRegistry.MemberKey(msa)));
                }
            else
                foreach (var msa in payload.Msas)
                    slots.Add(new OperationSlot(msa, null));
            return slots;
        });
    }
#pragma warning restore CA2255
}

/// <summary>The closed payload of a sense deletion.</summary>
/// <param name="Owner">The entry (top-level sense) or parent sense (sub-sense) that owns the target; a different owner makes the Draft stale.</param>
/// <param name="TopLevel">True when the target is a top-level sense of an entry.</param>
/// <param name="Entry">The entry that owns the target, directly or through its parent sense.</param>
/// <param name="Msas">The analyses of <paramref name="Entry"/> that no remaining sense uses and nothing else refers to.</param>
public sealed record LexSenseRemovePayload(CanonicalId Owner, bool TopLevel, CanonicalId Entry, IReadOnlyList<CanonicalId> Msas)
{
    /// <summary>Parses the closed payload, refusing any other property.</summary>
    public static LexSenseRemovePayload Parse(JsonElement after)
    {
        const string kind = LexSenseRemoveOperationKinds.Remove;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["owner", "topLevel", "entry", "msas"], kind);
        if (!after.TryGetProperty("topLevel", out var top) || top.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException($"'{kind}' operation 'after.topLevel' must be true or false.");
        if (!after.TryGetProperty("msas", out var msas) || msas.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation 'after.msas' must be an array of ids, possibly empty.");
        var ids = msas.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? CanonicalId.FromGuid(CanonicalId.Parse(item.GetString()!).ToGuid())
            : throw new ContractParseException($"'{kind}' operation 'after.msas' holds only ids.")).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            throw new ContractParseException($"'{kind}' operation 'after.msas' names an analysis twice.");
        return new LexSenseRemovePayload(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "owner", kind).ToGuid()),
            top.GetBoolean(),
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "entry", kind).ToGuid()), ids);
    }
}

/// <summary>What deleting one sense takes with it, and whether it may be deleted at all.</summary>
internal sealed record SenseRemovalPlan(
    ILexSense Sense, ICmObject Owner, ILexEntry Entry, ILexSense[] Subtree, IMoMorphSynAnalysis[] OrphanedAnalyses)
{
    internal bool TopLevel => Owner is ILexEntry;
}

/// <summary>
/// Decides whether a sense may be deleted and what goes with it. LibLCM deletes the sense's owned sub-senses and
/// examples, and quietly drops every reference to it, so anything that refers to the sense is refused here instead:
/// that is a lexical relation, a cross-reference, a component or primary lexeme of an entry reference, or a morph
/// bundle of a parser analysis. FieldWorks leaves the sense's analysis in the entry; here it goes too, but only when
/// no remaining sense and no other object uses it.
/// </summary>
internal static class SenseRemoval
{
    private const string Kind = LexSenseRemoveOperationKinds.Remove;

    /// <summary>Throws when the sense must not be deleted; otherwise says what the deletion removes.</summary>
    internal static SenseRemovalPlan Inspect(ILexSense sense)
    {
        var entry = EntryOf(sense) ?? throw new InvalidOperationException($"'{Kind}' needs a sense that belongs to an entry.");
        var owner = sense.Owner;
        if (owner is ILexEntry && entry.SensesOS.Count < 2)
            throw new InvalidOperationException(
                $"'{Kind}' refuses the only sense of an entry, as FieldWorks does: an entry must keep a sense. " +
                "Delete the entry, or edit the sense instead.");
        var subtree = sense.AllSenses.ToArray();
        var inside = Subtree(subtree);
        foreach (var item in subtree.Cast<ICmObject>().Concat(subtree.SelectMany(each => each.ExamplesOS)))
        foreach (var referrer in item.ReferringObjects.Where(other => !inside.Contains(other.Guid) && !IsInside(other, inside)))
            throw new InvalidOperationException(
                $"'{Kind}' refuses {Describe(item)} because {Describe(referrer)} still refers to it; " +
                "remove or retarget that reference first.");
        var remaining = entry.AllSenses.Where(other => subtree.All(item => item.Guid != other.Guid)).ToArray();
        var unused = subtree.Select(item => item.MorphoSyntaxAnalysisRA).OfType<IMoMorphSynAnalysis>()
            .DistinctBy(msa => msa.Guid)
            .Where(msa => msa.Owner?.Guid == entry.Guid && remaining.All(other => other.MorphoSyntaxAnalysisRA?.Guid != msa.Guid))
            .ToArray();
        // LibLCM removes an unused analysis and clears morph bundles that point at it.
        foreach (var msa in unused)
        foreach (var referrer in msa.ReferringObjects.Where(other => !inside.Contains(other.Guid)))
            throw new InvalidOperationException(
                $"'{Kind}' refuses {Describe(sense)} because deleting it would remove its analysis {Describe(msa)}, " +
                $"which {Describe(referrer)} still refers to; retarget that reference first.");
        var orphaned = unused;
        return new SenseRemovalPlan(sense, owner, entry, subtree, orphaned);
    }

    internal static ILexEntry? EntryOf(ILexSense sense)
    {
        ICmObject? current = sense;
        while (current is ILexSense parent)
            current = parent.Owner;
        return current as ILexEntry;
    }

    /// <summary>Identities of the senses and examples deleted with the target.</summary>
    private static HashSet<Guid> Subtree(IEnumerable<ILexSense> senses)
    {
        var result = new HashSet<Guid>();
        foreach (var item in senses)
        {
            result.Add(item.Guid);
            foreach (var example in item.ExamplesOS)
                result.Add(example.Guid);
        }
        return result;
    }

    private static bool IsInside(ICmObject item, HashSet<Guid> inside)
    {
        for (var owner = item.Owner; owner is not null; owner = owner.Owner)
            if (inside.Contains(owner.Guid))
                return true;
        return false;
    }

    /// <summary>A short, readable name for an object: its class, its id, and the part a linguist recognises.</summary>
    internal static string Describe(ICmObject item)
    {
        var id = CanonicalId.FromGuid(item.Guid).Value;
        var detail = item switch
        {
            ILexSense sense => Gloss(sense) is { Length: > 0 } gloss ? $" '{gloss}' of {Describe(EntryOf(sense)!)}" : "",
            ILexEntry entry => $" '{entry.HeadWord?.Text}'",
            ILexReference reference => $" in the lexical relation '{reference.OwnerOfClass<ILexRefType>()?.Name.BestAnalysisAlternative.Text}'" +
                                       $" with {string.Join(", ", reference.TargetsRS.Select(target => Name(target)))}",
            ILexEntryRef reference when reference.Owner is ILexEntry owner => $" of {Describe(owner)}",
            IWfiMorphBundle bundle => $" of the wordform analysis of '{bundle.OwnerOfClass<IWfiWordform>()?.Form.BestVernacularAlternative.Text}'",
            _ => "",
        };
        return $"{item.ClassName} {id}{detail}";
    }

    private static string Name(ICmObject target) => target switch
    {
        ILexSense sense => $"sense '{Gloss(sense)}' of '{EntryOf(sense)?.HeadWord?.Text}'",
        ILexEntry entry => $"entry '{entry.HeadWord?.Text}'",
        _ => target.ClassName,
    };

    /// <summary>The sense's gloss in the first writing system that has one.</summary>
    internal static string Gloss(ILexSense sense) => sense.Gloss.BestAnalysisAlternative?.Text is { } text &&
                                                      !text.StartsWith("***", StringComparison.Ordinal) ? text : "";
}

/// <summary>
/// Deletes a sense that carries content: the sense, its sub-senses and examples, and the entry analyses only they
/// used. It records exactly what goes in the Dry Run preview so a person reviews every deletion.
/// </summary>
internal sealed class LexSenseRemoveHandler : ICompoundOperationHandler
{
    internal static readonly LexSenseRemoveHandler Instance = new();
    private const string Kind = LexSenseRemoveOperationKinds.Remove;

    private LexSenseRemoveHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (senseId, plan, payload) = Resolve(cache, operation, checkPlan: true);
        var ownerBefore = ReadOwned(plan);
        var msasBefore = LexSenseDeleteHandler.ReadAnalyses(plan.Entry);
        var preview = Preview(plan);
        var owner = plan.Owner;
        var entry = plan.Entry;
        var analyses = plan.OrphanedAnalyses;
        touchedTargets.Add(senseId);
        touchedTargets.AddRange(plan.Subtree.Skip(1).Select(item => CanonicalId.FromGuid(item.Guid)));
        touchedTargets.AddRange(analyses.Select(msa => CanonicalId.FromGuid(msa.Guid)));
        plan.Sense.Delete();
        foreach (var msa in analyses.Where(msa => msa.IsValidObject))
            msa.Delete();
        var effects = new List<ExpectedEffect>
        {
            new(payload.Owner, OwnerField(payload), ownerBefore, ReadOwned(owner), preview),
        };
        if (analyses.Length != 0)
            effects.Add(new(payload.Entry, SnapshotFields.LexEntryMorphoSyntaxAnalyses, msasBefore,
                LexSenseDeleteHandler.ReadAnalyses(entry)));
        return effects;
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (_, plan, payload) = Resolve(cache, operation, checkPlan: false);
        var owned = ReadOwned(plan);
        var effects = new List<ExpectedEffect> { new(payload.Owner, OwnerField(payload), owned, owned, Preview(plan)) };
        if (payload.Msas.Count != 0)
        {
            var analyses = LexSenseDeleteHandler.ReadAnalyses(plan.Entry);
            effects.Add(new(payload.Entry, SnapshotFields.LexEntryMorphoSyntaxAnalyses, analyses, analyses));
        }
        return effects;
    }

    private static string OwnerField(LexSenseRemovePayload payload) =>
        payload.TopLevel ? SnapshotFields.LexEntrySenses : SnapshotFields.LexSenseSubsenses;

    private static (CanonicalId Id, SenseRemovalPlan Plan, LexSenseRemovePayload Payload) Resolve(
        LcmCache cache, OperationEnvelope operation, bool checkPlan)
    {
        var payload = LexSenseRemovePayload.Parse(operation.After ??
            throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        var (id, sense) = TargetResolution.Resolve<ILexSense>(cache, operation, Kind);
        var entry = SenseRemoval.EntryOf(sense);
        if (entry is null || sense.Owner.Guid != payload.Owner.ToGuid() || (sense.Owner is ILexEntry) != payload.TopLevel ||
            entry.Guid != payload.Entry.ToGuid())
            throw new InvalidOperationException($"'{Kind}' names an owner the sense no longer has; recompute the Draft.");
        // The footprint only locates the sense; Apply checks the refusals.
        var plan = checkPlan ? SenseRemoval.Inspect(sense)
            : new SenseRemovalPlan(sense, sense.Owner, entry, sense.AllSenses.ToArray(),
                payload.Msas.Select(msa => cache.ServiceLocator.ObjectRepository.TryGetObject(msa.ToGuid(), out var found)
                    ? found as IMoMorphSynAnalysis : null).OfType<IMoMorphSynAnalysis>().ToArray());
        if (checkPlan)
        {
            var planned = plan.OrphanedAnalyses.Select(msa => CanonicalId.FromGuid(msa.Guid)).Order().ToArray();
            if (!planned.SequenceEqual(payload.Msas.Order()))
                throw new InvalidOperationException(
                    $"'{Kind}' names analyses that do not match what the deletion would orphan now; recompute the Draft.");
        }
        return (id, plan, payload);
    }

    private static Dictionary<string, string> ReadOwned(SenseRemovalPlan plan) => ReadOwned(plan.Owner);

    private static Dictionary<string, string> ReadOwned(ICmObject owner)
    {
        var senses = owner switch
        {
            ILexEntry entry => entry.SensesOS.Cast<ICmObject>().ToArray(),
            ILexSense parent => parent.SensesOS.Cast<ICmObject>().ToArray(),
            _ => [],
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < senses.Length; i++)
            result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                CanonicalId.FromGuid(senses[i].Guid).Value;
        return result;
    }

    private static string? Text(ITsString? text) => text?.Text is { Length: > 0 } value ? value : null;

    /// <summary>The recorded before-state: every sense and analysis that goes.</summary>
    private static JsonElement Preview(SenseRemovalPlan plan)
    {
        var sense = plan.Sense;
        var siblings = plan.TopLevel ? plan.Entry.SensesOS.Cast<ICmObject>().ToList()
            : ((ILexSense)plan.Owner).SensesOS.Cast<ICmObject>().ToList();
        var index = siblings.FindIndex(item => item.Guid == sense.Guid);
        string? Neighbour(int at) => at >= 0 && at < siblings.Count ? CanonicalId.FromGuid(siblings[at].Guid).Value : null;
        return JsonSerializer.SerializeToElement(new
        {
            deletedSense = new
            {
                id = CanonicalId.FromGuid(sense.Guid).Value,
                owner = CanonicalId.FromGuid(plan.Owner.Guid).Value,
                entry = plan.Entry.HeadWord?.Text,
                kind = plan.TopLevel ? "sense" : "sub-sense",
                index,
                after = Neighbour(index - 1),
                before = Neighbour(index + 1),
            },
            deletedSenses = plan.Subtree.Select(item => new
            {
                id = CanonicalId.FromGuid(item.Guid).Value,
                parent = item.Guid == sense.Guid ? null : CanonicalId.FromGuid(item.Owner.Guid).Value,
                gloss = RemovalPreview.AllWritingSystems(item.Cache, item.Gloss),
                definition = RemovalPreview.AllWritingSystems(item.Cache, item.Definition),
                notes = RemovalPreview.SenseNotes(item),
                source = Text(item.Source),
                scientificName = Text(item.ScientificName),
                semanticDomains = item.SemanticDomainsRC.Select(RemovalPreview.PossibilityName).ToArray(),
                status = item.StatusRA is { } status ? RemovalPreview.PossibilityName(status) : null,
                analysis = item.MorphoSyntaxAnalysisRA is { } msa ? CanonicalId.FromGuid(msa.Guid).Value : null,
                examples = item.ExamplesOS.Select(RemovalPreview.Example).ToArray(),
                pictures = item.PicturesOS.Select(RemovalPreview.Picture).ToArray(),
            }).ToArray(),
            deletedAnalyses = plan.OrphanedAnalyses.Select(msa => new
            {
                id = CanonicalId.FromGuid(msa.Guid).Value,
                @class = msa.ClassName,
                partOfSpeech = VariantSenseAnalysisSafety.PartOfSpeech(msa) is { } pos
                    ? pos.Name.BestAnalysisAlternative?.Text : null,
            }).ToArray(),
            reversal = "createSenses with the same ids, placements, glosses, definitions, notes, source, scientific name, semantic domains, status, examples, translations and pictures, " +
                "recreate any deleted analysis with the same class and id, and point the senses at it",
        });
    }
}

/// <summary>Reads everything a deletion removes into plain dictionaries, in every writing system, for the Dry Run preview.</summary>
internal static class RemovalPreview
{
    /// <summary>Every non-empty alternative of a multilingual field, keyed by writing-system tag.</summary>
    internal static Dictionary<string, string> AllWritingSystems(LcmCache cache, IMultiAccessorBase? field)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (field is null)
            return result;
        foreach (var ws in field.AvailableWritingSystemIds)
            if (field.get_String(ws)?.Text is { Length: > 0 } text)
                result[WritingSystemName(cache, ws)] = text;
        return result;
    }

    /// <summary>The writing-system tag, or the numeric handle when the handle cannot be resolved.</summary>
    internal static string WritingSystemName(LcmCache cache, int handle)
    {
        try
        {
            if (cache.ServiceLocator.WritingSystemManager.Get(handle)?.Id is { Length: > 0 } tag)
                return tag;
        }
        catch (ArgumentException)
        {
        }
        catch (KeyNotFoundException)
        {
        }
        return handle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A possibility as a reader knows it: abbreviation and name.</summary>
    internal static string PossibilityName(ICmPossibility item)
    {
        var name = item.Name.BestAnalysisAlternative?.Text;
        var abbreviation = item.Abbreviation.BestAnalysisAlternative?.Text;
        var named = string.IsNullOrEmpty(name) || name.StartsWith("***", StringComparison.Ordinal) ? null : name;
        var abbreviated = string.IsNullOrEmpty(abbreviation) || abbreviation.StartsWith("***", StringComparison.Ordinal) ? null : abbreviation;
        return (abbreviated, named) switch
        {
            ({ } a, { } n) => $"{a} {n}",
            ({ } a, null) => a,
            (null, { } n) => n,
            _ => CanonicalId.FromGuid(item.Guid).Value,
        };
    }

    /// <summary>The sense's note fields that hold text, keyed by field name.</summary>
    internal static Dictionary<string, Dictionary<string, string>> SenseNotes(ILexSense sense)
    {
        var fields = new (string Name, IMultiAccessorBase Field)[]
        {
            ("generalNote", sense.GeneralNote), ("anthropologyNote", sense.AnthroNote), ("grammarNote", sense.GrammarNote),
            ("phonologyNote", sense.PhonologyNote), ("semanticsNote", sense.SemanticsNote),
            ("sociolinguisticsNote", sense.SocioLinguisticsNote), ("discourseNote", sense.DiscourseNote),
            ("bibliography", sense.Bibliography), ("restrictions", sense.Restrictions), ("encyclopedicInfo", sense.EncyclopedicInfo),
        };
        return fields.Select(item => (item.Name, Text: AllWritingSystems(sense.Cache, item.Field))).Where(item => item.Text.Count != 0)
            .ToDictionary(item => item.Name, item => item.Text, StringComparer.Ordinal);
    }

    /// <summary>An example sentence: its text, reference and every translation.</summary>
    internal static object Example(ILexExampleSentence example) => new
    {
        id = CanonicalId.FromGuid(example.Guid).Value,
        text = AllWritingSystems(example.Cache, example.Example),
        reference = example.Reference?.Text,
        translations = example.TranslationsOC.Select(translation => new
        {
            type = translation.TypeRA?.Name.BestAnalysisAlternative?.Text,
            text = AllWritingSystems(example.Cache, translation.Translation),
        }).ToArray(),
    };

    /// <summary>A picture: its file and caption.</summary>
    internal static object Picture(ICmPicture picture) => new
    {
        id = CanonicalId.FromGuid(picture.Guid).Value,
        file = picture.PictureFileRA?.InternalPath,
        caption = AllWritingSystems(picture.Cache, picture.Caption),
    };
}
