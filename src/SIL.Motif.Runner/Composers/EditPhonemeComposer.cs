using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Adds representations to, or respells representations of, an existing phoneme, so a digraph such as ng, ts or dy
/// joins the phoneme it spells instead of becoming a second segment. Each refusal begins with a stable name.
/// </summary>
public static class EditPhonemeComposer
{
    /// <summary>Refusal name: the edit changes nothing.</summary>
    public const string Empty = "phoneme-edit-empty";

    /// <summary>Refusal name: the target is not a phoneme of the first phoneme set.</summary>
    public const string NotInInventory = "phoneme-not-in-inventory";

    /// <summary>Refusal name: a new spelling is blank or holds whitespace or environment syntax.</summary>
    public const string Invalid = "phoneme-representation-invalid";

    /// <summary>Refusal name: a spelling already belongs to a phoneme or boundary, or is given twice.</summary>
    public const string Duplicate = "phoneme-representation-duplicate";

    /// <summary>Refusal name: a respelled representation is not one the phoneme has.</summary>
    public const string NotFound = "phoneme-representation-not-found";

    /// <summary>Refusal name: an environment writes the representation, so respelling would leave it stale.</summary>
    public const string Referenced = "phoneme-representation-referenced";

    /// <summary>Composes the code additions and in-place respellings.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditPhonemeIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Add.Count + intent.Respell.Count == 0)
            throw new InvalidOperationException($"{Empty}: name at least one representation to add or respell.");
        var phoneme = Resolve(cache, intent.Target);
        var ws = cache.DefaultVernWs;
        var own = phoneme.CodesOS.ToDictionary(code => Spelling(cache, code) ?? string.Empty, code => code, StringComparer.Ordinal);
        var added = intent.Add.Select(Literal).ToArray();
        var respelled = intent.Respell.Select(r => (From: r.From.Normalize(NormalizationForm.FormD), To: Literal(r.To))).ToArray();

        var incoming = added.Concat(respelled.Select(r => r.To)).ToArray();
        if (incoming.Distinct(StringComparer.Ordinal).Count() != incoming.Length ||
            respelled.Select(r => r.From).Distinct(StringComparer.Ordinal).Count() != respelled.Length)
            throw new InvalidOperationException($"{Duplicate}: a representation is named twice in one edit.");
        var inventory = SoundSystemAuthoring.FirstSet(cache);
        var owners = new Dictionary<string, IPhTerminalUnit>(StringComparer.Ordinal);
        foreach (var unit in inventory.PhonemesOC.Cast<IPhTerminalUnit>().Concat(inventory.BoundaryMarkersOC))
            foreach (var code in unit.CodesOS)
                if (Spelling(cache, code) is { } spelling) owners.TryAdd(spelling, unit);
        foreach (var spelling in incoming.Where(owners.ContainsKey))
            throw new InvalidOperationException($"{Duplicate}: {DescribeOwner(cache, spelling, owners[spelling], phoneme)}");
        foreach (var (from, _) in respelled)
        {
            if (!own.ContainsKey(from))
                throw new InvalidOperationException($"{NotFound}: the phoneme has no representation '{from}'.");
            if (ReadEnvironmentsWriting(cache, from).FirstOrDefault() is { } environment)
                throw new InvalidOperationException(
                    $"{Referenced}: environment '{environment.Name}' writes '{from}'; change that environment first.");
        }

        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the EditPhoneme composer.");
        foreach (var (from, to) in respelled)
            builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, SoundSystemAuthoring.Id(own[from]), cache, ws, to);
        var placement = SoundSystemAuthoring.Append(phoneme.CodesOS);
        foreach (var spelling in added)
        {
            var code = builder.Create(PhTerminalUnitCodesOperationKinds.Create, SoundSystemAuthoring.Id(phoneme), placement: placement);
            builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, code, cache, ws, spelling);
            placement = new Placement(code, null);
        }
        return builder.Operations;
    }

    /// <summary>Returns the environments and segment classes that use the target phoneme.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, EditPhonemeIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var phoneme = Resolve(cache, intent.Target);
        var environments = phoneme.CodesOS.Select(code => Spelling(cache, code)).OfType<string>()
            .SelectMany(spelling => ReadEnvironmentsWriting(cache, spelling));
        var classes = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.OfType<IPhNCSegments>()
            .Where(naturalClass => naturalClass.SegmentsRC.Contains(phoneme))
            .Select(naturalClass => new ComposedRelatedObject("natural-class",
                SoundSystemAuthoring.Id(naturalClass).Value, Named(naturalClass.Name.BestAnalysisAlternative.Text, naturalClass.Guid)));
        return environments.Concat(classes).DistinctBy(user => (user.Kind, user.Id))
            .OrderBy(user => user.Kind, StringComparer.Ordinal).ThenBy(user => user.Id, StringComparer.Ordinal).ToArray();
    }

    private static string DescribeOwner(LcmCache cache, string spelling, IPhTerminalUnit owner, IPhPhoneme target)
    {
        var kind = owner is IPhPhoneme ? "phoneme" : "boundary marker";
        var name = owner.Name.get_String(cache.DefaultVernWs)?.Text ?? owner.Name.BestAnalysisAlternative.Text;
        var shown = string.IsNullOrWhiteSpace(name) || name == "***" ? "(unnamed)" : name;
        var spellings = string.Join(", ", owner.CodesOS.Select(code => Spelling(cache, code)).OfType<string>()
            .Select(text => $"'{text}'"));
        var id = SoundSystemAuthoring.Id(owner).Value;
        var self = ReferenceEquals(owner, target) ? " (the phoneme being edited)" : string.Empty;
        return $"'{spelling}' already belongs to {kind} '{shown}' (id {id}, representations {spellings}){self} " +
            "in the first phoneme set. " + (ReferenceEquals(owner, target)
                ? "The phoneme already has it; nothing to add."
                : "Pick a spelling that no phoneme has, or edit that phoneme instead.");
    }

    private static IPhPhoneme Resolve(LcmCache cache, CanonicalId id)
    {
        var phoneme = ReferenceFieldLowering.Resolve<IPhPhoneme>(cache, id, "EditPhoneme");
        if (!SoundSystemAuthoring.FirstSet(cache).PhonemesOC.Contains(phoneme))
            throw new InvalidOperationException($"{NotInInventory}: the target is not a phoneme of the first phoneme set.");
        return phoneme;
    }

    private static string Literal(string text)
    {
        try { return SoundSystemAuthoring.RequireLiteral(text); }
        catch (InvalidOperationException exception) { throw new InvalidOperationException($"{Invalid}: {exception.Message}"); }
    }

    private static string? Spelling(LcmCache cache, IPhCode code) =>
        code.Representation.get_String(cache.DefaultVernWs)?.Text is { Length: > 0 } text
            ? text.Normalize(NormalizationForm.FormD) : null;

    private static IEnumerable<ComposedRelatedObject> ReadEnvironmentsWriting(LcmCache cache, string spelling) =>
        cache.LangProject.PhonologicalDataOA.EnvironmentsOS
            .Where(environment => Regex.Split(environment.StringRepresentation?.Text?.Normalize(NormalizationForm.FormD) ?? string.Empty,
                @"[\s\[\]()/_]+").Contains(spelling, StringComparer.Ordinal))
            .Select(environment => new ComposedRelatedObject("environment", SoundSystemAuthoring.Id(environment).Value,
                Named(environment.Name.BestAnalysisAlternative.Text, environment.Guid)));

    private static string Named(string? name, Guid guid) =>
        string.IsNullOrWhiteSpace(name) || name == "***" ? guid.ToString("D") : name;
}
