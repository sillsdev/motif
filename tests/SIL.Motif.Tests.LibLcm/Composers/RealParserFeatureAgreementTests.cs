using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Two inflectional affixes in different slots agree when both require the same feature value: the pinned parser
/// carries each affix's InflFeats requirement onto the word, so a mismatch has no analysis. Removing a requirement
/// takes the agreement away again.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserFeatureAgreementTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["m", "a", "p", "i", "k", "e", "l", "o", "t", "u", "n"];

    [RealParserFact]
    public async Task ASubjectAndARelativeMarkerAgreeThroughASharedFeatureAndRemovingTheRequirementEndsIt()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var subjectSlot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Subject", ws, false))), PartOfSpeechAffixSlotsOperationKinds.Create);
        var relativeSlot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Relative", ws, false))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [subjectSlot, relativeSlot], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "tun", verb).Form] = "tun";
        var affixes = new Dictionary<string, (CanonicalId Msa, CanonicalId Form)>();
        foreach (var (form, slot) in new[] { ("ma", subjectSlot), ("pi", subjectSlot), ("ke", relativeSlot), ("lo", relativeSlot) })
        {
            var entry = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
            var identity = (Msa: Created(entry, LexEntryMorphoSyntaxAnalysesOperationKinds.Create),
                Form: Created(entry, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
            affixes[form] = identity;
            names[identity.Form] = form;
        }

        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("relative concord", ws, [new("Group one", "g1"), new("Group two", "g2")], "rc")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Value(string text) => values.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);
        var structures = new Dictionary<string, CanonicalId>();
        foreach (var (form, group) in new[] { ("ma", "Group one"), ("ke", "Group one"), ("pi", "Group two"), ("lo", "Group two") })
        {
            var structure = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache,
                new(affixes[form].Msa))), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats);
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(structure, feature, Value(group))));
            structures[form] = structure;
        }
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["ma+ke+tun"], await Readings(tracer, path, "maketun", names));
        Assert.Equal(["pi+lo+tun"], await Readings(tracer, path, "pilotun", names));
        Assert.Empty(await Readings(tracer, path, "malotun", names));
        Assert.Empty(await Readings(tracer, path, "piketun", names));

        var removal = ApplyOps(cache, RemoveFeatureValueComposer.Build(cache, new(structures["ke"], feature)));
        Assert.Single(removal.Operations, op => op.Kind == FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs);
        Assert.Empty(Get<IFsFeatStruc>(cache, structures["ke"]).FeatureSpecsOC);
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["ma+ke+tun"], await Readings(tracer, path, "maketun", names));
        Assert.Equal(["pi+ke+tun"], await Readings(tracer, path, "piketun", names));
        Assert.Empty(await Readings(tracer, path, "malotun", names));
    }

    private async Task<string[]> Readings(PanGlossTracer tracer, string path, string word,
        IReadOnlyDictionary<CanonicalId, string> names)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }
}
