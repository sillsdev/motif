using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Composes owned phonemes and explicit codes; default placeholder codes are never authored.</summary>
public static class AuthorPhonemeComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorPhonemeIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (intent.Representations is null || intent.Representations.Count == 0)
            throw new InvalidOperationException("A phoneme needs at least one grapheme representation.");
        var codes = intent.Representations.Select(SoundSystemAuthoring.RequireLiteral).ToArray();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
            throw new InvalidOperationException("A phoneme's representations must be distinct after normalization.");
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
        var ws = cache.DefaultVernWs;
        var existingCodes = (set is null ? Array.Empty<IPhTerminalUnit>() : set.PhonemesOC.Cast<IPhTerminalUnit>().Concat(set.BoundaryMarkersOC))
            .SelectMany(p => p.CodesOS).Select(c => c.Representation.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD));
        if (existingCodes.Any(c => c is not null && codes.Contains(c, StringComparer.Ordinal)))
            throw new InvalidOperationException("A grapheme representation already belongs to a phoneme or boundary in the first set.");
        SoundSystemAuthoring.ValidateFeatures(cache, intent.Features);
        var builder = new SoundSystemAuthoring.Builder(mintId);
        var setId = set is not null ? SoundSystemAuthoring.Id(set) :
            builder.Create(PhPhonDataPhonemeSetsOperationKinds.Create, SoundSystemAuthoring.Id(cache.LangProject.PhonologicalDataOA));
        if (set is null)
        {
            SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, null, setId,
                LangProjectTags.kguidPhRuleMorphBdry, "morpheme", "+");
            SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, null, setId,
                LangProjectTags.kguidPhRuleWordBdry, "word", "#");
        }
        var phoneme = builder.Create(PhPhonemeSetPhonemesOperationKinds.Create, setId);
        builder.Text(PhTerminalUnitNameOperationKinds.SetName, phoneme, cache, ws, intent.Name);
        CanonicalId? prior = null;
        foreach (var representation in codes)
        {
            var code = builder.Create(PhTerminalUnitCodesOperationKinds.Create, phoneme,
                placement: prior is { } previous ? new Placement(previous, null) : null);
            builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, code, cache, ws, representation);
            prior = code;
        }
        if (intent.Features is { Count: > 0 })
            builder.Features(cache, phoneme, PhPhonemeFeaturesOperationKinds.Create, intent.Features);
        return builder.Operations;
    }
}

/// <summary>Composes a segment-defined or feature-defined natural class with an unambiguous abbreviation.</summary>
public static class AuthorNaturalClassComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorNaturalClassIntent intent,
        Func<CanonicalId>? mintId = null, CanonicalId? classId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        SoundSystemAuthoring.RequireAbbreviation(intent.Abbreviation);
        if ((intent.Members is null) == (intent.Features is null))
            throw new InvalidOperationException("A natural class needs exactly one of members or features.");
        var data = cache.LangProject.PhonologicalDataOA;
        var abbreviation = intent.Abbreviation.Normalize(NormalizationForm.FormD);
        if (data.NaturalClassesOS.Any(n => n.Abbreviation.BestAnalysisAlternative.Text?.Normalize(NormalizationForm.FormD) == abbreviation))
            throw new InvalidOperationException("The natural class abbreviation already exists; environment references would be ambiguous.");
        if (intent.Members is { } members)
        {
            if (members.Count == 0 || members.Distinct().Count() != members.Count)
                throw new InvalidOperationException("A segment class needs a nonempty list of distinct phoneme ids.");
            foreach (var member in members) SoundSystemAuthoring.RequirePhoneme(cache, member);
        }
        else if (intent.Features!.Count == 0) throw new InvalidOperationException("A feature class needs at least one feature value.");
        SoundSystemAuthoring.ValidateFeatures(cache, intent.Features);
        var builder = new SoundSystemAuthoring.Builder(mintId);
        var id = builder.Create(PhPhonDataNaturalClassesOperationKinds.Create, SoundSystemAuthoring.Id(data),
            new { @class = intent.Members is null ? "PhNCFeatures" : "PhNCSegments" },
            SoundSystemAuthoring.Append(data.NaturalClassesOS), classId);
        builder.Text(PhNaturalClassNameOperationKinds.SetName, id, cache, cache.DefaultAnalWs, intent.Name);
        builder.Text(PhNaturalClassAbbreviationOperationKinds.SetAbbreviation, id, cache, cache.DefaultAnalWs, abbreviation);
        if (intent.Members is { } segmentIds)
            foreach (var member in segmentIds) builder.Add(PhNCSegmentsSegmentsOperationKinds.AddRefSegments, id, new { member = member.Value });
        else builder.Features(cache, id, PhNCFeaturesFeaturesOperationKinds.Create, intent.Features!);
        return builder.Operations;
    }
}

