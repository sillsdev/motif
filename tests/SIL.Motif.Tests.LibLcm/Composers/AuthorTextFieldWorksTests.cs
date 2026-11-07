using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorTextFieldWorksTests(PristineProjectFixture pristine)
{
    [Fact]
    public void LaterExactCaseRefreshesEarlierBindingsAndSurvivesReopen()
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var baselines = new[] { "kay", "Kay", "a Kay" };
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = baselines.Select(text => new { segments = new[] { new { text } } }).ToArray() }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var textId = operations.Last().EntityId!.Value.ToGuid();
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var text = reopened.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId);
        var paragraphs = text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        Assert.Equal(baselines, paragraphs.Select(p => p.Contents.Text));
        var words = paragraphs.Select(p => p.SegmentsOS.SelectMany(s => s.AnalysesRS)
            .Where(a => a.Wordform is not null).Select(a => a.Wordform).ToArray()).ToArray();
        Assert.Equal("kay", words[0][0].Form.get_String(reopened.DefaultVernWs).Text);
        Assert.Equal("Kay", words[1][0].Form.get_String(reopened.DefaultVernWs).Text);
        Assert.Equal(words[1][0].Guid, words[2][1].Guid);
        Assert.NotEqual(words[0][0].Guid, words[1][0].Guid);
        var forms = reopened.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .Select(w => w.Form.get_String(reopened.DefaultVernWs).Text).ToArray();
        Assert.Single(forms, form => form == "kay");
        Assert.Single(forms, form => form == "Kay");
    }

    [Fact]
    public void ExactCaseAddedAfterCompositionStillRefusesDrift()
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = new[] { "kay", "Kay" }.Select(text => new { segments = new[] { new { text } } }).ToArray() }));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString("Kay", cache.DefaultVernWs)));
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, Proposal(operations)));
        Assert.Contains("word bindings changed", failure.Message);
    }

    [Fact]
    public void LateCaseVariantRefreshesA398ParagraphText()
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var baselines = Enumerable.Repeat("filler.", 398).ToArray();
        baselines[23] = "kay";
        baselines[40] = "Kay";
        baselines[397] = "a Kay";
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = baselines.Select(text => new { segments = new[] { new { text } } }).ToArray() }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        var paragraphs = text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        Assert.Equal(baselines, paragraphs.Select(p => p.Contents.Text));
        var earlier = paragraphs[40].SegmentsOS[0].AnalysesRS.First(a => a.Wordform is not null).Wordform;
        var later = paragraphs[397].SegmentsOS[0].AnalysesRS.Last(a => a.Wordform is not null).Wordform;
        Assert.Equal(later.Guid, earlier.Guid);
    }

    [Theory]
    [InlineData("kay|Kay|a Kay")]
    [InlineData("Kay|a Kay")]
    [InlineData("kay|a Kay")]
    [InlineData("kay|Kay")]
    [InlineData("kay|a Kay|Kay")]
    [InlineData("kay|Kay|Kay|a Kay")]
    public void CaseInventoryAndOrderControlsComposeAndDryRun(string input)
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var baselines = input.Split('|');
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = baselines.Select(text => new { segments = new[] { new { text } } }).ToArray() }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        Assert.Equal(baselines, text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().Select(p => p.Contents.Text));
        if (input == "kay|Kay|Kay|a Kay")
        {
            var paragraphs = text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().ToArray();
            var first = paragraphs[1].SegmentsOS[0].AnalysesRS.First(a => a.Wordform is not null).Wordform.Guid;
            var second = paragraphs[2].SegmentsOS[0].AnalysesRS.First(a => a.Wordform is not null).Wordform.Guid;
            var last = paragraphs[3].SegmentsOS[0].AnalysesRS.Last(a => a.Wordform is not null).Wordform.Guid;
            Assert.Equal(last, first);
            Assert.Equal(last, second);
        }
    }

    [Theory]
    [InlineData("fr", "Kay.")]
    [InlineData("fr", "\"Kay.")]
    [InlineData("ceb", "Kay.")]
    [InlineData("ceb", "\"Kay.")]
    public void LaterSentencePreservesSegmentsAndAnnotations(string ws, string initial)
    {
        using var cache = pristine.NewScratch();
        cache.ServiceLocator.WritingSystemManager.GetOrSet(ws, out var writingSystem);
        if (!cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Any(system => system.Id == ws))
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(writingSystem));
        cache.ServiceLocator.WritingSystemManager.Save();
        new FwDataProjectLoader().Save(cache);
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = new object[] {
                new { segments = new[] { new { text = "kay" } } },
                new { segments = new[] { new { text = initial } } },
                new { segments = new[] { new { text = "a?", freeTranslation = "first", notes = new[] { "note" } },
                    new { text = "Kay.", freeTranslation = "second", notes = Array.Empty<string>() } } } } }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        var paragraphs = text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        Assert.Equal(new[] { "kay", initial, "a? Kay." }, paragraphs.Select(p => p.Contents.Text));
        Assert.Equal(2, paragraphs[2].SegmentsOS.Count);
        Assert.Equal("first", paragraphs[2].SegmentsOS[0].FreeTranslation.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("note", Assert.Single(paragraphs[2].SegmentsOS[0].NotesOS).Content.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("second", paragraphs[2].SegmentsOS[1].FreeTranslation.get_String(cache.DefaultAnalWs).Text);
        var earlier = paragraphs[1].SegmentsOS.SelectMany(s => s.AnalysesRS).First(a => a.Wordform is not null).Wordform;
        var later = paragraphs[2].SegmentsOS[1].AnalysesRS.First(a => a.Wordform is not null).Wordform;
        Assert.Equal(later.Guid, earlier.Guid);
    }

    [Theory]
    [InlineData("alpha beta", "alpha beta.")]
    [InlineData("mag-ágaw", "mag-ágaw.")]
    [InlineData("alpha, beta", "alpha, beta.")]
    public void ExistingPhraseWordformsFollowParagraphParserAndSurviveReopen(string phrase, string baseline)
    {
        using var cache = pristine.NewScratch();
        phrase = phrase.Normalize(NormalizationForm.FormD);
        baseline = baseline.Normalize(NormalizationForm.FormD);
        IWfiWordform existing = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            existing = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(phrase, cache.DefaultVernWs)));
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T",
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            paragraphs = new[] { new { segments = new[] { new { text = baseline } } } } }));
        Assert.Single(operations);
        Assert.Single(cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances());
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var textId = operations.Last().EntityId!.Value.ToGuid();
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var text = reopened.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId);
        var para = Assert.Single(text.ContentsOA.ParagraphsOS.OfType<IStTxtPara>());
        Assert.Equal(baseline, para.Contents.Text);
        Assert.Equal(existing.Guid, Assert.Single(para.SegmentsOS.SelectMany(s => s.AnalysesRS),
            a => a.Wordform is not null).Wordform.Guid);
    }

    [Fact]
    public void PhraseAddedAfterCompositionIsNotSilentlyBoundToOtherWordforms()
    {
        using var cache = pristine.NewScratch();
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T",
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            paragraphs = new[] { new { segments = new[] { new { text = "alpha beta." } } } } }));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("alpha beta", cache.DefaultVernWs)));
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, Proposal(operations)));
        Assert.Contains("FieldWorks", failure.Message);
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
    }

    [Fact]
    public void ChangedPhraseBindingWithTheSameWordCountIsRejected()
    {
        using var cache = pristine.NewScratch();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("beta gamma", cache.DefaultVernWs)));
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T",
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            paragraphs = new[] { new { segments = new[] { new { text = "alpha beta gamma." } } } } }));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("alpha beta", cache.DefaultVernWs)));
        var failure = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, Proposal(operations)));
        Assert.Contains("word bindings changed", failure.Message);
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
    }

    [Fact]
    public void KnownWritingSystemOutsideProjectListsCanBeUsedForPlanning()
    {
        using var cache = pristine.NewScratch();
        cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var writingSystem);
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws = writingSystem.Id,
            paragraphs = new[] { new { segments = new[] { new { text = "bonjour." } } } } }));
        Assert.Contains(operations, operation => operation.Kind == LangProjectWordformsOperationKinds.Create);
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TokenizationAndSegmentsAgreeWithParagraphParserIncludingConfiguredHyphens(bool hyphenIsWordForming)
    {
        using var cache = pristine.NewScratch();
        var writingSystem = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
        writingSystem.CharacterSets.Clear();
        if (hyphenIsWordForming)
            writingSystem.CharacterSets.Add(new CharacterSetDefinition("main") { Characters = { "-" } });
        cache.ServiceLocator.WritingSystemManager.Save();
        var baseline = "(nakapaluwas) mag-ágaw ug, ? Ug! 123 😊.".Normalize(NormalizationForm.FormD);
        var intent = Parse(new { title = "T", ws = writingSystem.Id,
            paragraphs = new[] { new { segments = new[] { new { text = baseline } } } } });
        var wordformsBefore = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
        var operations = AuthorTextDraftBuilder.Build(cache, intent);
        Assert.Equal(wordformsBefore, cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count);
        Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var authored = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        var para = Assert.Single(authored.ContentsOA.ParagraphsOS.OfType<IStTxtPara>());
        IStTxtPara expected = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
            text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            expected = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
            text.ContentsOA.ParagraphsOS.Add(expected);
            expected.Contents = TsStringUtils.MakeString(baseline, cache.DefaultVernWs);
            ParagraphParser.ParseParagraph(expected);
        });
        Assert.Equal(expected.SegmentsOS.Select(s => s.BeginOffset), para.SegmentsOS.Select(s => s.BeginOffset));
        Assert.Equal(expected.SegmentsOS.SelectMany(s => s.AnalysesRS).Select(Form), para.SegmentsOS.SelectMany(s => s.AnalysesRS).Select(Form));
        var words = para.SegmentsOS.SelectMany(s => s.AnalysesRS).OfType<IWfiWordform>()
            .Select(w => w.Form.get_String(cache.DefaultVernWs).Text).ToArray();
        Assert.Contains("nakapaluwas", words);
        Assert.Contains("ug", words);
        Assert.DoesNotContain("?", words);
        Assert.Equal(hyphenIsWordForming, words.Contains("mag-ágaw"));
        Assert.Equal(dryRun.ExpectedEffects.Last().After["structure"], receipt.ActualEffects.Last().After["structure"]);

        string Form(IAnalysis analysis) => analysis is IPunctuationForm punctuation ? "punct:" + punctuation.Form.Text
            : "word:" + analysis.Wordform.Form.get_String(cache.DefaultVernWs).Text;
    }

    [Fact]
    public void TranslationsNotesPunctuationAndIdentitiesSurviveSaveAndReopen()
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new
        {
            title = "T", ws, analysisWs = analysis,
            paragraphs = new[] { new { segments = new[]
            {
                new { text = "(nakapaluwas) ug?", freeTranslation = "Was someone rescued?", literalTranslation = "rescued and?", notes = new[] { "First note.", "Second note." } },
                new { text = "ug!", freeTranslation = "and!", literalTranslation = "and", notes = Array.Empty<string>() },
            } } },
        }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var textId = operations.Last().EntityId!.Value.ToGuid();
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId);
        var before = TextOwningSnapshots.Summary(cache, text);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var read = reopened.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId);
        var paragraph = Assert.Single(read.ContentsOA.ParagraphsOS.OfType<IStTxtPara>());
        Assert.Equal("(nakapaluwas) ug? ug!", paragraph.Contents.Text);
        Assert.Equal(2, paragraph.SegmentsOS.Count);
        Assert.Equal("Was someone rescued?", paragraph.SegmentsOS[0].FreeTranslation.get_String(reopened.DefaultAnalWs).Text);
        Assert.Equal("rescued and?", paragraph.SegmentsOS[0].LiteralTranslation.get_String(reopened.DefaultAnalWs).Text);
        Assert.Equal(["First note.", "Second note."], paragraph.SegmentsOS[0].NotesOS.Select(n => n.Content.get_String(reopened.DefaultAnalWs).Text));
        Assert.Equal(before.OrderBy(p => p.Key), TextOwningSnapshots.Summary(reopened, read).OrderBy(p => p.Key));
        NonUndoableUnitOfWorkHelper.Do(reopened.ActionHandlerAccessor, () =>
            paragraph.SegmentsOS[0].NotesOS[0].Content.set_String(reopened.DefaultAnalWs, "Changed note."));
        Assert.NotEqual(before["structure"], TextOwningSnapshots.Summary(reopened, read)["structure"]);
    }

    [Fact]
    public void MultilineAnnotationsCannotMasqueradeAsAdjacentDigestFields()
    {
        using var cache = pristine.NewScratch();
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T", ws,
            paragraphs = new[] { new { segments = new[] { new { text = "one.", freeTranslation = "a", literalTranslation = "b" } } } } }));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        var segment = Assert.Single(((IStTxtPara)text.ContentsOA.ParagraphsOS[0]).SegmentsOS);
        var field = "literal:" + CanonicalId.FromGuid(segment.Guid).Value + ":" + cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            segment.FreeTranslation.set_String(cache.DefaultAnalWs, "a\n" + field + "=b");
            segment.LiteralTranslation.set_String(cache.DefaultAnalWs, "c");
        });
        var first = TextOwningSnapshots.Summary(cache, text)["structure"];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            segment.FreeTranslation.set_String(cache.DefaultAnalWs, "a");
            segment.LiteralTranslation.set_String(cache.DefaultAnalWs, "b\n" + field + "=c");
        });
        Assert.NotEqual(first, TextOwningSnapshots.Summary(cache, text)["structure"]);
    }

    [Fact]
    public void PunctuationOnlyTextHasPunctuationAnalysesAndNoWordformCreates()
    {
        using var cache = pristine.NewScratch();
        var operations = AuthorTextDraftBuilder.Build(cache, Parse(new { title = "T",
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            paragraphs = new[] { new { segments = new[] { new[] { "?" } } } } }));
        Assert.Single(operations);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(operations.Last().EntityId!.Value.ToGuid());
        Assert.IsAssignableFrom<IPunctuationForm>(Assert.Single(Assert.Single(((IStTxtPara)text.ContentsOA.ParagraphsOS[0]).SegmentsOS).AnalysesRS));
        Assert.Equal("0", dryRun.ExpectedEffects.Last().After["words"]);
    }

    [Theory]
    [InlineData("one. two.")]
    [InlineData("one")]
    public void AnnotationsCannotSilentlyMoveAcrossFieldWorksSegmentBoundaries(string annotated)
    {
        using var cache = pristine.NewScratch();
        var intent = Parse(new { title = "T", ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            paragraphs = new[] { new { segments = new object[] { new { text = annotated, freeTranslation = "translation" }, new { text = "two" } } } } });
        Assert.Contains("exactly one FieldWorks segment", Assert.Throws<InvalidOperationException>(() =>
            AuthorTextDraftBuilder.Build(cache, intent)).Message);
    }

    [Fact]
    public void AnnotationsRefuseAVernacularWritingSystemInTheAnalysisSlot()
    {
        using var cache = pristine.NewScratch();
        var vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var intent = Parse(new { title = "T", ws = vern, analysisWs = vern,
            paragraphs = new[] { new { segments = new[] { new { text = "one.", notes = new[] { "note" } } } } } });
        Assert.Contains("analysis writing system", Assert.Throws<InvalidOperationException>(() => AuthorTextDraftBuilder.Build(cache, intent)).Message);
    }

    private static AuthorTextIntent Parse(object intent) => AuthorTextIntentParser.Parse(JsonSerializer.SerializeToElement(intent));
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["texts"] = "1.0", ["analysis"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
