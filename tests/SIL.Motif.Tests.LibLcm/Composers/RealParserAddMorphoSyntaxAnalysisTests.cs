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

/// <summary>Checks with PanGloss that one suffix entry parses on two categories once it holds two analyses.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserAddMorphoSyntaxAnalysisTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    [RealParserFact]
    public async Task AVerbSuffixParsesOnCopulaStemsOnlyAfterTheEntryGainsACopulaAnalysis()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var copula = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Copula", ws, "COP"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kaun", verb).Form] = "kaun";
        names[AddStem(cache, "lilit", copula).Form] = "lilit";
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ig", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphSuffix), "not", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "ig";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["kaun+ig"], await Readings(tracer, path, "kaunig", names));
        Assert.Empty(await Readings(tracer, path, "lilitig", names));

        var second = Created(ApplyOps(cache, AddMorphoSyntaxAnalysisComposer.Build(cache, new(
            Created(created, LexDbEntriesOperationKinds.Create), new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, copula),
            Gloss: "not (copula)", AnalysisWritingSystem: ws))), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var entry = Get<ILexEntry>(cache, Created(created, LexDbEntriesOperationKinds.Create));
        Assert.Equal(2, entry.MorphoSyntaxAnalysesOC.Count);
        Assert.Equal(second.ToGuid(), entry.SensesOS[1].MorphoSyntaxAnalysisRA.Guid);
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["kaun+ig"], await Readings(tracer, path, "kaunig", names));
        Assert.Equal(["lilit+ig"], await Readings(tracer, path, "lilitig", names));
    }

    [RealParserFact]
    public async Task ADerivationalPrefixWithOneAnalysisPerHostClassAttachesToEachClassAndNoOther()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var noun = AddCategory(cache, "Noun");
        var first = AddInflectionClass(cache, verb, "Class 1");
        var second = AddInflectionClass(cache, verb, "Class 2");
        var third = AddInflectionClass(cache, verb, "Class 3");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kaun", verb, first).Form] = "kaun";
        names[AddStem(cache, "palit", verb, second).Form] = "palit";
        names[AddStem(cache, "gatu", verb, third).Form] = "gatu";
        names[AddStem(cache, "lilit", verb).Form] = "lilit";
        var subclass = Created(ApplyOps(cache, AuthorInflectionClassComposer.Build(cache,
            new(verb, "Class 1a", ws, Parent: first))), MoInflClassSubclassesOperationKinds.Create);
        names[AddStem(cache, "tunal", verb, subclass).Form] = "tunal";
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("pag", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "agentive", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun))));
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create), FromInflectionClass: first)));
        new FwDataProjectLoader().Save(cache);
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "pagpalit", names));
        Assert.Equal(["pag+tunal"], await Readings(tracer, path, "pagtunal", names));

        ApplyOps(cache, AddMorphoSyntaxAnalysisComposer.Build(cache, new(entry,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun), Gloss: "agentive (class 2)",
            AnalysisWritingSystem: ws, FromInflectionClass: second)));
        Assert.Equal(2, Get<ILexEntry>(cache, entry).MorphoSyntaxAnalysesOC.OfType<IMoDerivAffMsa>().Count());
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));
        Assert.Empty(await Readings(tracer, path, "paggatu", names));
        Assert.Empty(await Readings(tracer, path, "paglilit", names));
    }

    [RealParserFact]
    public async Task OneRelativeEntryWithTwoAnalysesThatDifferOnlyByFeatureRequirementsFiresEachOnItsOwnFeatureOnly()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, ["m", "a", "p", "i", "k", "e", "t", "u", "n"]);
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
        var msas = new Dictionary<string, CanonicalId>();
        CanonicalId Affix(string form, CanonicalId slot)
        {
            var entry = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
            names[Created(entry, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = form;
            msas[form] = Created(entry, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
            return Created(entry, LexDbEntriesOperationKinds.Create);
        }
        Affix("ma", subjectSlot);
        Affix("pi", subjectSlot);
        var relative = Affix("ke", relativeSlot);

        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("relative concord", ws, [new("Group one", "g1"), new("Group two", "g2")], "rc")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Value(string text) => values.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);
        void Require(CanonicalId msa, string group)
        {
            var structure = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(msa))),
                MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats);
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(structure, feature, Value(group))));
        }
        Require(msas["ma"], "Group one");
        Require(msas["pi"], "Group two");

        // The entry's only analysis is identical to a new one until it carries a requirement; then the two differ.
        var plain = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(cache, new(relative,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [relativeSlot]),
            Gloss: "again", AnalysisWritingSystem: ws)));
        Assert.Contains("identical analysis", plain.Message, StringComparison.Ordinal);
        Require(msas["ke"], "Group one");
        var second = Created(ApplyOps(cache, AddMorphoSyntaxAnalysisComposer.Build(cache, new(relative,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [relativeSlot]),
            Gloss: "ke two", AnalysisWritingSystem: ws))), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Require(second, "Group two");
        Assert.Equal(2, Get<ILexEntry>(cache, relative).MorphoSyntaxAnalysesOC.Count);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal([msas["ke"].Value], await MsasOf(tracer, path, "maketun"));
        Assert.Equal([second.Value], await MsasOf(tracer, path, "piketun"));
    }

    private async Task<string[]> MsasOf(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        var found = outcome.Document!.Analyses.SelectMany(analysis => analysis.Morphs)
            .Where(morph => morph.Form == "ke").Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.MsaId!)).Value)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {string.Join(" | ", found)}");
        return found;
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