/// <summary>
/// Typed contexts become an environment string understood by the parser. Context graph fields and
/// unconstrained pattern strings are outside this intent, avoiding silent loss of allomorph restrictions.
/// </summary>
public static class AuthorEnvironmentComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorEnvironmentIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (intent.Left is null || intent.Right is null || intent.Left.Count + intent.Right.Count == 0)
            throw new InvalidOperationException("An environment needs at least one left or right context; an unconditional form needs no environment.");
        var pattern = RenderPattern(cache, intent.Left, intent.Right);
        var data = cache.LangProject.PhonologicalDataOA;
        var builder = new SoundSystemAuthoring.Builder(mintId);
        var id = builder.Create(PhPhonDataEnvironmentsOperationKinds.Create, SoundSystemAuthoring.Id(data),
            placement: SoundSystemAuthoring.Append(data.EnvironmentsOS));
        builder.Text(PhEnvironmentNameOperationKinds.SetName, id, cache, cache.DefaultAnalWs, intent.Name);
        builder.Add(PhEnvironmentStringRepresentationOperationKinds.Set, id, new
        {
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), text = pattern,
            left = ContextPayload(intent.Left), right = ContextPayload(intent.Right),
        });
        return builder.Operations;
    }

    internal static string RenderPattern(LcmCache cache, IReadOnlyList<EnvironmentContext> left, IReadOnlyList<EnvironmentContext> right) =>
        $"/ {Render(cache, left, true)} _ {Render(cache, right, false)}".TrimEnd();

    internal static Dictionary<string, string>[] ContextPayload(IReadOnlyList<EnvironmentContext> contexts) =>
        contexts.Select(context => context.Phoneme is { } phoneme ? new Dictionary<string, string> { ["phoneme"] = phoneme.Value } :
            context.NaturalClass is { } naturalClass ? new Dictionary<string, string> { ["naturalClass"] = naturalClass.Value } :
            context.BoundaryMarker is { } marker ? new Dictionary<string, string> { ["boundaryMarker"] = marker.Value } :
            new Dictionary<string, string> { ["boundary"] = context.Boundary! }).ToArray();

    private static string Render(LcmCache cache, IReadOnlyList<EnvironmentContext> contexts, bool left)
    {
        var tokens = new List<string>();
        for (var i = 0; i < contexts.Count; i++)
        {
            var context = contexts[i] ?? throw new InvalidOperationException("An environment context cannot be null.");
            if ((context.Phoneme is null ? 0 : 1) + (context.NaturalClass is null ? 0 : 1) +
                (context.Boundary is null ? 0 : 1) + (context.BoundaryMarker is null ? 0 : 1) != 1)
                throw new InvalidOperationException("Each context names exactly one phoneme, natural class or boundary marker.");
            if (context.Phoneme is { } phonemeId)
            {
                var phoneme = SoundSystemAuthoring.RequirePhoneme(cache, phonemeId);
                var code = phoneme.CodesOS.Select(c => c.Representation.get_String(cache.DefaultVernWs)?.Text)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                tokens.Add(SoundSystemAuthoring.RequireLiteral(code!));
            }
            else if (context.NaturalClass is { } classId)
            {
                var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, classId, "AuthorEnvironment");
                if (!cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Contains(naturalClass))
                    throw new InvalidOperationException("A context natural class must belong to the project's phonological data.");
                var abbreviation = naturalClass.Abbreviation.BestAnalysisAlternative.Text;
                SoundSystemAuthoring.RequireAbbreviation(abbreviation!);
                if (cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Count(n =>
                    n.Abbreviation.BestAnalysisAlternative.Text == abbreviation) != 1)
                    throw new InvalidOperationException("A context natural class abbreviation is ambiguous.");
                if (naturalClass is IPhNCSegments segments)
                {
                    if (segments.SegmentsRC.Count == 0) throw new InvalidOperationException("An empty natural class cannot condition an environment.");
                    foreach (var member in segments.SegmentsRC) SoundSystemAuthoring.RequirePhoneme(cache, SoundSystemAuthoring.Id(member));
                }
                else if (naturalClass is IPhNCFeatures features && (features.FeaturesOA is null || features.FeaturesOA.FeatureSpecsOC.Count == 0))
                    throw new InvalidOperationException("An empty feature class cannot condition an environment.");
                tokens.Add($"[{abbreviation}]");
            }
            else if (context.BoundaryMarker is { } markerId)
            {
                var marker = SoundSystemAuthoring.FirstSet(cache).BoundaryMarkersOC
                    .SingleOrDefault(item => CanonicalId.FromGuid(item.Guid) == markerId);
                if (marker is null)
                    throw new InvalidOperationException("A context boundary must be an existing marker in the first phoneme set.");
                var code = marker.CodesOS.Select(item => item.Representation.get_String(cache.DefaultVernWs)?.Text)
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
                if (code is null)
                    throw new InvalidOperationException("A context boundary marker needs a usable vernacular code.");
                tokens.Add(SoundSystemAuthoring.RequireLiteral(code));
            }
            else
            {
                if (context.Boundary is not ("word" or "morpheme"))
                    throw new InvalidOperationException("Boundary must be 'word' or 'morpheme'.");
                if (context.Boundary == "word" && i != (left ? 0 : contexts.Count - 1))
                    throw new InvalidOperationException("A word boundary must be at the outer edge of its context.");
                if (context.Boundary == "morpheme" && !SoundSystemAuthoring.FirstSet(cache).BoundaryMarkersOC.Any(b =>
                    b.Guid == LangProjectTags.kguidPhRuleMorphBdry))
                    throw new InvalidOperationException("A morpheme boundary needs a '+' boundary marker in the first phoneme set.");
                tokens.Add(context.Boundary == "word" ? "#" : "+");
            }
        }
        return string.Join(" ", tokens);
    }
}

