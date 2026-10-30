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
        Assert.Equal("morpheme 'aaa' cannot occur somewhere to the right of bbb (the first comes after the rest in the word)", preview.GetProperty("prohibits").GetString());
        Assert.Contains("cannot occur somewhere to the right of", proposal.Operations.Single().Rationale);
    }

    [Fact]
    public void TheDryRunSpellsOutWhichSideTheFirstIsOnForEveryDirection()
    {
        var (a, b, _) = Seed();
        string Says(string adjacency) => Assert.Single(ScratchDryRun.Of(_cache, Proposal(Build("allomorph", a, [b], adjacency))).ExpectedEffects)
            .Preview!.Value.GetProperty("prohibits").GetString()!;
        Assert.Equal("allomorph 'aaa' cannot occur in the same word as bbb", Says("anywhere"));
        Assert.EndsWith("immediately to the right of bbb (the first comes after the rest in the word)", Says("adjacent-to-right"));
        Assert.EndsWith("somewhere to the left of bbb (the first comes before the rest in the word)", Says("somewhere-to-left"));
        Assert.EndsWith("immediately to the left of bbb (the first comes before the rest in the word)", Says("adjacent-to-left"));
    }

    [Fact]
    public void APrefixFirstWithAnAdjacencyToItsRightOfTheStemWarnsInTheDryRunButIsStillStaged()
    {
        var (stem, _, _) = Seed();
        var prefix = SeedAffix("pa", MoMorphTypeTags.kguidMorphPrefix, MsaType.kInfl);
        foreach (var adjacency in new[] { "somewhere-to-right", "adjacent-to-right" })
        {
            var warning = PreviewWarning(Build("allomorph", prefix, [stem], adjacency));
            Assert.Contains("never match", warning);
            Assert.Contains(adjacency.Replace("right", "left"), warning);
        }
        foreach (var adjacency in new[] { "anywhere", "somewhere-to-left", "adjacent-to-left" })
            Assert.Null(PreviewWarning(Build("allomorph", prefix, [stem], adjacency)));
        var morpheme = AuthorAdhocProhibitionComposer.Build(_cache, new("morpheme", Id(prefix.MorphoSyntaxAnalysesOC.Single()),
            [Id(stem.MorphoSyntaxAnalysesOC.Single())], "adjacent-to-right"));
        Assert.Contains("never match", PreviewWarning(morpheme));
        Assert.Contains("never match", Assert.Single(morpheme).Rationale);
    }

    [Fact]
    public void ASuffixFirstWithAnAdjacencyToItsLeftOfTheStemWarnsButAPrefixAmongTheRestDoesNot()
    {
        var (stem, _, _) = Seed();
        var suffix = SeedAffix("an", MoMorphTypeTags.kguidMorphSuffix, MsaType.kInfl);
        var prefix = SeedAffix("pa", MoMorphTypeTags.kguidMorphPrefix, MsaType.kInfl);
        Assert.Contains("never match", PreviewWarning(Build("allomorph", suffix, [stem], "somewhere-to-left")));
        Assert.Null(PreviewWarning(Build("allomorph", suffix, [stem], "somewhere-to-right")));
        Assert.Null(PreviewWarning(Build("allomorph", prefix, [SeedAffix("ka", MoMorphTypeTags.kguidMorphPrefix, MsaType.kInfl)], "adjacent-to-right")));
        Assert.Null(PreviewWarning(Build("allomorph", stem, [prefix], "adjacent-to-right")));
    }

    private string? PreviewWarning(IReadOnlyList<OperationEnvelope> operations)
    {
        var preview = Assert.Single(ScratchDryRun.Of(_cache, Proposal(operations)).ExpectedEffects).Preview!.Value;
        return preview.TryGetProperty("warning", out var warning) ? warning.GetString() : null;
    }

    private ILexEntry SeedAffix(string form, Guid morphType, MsaType msa)
    {
        ILexEntry entry = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var type = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
            entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
                TsStringUtils.MakeString(form, _cache.DefaultVernWs), "gloss " + form, new SandboxGenericMSA { MsaType = msa });
        });
        return entry;
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
