using SIL.Motif.Runner.Execution;
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

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorLexicalEntryComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public AuthorLexicalEntryComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void StemEntry_DryRunApplyAndReopen_BuildFormSenseGlossDefinitionAndStemMsa()
    {
        var category = Category(_cache);
        var nounClass = CreateInflectionClass(_cache, category, "7/8");
        var intent = new AuthorLexicalEntryIntent("kitabu", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphStem),
            "book", Anal(_cache), new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(category),
                InflectionClass: Id(nounClass)), "a bound set of written pages");

        var operations = AuthorLexicalEntryComposer.Build(_cache, intent);
        var entryId = Single(operations, LexDbEntriesOperationKinds.Create).EntityId!.Value;
        var msaId = Single(operations, LexEntryMorphoSyntaxAnalysesOperationKinds.Create).EntityId!.Value;
        var senseId = Single(operations, LexEntrySensesOperationKinds.Create).EntityId!.Value;
        Assert.Equal(Id(_cache.LangProject.LexDbOA), Single(operations, LexDbEntriesOperationKinds.Create).Target);
        Assert.False(Repository(_cache).IsValidObjectId(entryId.ToGuid()));

        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var membership = Assert.Single(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.LexDbEntries);
        Assert.Equal(entryId.Value, Assert.Single(membership.After).Value);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == senseId &&
            effect.Field == SnapshotFields.LexSenseDefinition);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-lexical-entry-tests");
        AssertStemEntry(_cache, entryId, msaId, senseId, category.Guid, nounClass.Guid);

        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        AssertStemEntry(reopened, entryId, msaId, senseId, category.Guid, nounClass.Guid);
        var sense = (ILexSense)Repository(reopened).GetObject(senseId.ToGuid());
        Assert.True(LexSenseAuthoringSnapshotter.Snapshot(reopened, sense).AlternativesFields
            .ContainsKey(SnapshotFields.LexSenseDefinition));
    }

    [Fact]
    public void InflectionalSuffix_FillsItsSlotAndCarriesAnAffixAllomorph()
    {
        var category = Category(_cache);
        var slot = CreateSlot(_cache, category);
        var intent = new AuthorLexicalEntryIntent("-ni", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphSuffix),
            "LOC", Anal(_cache), new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, Id(category),
                Slots: [Id(slot)]));

        var proposal = Proposal(AuthorLexicalEntryComposer.Build(_cache, intent));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-lexical-entry-tests");

        var entry = (ILexEntry)Repository(_cache).GetObject(
            proposal.Operations.Single(o => o.Kind == LexDbEntriesOperationKinds.Create).EntityId!.Value.ToGuid());
        Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(Assert.Single(entry.MorphoSyntaxAnalysesOC));
        Assert.Equal(category.Guid, msa.PartOfSpeechRA?.Guid);
        Assert.Equal([slot.Guid], msa.SlotsRC.Select(item => item.Guid));
        Assert.Equal(msa.Guid, Assert.Single(entry.SensesOS).MorphoSyntaxAnalysisRA?.Guid);
    }

    [Fact]
    public void DerivationalPrefix_RecordsFromAndToCategories()
    {
        var from = Category(_cache);
        var to = CreateCategory(_cache, "Verbo");
        var intent = new AuthorLexicalEntryIntent("ki-", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix),
            "NMLZ", Anal(_cache), new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, Id(from), Id(to)));

        var proposal = Proposal(AuthorLexicalEntryComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "author-lexical-entry-tests");

        var msa = Assert.IsAssignableFrom<IMoDerivAffMsa>(Repository(_cache).GetObject(proposal.Operations
            .Single(o => o.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create).EntityId!.Value.ToGuid()));
        Assert.Equal(from.Guid, msa.FromPartOfSpeechRA?.Guid);
        Assert.Equal(to.Guid, msa.ToPartOfSpeechRA?.Guid);
    }

    [Fact]
    public void Composer_RefusesMismatchedKindsWrongWritingSystemsAndForeignSlots()
    {
        var category = Category(_cache);
        var stem = MorphType(MoMorphTypeTags.kguidMorphStem);
        var suffix = MorphType(MoMorphTypeTags.kguidMorphSuffix);

        var stemAsAffix = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent("x", Vern(_cache), stem, "x", Anal(_cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, Id(category)))));
        Assert.Contains("needs a stem msa", stemAsAffix.Message, StringComparison.Ordinal);

        var affixAsStem = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent("-x", Vern(_cache), suffix, "x", Anal(_cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(category)))));
        Assert.Contains("affix msa", affixAsStem.Message, StringComparison.Ordinal);

        var analysisAsForm = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent("x", Anal(_cache) == Vern(_cache) ? "qaa-x-no" : Anal(_cache), stem, "x",
                Anal(_cache), new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(category)))));
        Assert.Contains("vernacular", analysisAsForm.Message, StringComparison.Ordinal);

        var other = CreateCategory(_cache, "Otra");
        var foreignSlot = CreateSlot(_cache, other);
        var slotRefusal = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent("-x", Vern(_cache), suffix, "x", Anal(_cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, Id(category), Slots: [Id(foreignSlot)]))));
        Assert.Contains("does not belong to the category", slotRefusal.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent(" ", Vern(_cache), stem, "x", Anal(_cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(category)))));
    }

    [Fact]
    public void EntryCreate_RefusesIdentityCollisionsAndATargetThatIsNotTheLexicon()
    {
        var existing = Repository(_cache).GetObject(_seed.FirstEntryId);
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([EntryCreate(Id(_cache.LangProject.LexDbOA), Id(existing))])));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);

        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([EntryCreate(Id(_cache.LangProject.LexDbOA), Id(Category(_cache)))])));
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Proposal([EntryCreate(Id(Category(_cache)), CanonicalId.Mint())])));
    }

    [Fact]
    public void EntryCreate_WithAPayload_IsRejectedByTheClosedSchema()
    {
        var operation = new OperationEnvelope(CanonicalId.Mint(), LexDbEntriesOperationKinds.Create,
            entityId: CanonicalId.Mint(), target: Id(_cache.LangProject.LexDbOA),
            after: JsonSerializer.SerializeToElement(new { headword = "walk" }));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
    }

    [Fact]
    public void FailureAfterTheSenseRollsBackTheWholeEntry()
    {
        var count = _cache.LangProject.LexDbOA.Entries.Count();
        var proposal = Proposal(AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("mti",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphStem), "tree", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(Category(_cache))))));
        var entryId = proposal.Operations.Single(o => o.Kind == LexDbEntriesOperationKinds.Create).EntityId!.Value;
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var exception = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
            _cache, proposal, dryRun.Anchor, "author-lexical-entry-tests", string.Empty,
            afterOperation: (_, operation) =>
            {
                if (operation.Kind == LexicalSenseOperationKinds.SetGloss)
                    throw new InvalidOperationException("injected after the gloss");
            }));

        Assert.Equal("injected after the gloss", exception.Message);
        Assert.False(Repository(_cache).IsValidObjectId(entryId.ToGuid()));
        Assert.Equal(count, _cache.LangProject.LexDbOA.Entries.Count());
    }

    [Theory]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en","msa":{"kind":"stem","category":"AAAAAAAAAAAAAAAAAAAAAA"},"citation":"p. 1"}""")]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en"}""")]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en","msa":{"kind":"stem","category":"AAAAAAAAAAAAAAAAAAAAAA","toCategory":"AAAAAAAAAAAAAAAAAAAAAA"}}""")]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en","msa":{"kind":"derivational","category":"AAAAAAAAAAAAAAAAAAAAAA"}}""")]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en","msa":{"kind":"stem","category":"AAAAAAAAAAAAAAAAAAAAAA","slots":[]}}""")]
    [InlineData("""{"form":"x","formWs":"sw","morphType":"AAAAAAAAAAAAAAAAAAAAAA","gloss":"x","analysisWs":"en","msa":{"kind":"root","category":"AAAAAAAAAAAAAAAAAAAAAA"}}""")]
    public void IntentParser_IsClosed(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.ThrowsAny<Exception>(() => AuthorLexicalEntryIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void IntentParser_ReadsAnInflectionalAffix()
    {
        var category = CanonicalId.Mint();
        var slot = CanonicalId.Mint();
        var morphType = CanonicalId.Mint();
        using var document = JsonDocument.Parse($$$"""
            {"form":"-ni","formWs":"sw","morphType":"{{{morphType.Value}}}","gloss":"LOC","analysisWs":"en",
             "definition":"locative","msa":{"kind":"inflectional","category":"{{{category.Value}}}","slots":["{{{slot.Value}}}"]}}
            """);
        var intent = AuthorLexicalEntryIntentParser.Parse(document.RootElement);
        Assert.Equal(LexicalEntryMsaKind.Inflectional, intent.Msa.Kind);
        Assert.Equal([slot], intent.Msa.Slots!);
        Assert.Equal("locative", intent.Definition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DirectIntentCannotSilentlyDiscardMsaFieldsOrAnUnknownKind(int invalidCase)
    {
        var category = Id(Category(_cache));
        var nounClass = Id(CreateInflectionClass(_cache, Category(_cache), "Class"));
        var msa = invalidCase switch
        {
            0 => new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, ToCategory: category),
            1 => new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category, Slots: []),
            2 => new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, InflectionClass: nounClass),
            3 => new LexicalEntryMsa((LexicalEntryMsaKind)999, category),
            _ => new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, category),
        };
        var morphType = invalidCase == 1 ? MoMorphTypeTags.kguidMorphStem : MoMorphTypeTags.kguidMorphSuffix;
        var exception = Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache,
            new AuthorLexicalEntryIntent("x", Vern(_cache), MorphType(morphType), "X", Anal(_cache), msa)));
        Assert.Contains("AuthorLexicalEntry", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LexDbEntriesOperationKinds.Create)]
    [InlineData(LexEntrySensesOperationKinds.Create)]
    [InlineData(LexEntryMorphoSyntaxAnalysesOperationKinds.Create)]
    public void CreatesCannotAddressOneMemberSlotTwice(string kind)
    {
        var owner = CanonicalId.Mint();
        var member = CanonicalId.Mint();
        OperationEnvelope Create() => new(CanonicalId.Mint(), kind, target: owner, entityId: member);
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([Create(), Create()]));
    }

    [Fact]
    public void DefinitionSetAndClearShareAWritingSystemSlot()
    {
        var sense = CanonicalId.Mint();
        var set = new OperationEnvelope(CanonicalId.Mint(), LexSenseDefinitionOperationKinds.SetDefinition,
            target: sense, after: JsonSerializer.SerializeToElement(new { ws = "en", text = "definition" }));
        var clear = new OperationEnvelope(CanonicalId.Mint(), LexSenseDefinitionOperationKinds.ClearDefinition,
            target: sense, after: JsonSerializer.SerializeToElement(new { ws = "en" }));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([set, clear]));
    }

    [Theory]
    [InlineData("MoStemMsa")]
    [InlineData("MoInflAffMsa")]
    [InlineData("MoDerivAffMsa")]
    [InlineData("MoUnclassifiedAffixMsa")]
    public void MsaCreationPreservesItsConcreteClassAndEntryOwner(string concreteClass)
    {
        var entry = (ILexEntry)Repository(_cache).GetObject(_seed.FirstEntryId);
        var id = CanonicalId.Mint();
        var proposal = Proposal([new(CanonicalId.Mint(), LexEntryMorphoSyntaxAnalysesOperationKinds.Create,
            target: Id(entry), entityId: id, after: JsonSerializer.SerializeToElement(new { @class = concreteClass }))]);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "msa-tests");
        var created = Repository(_cache).GetObject(id.ToGuid());
        Assert.Equal(concreteClass, created.ClassName);
        Assert.Equal(entry.Guid, created.Owner?.Guid);
    }

    [Fact]
    public void DefinitionUsesRichStringNormalizationAndClearLeavesOtherAlternativesAndGlossAlone()
    {
        var sense = (ILexSense)Repository(_cache).GetObject(_seed.FirstSenseId);
        var gloss = sense.Gloss.get_String(_cache.DefaultAnalWs).Text;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            sense.Definition.set_String(_cache.DefaultVernWs,
                SIL.LCModel.Core.Text.TsStringUtils.MakeString("Other definition", _cache.DefaultVernWs)));
        var set = Proposal([new(CanonicalId.Mint(), LexSenseDefinitionOperationKinds.SetDefinition,
            target: Id(sense), after: JsonSerializer.SerializeToElement(new { ws = Anal(_cache), text = "e\u0301" }))]);
        ProposalApplier.Apply(_cache, set, ScratchDryRun.Of(_cache, set).Anchor, "definition-tests");
        var before = LexSenseAuthoringSnapshotter.Snapshot(_cache, sense);
        Assert.Equal("\u00e9", before.AlternativesFields[SnapshotFields.LexSenseDefinition][Anal(_cache)]);
        var clear = Proposal([new(CanonicalId.Mint(), LexSenseDefinitionOperationKinds.ClearDefinition,
            target: Id(sense), after: JsonSerializer.SerializeToElement(new { ws = Anal(_cache) }))]);
        ProposalApplier.Apply(_cache, clear, ScratchDryRun.Of(_cache, clear).Anchor, "definition-tests");
        var after = LexSenseAuthoringSnapshotter.Snapshot(_cache, sense);
        Assert.False(after.AlternativesFields[SnapshotFields.LexSenseDefinition].ContainsKey(Anal(_cache)));
        Assert.Equal("Other definition", after.AlternativesFields[SnapshotFields.LexSenseDefinition][Vern(_cache)]);
        Assert.Equal(gloss, sense.Gloss.get_String(_cache.DefaultAnalWs).Text);
        var diff = SoundSystemSnapshots.Compare(new Dictionary<CanonicalId, ObjectSnapshot> { [Id(sense)] = before },
            new Dictionary<CanonicalId, ObjectSnapshot> { [Id(sense)] = after });
        Assert.Equal(SnapshotFields.LexSenseDefinition, Assert.Single(diff).Field);
    }

    [Fact]
    public void SenseCreationRequiresPlacementAndRefusesDriftBeforeWriting()
    {
        var entry = (ILexEntry)Repository(_cache).GetObject(_seed.FirstEntryId);
        var member = CanonicalId.Mint();
        OperationEnvelope Create(Placement? placement) => new(CanonicalId.Mint(), LexEntrySensesOperationKinds.Create,
            target: Id(entry), entityId: member, after: JsonSerializer.SerializeToElement(new { }), placement: placement);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(null)])));
        var proposal = Proposal([Create(new Placement(Id(entry.SensesOS.Last()), null))]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            entry.SensesOS.Add(_cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create()));
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "sense-tests"));
        Assert.False(Repository(_cache).IsValidObjectId(member.ToGuid()));
        var refreshed = Proposal([Create(new Placement(Id(entry.SensesOS.Last()), null))]);
        ProposalApplier.Apply(_cache, refreshed, ScratchDryRun.Of(_cache, refreshed).Anchor, "sense-tests");
        Assert.Equal(member.ToGuid(), entry.SensesOS.Last().Guid);
    }

    [Theory]
    [InlineData(LexEntrySensesOperationKinds.Create)]
    [InlineData(LexEntryMorphoSyntaxAnalysesOperationKinds.Create)]
    public void OwnedCreatePayloadsAndIdentityCollisionsAreRefused(string kind)
    {
        var entry = (ILexEntry)Repository(_cache).GetObject(_seed.FirstEntryId);
        var existing = kind == LexEntrySensesOperationKinds.Create
            ? (ICmObject)entry.SensesOS.First() : entry.MorphoSyntaxAnalysesOC.First();
        var payload = kind == LexEntrySensesOperationKinds.Create
            ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(new { @class = existing.ClassName });
        OperationEnvelope Create(CanonicalId member, JsonElement after) => new(CanonicalId.Mint(), kind,
            target: Id(entry), entityId: member, after: after);
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(Id(existing), payload)])));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(Id(entry), payload)])));
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(CanonicalId.Mint(), JsonSerializer.SerializeToElement(new { extra = "unknown" }))])));
    }

    private static void AssertStemEntry(LcmCache cache, CanonicalId entryId, CanonicalId msaId, CanonicalId senseId,
        Guid category, Guid nounClass)
    {
        var entry = Assert.IsAssignableFrom<ILexEntry>(Repository(cache).GetObject(entryId.ToGuid()));
        var form = Assert.IsAssignableFrom<IMoStemAllomorph>(entry.LexemeFormOA);
        Assert.Equal("kitabu", form.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal(MoMorphTypeTags.kguidMorphStem, form.MorphTypeRA?.Guid);
        var msa = Assert.IsAssignableFrom<IMoStemMsa>(Assert.Single(entry.MorphoSyntaxAnalysesOC));
        Assert.Equal(msaId.ToGuid(), msa.Guid);
        Assert.Equal(category, msa.PartOfSpeechRA?.Guid);
        Assert.Equal(nounClass, msa.InflectionClassRA?.Guid);
        var sense = Assert.Single(entry.SensesOS);
        Assert.Equal(senseId.ToGuid(), sense.Guid);
        Assert.Equal("book", sense.Gloss.get_String(cache.DefaultAnalWs)?.Text);
        Assert.Equal("a bound set of written pages", sense.Definition.get_String(cache.DefaultAnalWs)?.Text);
        Assert.Equal(msa.Guid, sense.MorphoSyntaxAnalysisRA?.Guid);
    }

    private IPartOfSpeech Category(LcmCache cache) =>
        (IPartOfSpeech)Repository(cache).GetObject(_seed.PartOfSpeechId);

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

    private static IMoInflClass CreateInflectionClass(LcmCache cache, IPartOfSpeech category, string name)
    {
        IMoInflClass result = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            result = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
            category.InflectionClassesOC.Add(result);
            result.Name.set_String(cache.DefaultAnalWs, name);
        });
        return result;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category)
    {
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
        });
        return slot;
    }

    private static OperationEnvelope EntryCreate(CanonicalId target, CanonicalId entityId) =>
        new(CanonicalId.Mint(), LexDbEntriesOperationKinds.Create, entityId: entityId, target: target,
            after: JsonSerializer.SerializeToElement(new { }));

    private static OperationEnvelope Single(IReadOnlyList<OperationEnvelope> operations, string kind) =>
        Assert.Single(operations, operation => operation.Kind == kind);

    private static CanonicalId MorphType(Guid guid) => CanonicalId.FromGuid(guid);

    private static ICmObjectRepository Repository(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    private static string Vern(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);

    private static string Anal(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
        operations);
}