internal static class SoundSystemAuthoring
{
    internal static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    internal static Placement? Append<T>(IEnumerable<T> sequence) where T : ICmObject =>
        sequence.LastOrDefault() is { } last ? new Placement(Id(last), null) : null;

    internal static IPhPhonemeSet FirstSet(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault() ??
        throw new InvalidOperationException("The project needs its first phoneme set before authoring phonemes.");

    internal static IReadOnlyList<OperationEnvelope> EnsureOwnedBoundaryMarker(LcmCache cache, Builder builder,
        IPhPhonemeSet? set, CanonicalId setId, Guid guid, string boundaryName, string representation)
    {
        var start = builder.Operations.Count;
        if (cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var existing))
        {
            if (existing is not IPhBdryMarker marker)
                throw new InvalidOperationException(
                    $"The reserved {boundaryName} boundary identity already exists as {existing.ClassName}, not a PhBdryMarker.");
            if (set is null || !set.BoundaryMarkersOC.Contains(marker))
                throw new InvalidOperationException(
                    $"The reserved {boundaryName} boundary marker must belong to the first phoneme set.");
            return Array.Empty<OperationEnvelope>();
        }

        var markerId = builder.Create(PhPhonemeSetBoundaryMarkersOperationKinds.Create, setId,
            entityId: CanonicalId.FromGuid(guid));
        builder.Text(PhTerminalUnitNameOperationKinds.SetName, markerId, cache, cache.DefaultVernWs, representation);
        var codeId = builder.Create(PhTerminalUnitCodesOperationKinds.Create, markerId);
        builder.Text(PhCodeRepresentationOperationKinds.SetRepresentation, codeId, cache,
            cache.DefaultVernWs, representation);
        return builder.Operations.Skip(start).ToArray();
    }

