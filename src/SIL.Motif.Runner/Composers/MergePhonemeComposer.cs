using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Merges one phoneme into another so a spelling the source owned becomes another way to write the target, and the
/// source no longer exists. Every written representation of the source is added to the target after the target's own.
/// The source leaves every natural class it is in and the target's own classes govern the merged sound from then on.
/// Anything else that uses the source (a rule, an environment that writes one of its spellings, an affix process)
/// would silently change meaning or lose a reference, so each is refused by name. Each refusal begins with a stable name.
/// </summary>
public static class MergePhonemeComposer
{
    /// <summary>Refusal name: the source and the target are the same phoneme.</summary>
    public const string Same = "phoneme-merge-same";

    /// <summary>Refusal name: the source has no written representation to move.</summary>
    public const string NothingToMove = "phoneme-merge-nothing-to-move";

    /// <summary>Refusal name: a representation of the source also belongs to another phoneme or boundary.</summary>
    public const string CodeConflict = "phoneme-merge-code-conflict";

    /// <summary>Refusal name: a rule, environment or other object still uses the source.</summary>
    public const string Referenced = "phoneme-merge-referenced";

    /// <summary>Refusal name: the source is the only member of a natural class, which would be left empty.</summary>
    public const string ClassWouldEmpty = "phoneme-merge-class-would-empty";

