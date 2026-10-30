using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ExampleComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public ExampleComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private string Vern => _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

    private string Anal => _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);

    [Fact]
    public void AnExampleMovesBetweenSensesOfOneEntryKeepingItsIdentityTextAndTranslations()
    {
        var root = Entry("ayu", ["tree", "kaayu"]);
        var example = AddExample(root.Senses[1], "daghang kaayu", "many trees", "Wolff 12");
        var operations = MoveExampleComposer.Build(_cache, new(Id(example), Id(root.Senses[0])));
        var move = Assert.Single(operations);
        Assert.Equal(LexExampleOperationKinds.Move, move.Kind);
        Assert.Null(move.Placement);
        ApplyAll(operations);

        Assert.Empty(Sense(root.Senses[1]).ExamplesOS);
        var moved = Assert.Single(Sense(root.Senses[0]).ExamplesOS);
        Assert.Equal(example, moved.Guid);
        Assert.Equal("daghang kaayu", moved.Example.get_String(_cache.DefaultVernWs).Text);
        Assert.Equal("Wolff 12", moved.Reference.Text);
        Assert.Equal("many trees", Assert.Single(moved.TranslationsOC).Translation.get_String(_cache.DefaultAnalWs).Text);
    }

    [Fact]
    public void AnExampleMovesAcrossEntriesAndLandsAfterTheNamedNeighbour()
    {
        var source = Entry("kaayu", ["tree", "wood"]);
        var target = Entry("ayu", ["plant", "stick"]);
        var moving = AddExample(source.Senses[0], "bukid", "hill", null);
        var first = AddExample(target.Senses[0], "una", "first", null);
        var second = AddExample(target.Senses[0], "ikaduha", "second", null);

        var operations = MoveExampleComposer.Build(_cache, new(Id(moving), Id(target.Senses[0]), After: Id(first)));
        Assert.Equal(first, Assert.Single(operations).Placement!.After!.Value.ToGuid());
        ApplyAll(operations);

        Assert.Equal([first, moving, second], Sense(target.Senses[0]).ExamplesOS.Select(item => item.Guid));
        Assert.Empty(Sense(source.Senses[0]).ExamplesOS);
        Assert.Equal(target.Entry, Sense(target.Senses[0]).ExamplesOS[1].Owner.Owner.Guid);
    }

    [Fact]
    public void WithoutANeighbourAMovedExampleGoesLastAndAMissingNeighbourIsRefused()
    {
        var root = Entry("ayu", ["tree", "wood"]);
        var existing = AddExample(root.Senses[1], "una", "first", null);
        var moving = AddExample(root.Senses[0], "ikaduha", "second", null);
        var other = AddExample(root.Senses[0], "ikatulo", "third", null);

        var operations = MoveExampleComposer.Build(_cache, new(Id(moving), Id(root.Senses[1])));
        ApplyAll(operations);
        Assert.Equal([existing, moving], Sense(root.Senses[1]).ExamplesOS.Select(item => item.Guid));

        var refused = Assert.Throws<InvalidOperationException>(() =>
            MoveExampleComposer.Build(_cache, new(Id(other), Id(root.Senses[1]), Before: Id(other))));
        Assert.Contains("not an example of the target sense", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownExampleAnUnknownSenseAndTheWrongKindOfIdAreRefused()
    {
        var root = Entry("ayu", ["tree", "wood"]);
        var example = AddExample(root.Senses[0], "una", "first", null);
        Assert.ThrowsAny<Exception>(() =>
            MoveExampleComposer.Build(_cache, new(Id(Guid.NewGuid()), Id(root.Senses[1]))));
        Assert.ThrowsAny<Exception>(() =>
            MoveExampleComposer.Build(_cache, new(Id(example), Id(Guid.NewGuid()))));
        Assert.Contains("example sentence", Assert.Throws<InvalidOperationException>(() =>
            MoveExampleComposer.Build(_cache, new(Id(root.Senses[0]), Id(root.Senses[1])))).Message, StringComparison.Ordinal);
        Assert.Contains("must name a sense", Assert.Throws<InvalidOperationException>(() =>
            MoveExampleComposer.Build(_cache, new(Id(example), Id(root.Entry)))).Message, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            AddExampleComposer.Build(_cache, AddIntent(Guid.NewGuid(), "una", null)));
    }

    [Fact]
    public void MovingAnExampleToItsOwnSenseIsRefusedAtComposeAndAtApply()
    {
        var root = Entry("ayu", ["tree", "wood"]);
        var example = AddExample(root.Senses[0], "una", "first", null);
        Assert.Contains("already belongs", Assert.Throws<InvalidOperationException>(() =>
            MoveExampleComposer.Build(_cache, new(Id(example), Id(root.Senses[0])))).Message, StringComparison.Ordinal);

        var operations = MoveExampleComposer.Build(_cache, new(Id(example), Id(root.Senses[1])));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Sense(root.Senses[1]).ExamplesOS.Add(Example(example)));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
    }

    [Fact]
    public void AnExampleThatSomethingElseReferToIsRefusedNamingTheReferrer()
    {
        var root = Entry("ayu", ["tree", "wood"]);
        var example = AddExample(root.Senses[0], "una", "first", null);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var type = _cache.ServiceLocator.GetInstance<ILexRefTypeFactory>().Create();
            _cache.LangProject.LexDbOA.ReferencesOA ??= _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            _cache.LangProject.LexDbOA.ReferencesOA.PossibilitiesOS.Add(type);
            type.Name.set_String(_cache.DefaultAnalWs, "Citations");
            var relation = _cache.ServiceLocator.GetInstance<ILexReferenceFactory>().Create();
            type.MembersOC.Add(relation);
            relation.TargetsRS.Add(Example(example));
        });
        var refused = Assert.Throws<InvalidOperationException>(() =>
            MoveExampleComposer.Build(_cache, new(Id(example), Id(root.Senses[1]))));
        Assert.Contains("Citations", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewExampleCarriesSeveralWritingSystemsAndTranslationsAndGoesLast()
    {
        var root = Entry("ayu", ["tree"]);
        var present = AddExample(root.Senses[0], "una", "first", null);
        var intent = new AddExampleIntent(Id(root.Senses[0]),
            [new(Vern, "gibuksan ang ayu"), new(Other, "arbre est ouvert")], "Elder 3",
            [new("free", [new(Anal, "the tree was split"), new(Other, "arbre fut fendu")]),
             new("literal", [new(Anal, "split the tree")])]);
        var operations = AddExampleComposer.Build(_cache, intent);
        var create = Assert.Single(operations);
        Assert.Equal(present, create.Placement!.After!.Value.ToGuid());
        ApplyAll(operations);

        var added = Sense(root.Senses[0]).ExamplesOS[1];
        Assert.Equal(create.EntityId!.Value.ToGuid(), added.Guid);
        Assert.Equal("gibuksan ang ayu", added.Example.get_String(_cache.DefaultVernWs).Text);
        Assert.Equal("arbre est ouvert", added.Example.get_String(OtherHandle).Text);
        Assert.Equal("Elder 3", added.Reference.Text);
        Assert.Equal(2, added.TranslationsOC.Count);
        var free = added.TranslationsOC.Single(item => item.TypeRA.Guid == CmPossibilityTags.kguidTranFreeTranslation);
        Assert.Equal("the tree was split", free.Translation.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal("arbre fut fendu", free.Translation.get_String(OtherHandle).Text);
        Assert.Contains(added.TranslationsOC, item => item.TypeRA.Guid == CmPossibilityTags.kguidTranLiteralTranslation);
    }

    [Fact]
    public void ANewExampleRefusesAnUnknownWritingSystemEmptyOrRepeatedTextAndAForeignNeighbour()
    {
        var root = Entry("ayu", ["tree", "wood"]);
        var foreign = AddExample(root.Senses[1], "una", "first", null);
        Assert.Contains("writing systems", Assert.Throws<InvalidOperationException>(() =>
            AddExampleComposer.Build(_cache, new(Id(root.Senses[0]), [new("zz-Latn-x-nowhere", "x")], null, []))).Message,
            StringComparison.Ordinal);
        Assert.Contains("not an example", Assert.Throws<InvalidOperationException>(() =>
            AddExampleComposer.Build(_cache, new(Id(root.Senses[0]), [new(Vern, "x")], null, [], After: Id(foreign)))).Message,
            StringComparison.Ordinal);
        foreach (var bad in new[]
                 {
                     "{\"sense\":\"" + Id(root.Senses[0]).Value + "\",\"text\":[]}",
                     "{\"sense\":\"" + Id(root.Senses[0]).Value + "\",\"text\":[{\"ws\":\"" + Vern + "\",\"text\":\"\"}]}",
                     "{\"sense\":\"" + Id(root.Senses[0]).Value + "\",\"text\":[{\"ws\":\"" + Vern + "\",\"text\":\"a\"},{\"ws\":\"" + Vern + "\",\"text\":\"b\"}]}",
                     "{\"sense\":\"" + Id(root.Senses[0]).Value + "\",\"text\":[{\"ws\":\"" + Vern + "\",\"text\":\"a\"}],\"force\":true}",
                     "{\"sense\":\"" + Id(root.Senses[0]).Value + "\",\"text\":[{\"ws\":\"" + Vern + "\",\"text\":\"a\"}],\"translations\":[{\"type\":\"odd\",\"text\":[{\"ws\":\"" + Anal + "\",\"text\":\"b\"}]}]}",
                 })
        {
            using var document = JsonDocument.Parse(bad);
            Assert.ThrowsAny<Exception>(() => AddExampleIntentParser.Parse(document.RootElement));
        }
    }

    [Fact]
    public void ThePayloadsAreClosed()
    {
        using var create = JsonDocument.Parse("{\"text\":[{\"ws\":\"x\",\"text\":\"y\"}],\"force\":true}");
        using var move = JsonDocument.Parse(
            $"{{\"fromSense\":\"{Id(Guid.NewGuid()).Value}\",\"toSense\":\"{Id(Guid.NewGuid()).Value}\",\"force\":true}}");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => LexExampleCreatePayload.Parse(create.RootElement));
        Assert.ThrowsAny<Exception>(() => LexExampleMovePayload.Parse(move.RootElement));
        Assert.ThrowsAny<Exception>(() => LexExampleMovePayload.Parse(missing.RootElement));
    }

    [Fact]
    public void TheMoveDryRunShowsTheFullExampleBothSensesAndTheFootprintChangesNothing()
    {
        var source = Entry("kaayu", ["tree", "wood"]);
        var target = Entry("ayu", ["plant"]);
        var example = AddExample(source.Senses[1], "gibuksan ang ayu", "the tree was split", "Elder 3");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            Sense(source.Senses[1]).Gloss.set_String(OtherHandle, "bois");
            var item = Example(example);
            item.Example.set_String(OtherHandle, "arbre est ouvert");
            item.TranslationsOC.Single().Translation.set_String(OtherHandle, "arbre fut fendu");
        });
        var operations = MoveExampleComposer.Build(_cache, new(Id(example), Id(target.Senses[0])));
        var proposal = Proposal(operations);
        var digestBefore = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var from = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexSenseExamples && e.CanonicalId == Id(source.Senses[1]));
        var to = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexSenseExamples && e.CanonicalId == Id(target.Senses[0]));
        Assert.Single(from.Before);
        Assert.Empty(from.After);
        Assert.Empty(to.Before);
        Assert.Equal(example, CanonicalId.Parse(Assert.Single(to.After).Value).ToGuid());
        foreach (var preview in new[] { from.Preview!.Value.GetRawText(), to.Preview!.Value.GetRawText() })
            foreach (var expected in new[]
                     {
                         "gibuksan ang ayu", "arbre est ouvert", "the tree was split", "arbre fut fendu", "Elder 3",
                         "wood", "bois", "plant", "kaayu", "\"ayu\"", Id(source.Senses[1]).Value, Id(target.Senses[0]).Value,
                     })
                Assert.Contains(expected, preview, StringComparison.Ordinal);

        Assert.Single(Sense(source.Senses[1]).ExamplesOS);
        Assert.Equal(digestBefore, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
    }

    [Fact]
    public void TheAddDryRunShowsTheFullNewExampleAndTheSenseAndTheFootprintChangesNothing()
    {
        var root = Entry("ayu", ["tree"]);
        var intent = new AddExampleIntent(Id(root.Senses[0]), [new(Vern, "gibuksan ang ayu"), new(Other, "arbre est ouvert")],
            "Elder 3", [new("free", [new(Anal, "the tree was split"), new(Other, "arbre fut fendu")])]);
        var proposal = Proposal(AddExampleComposer.Build(_cache, intent));
        var digestBefore = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexSenseExamples);
        Assert.Empty(effect.Before);
        Assert.Single(effect.After);
        var preview = effect.Preview!.Value.GetRawText();
        foreach (var expected in new[]
                 {
                     "gibuksan ang ayu", "arbre est ouvert", "Elder 3", "the tree was split", "arbre fut fendu",
                     "free", "tree", "ayu", Id(root.Senses[0]).Value,
                 })
            Assert.Contains(expected, preview, StringComparison.Ordinal);

        Assert.Empty(Sense(root.Senses[0]).ExamplesOS);
        Assert.Equal(digestBefore, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
    }

    private AddExampleIntent AddIntent(Guid sense, string text, string? reference) =>
        new(Id(sense), [new(Vern, text)], reference, []);

    private void ApplyAll(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "example-tests");
    }

    /// <summary>A project writing system other than the default vernacular and analysis ones.</summary>
    private string Other => _cache.ServiceLocator.WritingSystems.VernacularWritingSystems
        .Concat(_cache.ServiceLocator.WritingSystems.AnalysisWritingSystems)
        .Select(system => system.Id).First(id => id != Vern && id != Anal);

    private int OtherHandle => _cache.WritingSystemFactory.GetWsFromStr(Other);

    private sealed record EntrySeed(Guid Entry, Guid[] Senses);

    private EntrySeed Entry(string form, string[] glosses)
    {
        var services = _cache.ServiceLocator;
        var senses = new List<Guid>();
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var lexemeForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexemeForm;
            lexemeForm.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexemeForm.Form.set_String(_cache.DefaultVernWs, form);
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(_seed.PartOfSpeechId);
            foreach (var gloss in glosses)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(_cache.DefaultAnalWs, gloss);
                sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
        });
        return new EntrySeed(entryId, senses.ToArray());
    }

    private Guid AddExample(Guid sense, string text, string translation, string? reference)
    {
        Guid id = default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var example = _cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
            Sense(sense).ExamplesOS.Add(example);
            id = example.Guid;
            example.Example.set_String(_cache.DefaultVernWs, text);
            if (reference is not null)
                example.Reference = SIL.LCModel.Core.Text.TsStringUtils.MakeString(reference, _cache.DefaultVernWs);
            var made = _cache.ServiceLocator.GetInstance<ICmTranslationFactory>().Create(example,
                _cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().GetObject(CmPossibilityTags.kguidTranFreeTranslation));
            made.Translation.set_String(_cache.DefaultAnalWs, translation);
        });
        return id;
    }

    private ILexSense Sense(Guid id) => _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(id);

    private ILexExampleSentence Example(Guid id) => _cache.ServiceLocator.GetInstance<ILexExampleSentenceRepository>().GetObject(id);

    private static CanonicalId Id(Guid guid) => CanonicalId.FromGuid(guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
        operations);
}