    internal static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name must not be blank.");
    }

    internal static string RequireLiteral(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(c => char.IsWhiteSpace(c) || "[]()#_/*+|\\".Contains(c)))
            throw new InvalidOperationException("A grapheme must be nonempty and contain no whitespace or environment syntax characters.");
        return text.Normalize(NormalizationForm.FormD);
    }

    internal static void RequireAbbreviation(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Regex.IsMatch(text, @"\A[\p{L}\p{N}][\p{L}\p{N}\p{M}-]*\z"))
            throw new InvalidOperationException("A natural class abbreviation needs letters, digits or hyphens only.");
    }

    internal static IPhPhoneme RequirePhoneme(LcmCache cache, CanonicalId id)
    {
        var phoneme = ReferenceFieldLowering.Resolve<IPhPhoneme>(cache, id, "sound-system composer");
        if (!FirstSet(cache).PhonemesOC.Contains(phoneme))
            throw new InvalidOperationException("A phoneme must belong to the parser's first phoneme set.");
        if (!phoneme.CodesOS.Any(c => !string.IsNullOrWhiteSpace(c.Representation.get_String(cache.DefaultVernWs)?.Text)))
            throw new InvalidOperationException("A phoneme needs a usable vernacular code before it can be referenced.");
        foreach (var code in phoneme.CodesOS)
            RequireLiteral(code.Representation.get_String(cache.DefaultVernWs)?.Text!);
        return phoneme;
    }

    internal static void ValidateFeatures(LcmCache cache, IReadOnlyList<PhonologicalFeatureValue>? values)
    {
        if (values is null) return;
        if (values.Select(v => v.Feature).Distinct().Count() != values.Count)
            throw new InvalidOperationException("A feature description cannot choose two values for the same feature.");
        foreach (var pair in values)
        {
            var feature = ReferenceFieldLowering.Resolve<IFsClosedFeature>(cache, pair.Feature, "phonological feature value");
            var value = ReferenceFieldLowering.Resolve<IFsSymFeatVal>(cache, pair.Value, "phonological feature value");
            if (!cache.LangProject.PhFeatureSystemOA.FeaturesOC.Contains(feature) || !feature.ValuesOC.Contains(value))
                throw new InvalidOperationException("Each phonological value must belong to its named closed feature in the phonological feature system.");
        }
    }

    internal sealed class Builder(Func<CanonicalId>? mintId)
    {
        private readonly Func<CanonicalId> _mint = mintId ?? (() => CanonicalId.Mint());
        private readonly Dictionary<CanonicalId, CanonicalId> _creates = new();
        internal List<OperationEnvelope> Operations { get; } = new();

        internal CanonicalId Create(string kind, CanonicalId owner, object? after = null,
            Placement? placement = null, CanonicalId? entityId = null)
        {
            var id = entityId ?? _mint();
            var operation = Add(kind, owner, after ?? new { }, id, placement);
            _creates.Add(id, operation.OperationId);
            return id;
        }

        internal OperationEnvelope Add(string kind, CanonicalId target, object after, CanonicalId? entityId = null, Placement? placement = null)
        {
            var referenced = new List<CanonicalId> { target };
            if (placement?.After is { } left) referenced.Add(left);
            if (placement?.Before is { } right) referenced.Add(right);
            var dependencies = referenced.Where(_creates.ContainsKey).Select(id => _creates[id]).Distinct()
                .Select(id => new OperationDependency(id)).ToArray();
            var operation = new OperationEnvelope(_mint(), kind, entityId, target, JsonSerializer.SerializeToElement(after),
                placement, dependencies, rationale: "Authored sound-system intent.");
            Operations.Add(operation);
            return operation;
        }

        internal void Text(string kind, CanonicalId target, LcmCache cache, int ws, string text) =>
            Add(kind, target, new { ws = cache.WritingSystemFactory.GetStrFromWs(ws), text });

        internal void Features(LcmCache cache, CanonicalId target, string kind, IReadOnlyList<PhonologicalFeatureValue> values)
        {
            var structure = Create(kind, target);
            foreach (var pair in values)
            {
                var spec = Create(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, structure);
                Add(FsFeatureSpecificationFeatureOperationKinds.SetFeature, spec, new { @ref = pair.Feature.Value });
                Add(FsClosedValueValueOperationKinds.SetValue, spec, new { @ref = pair.Value.Value });
            }
        }
    }
}
