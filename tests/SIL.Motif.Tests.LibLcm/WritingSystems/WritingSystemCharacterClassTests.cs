using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.WritingSystems;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

/// <summary>
/// A Proposal can move characters of a writing system between word-forming and punctuation, and the Dry Run,
/// the Trial's copy and Apply all see the changed writing system.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class WritingSystemCharacterClassTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Open = "‘";
    private const string Close = "’";
    private readonly List<string> _roots = [];
    private readonly List<LcmCache> _caches = [];

    public void Dispose()
    {
        foreach (var cache in _caches.Where(cache => !cache.IsDisposed)) cache.Dispose();
        foreach (var root in _roots)
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void AWritingSystemChangeReachesTheStoreWhenTheProjectIsSaved()
    {
        var cache = CebuanoLikeProject();
        var ws = Vernacular(cache);
        ws.CharacterSets.Add(new CharacterSetDefinition("punctuation") { Characters = { "~" } });
        ws.CharacterSets.TryGet("main", out var main);
        main.Characters.Add("-");

        new FwDataProjectLoader().Save(cache);

        using var copy = new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path, NewRoot());
        var copied = Vernacular(copy);
        Assert.True(copied.CharacterSets.TryGet("main", out var copiedMain));
        Assert.Equal(new[] { "-", Open, Close }.Order(), copiedMain.Characters.Order());
        Assert.True(copied.CharacterSets.TryGet("punctuation", out var copiedPunctuation));
        Assert.Equal(["~"], copiedPunctuation.Characters);
        Assert.True(copied.get_IsWordForming('-'));
    }

    [Fact]
    public void AScratchOpenedInPlaceOnAPublishedBaselineLeavesItsWritingSystemStoreUnwritten()
    {
        var path = pristine.CopyProjectFile();
        var store = Path.Combine(Path.GetDirectoryName(path)!, "WritingSystemStore");
        Proposal proposal;
        using (var live = new FwDataProjectLoader().LoadScratchCache(path))
        {
            Vernacular(live).CharacterSets.Add(new CharacterSetDefinition("main") { Characters = { Open, Close } });
            new FwDataProjectLoader().Save(live);
            proposal = Propose(PunctuationFor(live));
        }
        var before = Directory.GetFiles(store).ToDictionary(file => file, File.ReadAllBytes);

        using (var scratch = new BaselineScratchFactory().OpenSingleUse(path))
        {
            var plan = PrerequisiteExecutionPlan.Create(proposal, [], []);
            ProposalDryRunner.Run(scratch, plan);
            Assert.False(Vernacular(scratch.PeekCache()).get_IsWordForming(Open[0]));
        }

        foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void PunctuationMovesTheCharactersFromMainToThePunctuationSetAndBackAgain()
    {
        var cache = CebuanoLikeProject();
        var toPunctuation = Propose(PunctuationFor(cache));

        var dryRun = ScratchDryRun.Of(cache, toPunctuation);

        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.LangProjectWritingSystemCharacterClass, effect.Field);
        var ws = Vernacular(cache).Id;
        Assert.Equal("U+2018=wordForming/main U+2019=wordForming/main", effect.Before[ws]);
        Assert.Equal("U+2018=punctuation/punctuation U+2019=punctuation/punctuation", effect.After[ws]);
        Assert.True(Vernacular(cache).get_IsWordForming(Open[0]), "a Dry Run leaves the project alone");

        ProposalApplier.Apply(cache, toPunctuation, dryRun.Anchor, "motif-tests");
        new FwDataProjectLoader().Save(cache);
        Assert.False(Vernacular(cache).get_IsWordForming(Open[0]));
        Assert.False(Vernacular(cache).get_IsWordForming(Close[0]));
        var ldml = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(cache.ProjectId.Path)!, "WritingSystemStore", $"{ws}.ldml"));
        Assert.Contains("type=\"punctuation\"", ldml, StringComparison.Ordinal);
        using (var reopened = new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path, NewRoot()))
        {
            var reloaded = Vernacular(reopened);
            Assert.False(reloaded.CharacterSets.TryGet("main", out _));
            Assert.True(reloaded.CharacterSets.TryGet("punctuation", out var listed));
            Assert.Equal([Open, Close], listed.Characters.Order());
        }

        var back = Propose(Compose(cache, WritingSystemCharacterClass.WordForming));
        var backRun = ScratchDryRun.Of(cache, back);
        Assert.Equal("U+2018=wordForming/main U+2019=wordForming/main", backRun.ExpectedEffects.Single().After[ws]);
        ProposalApplier.Apply(cache, back, backRun.Anchor, "motif-tests");
        Assert.True(Vernacular(cache).get_IsWordForming(Open[0]));
        Assert.False(Vernacular(cache).CharacterSets.TryGet("punctuation", out _));
    }

    [Fact]
    public void ALetterCannotBeMadePunctuation()
    {
        var cache = CebuanoLikeProject();

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            SetCharacterClassComposer.Build(cache, new(Vernacular(cache).Id, WritingSystemCharacterClass.Punctuation, "a")));

        Assert.Contains("U+0061", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be made punctuation", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("xx-unknown", "‘", "not one of this project's writing systems")]
    [InlineData("", "‘", "must name a writing system")]
    public void AnUnknownWritingSystemIsRefused(string tag, string characters, string reason)
    {
        var cache = CebuanoLikeProject();
        var failure = Assert.ThrowsAny<Exception>(() =>
            SetCharacterClassComposer.Build(cache, new(tag, WritingSystemCharacterClass.Punctuation, characters)));
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceAndMultiCharacterTextAreRefused()
    {
        var cache = CebuanoLikeProject();
        Assert.ThrowsAny<Exception>(() => SetCharacterClassComposer.Build(cache,
            new(Vernacular(cache).Id, WritingSystemCharacterClass.Punctuation, "‘ ’")));
    }

    [Fact]
    public void TextsAreRetokenisedSoTheQuotesStopFormingWords()
    {
        var cache = CebuanoLikeProject();
        var paragraph = AddParsedParagraph(cache, $"{Open}baba{Close} ug.");
        Assert.Equal([$"{Open}baba{Close}", "ug"], Words(cache, paragraph));

        var proposal = Propose(PunctuationFor(cache));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Equal(["baba", "ug"], Words(cache, paragraph));
        Assert.Equal(2, paragraph.SegmentsOS.SelectMany(segment => segment.AnalysesRS).OfType<IPunctuationForm>()
            .Count(form => form.Form.Text is Open or Close));
    }

    [Fact]
    public void AParagraphWhoseFirstRunIsAnotherWritingSystemIsRetokenised()
    {
        var cache = CebuanoLikeProject();
        var other = cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem.Handle;
        var paragraph = AddParsedParagraph(cache, $"{Open}baba{Close} ug.");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var builder = TsStringUtils.MakeStrBldr();
            builder.Replace(0, 0, "Hi ", TsStringUtils.MakeProps(string.Empty, other));
            builder.Replace(builder.Length, builder.Length, $"{Open}baba{Close} ug.", TsStringUtils.MakeProps(string.Empty, cache.DefaultVernWs));
            paragraph.Contents = builder.GetString();
            ParagraphParser.ParseParagraph(paragraph);
        });
        Assert.Equal(other, paragraph.Contents.get_WritingSystem(0));
        Assert.Contains($"{Open}baba{Close}", Words(cache, paragraph));

        var proposal = Propose(PunctuationFor(cache));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Contains("baba", Words(cache, paragraph));
        Assert.DoesNotContain($"{Open}baba{Close}", Words(cache, paragraph));
    }

    [Fact]
    public void ListingAnUnlistedNonLetterAsPunctuationParsesNothing()
    {
        var cache = CebuanoLikeProject();
        var paragraph = AddParsedParagraph(cache, "baba ~ ug.");
        var wordformsBefore = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
        var stamp = paragraph.SegmentsOS.Select(segment => segment.Guid).ToArray();
        var operations = SetCharacterClassComposer.Build(cache, new(Vernacular(cache).Id, WritingSystemCharacterClass.Punctuation, "~"));
        var proposal = Propose(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);

        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.True(Vernacular(cache).CharacterSets.TryGet("punctuation", out var listed));
        Assert.Equal(["~"], listed.Characters);
        Assert.Equal(stamp, paragraph.SegmentsOS.Select(segment => segment.Guid).ToArray());
        Assert.Equal(wordformsBefore, cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count);
    }

    [Fact]
    public void ATrialCopyOfTheChangedScratchReadsTheChangedWritingSystem()
    {
        var cache = CebuanoLikeProject();
        AddParsedParagraph(cache, $"{Open}baba{Close} ug.");
        new FwDataProjectLoader().Save(cache);
        var proposal = Propose(PunctuationFor(cache));
        var root = NewRoot();
        var loader = new FwDataProjectLoader();
        string candidatePath;
        using (var scratch = DryRunScratch.Adopt(new ScratchCacheFactory(loader).CreateFromFileCopy(cache.ProjectId.Path, root), "test"))
        {
            ProposalDryRunner.Run(scratch, PrerequisiteExecutionPlan.Create(proposal, [], []));
            loader.Save(scratch.PeekCache());
            candidatePath = scratch.PeekCache().ProjectId.Path;
        }

        using var reopened = new FwDataProjectLoader().LoadScratchCache(candidatePath);

        Assert.False(Vernacular(reopened).get_IsWordForming(Open[0]));
        var wordforms = reopened.ServiceLocator.GetInstance<IStTxtParaRepository>().AllInstances()
            .SelectMany(para => para.SegmentsOS).SelectMany(segment => segment.AnalysesRS)
            .OfType<IWfiWordform>().Select(word => word.Form.get_String(reopened.DefaultVernWs).Text).ToArray();
        Assert.Contains("baba", wordforms);
        Assert.DoesNotContain($"{Open}baba{Close}", wordforms);
    }

    [Fact]
    public void TwoOperationsOnDifferentCharactersOfOneWritingSystemDoNotShareASlot()
    {
        var cache = CebuanoLikeProject();
        var ws = Vernacular(cache).Id;
        var first = SetCharacterClassComposer.Build(cache, new(ws, WritingSystemCharacterClass.Punctuation, Open));
        var second = SetCharacterClassComposer.Build(cache, new(ws, WritingSystemCharacterClass.Punctuation, Close));
        var same = SetCharacterClassComposer.Build(cache, new(ws, WritingSystemCharacterClass.WordForming, Open));

        var slots = (OperationEnvelope operation) => OperationSlotRegistry.Resolve(operation);

        Assert.Empty(slots(first.Single()).Intersect(slots(second.Single())));
        Assert.Single(slots(first.Single()).Intersect(slots(same.Single())));
    }

    private LcmCache CebuanoLikeProject()
    {
        var cache = pristine.NewScratch();
        _caches.Add(cache);
        var ws = Vernacular(cache);
        ws.CharacterSets.Clear();
        ws.CharacterSets.Add(new CharacterSetDefinition("main") { Characters = { Open, Close } });
        new FwDataProjectLoader().Save(cache);
        return cache;
    }

    private static CoreWritingSystemDefinition Vernacular(LcmCache cache) =>
        cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;

    private static IReadOnlyList<OperationEnvelope> PunctuationFor(LcmCache cache) =>
        Compose(cache, WritingSystemCharacterClass.Punctuation);

    private static IReadOnlyList<OperationEnvelope> Compose(LcmCache cache, WritingSystemCharacterClass characterClass) =>
        SetCharacterClassComposer.Build(cache, new(Vernacular(cache).Id, characterClass, Open + Close));

    private static Proposal Propose(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    private static IStTxtPara AddParsedParagraph(LcmCache cache, string text)
    {
        IStTxtPara paragraph = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var created = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
            created.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
            created.ContentsOA.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(text, cache.DefaultVernWs);
            ParagraphParser.ParseParagraph(paragraph);
        });
        new FwDataProjectLoader().Save(cache);
        return paragraph;
    }

    private static string[] Words(LcmCache cache, IStTxtPara paragraph) =>
        paragraph.SegmentsOS.SelectMany(segment => segment.AnalysesRS).OfType<IWfiWordform>()
            .Select(word => word.Form.get_String(cache.DefaultVernWs).Text).ToArray();

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.WsClass", Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        return root;
    }
}
