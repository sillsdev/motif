using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>A second meaning goes onto the entry under an analysis it has, and the refusals name the next command.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AddSenseComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _verb;
    private readonly CanonicalId _particle;
    private readonly CanonicalId _stem;
    private readonly CanonicalId _verbMsa;

    public AddSenseComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _verb = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        _particle = AddCategory(_cache, "Particle");
        (_stem, _verbMsa, _) = AddStem(_cache, "mao", _verb);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ASecondSenseSharesTheEntrysOnlyAnalysisWithoutAHomograph()
    {
        var entry = Get<ILexEntry>(_cache, _stem);
        var entriesBefore = _cache.LangProject.LexDbOA.Entries.Count();
        ApplyOps(_cache, AddSenseComposer.Build(_cache,
            new(_stem, "be extinguished", Anal(_cache), "go out, of fire")));

        Assert.Equal(entriesBefore, _cache.LangProject.LexDbOA.Entries.Count());
        Assert.Equal(2, entry.SensesOS.Count);
        Assert.Single(entry.MorphoSyntaxAnalysesOC);
        var added = entry.SensesOS[1];
        Assert.Equal("be extinguished", added.Gloss.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal("go out, of fire", added.Definition.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal(_verbMsa.ToGuid(), added.MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void AnEntryWithSeveralAnalysesRefusesAndGivesTheIntentToRunWithEachAnalysisId()
    {
        var second = Created(ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_stem,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _particle), Gloss: "so", AnalysisWritingSystem: Anal(_cache)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            AddSenseComposer.Build(_cache, new(_stem, "thus", Anal(_cache))));
        Assert.Contains("has 2 analyses", refused.Message, StringComparison.Ordinal);
        Assert.Contains(_verbMsa.Value, refused.Message, StringComparison.Ordinal);
        Assert.Contains(second.Value, refused.Message, StringComparison.Ordinal);
        Assert.Contains("(stem, Particle)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("compose-add-sense with {\"entry\":", refused.Message, StringComparison.Ordinal);

        ApplyOps(_cache, AddSenseComposer.Build(_cache, new(_stem, "thus", Anal(_cache), Analysis: second)));
        Assert.Equal(second.ToGuid(), Get<ILexEntry>(_cache, _stem).SensesOS.Last().MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void AForeignAnalysisASameAnalysisDuplicateGlossAndAnUnknownWritingSystemAreRefusedWithTheNextStep()
    {
        var other = AddStem(_cache, "kaun", _verb);
        var foreign = Assert.Throws<InvalidOperationException>(() =>
            AddSenseComposer.Build(_cache, new(_stem, "x", Anal(_cache), Analysis: other.Msa)));
        Assert.Contains("is not an analysis of entry", foreign.Message, StringComparison.Ordinal);
        Assert.Contains(_verbMsa.Value, foreign.Message, StringComparison.Ordinal);
        Assert.Contains("compose-add-morphosyntax-analysis", foreign.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            AddSenseComposer.Build(_cache, new(_stem, "mao", Anal(_cache))));
        Assert.Contains("already has a sense glossed 'mao' under that analysis", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("compose-edit-gloss", duplicate.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            AddSenseComposer.Build(_cache, new(_stem, "x", "zz-Latn-x-nowhere")));
        Assert.Throws<InvalidOperationException>(() => AddSenseComposer.Build(_cache,
            new(CanonicalId.Mint(), "x", Anal(_cache))));
    }

    [Fact]
    public void ASameGlossUnderAnotherAnalysisIsAllowedAndUnderTheSameOneIsRefused()
    {
        var particle = Created(ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_stem,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _particle), Gloss: "so", AnalysisWritingSystem: Anal(_cache)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        Assert.Throws<InvalidOperationException>(() =>
            AddSenseComposer.Build(_cache, new(_stem, "mao", Anal(_cache), Analysis: _verbMsa)));
        ApplyOps(_cache, AddSenseComposer.Build(_cache, new(_stem, "mao", Anal(_cache), Analysis: particle)));

        var entry = Get<ILexEntry>(_cache, _stem);
        Assert.Equal(particle.ToGuid(), entry.SensesOS.Last().MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal("mao", entry.SensesOS.Last().Gloss.get_String(_cache.DefaultAnalWs).Text);
    }

    [Fact]
    public void TheIntentIsClosedAndNeedsEntryGlossAndWritingSystem()
    {
        var missing = JsonSerializer.SerializeToElement(new { entry = _stem.Value, gloss = "x" });
        Assert.ThrowsAny<Exception>(() => AddSenseIntentParser.Parse(missing));
        var extra = JsonSerializer.SerializeToElement(new { entry = _stem.Value, gloss = "x", analysisWs = "en", msa = 1 });
        Assert.Throws<ContractParseException>(() => AddSenseIntentParser.Parse(extra));
    }

    [Fact]
    public void ASenseCanBePointedAtAnotherAnalysisOfItsEntryAndTheOrphanedAnalysisIsNamedInTheRationale()
    {
        var particle = Created(ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(_stem,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _particle), Gloss: "so", AnalysisWritingSystem: Anal(_cache)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var entry = Get<ILexEntry>(_cache, _stem);
        var original = Id(entry.SensesOS[0]);

        var operations = SetSenseMsaComposer.Build(_cache, new(original, particle));
        Assert.Contains("LibLCM removes that analysis", Assert.Single(operations).Rationale, StringComparison.Ordinal);
        ApplyOps(_cache, operations);

        Assert.All(entry.SensesOS, sense => Assert.Equal(particle.ToGuid(), sense.MorphoSyntaxAnalysisRA.Guid));
        Assert.Single(entry.MorphoSyntaxAnalysesOC);
        Assert.Equal("mao", entry.SensesOS[0].Gloss.get_String(_cache.DefaultAnalWs).Text);
    }

    [Fact]
    public void ARepointingRefusesForeignAnalysesSameAnalysisAndNonSensesAndNamesTheNextCommand()
    {
        var other = AddStem(_cache, "kaun", _verb);
        var sense = Id(Get<ILexEntry>(_cache, _stem).SensesOS[0]);

        var foreign = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(sense, other.Msa)));
        Assert.Contains("is not an analysis of the sense's entry", foreign.Message, StringComparison.Ordinal);
        Assert.Contains(_verbMsa.Value, foreign.Message, StringComparison.Ordinal);
        Assert.Contains("compose-add-morphosyntax-analysis with {\"entry\":\"" + _stem.Value, foreign.Message, StringComparison.Ordinal);
        Assert.Contains("\"senses\":[\"" + sense.Value + "\"]", foreign.Message, StringComparison.Ordinal);

        var same = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(sense, _verbMsa)));
        Assert.Contains("already uses analysis", same.Message, StringComparison.Ordinal);

        var notASense = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(_stem, _verbMsa)));
        Assert.Contains("'sense' must name a sense", notASense.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepointingKeepsAnAffixSenseOnAnAffixAnalysis()
    {
        var suffix = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("ig",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "not", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _verb))));
        var entryId = Created(suffix, LexDbEntriesOperationKinds.Create);
        var second = Created(ApplyOps(_cache, AddMorphoSyntaxAnalysisComposer.Build(_cache, new(entryId,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _particle), Gloss: "no", AnalysisWritingSystem: Anal(_cache)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var entry = Get<ILexEntry>(_cache, entryId);

        ApplyOps(_cache, SetSenseMsaComposer.Build(_cache, new(Id(entry.SensesOS[0]), second)));
        Assert.Equal(second.ToGuid(), entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void ARepointingRefusesAnAffixAnalysisForAStemSenseAndAStemAnalysisForAnAffixSense()
    {
        var suffix = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("ig",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "not", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _verb))));
        var affix = Get<ILexEntry>(_cache, Created(suffix, LexDbEntriesOperationKinds.Create));
        var stem = Get<ILexEntry>(_cache, _stem);
        IMoInflAffMsa affixOnStem = null!;
        IMoStemMsa stemOnAffix = null!;
        UndoableUnitOfWorkHelper.Do("undo", "redo", _cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            affixOnStem = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            stem.MorphoSyntaxAnalysesOC.Add(affixOnStem);
            stemOnAffix = _cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            affix.MorphoSyntaxAnalysesOC.Add(stemOnAffix);
        });

        var toAffix = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(Id(stem.SensesOS[0]), Id(affixOnStem))));
        Assert.Contains("a stem form", toAffix.Message, StringComparison.Ordinal);
        var toStem = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(Id(affix.SensesOS[0]), Id(stemOnAffix))));
        Assert.Contains("an affix form", toStem.Message, StringComparison.Ordinal);

        UndoableUnitOfWorkHelper.Do("undo", "redo", _cache.ServiceLocator.GetInstance<IActionHandler>(), () => stem.LexemeFormOA = null);
        var noForm = Assert.Throws<InvalidOperationException>(() =>
            SetSenseMsaComposer.Build(_cache, new(Id(stem.SensesOS[0]), Id(affixOnStem))));
        Assert.Contains("has no lexeme form", noForm.Message, StringComparison.Ordinal);
    }
}
