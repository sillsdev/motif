using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorAdhocProhibitionComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    public AuthorAdhocProhibitionComposerTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void AMorphemeProhibitionIsCreatedThroughDryRunAndApplyAsFieldWorksHoldsIt()
    {
        var (a, b, _) = Seed();
        var proposal = Proposal(AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", Id(a.MorphoSyntaxAnalysesOC.Single()), [Id(b.MorphoSyntaxAnalysesOC.Single())], "anywhere")));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoMorphDataAdhocCoProhibitions, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Single(effect.After);
        Assert.Empty(_cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        var created = Assert.IsAssignableFrom<IMoMorphAdhocProhib>(Assert.Single(_cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC));
        Assert.Equal(a.MorphoSyntaxAnalysesOC.Single().Guid, created.FirstMorphemeRA.Guid);
        Assert.Equal(b.MorphoSyntaxAnalysesOC.Single().Guid, Assert.Single(created.RestOfMorphsRS).Guid);
        Assert.Equal(0, created.Adjacency);
        Assert.False(created.Disabled);
        Assert.Equal(CanonicalId.Parse(effect.After.Single().Value).ToGuid(), created.Guid);
    }

    [Fact]
    public void AnAllomorphProhibitionKeepsItsAdjacencyAndOrderedRest()
    {
        var (a, b, c) = Seed();
        var proposal = Proposal(AuthorAdhocProhibitionComposer.Build(_cache,
            new("allomorph", Id(a.LexemeFormOA), [Id(b.LexemeFormOA), Id(c.LexemeFormOA)], "adjacent-to-left")));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
        var created = Assert.IsAssignableFrom<IMoAlloAdhocProhib>(Assert.Single(_cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC));
        Assert.Equal(a.LexemeFormOA.Guid, created.FirstAllomorphRA.Guid);
        Assert.Equal([b.LexemeFormOA.Guid, c.LexemeFormOA.Guid], created.RestOfAllosRS.Select(x => x.Guid));
        Assert.Equal(4, created.Adjacency);
        Assert.False(created.Disabled);
    }

    [Fact]
    public void TheDryRunStatesPlainlyWhatIsProhibitedNextToWhat()
    {
        var (a, b, _) = Seed();
        var proposal = Proposal(AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", Id(a.MorphoSyntaxAnalysesOC.Single()), [Id(b.MorphoSyntaxAnalysesOC.Single())], "somewhere-to-right")));
        var preview = Assert.Single(ScratchDryRun.Of(_cache, proposal).ExpectedEffects).Preview!.Value;
        Assert.Equal("morpheme 'aaa' cannot occur somewhere to the right of bbb", preview.GetProperty("prohibits").GetString());
        Assert.Contains("cannot occur somewhere to the right of", proposal.Operations.Single().Rationale);
    }

    [Fact]
    public void ADuplicateOfAnExistingProhibitionIsRefusedIncludingAReorderedAnywhereRest()
    {
        var (a, b, c) = Seed();
        Apply(Build("allomorph", a, [b, c], "anywhere"));
        Assert.Equal(RefusalCodes.AdhocProhibitionDuplicate, Code(() => Build("allomorph", a, [c, b], "anywhere")));
        Build("allomorph", a, [b], "anywhere");
        Apply(Build("allomorph", a, [b, c], "adjacent-to-right"));
        Assert.Equal(RefusalCodes.AdhocProhibitionDuplicate, Code(() => Build("allomorph", a, [b, c], "adjacent-to-right")));
        Assert.NotEmpty(Build("allomorph", a, [c, b], "adjacent-to-right"));
    }

    [Fact]
    public void UnknownOrWrongKindOfTargetsAreRefused()
    {
        var (a, b, _) = Seed();
        var missing = CanonicalId.Mint();
        var msa = Id(b.MorphoSyntaxAnalysesOC.Single());
        Assert.Equal(RefusalCodes.AdhocProhibitionInvalidMorpheme, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", Id(a.MorphoSyntaxAnalysesOC.Single()), [missing], "anywhere"))));
        Assert.Equal(RefusalCodes.AdhocProhibitionInvalidMorpheme, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", Id(a.LexemeFormOA), [msa], "anywhere"))));
        Assert.Equal(RefusalCodes.AdhocProhibitionInvalidAllomorph, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("allomorph", missing, [Id(b.LexemeFormOA)], "anywhere"))));
        Assert.Equal(RefusalCodes.AdhocProhibitionInvalidAllomorph, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("allomorph", Id(a.LexemeFormOA), [msa], "anywhere"))));
        Assert.ThrowsAny<InvalidOperationException>(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("allomorph", Id(a.LexemeFormOA), [], "anywhere")));
    }

    [Fact]
    public void AFirstMorphemeOrAllomorphThatIsAlsoInTheRestIsRefused()
    {
        var (a, b, _) = Seed();
        var msa = Id(a.MorphoSyntaxAnalysesOC.Single());
        Assert.Equal(RefusalCodes.AdhocProhibitionSelf, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", msa, [Id(b.MorphoSyntaxAnalysesOC.Single()), msa], "anywhere"))));
        Assert.Equal(RefusalCodes.AdhocProhibitionSelf, Code(() => AuthorAdhocProhibitionComposer.Build(_cache,
            new("allomorph", Id(a.LexemeFormOA), [Id(a.LexemeFormOA)], "adjacent-to-left"))));
    }

    [Fact]
    public void AProhibitionCreatedAfterTheDryRunInvalidatesIt()
    {
        var (a, b, _) = Seed();
        var proposal = Proposal(Build("allomorph", a, [b], "anywhere"));
        var dry = ScratchDryRun.Of(_cache, proposal);
        Apply(Build("allomorph", a, [b], "anywhere"));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester"));
    }

    [Fact]
    public void TheParserRejectsAnUnknownAdjacencyAndUnknownProperties()
    {
        var (a, b, _) = Seed();
        string Json(object value) => JsonSerializer.Serialize(value);
        Assert.Throws<ContractParseException>(() => AuthorAdhocProhibitionIntentParser.Parse(JsonDocument.Parse(Json(new
            { kind = "allomorph", first = Id(a.LexemeFormOA).Value, rest = new[] { Id(b.LexemeFormOA).Value }, adjacency = "left" })).RootElement));
        Assert.Throws<ContractParseException>(() => AuthorAdhocProhibitionIntentParser.Parse(JsonDocument.Parse(Json(new
            { kind = "rule", first = Id(a.LexemeFormOA).Value, rest = new[] { Id(b.LexemeFormOA).Value } })).RootElement));
        var parsed = AuthorAdhocProhibitionIntentParser.Parse(JsonDocument.Parse(Json(new
            { kind = "allomorph", first = Id(a.LexemeFormOA).Value, rest = new[] { Id(b.LexemeFormOA).Value } })).RootElement);
        Assert.Equal("anywhere", parsed.Adjacency);
    }

    private IReadOnlyList<OperationEnvelope> Build(string kind, ILexEntry first, ILexEntry[] rest, string adjacency) =>
        AuthorAdhocProhibitionComposer.Build(_cache, new(kind, Id(first.LexemeFormOA), rest.Select(e => Id(e.LexemeFormOA)).ToArray(), adjacency));

    private void Apply(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
    }

    private static string Code(Action action) => Assert.ThrowsAny<ProposalRefusalException>(action).Code;

    private (ILexEntry A, ILexEntry B, ILexEntry C) Seed()
    {
        ILexEntry a = null!, b = null!, c = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            ILexEntry Make(string form) => services.GetInstance<ILexEntryFactory>().Create(stem,
                TsStringUtils.MakeString(form, _cache.DefaultVernWs), "gloss " + form, new SandboxGenericMSA { MsaType = MsaType.kStem });
            a = Make("aaa"); b = Make("bbb"); c = Make("ccc");
        });
        return (a, b, c);
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
