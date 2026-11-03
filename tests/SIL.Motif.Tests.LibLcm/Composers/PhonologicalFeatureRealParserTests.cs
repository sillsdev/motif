using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// A synthetic grammar authored only through the phonological-feature composers: an archiphoneme N, a rule
/// N -> m before a class defined by place features, and two stems that begin with N.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PhonologicalFeatureRealParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AFeatureDefinedClassMatchesInARuleAndFollowsEditsToPhonemesAndToTheClass()
    {
        using var cache = pristine.NewScratch();
        var all = new List<OperationEnvelope>();
        var featureOperations = AuthorPhonologicalFeatureComposer.Build(cache, new("place",
            [new("labial", "lab"), new("coronal", "cor")], "plc"));
        SoundSystemComposerTests.Execute(cache, featureOperations);
        all.AddRange(featureOperations);
        var place = featureOperations.First().EntityId!.Value;
        var values = featureOperations.Where(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(o => o.EntityId!.Value).ToArray();
        PhonologicalFeatureValue labial = new(place, values[0]), coronal = new(place, values[1]);

        // A phoneme with no value for a feature unifies with every value, so each carries a distinct manner.
        var mannerOperations = AuthorPhonologicalFeatureComposer.Build(cache, new("manner",
            [new("stop"), new("nasal"), new("vowel"), new("archi")]));
        SoundSystemComposerTests.Execute(cache, mannerOperations);
        all.AddRange(mannerOperations);
        var manner = mannerOperations.First().EntityId!.Value;
        var mannerValues = mannerOperations.Where(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(o => new PhonologicalFeatureValue(manner, o.EntityId!.Value)).ToArray();
        PhonologicalFeatureValue stop = mannerValues[0], nasal = mannerValues[1], vowel = mannerValues[2], archi = mannerValues[3];

        var phonemes = new Dictionary<string, CanonicalId>();
        // p and t start with each other's place; the edits below set both right.
        foreach (var (letter, features) in new (string, PhonologicalFeatureValue[])[]
                 { ("p", [coronal, stop]), ("t", [labial, stop]), ("m", [labial, nasal]), ("N", [archi]), ("a", [vowel]) })
            phonemes.Add(letter, SoundSystemComposerTests.Compose(cache, all,
                () => AuthorPhonemeComposer.Build(cache, new(letter, [letter], features))));
        var labials = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorNaturalClassComposer.Build(cache, new("place class", "PLC", Features: [labial])));
        var rule = AuthorPhonologicalRuleComposer.Build(cache, new AuthorPhonologicalRuleIntent(
            "archiphoneme N becomes m before the class", PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phonemes["N"])], [new(Phoneme: phonemes["m"])], [], [new(NaturalClass: labials)]));
        SoundSystemComposerTests.Execute(cache, rule);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        var first = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var second = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId);
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(first.Guid), stemType, ws, "Npa")));
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(second.Guid), stemType, ws, "Nta")));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single());
        });
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        async Task<bool> ParsesAs(string word, ILexEntry entry)
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            return outcome.Document!.Analyses.SelectMany(a => a.Morphs)
                .Any(morph => morph.FormId == entry.LexemeFormOA!.Guid.ToString("D"));
        }

        // Before the edits the class holds t and not p, so the rule applies in the wrong place.
        Assert.False(await ParsesAs("mpa", first));
        Assert.True(await ParsesAs("mta", second));

        var setP = EditPhonemeFeaturesComposer.Build(cache, new(phonemes["p"], [labial], []));
        Assert.DoesNotContain(setP, o => o.Kind == PhPhonemeFeaturesOperationKinds.Create);
        SoundSystemComposerTests.Execute(cache, setP);
        var setT = EditPhonemeFeaturesComposer.Build(cache, new(phonemes["t"], [coronal], []));
        Assert.DoesNotContain(setT, o => o.Kind == PhPhonemeFeaturesOperationKinds.Create);
        SoundSystemComposerTests.Execute(cache, setT);
        loader.Save(cache);
        Assert.True(await ParsesAs("mpa", first));
        Assert.False(await ParsesAs("mta", second));

        SoundSystemComposerTests.Execute(cache, EditNaturalClassFeaturesComposer.Build(cache,
            new(labials, [coronal], [])));
        loader.Save(cache);
        Assert.False(await ParsesAs("mpa", first));
        Assert.True(await ParsesAs("mta", second));
    }
}