    /// <summary>Composes the class removals, the source's removal and the representations added to the target.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MergePhonemeIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var (source, target) = ResolvePair(cache, intent);
        var moved = source.CodesOS.Select(code => EditPhonemeComposer.Spelling(cache, code)).OfType<string>().ToArray();
        if (moved.Length == 0)
            throw new InvalidOperationException($"{NothingToMove}: the phoneme being merged has no written representation to move.");
        RequireNoConflict(cache, source, target, moved);
        var classes = ClassesOf(cache, source);
        foreach (var naturalClass in classes.Where(naturalClass => naturalClass.SegmentsRC.Count == 1))
            throw new InvalidOperationException(
                $"{ClassWouldEmpty}: natural class '{EditPhonemeComposer.Named(naturalClass.Name.BestAnalysisAlternative.Text, naturalClass.Guid)}' " +
                "has only this phoneme, so it would be left empty. Edit or remove that class first.");
        RequireNoOtherUse(cache, source, moved);

        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the MergePhoneme composer.");
        var removals = classes.Select(naturalClass => builder.Add(PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments,
            SoundSystemAuthoring.Id(naturalClass), new { member = SoundSystemAuthoring.Id(source).Value })).ToArray();
        var removal = builder.Add(PhonemeDeleteOperationKinds.DeletePhonemes, SoundSystemAuthoring.Id(source), new { },
            requires: removals);
        var placement = SoundSystemAuthoring.Append(target.CodesOS);
        foreach (var spelling in moved)
        {
            var code = builder.Create(PhTerminalUnitCodesOperationKinds.Create, SoundSystemAuthoring.Id(target),
                placement: placement, requires: [removal]);
            builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, code, cache, cache.DefaultVernWs, spelling);
            placement = new Placement(code, null);
        }
        return builder.Operations;
    }

    /// <summary>Returns the natural classes the source leaves and the environments that write its spellings.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, MergePhonemeIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var (source, _) = ResolvePair(cache, intent);
        var environments = source.CodesOS.Select(code => EditPhonemeComposer.Spelling(cache, code)).OfType<string>()
            .SelectMany(spelling => EditPhonemeComposer.ReadEnvironmentsWriting(cache, spelling));
        var classes = ClassesOf(cache, source).Select(naturalClass => new ComposedRelatedObject("natural-class",
            SoundSystemAuthoring.Id(naturalClass).Value,
            EditPhonemeComposer.Named(naturalClass.Name.BestAnalysisAlternative.Text, naturalClass.Guid) +
            " - the source phoneme will be dropped from this class"));
        return environments.Concat(classes).DistinctBy(user => (user.Kind, user.Id))
            .OrderBy(user => user.Kind, StringComparer.Ordinal).ThenBy(user => user.Id, StringComparer.Ordinal).ToArray();
    }

    private static (IPhPhoneme Source, IPhPhoneme Target) ResolvePair(LcmCache cache, MergePhonemeIntent intent)
    {
        var source = Resolve(cache, intent.Source, "source");
        var target = Resolve(cache, intent.Target, "target");
        if (source.Guid == target.Guid)
            throw new InvalidOperationException($"{Same}: a phoneme cannot be merged into itself.");
        return (source, target);
    }

    private static IPhPhoneme Resolve(LcmCache cache, CanonicalId id, string role)
    {
        var phoneme = ReferenceFieldLowering.Resolve<IPhPhoneme>(cache, id, "MergePhoneme");
        if (!SoundSystemAuthoring.FirstSet(cache).PhonemesOC.Contains(phoneme))
            throw new InvalidOperationException(
                $"{EditPhonemeComposer.NotInInventory}: the {role} is not a phoneme of the first phoneme set.");
        return phoneme;
    }

    private static IPhNCSegments[] ClassesOf(LcmCache cache, IPhPhoneme source) =>
        cache.LangProject.PhonologicalDataOA.NaturalClassesOS.OfType<IPhNCSegments>()
            .Where(naturalClass => naturalClass.SegmentsRC.Contains(source)).ToArray();

    private static void RequireNoConflict(LcmCache cache, IPhPhoneme source, IPhPhoneme target, IReadOnlyList<string> moved)
    {
        var inventory = SoundSystemAuthoring.FirstSet(cache);
        foreach (var unit in inventory.PhonemesOC.Cast<IPhTerminalUnit>().Concat(inventory.BoundaryMarkersOC)
                     .Where(unit => unit.Guid != source.Guid))
            foreach (var spelling in unit.CodesOS.Select(code => EditPhonemeComposer.Spelling(cache, code)).OfType<string>()
                         .Where(moved.Contains))
            {
                var kind = unit is IPhPhoneme ? "phoneme" : "boundary marker";
                var owner = unit.Name.get_String(cache.DefaultVernWs)?.Text ?? unit.Name.BestAnalysisAlternative.Text;
                var side = unit.Guid == target.Guid ? " (the phoneme being merged into)" : string.Empty;
                throw new InvalidOperationException(
                    $"{CodeConflict}: '{spelling}' is also a representation of {kind} '{owner}' " +
                    $"(id {SoundSystemAuthoring.Id(unit).Value}){side}. A spelling can belong to one phoneme only; " +
                    "fix that spelling with compose-edit-phoneme first.");
            }
    }

    private static void RequireNoOtherUse(LcmCache cache, IPhPhoneme source, IReadOnlyList<string> moved)
    {
        foreach (var spelling in moved)
            if (EditPhonemeComposer.ReadEnvironmentsWriting(cache, spelling).FirstOrDefault() is { } environment)
                throw new InvalidOperationException(
                    $"{Referenced}: environment '{environment.Name}' writes '{spelling}'. After the merge it would also " +
                    "match the other phoneme's words, so it cannot be kept as it is; change that environment first.");
        foreach (var user in source.ReferringObjects.Where(user => user is not IPhNCSegments))
            throw new InvalidOperationException(
                $"{Referenced}: the phoneme is still used by {Describe(user)}. Merging would change what it means, " +
                "so change that use first.");
    }

    private static string Describe(ICmObject user)
    {
        for (var owner = user; owner is not null; owner = owner.Owner)
            if (owner is IPhSegmentRule rule)
                return $"phonological rule '{EditPhonemeComposer.Named(rule.Name.BestAnalysisAlternative.Text, rule.Guid)}' " +
                    $"(id {SoundSystemAuthoring.Id(rule).Value})";
        return $"a {user.ClassName} (id {SoundSystemAuthoring.Id(user).Value})";
    }
}
