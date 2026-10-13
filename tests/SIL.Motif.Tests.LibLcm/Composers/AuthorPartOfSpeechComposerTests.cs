using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorPartOfSpeechComposerTests : IDisposable
{
    private readonly LcmCache _cache;

    public AuthorPartOfSpeechComposerTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void TopLevelCategory_DryRunApplyAndReopen_PreserveIdentityOwnerNameAndAbbreviation()
    {
        var list = _cache.LangProject.PartsOfSpeechOA;
        var before = list.PossibilitiesOS.Count;
        var categoryId = CanonicalId.Mint();
        var ids = new Queue<CanonicalId>([CanonicalId.Mint(), categoryId, CanonicalId.Mint(), CanonicalId.Mint()]);

        var operations = AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Nomino", Ws(_cache), "nom"), () => ids.Dequeue());

        var create = Assert.Single(operations, operation =>
            operation.Kind == CmPossibilityListPossibilitiesOperationKinds.Create);
        Assert.Equal(categoryId, create.EntityId);
        Assert.Equal(Id(list), create.Target);
        Assert.All(operations.Where(operation => operation != create), operation =>
            Assert.Contains(operation.DependsOn, dependency => dependency.OperationId == create.OperationId));
        Assert.Equal(before, list.PossibilitiesOS.Count);

        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == Id(list) &&
            effect.Field == SnapshotFields.CmPossibilityListPossibilities);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == categoryId &&
            effect.Field == SnapshotFields.CmPossibilityName);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == categoryId &&
            effect.Field == SnapshotFields.CmPossibilityAbbreviation);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-part-of-speech-tests");
        var created = Assert.IsAssignableFrom<IPartOfSpeech>(Repository(_cache).GetObject(categoryId.ToGuid()));
        Assert.Equal(list.Guid, created.Owner?.Guid);
        Assert.Equal("Nomino", created.Name.get_String(_cache.DefaultAnalWs)?.Text);
        Assert.Equal("nom", created.Abbreviation.get_String(_cache.DefaultAnalWs)?.Text);
        var snapshot = CmPossibilityAuthoringSnapshotter.Snapshot(_cache, created);
        Assert.True(snapshot.AlternativesFields.ContainsKey(SnapshotFields.CmPossibilityName));

        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reopenedCategory = Assert.IsAssignableFrom<IPartOfSpeech>(
            Repository(reopened).GetObject(categoryId.ToGuid()));
        Assert.Equal(reopened.LangProject.PartsOfSpeechOA.Guid, reopenedCategory.Owner?.Guid);
        Assert.Equal("Nomino", reopenedCategory.Name.get_String(reopened.DefaultAnalWs)?.Text);
        Assert.Equal(snapshot.AlternativesFields[SnapshotFields.CmPossibilityName],
            CmPossibilityAuthoringSnapshotter.Snapshot(reopened, reopenedCategory)
                .AlternativesFields[SnapshotFields.CmPossibilityName]);
    }

    [Fact]
    public void Subcategory_IsOwnedByItsParentPartOfSpeech()
    {
        var parent = CreateCategory(_cache, "Verbo");
        var operations = AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Verbo transitivo", Ws(_cache), Parent: Id(parent)));
        var create = Assert.Single(operations, operation =>
            operation.Kind == CmPossibilitySubPossibilitiesOperationKinds.Create);
        Assert.Equal(Id(parent), create.Target);

        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-part-of-speech-tests");

        var child = Assert.IsAssignableFrom<IPartOfSpeech>(Repository(_cache).GetObject(create.EntityId!.Value.ToGuid()));
        Assert.Equal(parent.Guid, child.Owner?.Guid);
        Assert.Contains(child, parent.SubPossibilitiesOS);
    }

    [Fact]
    public void Composer_RefusesADuplicateNameAnUnknownWritingSystemAndAParentThatIsNotACategory()
    {
        CreateCategory(_cache, "Adjetivo");

        var duplicate = Assert.Throws<InvalidOperationException>(() => AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Adjetivo", Ws(_cache))));
        Assert.Contains("already exists", duplicate.Message, StringComparison.Ordinal);

        var unknownWs = Assert.Throws<InvalidOperationException>(() => AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Adverbio", "qaa-x-nowhere")));
        Assert.Contains("not known to this project", unknownWs.Message, StringComparison.Ordinal);

        var morphType = _cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.First();
        Assert.ThrowsAny<Exception>(() => AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Adverbio", Ws(_cache), Parent: Id(morphType))));

        Assert.Throws<InvalidOperationException>(() => AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("  ", Ws(_cache))));
    }

    [Fact]
    public void CreateKind_RefusesAListWhoseItemsAreNotPartsOfSpeechAndAnyOtherClass()
    {
        var morphTypes = _cache.LangProject.LexDbOA.MorphTypesOA;
        var count = morphTypes.PossibilitiesOS.Count;
        var wrongList = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([CreateOperation(Id(morphTypes), CanonicalId.Mint(), "PartOfSpeech")])));
        Assert.Contains("parts-of-speech list", wrongList.Message, StringComparison.Ordinal);
        Assert.Equal(count, morphTypes.PossibilitiesOS.Count);

        var wrongClass = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([CreateOperation(Id(_cache.LangProject.PartsOfSpeechOA), CanonicalId.Mint(), "CmPossibility")])));
        Assert.Contains("class must be PartOfSpeech", wrongClass.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateKind_RefusesSameTypeAndWrongTypeIdentityCollisions()
    {
        var list = _cache.LangProject.PartsOfSpeechOA;
        var existing = CreateCategory(_cache, "Existente");

        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([CreateOperation(Id(list), Id(existing), "PartOfSpeech")])));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);

        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([CreateOperation(Id(list), Id(_cache.LangProject.LexDbOA), "PartOfSpeech")])));
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureAfterTheNameRollsBackTheNewCategory()
    {
        var list = _cache.LangProject.PartsOfSpeechOA;
        var before = list.PossibilitiesOS.Select(item => item.Guid).ToArray();
        var proposal = Proposal(AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Pronombre", Ws(_cache), "pro")));
        var categoryId = proposal.Operations.Single(operation => operation.EntityId is not null).EntityId!.Value;
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var exception = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
            _cache, proposal, dryRun.Anchor, "author-part-of-speech-tests", string.Empty,
            afterOperation: (_, operation) =>
            {
                if (operation.Kind == CmPossibilityNameOperationKinds.SetName)
                    throw new InvalidOperationException("injected after naming");
            }));

        Assert.Equal("injected after naming", exception.Message);
        Assert.False(Repository(_cache).IsValidObjectId(categoryId.ToGuid()));
        Assert.Equal(before, list.PossibilitiesOS.Select(item => item.Guid));
    }

    [Theory]
    [InlineData("""{"name":"noun","ws":"en","citation":"Ashton 1944 p. 12"}""")]
    [InlineData("""{"ws":"en"}""")]
    [InlineData("""{"name":"noun"}""")]
    [InlineData("""{"name":"noun","ws":"en","parent":"not-an-id"}""")]
    public void IntentParser_IsClosed(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.ThrowsAny<Exception>(() => AuthorPartOfSpeechIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void IntentParser_ReadsEveryField()
    {
        var parent = CanonicalId.Mint();
        using var document = JsonDocument.Parse(
            $$"""{"name":"noun","ws":"en","abbreviation":"n","parent":"{{parent.Value}}"}""");
        Assert.Equal(new AuthorPartOfSpeechIntent("noun", "en", "n", parent),
            AuthorPartOfSpeechIntentParser.Parse(document.RootElement));
    }


    [Fact]
    public void SlotValidator_RefusesTwoWritesToOneCategoryNameInOneWritingSystem()
    {
        var category = CanonicalId.Mint();
        var setEn = NameOperation(CmPossibilityNameOperationKinds.SetName, category, new { ws = "en", text = "noun" });
        var setEnAgain = NameOperation(CmPossibilityNameOperationKinds.SetName, category, new { ws = "en", text = "n" });
        var clearEn = NameOperation(CmPossibilityNameOperationKinds.ClearName, category, new { ws = "en" });
        var setFr = NameOperation(CmPossibilityNameOperationKinds.SetName, category, new { ws = "fr", text = "nom" });

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([setEn, setEnAgain]));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([setEn, clearEn]));
        Assert.Null(ProposalOperationSlotValidator.FindConflict([setEn, setFr]));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([
            NameOperation(CmPossibilityAbbreviationOperationKinds.SetAbbreviation, category, new { ws = "en", text = "n" }),
            NameOperation(CmPossibilityAbbreviationOperationKinds.ClearAbbreviation, category, new { ws = "en" })]));
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(Proposal([setEn, clearEn])));
        var member = CanonicalId.Mint();
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([
            CreateOperation(category, member, "PartOfSpeech"), CreateOperation(category, member, "PartOfSpeech")]));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([
            new(CanonicalId.Mint(), CmPossibilitySubPossibilitiesOperationKinds.Create, target: category, entityId: member),
            new(CanonicalId.Mint(), CmPossibilitySubPossibilitiesOperationKinds.Create, target: category, entityId: member)]));
    }

    [Fact]
    public void ClearName_RemovesOneWritingSystemsName_AndTheSnapshotDiffShowsIt()
    {
        var category = CreateCategory(_cache, "Interjección");
        var before = new Dictionary<CanonicalId, ObjectSnapshot>
            { [Id(category)] = CmPossibilityAuthoringSnapshotter.Snapshot(_cache, category) };
        var proposal = Proposal([NameOperation(CmPossibilityNameOperationKinds.ClearName, Id(category),
            new { ws = Ws(_cache) })]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == Id(category) &&
            effect.Field == SnapshotFields.CmPossibilityName);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-part-of-speech-tests");

        Assert.True(string.IsNullOrEmpty(category.Name.get_String(_cache.DefaultAnalWs)?.Text));
        var after = new Dictionary<CanonicalId, ObjectSnapshot>
            { [Id(category)] = CmPossibilityAuthoringSnapshotter.Snapshot(_cache, category) };
        var change = Assert.Single(SoundSystemSnapshots.Compare(before, after));
        Assert.Equal(SnapshotFields.CmPossibilityName, change.Field);
        Assert.Empty(SoundSystemSnapshots.Compare(after, after));
    }

    [Fact]
    public void ACategoryAddedAfterTheDryRun_IsFootprintDrift_SoApplyNeedsAFreshDryRun()
    {
        var list = _cache.LangProject.PartsOfSpeechOA;
        var proposal = Proposal(AuthorPartOfSpeechComposer.Build(_cache, new AuthorPartOfSpeechIntent("Numeral", Ws(_cache))));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        CreateCategory(_cache, "Concurrent");
        var count = list.PossibilitiesOS.Count;

        var exception = Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(
            _cache, proposal, dryRun.Anchor, "author-part-of-speech-tests"));

        Assert.Contains("Footprint drift detected", exception.Message, StringComparison.Ordinal);
        Assert.Equal(count, list.PossibilitiesOS.Count);
        var rerun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, rerun.Anchor, "author-part-of-speech-tests");
        Assert.Equal(count + 1, list.PossibilitiesOS.Count);
    }

    [Fact]
    public void ADryRunFromAnotherSnapshotProjection_IsRefusedByApplyWithAFreshDryRunInstruction()
    {
        var proposal = Proposal(AuthorPartOfSpeechComposer.Build(_cache, new AuthorPartOfSpeechIntent("Clitic", Ws(_cache))));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var stale = dryRun.Anchor with { ProjectionVersion = "4" };

        var exception = Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(
            _cache, proposal, stale, "author-part-of-speech-tests"));

        Assert.Contains("projection version 4", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Run a fresh Dry Run", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_cache.LangProject.PartsOfSpeechOA.PossibilitiesOS, item =>
            item.Name.get_String(_cache.DefaultAnalWs)?.Text == "Clitic");
    }

    [Fact]
    public void DuplicateNamesIncludeSubcategoriesAndEquivalentUnicode()
    {
        var parent = CreateCategory(_cache, "Parent");
        var proposal = Proposal(AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Interjección", Ws(_cache), Parent: Id(parent))));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "category-tests");
        Assert.Throws<InvalidOperationException>(() => AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Interjeccio\u0301n", Ws(_cache))));
    }

    [Theory]
    [InlineData(CmPossibilityListPossibilitiesOperationKinds.Create)]
    [InlineData(CmPossibilitySubPossibilitiesOperationKinds.Create)]
    public void CreatePayloadRejectsUnknownPropertiesAndPlacement(string kind)
    {
        var owner = kind == CmPossibilityListPossibilitiesOperationKinds.Create
            ? Id(_cache.LangProject.PartsOfSpeechOA) : Id(CreateCategory(_cache, "Parent"));
        var unknown = new OperationEnvelope(CanonicalId.Mint(), kind, target: owner, entityId: CanonicalId.Mint(),
            after: JsonSerializer.SerializeToElement(new { @class = "PartOfSpeech", name = "Unbound" }));
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, Proposal([unknown])));
        var positioned = new OperationEnvelope(CanonicalId.Mint(), kind, target: owner, entityId: CanonicalId.Mint(),
            after: JsonSerializer.SerializeToElement(new { @class = "PartOfSpeech" }),
            placement: new Placement(after: CanonicalId.Mint(), before: null));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([positioned])));
    }

    [Fact]
    public void SubcategoryCreationRefusesAnOwnerThatIsNotAPartOfSpeech()
    {
        var owner = _cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.First();
        var operation = new OperationEnvelope(CanonicalId.Mint(), CmPossibilitySubPossibilitiesOperationKinds.Create,
            target: Id(owner), entityId: CanonicalId.Mint(),
            after: JsonSerializer.SerializeToElement(new { @class = "PartOfSpeech" }));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
    }

    private static OperationEnvelope NameOperation(string kind, CanonicalId target, object after) =>
        new(CanonicalId.Mint(), kind, target: target, after: JsonSerializer.SerializeToElement(after));

    private static OperationEnvelope CreateOperation(CanonicalId target, CanonicalId entityId, string concreteClass) =>
        new(CanonicalId.Mint(), CmPossibilityListPossibilitiesOperationKinds.Create, entityId: entityId,
            target: target, after: JsonSerializer.SerializeToElement(new { @class = concreteClass }));

    private static IPartOfSpeech CreateCategory(LcmCache cache, string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, name);
        });
        return category;
    }

    private static ICmObjectRepository Repository(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    private static string Ws(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lists"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
