using SIL.Motif.Contract.Commands;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.HandAnalyses;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class HandAnalysisCheckTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CheckResolvesLinkedRecordsAndChangesNoSavedProjectRecords()
    {
        var path = pristine.CopyProjectFile();
        HandAnalysisInput input;
        Guid msa;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            SeededProject.SeedText(cache, pristine.Seed);
            msa = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId)
                .MorphoSyntaxAnalysisRA.Guid;
            input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
            { Pieces = [HandAnalysisTestInput.Linked(pristine.Seed, msa)] };
            new FwDataProjectLoader().Save(cache);
        }
        var bytes = File.ReadAllBytes(path);
        var files = Directory.GetFiles(Path.GetDirectoryName(path)!, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllBytes);
        var outcome = HandAnalysisCommands.Check(new HandAnalysisCheckRequest(path, HandAnalysisJson.Serialize(input)));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.False(outcome.Value!.Changed);
        Assert.Single(outcome.Value.WordformMatches);
        var piece = Assert.Single(outcome.Value.Pieces);
        Assert.Equal(pristine.Seed.FirstEntryId, piece.EntryId);
        Assert.Equal(pristine.Seed.FirstLexemeFormId, piece.AllomorphId);
        Assert.Equal(pristine.Seed.FirstSenseId, piece.SenseId);
        Assert.Equal(msa, piece.MsaId);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(Path.GetDirectoryName(path)!, "*", SearchOption.AllDirectories).Order());
        foreach (var (file, before) in files) Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".motif.db")));
    }
    [Fact]
    public void NativePartialLinksArePreservedAndWrongOwnersOrClassesAreRefused()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(pristine.Seed.FirstSenseId);
        var piece = HandAnalysisTestInput.Linked(pristine.Seed, sense.MorphoSyntaxAnalysisRA.Guid);
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with { Pieces = [piece] };
        var partial = piece with { Link = piece.Link! with { Entry = null, Allomorph = null } };
        var result = HandAnalysisCommands.Check(cache, input with { Pieces = [partial] });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(result.Value!.Pieces[0].AllomorphId);
        Assert.Equal(sense.Guid, result.Value.Pieces[0].SenseId);
        Assert.Equal(sense.MorphoSyntaxAnalysisRA.Guid, result.Value.Pieces[0].MsaId);
        foreach (var bad in new[]
        {
            piece with { Link = piece.Link! with { Entry = pristine.Seed.SecondEntryId.ToString() } },
            piece with { Link = piece.Link! with { Allomorph = pristine.Seed.FirstSenseId.ToString() } },
            piece with { Link = piece.Link! with { Sense = pristine.Seed.SecondSenseId.ToString() } },
            piece with { Link = piece.Link! with { Msa = pristine.Seed.FirstLexemeFormId.ToString() } }
        })
        {
            var rejected = HandAnalysisCommands.Check(cache, input with { Pieces = [bad] });
            Assert.False(rejected.Succeeded);
            Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, rejected.Refusal!.Code);
            Assert.StartsWith("/pieces/0/link/", rejected.Refusal.Facts["pointer"]);
        }
        var foreign = HandAnalysisCommands.Check(cache, input with { Expected = input.Expected with { ProjectGuid = Guid.NewGuid() } });
        Assert.Equal(RefusalCodes.HandAnalysisStale, foreign.Refusal!.Code);
        var ws = HandAnalysisCommands.Check(cache, input with { Wordform = input.Wordform with { Ws = "de" } });
        Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, ws.Refusal!.Code);
        Assert.Equal("/wordform/ws", ws.Refusal.Facts["pointer"]);
    }

    [Fact]
    public void MarkerDefaultsUseProjectValuesAndUnknownMarkersContinueAsStem()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var types = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>();
        var prefix = types.GetObject(MoMorphTypeTags.kguidMorphPrefix);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => prefix.Postfix = ">>");
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid);
        HandAnalysisPiece Piece(string before, string after, string text = "typed") => new("piece", new("fr", text, new(before, after)), null);
        foreach (var (piece, expected) in new[]
        {
            (Piece("", ">>"), prefix.Guid), (Piece("*", ""), MoMorphTypeTags.kguidMorphBoundStem),
            (Piece("", ""), MoMorphTypeTags.kguidMorphStem), (Piece("", "", "two words"), MoMorphTypeTags.kguidMorphPhrase)
        })
        {
            var result = HandAnalysisCommands.Check(cache, input with { Pieces = [piece] });
            Assert.True(result.Succeeded, result.Refusal?.Message);
            Assert.Equal(expected, result.Value!.Pieces[0].EffectiveMorphTypeId);
        }
        var unknown = HandAnalysisCommands.Check(cache, input with { Pieces = [Piece("<unknown>", "")] });
        Assert.True(unknown.Succeeded, unknown.Refusal?.Message);
        Assert.Equal(MoMorphTypeTags.kguidMorphStem, unknown.Value!.Pieces[0].EffectiveMorphTypeId);
        Assert.Equal("<unknown>typed", unknown.Value.Pieces[0].TypedSpelling);
        Assert.Contains(unknown.Value.Diagnostics, warning => warning.Code == "marker-fallback" && warning.Consequence == "continue-as-stem");
        var mismatched = HandAnalysisCommands.Check(cache, input with
        {
            Pieces = [Piece("", ">>") with { Form = new("fr", "typed", new("", ">>"), MoMorphTypeTags.kguidMorphStem) }]
        });
        Assert.Equal(RefusalCodes.HandAnalysisTypeMismatch, mismatched.Refusal!.Code);
    }

    [Fact]
    public void NfdWordformLookupKeepsCaseAndReturnsEveryAmbiguousIdentity()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        IWfiWordform first = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString("e\u0301", cache.DefaultVernWs));
        });
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid, "é");
        var exact = HandAnalysisCommands.Check(cache, input);
        Assert.True(exact.Succeeded, exact.Refusal?.Message);
        Assert.Equal(first.Guid, exact.Value!.WordformId);
        var upper = HandAnalysisCommands.Check(cache, input with { Wordform = input.Wordform with { CaseForm = "É" } });
        Assert.True(upper.Succeeded, upper.Refusal?.Message);
        Assert.Empty(upper.Value!.WordformMatches);
        IWfiWordform second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            second = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            second.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("e\u0301", cache.DefaultVernWs));
        });
        var ambiguous = HandAnalysisCommands.Check(cache, input);
        Assert.Equal(RefusalCodes.HandAnalysisAmbiguousWordform, ambiguous.Refusal!.Code);
        Assert.Contains(first.Guid.ToString(), ambiguous.Refusal.Facts["matches"]);
        Assert.Contains(second.Guid.ToString(), ambiguous.Refusal.Facts["matches"]);
        var selected = HandAnalysisCommands.Check(cache, input with { Wordform = input.Wordform with { Id = second.Guid } });
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        Assert.Equal(second.Guid, selected.Value!.WordformId);
        Assert.Equal(2, selected.Value.WordformMatches.Count);
    }

    [Fact]
    public void AllomorphFamilyMismatchReportsTheExplicitMsaConsequenceWithoutCreatingRecords()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid);
        var creation = new HandAnalysisAllomorphCreation("newForm", pristine.Seed.FirstEntryId.ToString(),
            new Dictionary<string, string> { ["fr"] = "typed" }, MoMorphTypeTags.kguidMorphPrefix, "refuse");
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var refused = HandAnalysisCommands.Check(cache, input with { Creations = [creation] });
        Assert.Equal(RefusalCodes.HandAnalysisTypeMismatch, refused.Refusal!.Code);
        Assert.Equal("would-refuse-without-explicit-compatible-msa", refused.Refusal.Facts["consequence"]);
        var acknowledged = HandAnalysisCommands.Check(cache, input with
        {
            Creations = [creation with { Mismatch = "add-compatible-msa", MsaKey = "newMsa" }]
        });
        Assert.True(acknowledged.Succeeded, acknowledged.Refusal?.Message);
        Assert.Contains(acknowledged.Value!.Diagnostics, warning => warning.Code == "type-mismatch" && warning.Consequence == "unclassified-affix-msa");
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Fact]
    public void TextCheckUsesNativeSlotsAndRefusesChangedAssignmentOrPunctuation()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(text.FirstParagraphId);
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
        {
            Occurrence = new HandAnalysisTextAnchor(text.TextId, text.FirstParagraphId, text.FirstSegmentId, 0, 1)
        };
        input = input with { Expected = input.Expected with { AssignmentId = text.ApprovedAnalysisId, ParseIsCurrent = paragraph.ParseIsCurrent } };
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        var stale = HandAnalysisCommands.Check(cache, input with { Expected = input.Expected with { AssignmentId = Guid.NewGuid() } });
        Assert.Equal(RefusalCodes.HandAnalysisStale, stale.Refusal!.Code);
        var punctuation = HandAnalysisCommands.Check(cache, input with
        {
            Occurrence = new HandAnalysisTextAnchor(text.TextId, text.FirstParagraphId, text.FirstSegmentId, 1, 1)
        });
        Assert.Equal(RefusalCodes.HandAnalysisStale, punctuation.Refusal!.Code);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Fact]
    public void CustomMarkerTiesUseListOrderAndFallbackPermitsAnExplicitCreation()
    {
        var path = pristine.CopyProjectFile();
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        IMoMorphType first = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IMoMorphTypeFactory>().Create();
            cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.Add(first);
            first.Prefix = "<<"; first.Postfix = ">>";
            var second = cache.ServiceLocator.GetInstance<IMoMorphTypeFactory>().Create();
            cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.Add(second);
            second.Prefix = "<<"; second.Postfix = ">>";
        });
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid);
        var piece = new HandAnalysisPiece("piece", new("fr", "typed", new("<<", ">>")), null);
        var tied = HandAnalysisCommands.Check(cache, input with { Pieces = [piece] });
        Assert.True(tied.Succeeded, tied.Refusal?.Message);
        Assert.Equal(first.Guid, tied.Value!.Pieces[0].EffectiveMorphTypeId);
        Assert.Equal(2, tied.Value.Pieces[0].MorphTypeMatches.Count);
        var fallback = piece with
        {
            Form = piece.Form with { Markers = new("<unknown>", "") },
            Link = new("newEntry", "newForm", "newSense", "newMsa")
        };
        var creation = new HandAnalysisEntryCreation("newEntry", "newForm", "newSense", "newMsa",
            new Dictionary<string, string> { ["fr"] = "<unknown>typed" }, MoMorphTypeTags.kguidMorphStem,
            new Dictionary<string, string>(), new("stem", null));
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var allowed = HandAnalysisCommands.Check(cache, input with { Pieces = [fallback], Creations = [creation] });
        Assert.True(allowed.Succeeded, allowed.Refusal?.Message);
        Assert.Equal(MoMorphTypeTags.kguidMorphStem, allowed.Value!.Pieces[0].EffectiveMorphTypeId);
        Assert.Equal("<unknown>typed", allowed.Value.Pieces[0].TypedSpelling);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartingAnalysisAndGlossRemainIndependentOfTheFinalSelection(bool selectFinal)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var original = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
        IWfiAnalysis final = null!;
        IWfiGloss originalGloss = null!;
        IWfiGloss finalGloss = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            final = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            ((IWfiWordform)original.Owner).AnalysesOC.Add(final);
            originalGloss = cache.ServiceLocator.GetInstance<IWfiGlossFactory>().Create();
            original.MeaningsOC.Add(originalGloss);
            finalGloss = cache.ServiceLocator.GetInstance<IWfiGlossFactory>().Create();
            final.MeaningsOC.Add(finalGloss);
        });
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
        { WordGloss = new HandAnalysisAutoGloss(["en"], new Dictionary<string, string>(), originalGloss.Guid) };
        var json = System.Text.Json.Nodes.JsonNode.Parse(HandAnalysisJson.Serialize(input))!;
        json["startedFromAnalysisId"] = original.Guid.ToString();
        if (selectFinal) json["selectedAnalysisId"] = final.Guid.ToString();
        var parsed = HandAnalysisJson.Parse(json.ToJsonString());
        Assert.Empty(parsed.Errors);
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, parsed.Input!);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        json["wordGloss"]!["startedFromId"] = finalGloss.Guid.ToString();
        var wrongStart = HandAnalysisCommands.Check(cache, HandAnalysisJson.Parse(json.ToJsonString()).Input!);
        Assert.Equal("/wordGloss/startedFromId", wrongStart.Refusal!.Facts["pointer"]);
        if (selectFinal)
        {
            json["wordGloss"] = new System.Text.Json.Nodes.JsonObject
            { ["mode"] = "select", ["enabledWs"] = new System.Text.Json.Nodes.JsonArray("en"), ["id"] = finalGloss.Guid.ToString() };
            Assert.True(HandAnalysisCommands.Check(cache, HandAnalysisJson.Parse(json.ToJsonString()).Input!).Succeeded);
            json["wordGloss"]!["id"] = originalGloss.Guid.ToString();
            var wrongFinal = HandAnalysisCommands.Check(cache, HandAnalysisJson.Parse(json.ToJsonString()).Input!);
            Assert.Equal("/wordGloss/id", wrongFinal.Refusal!.Facts["pointer"]);
        }
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void CaseSelectionKeepsOriginalProvenanceAndNativeGuardsSeparate(bool targetExists, bool nativeAnchor)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(text.FirstParagraphId);
        var original = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(text.AnalysedWordformId);
        IWfiWordform? target = null;
        IWfiGloss originalGloss = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            paragraph.Contents = TsStringUtils.MakeString("The.", cache.DefaultVernWs);
            original.Form.set_String(cache.DefaultVernWs, "The");
            originalGloss = cache.ServiceLocator.GetInstance<IWfiGlossFactory>().Create();
            cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId).MeaningsOC.Add(originalGloss);
            if (targetExists) target = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("the", cache.DefaultVernWs));
        });
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
        {
            Wordform = new("fr", "The", "the", target?.Guid), StartedFromAnalysisId = text.ApprovedAnalysisId,
            WordGloss = new HandAnalysisAutoGloss(["en"], new Dictionary<string, string>(), originalGloss.Guid)
        };
        if (nativeAnchor) input = input with
            { Occurrence = new HandAnalysisTextAnchor(text.TextId, text.FirstParagraphId, text.FirstSegmentId, 0, 1) };
        if (nativeAnchor) input = input with { Expected = input.Expected with
            { AssignmentId = text.ApprovedAnalysisId, ParseIsCurrent = paragraph.ParseIsCurrent } };
        Assert.Empty(HandAnalysisJson.Parse(HandAnalysisJson.Serialize(input)).Errors);
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(target?.Guid, result.Value!.WordformId);
        if (!targetExists) Assert.Contains(result.Value.Diagnostics, item => item.Code == "wordform-absent");
        var glossOnly = HandAnalysisCommands.Check(cache, input with { StartedFromAnalysisId = null });
        Assert.True(glossOnly.Succeeded, glossOnly.Refusal?.Message);
        if (nativeAnchor)
        {
            var stale = HandAnalysisCommands.Check(cache, input with { Expected = input.Expected with { AssignmentId = Guid.NewGuid() } });
            Assert.Equal(RefusalCodes.HandAnalysisStale, stale.Refusal!.Code);
        }
        else Assert.Contains(result.Value.Diagnostics, item => item.Code == "source-unavailable");
        var unrelated = HandAnalysisCommands.Check(cache, input with { Wordform = new("fr", "The", SeededProject.UnanalysedWordForm) });
        Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, unrelated.Refusal!.Code);
        Assert.Equal("/wordform/caseForm", unrelated.Refusal.Facts["pointer"]);
        Assert.Equal("The", original.Form.get_String(cache.DefaultVernWs).Text);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData(false, "omitted")]
    [InlineData(true, "omitted")]
    [InlineData(false, "opening")]
    [InlineData(true, "opening")]
    [InlineData(false, "other")]
    [InlineData(true, "other")]
    public void NativeBareUppercaseAttachmentAllowsLowercaseOpeningProvenance(bool explicitStart, string finalSelection)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(text.FirstParagraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.FirstSegmentId);
        IWfiWordform upper = null!, lower = null!;
        IWfiAnalysis opening = null!, final = null!, wrongWs = null!;
        IWfiGloss openingGloss = null!, finalGloss = null!, wrongWsGloss = null!, unrelatedGloss = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            paragraph.Contents = TsStringUtils.MakeString("The.", cache.DefaultVernWs);
            var words = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
            upper = words.Create(TsStringUtils.MakeString("The", cache.DefaultVernWs));
            lower = words.Create(TsStringUtils.MakeString("the", cache.DefaultVernWs));
            segment.AnalysesRS[0] = upper;
            var analyses = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>();
            opening = analyses.Create();
            lower.AnalysesOC.Add(opening);
            final = analyses.Create();
            lower.AnalysesOC.Add(final);
            var otherWsWord = words.Create(TsStringUtils.MakeString("the", cache.DefaultAnalWs));
            wrongWs = analyses.Create();
            otherWsWord.AnalysesOC.Add(wrongWs);
            var glosses = cache.ServiceLocator.GetInstance<IWfiGlossFactory>();
            openingGloss = glosses.Create();
            opening.MeaningsOC.Add(openingGloss);
            openingGloss.Form.set_String(cache.DefaultAnalWs, "opening");
            finalGloss = glosses.Create();
            final.MeaningsOC.Add(finalGloss);
            finalGloss.Form.set_String(cache.DefaultAnalWs, "final");
            wrongWsGloss = glosses.Create();
            wrongWs.MeaningsOC.Add(wrongWsGloss);
            unrelatedGloss = glosses.Create();
            cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId)
                .MeaningsOC.Add(unrelatedGloss);
        });
        var selected = finalSelection switch { "opening" => opening, "other" => final, _ => null };
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
        {
            Occurrence = new HandAnalysisTextAnchor(text.TextId, text.FirstParagraphId, text.FirstSegmentId, 0, 1),
            Wordform = new("fr", "The", "the", lower.Guid),
            StartedFromAnalysisId = explicitStart ? opening.Guid : null,
            SelectedAnalysisId = selected?.Guid,
            WordGloss = new HandAnalysisAutoGloss(["en"], new Dictionary<string, string>(), openingGloss.Guid)
        };
        input = input with { Expected = input.Expected with
            { AssignmentId = upper.Guid, ParseIsCurrent = paragraph.ParseIsCurrent } };
        Assert.Empty(HandAnalysisJson.Parse(HandAnalysisJson.Serialize(input)).Errors);
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var assignments = segment.AnalysesRS.Select(item => item.Guid).ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(lower.Guid, result.Value!.WordformId);

        void Refused(HandAnalysisInput invalid, string code, string pointer)
        {
            var refusal = HandAnalysisCommands.Check(cache, invalid).Refusal;
            Assert.NotNull(refusal);
            Assert.Equal(code, refusal.Code);
            Assert.Equal(pointer, refusal.Facts["pointer"]);
        }
        Refused(input with { Expected = input.Expected with { AssignmentId = opening.Guid } },
            RefusalCodes.HandAnalysisStale, "/expected");
        Refused(input with { Expected = input.Expected with { ParseIsCurrent = !paragraph.ParseIsCurrent } },
            RefusalCodes.HandAnalysisStale, "/expected");
        foreach (var invalidStart in new[] { text.ApprovedAnalysisId, wrongWs.Guid })
            Refused(input with { StartedFromAnalysisId = invalidStart },
                RefusalCodes.HandAnalysisInvalidLink, "/startedFromAnalysisId");
        foreach (var invalidGloss in new[] { unrelatedGloss.Guid, wrongWsGloss.Guid })
            Refused(input with { StartedFromAnalysisId = null,
                WordGloss = new HandAnalysisAutoGloss(["en"], new Dictionary<string, string>(), invalidGloss) },
                RefusalCodes.HandAnalysisInvalidLink, "/wordGloss/startedFromId");
        if (explicitStart)
            Refused(input with { WordGloss = new HandAnalysisAutoGloss(["en"], new Dictionary<string, string>(), finalGloss.Guid) },
                RefusalCodes.HandAnalysisInvalidLink, "/wordGloss/startedFromId");
        Refused(input with { SelectedAnalysisId = text.ApprovedAnalysisId },
            RefusalCodes.HandAnalysisInvalidLink, "/selectedAnalysisId");
        Refused(input with { Wordform = new("fr", "The", "The", upper.Guid), SelectedAnalysisId = opening.Guid },
            RefusalCodes.HandAnalysisInvalidLink, "/selectedAnalysisId");
        Refused(input with { Wordform = new("fr", "The", "The", upper.Guid), SelectedAnalysisId = null,
            WordGloss = new HandAnalysisSelectGloss(["en"], openingGloss.Guid) },
            RefusalCodes.HandAnalysisInvalidLink, "/wordGloss/id");
        if (selected is not null)
        {
            var selectedGloss = selected == opening ? openingGloss : finalGloss;
            Assert.True(HandAnalysisCommands.Check(cache, input with
                { WordGloss = new HandAnalysisSelectGloss(["en"], selectedGloss.Guid) }).Succeeded);
            Refused(input with { WordGloss = new HandAnalysisSelectGloss(["en"],
                selected == opening ? finalGloss.Guid : openingGloss.Guid) },
                RefusalCodes.HandAnalysisInvalidLink, "/wordGloss/id");
        }
        Assert.Equal(assignments, segment.AnalysesRS.Select(item => item.Guid));
        Assert.Empty(upper.AnalysesOC);
        Assert.Equal("The", upper.Form.get_String(cache.DefaultVernWs).Text);
        Assert.Equal("the", lower.Form.get_String(cache.DefaultVernWs).Text);
        Assert.Equal("opening", openingGloss.Form.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("final", finalGloss.Form.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("empty", false)]
    [InlineData("whitespace", false)]
    [InlineData("other-only", false)]
    [InlineData("default-only", true)]
    [InlineData("empty-other", true)]
    public void NewSenseRequiresANonblankDefaultAnalysisGloss(string alternatives, bool accepted)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystemManager.GetOrSet("de", out var german);
            cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(german);
        });
        var glosses = new Dictionary<string, string>();
        if (alternatives == "empty") glosses["en"] = "";
        if (alternatives == "whitespace") glosses["en"] = " \t ";
        if (alternatives == "other-only") glosses["de"] = "Sinn";
        if (accepted) glosses["en"] = "meaning";
        if (alternatives == "empty-other") glosses["de"] = "";
        var creation = new HandAnalysisSenseCreation("sense", pristine.Seed.FirstEntryId.ToString(),
            glosses, new(null, new("stem", null)));
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with { Creations = [creation] };
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        Assert.Equal(accepted, result.Succeeded);
        if (accepted) Assert.Contains(result.Value!.Diagnostics, item => item.Code == "new-sense");
        else
        {
            Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, result.Refusal!.Code);
            Assert.Equal("/creations/0/glosses/en", result.Refusal.Facts["pointer"]);
        }
        var entry = new HandAnalysisEntryCreation("entry", "form", "sense", "msa",
            new Dictionary<string, string> { ["fr"] = "typed" }, MoMorphTypeTags.kguidMorphStem,
            new Dictionary<string, string>(), new("stem", null));
        Assert.True(HandAnalysisCommands.Check(cache, input with { Creations = [entry] }).Succeeded);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void RequestedVariantLinksAreCheckedTogetherWithoutChangingNativeRecords(bool reverseOrder, bool senseComponent, bool cycle)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var source = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var middle = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId);
        ILexEntry last = null!;
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            last = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            last.SensesOS.Add(cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create());
            type = cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
            cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS.Add(type);
        });
        var entries = new[] { source, middle, last };
        foreach (var entry in entries) Assert.Empty(entry.EntryRefsOS);
        HandAnalysisVariantComponent Component(ILexEntry entry) => senseComponent
            ? new(SenseId: entry.SensesOS[0].Guid) : new(EntryId: entry.Guid);
        HandAnalysisCreation first = new HandAnalysisVariantCreation("first", source.Guid.ToString(), Component(middle), [type.Guid]);
        HandAnalysisCreation second = new HandAnalysisVariantCreation("second", middle.Guid.ToString(),
            Component(cycle ? source : last), [type.Guid]);
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid) with
            { Creations = reverseOrder ? [second, first] : [first, second] };
        Assert.Empty(HandAnalysisJson.Parse(HandAnalysisJson.Serialize(input)).Errors);
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        if (cycle)
        {
            Assert.False(result.Succeeded);
            Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, result.Refusal!.Code);
            Assert.Equal("/creations/1/component/" + (senseComponent ? "senseId" : "entryId"), result.Refusal.Facts["pointer"]);
        }
        else
        {
            Assert.True(result.Succeeded, result.Refusal?.Message);
            Assert.DoesNotContain(result.Value!.Diagnostics, item => item.Code == "variant-graph-unavailable");
        }
        foreach (var entry in entries) Assert.Empty(entry.EntryRefsOS);
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

    [Theory]
    [InlineData("valid-reference", null)]
    [InlineData("complex-reference", "/pieces/0/variantRef")]
    [InlineData("wrong-reference-type", "/pieces/0/variantRef")]
    [InlineData("valid-creation", null)]
    [InlineData("wrong-creation-type", "/creations/0/variantTypeIds/0")]
    [InlineData("creation-self-entry", "/creations/0/component/entryId")]
    [InlineData("creation-self-sense", "/creations/0/component/senseId")]
    [InlineData("creation-cycle", "/creations/0/component/entryId")]
    [InlineData("new-entry", null)]
    [InlineData("wrong-new-entry-type", "/creations/0/variantTypeIds/0")]
    [InlineData("local-entry", null)]
    public void VariantChecksRequireVariantReferencesTypesAndAnAcyclicGraph(string scenario, string? pointer)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var source = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId);
        ILexEntryRef reference = null!;
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            type = cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
            if (scenario.StartsWith("wrong-")) cache.LangProject.LexDbOA.ComplexEntryTypesOA.PossibilitiesOS.Add(type);
            else
            {
                var parent = cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
                cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS.Add(parent);
                parent.SubPossibilitiesOS.Add(type);
            }
            reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            source.EntryRefsOS.Add(reference);
            reference.RefType = scenario == "complex-reference" ? LexEntryRefTags.krtComplexForm : LexEntryRefTags.krtVariant;
            if (!scenario.EndsWith("-cycle")) reference.ComponentLexemesRS.Add(target);
            if (scenario != "complex-reference") reference.VariantEntryTypesRS.Add(type);
            if (scenario.EndsWith("-cycle"))
            {
                var reverse = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
                target.EntryRefsOS.Add(reverse);
                reverse.RefType = LexEntryRefTags.krtVariant;
                reverse.ComponentLexemesRS.Add(source);
            }
        });
        var input = HandAnalysisTestInput.Create(cache.LangProject.Guid);
        if (scenario.Contains("reference"))
            input = input with { Pieces = [new("piece", new("fr", "typed", new("", "")),
                new(source.Guid.ToString(), null, target.SensesOS[0].Guid.ToString(), null), VariantRef: reference.Guid.ToString())] };
        else
        {
            var component = scenario switch
            {
                "creation-self-entry" => new HandAnalysisVariantComponent(EntryId: source.Guid),
                "creation-self-sense" => new HandAnalysisVariantComponent(SenseId: source.SensesOS[0].Guid),
                _ => new HandAnalysisVariantComponent(EntryId: target.Guid)
            };
            if (scenario.Contains("new-entry"))
                input = input with { Creations = [new HandAnalysisVariantEntryCreation("variant", "form", "ref",
                    new Dictionary<string, string> { ["fr"] = "typed" }, MoMorphTypeTags.kguidMorphStem, component, [type.Guid])] };
            else if (scenario == "local-entry")
                input = input with { Creations = [
                    new HandAnalysisEntryCreation("entry", "form", "sense", "msa", new Dictionary<string, string> { ["fr"] = "typed" },
                        MoMorphTypeTags.kguidMorphStem, new Dictionary<string, string>(), new("stem", null)),
                    new HandAnalysisVariantCreation("variant", "entry", component, [type.Guid])] };
            else input = input with { Creations = [new HandAnalysisVariantCreation("variant", source.Guid.ToString(), component, [type.Guid])] };
        }
        var before = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order().ToArray();
        var result = HandAnalysisCommands.Check(cache, input);
        if (pointer is null)
        {
            Assert.True(result.Succeeded, result.Refusal?.Message);
            if (scenario is "new-entry" or "local-entry")
                Assert.Contains(result.Value!.Diagnostics, item => item.Code == "variant-graph-unavailable");
        }
        else
        {
            Assert.False(result.Succeeded);
            Assert.Equal(RefusalCodes.HandAnalysisInvalidLink, result.Refusal!.Code);
            Assert.Equal(pointer, result.Refusal.Facts["pointer"]);
        }
        Assert.Equal(before, cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances().Select(item => item.Guid).Order());
    }

}
