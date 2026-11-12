using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// A derivational affix with its from and to categories swapped is a valid affix no stem can feed: the composer names
/// the direction, and the Dry Run warns when nothing could feed the from-category.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class DerivationalDirectionTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _noun;
    private readonly CanonicalId _adjective;

    public DerivationalDirectionTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _noun = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<IPartOfSpeech>(_cache, _noun).Name.set_String(_cache.DefaultAnalWs, "Noun"));
        _adjective = AddCategory(_cache, "Adjective");
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private IReadOnlyList<OperationEnvelope> Prefix(CanonicalId from, CanonicalId to) =>
        AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("di", Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "having", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, from, to)));

    private static IEnumerable<string> Warnings(SIL.Motif.Model.DryRun.DryRun dryRun) => dryRun.ExpectedEffects
        .Where(effect => effect.Field == "dryRunWarning").Select(effect => effect.After["warning"]);

    [Fact]
    public void TheComposerSummaryNamesTheDirectionByCategoryName()
    {
        var authored = JsonSerializer.SerializeToElement(new
        {
            form = "di", formWs = Vern(_cache), morphType = MorphType(MoMorphTypeTags.kguidMorphPrefix).Value,
            gloss = "having", analysisWs = Anal(_cache),
            msa = new { kind = "derivational", category = _adjective.Value, toCategory = _noun.Value },
        });

        var related = Assert.Single(ProposalCommands.DraftComposers["compose-author-lexical-entry"]
            .RelatedObjects(_cache, authored)!);

        Assert.Equal("derivation", related.Kind);
        Assert.Equal("Adjective -> Noun", related.Name);
    }

    private LexicalEntryMsa Swapped => new(LexicalEntryMsaKind.Derivational, _adjective, _noun);

    private static void AssertNamesTheDirection(IReadOnlyList<SIL.Motif.Contract.Responses.ComposedRelatedObject>? related)
    {
        var direction = Assert.Single(related!);
        Assert.Equal(("derivation", "Adjective -> Noun"), (direction.Kind, direction.Name));
    }

    [Fact]
    public void ACircumfixNamesTheDirection() => AssertNamesTheDirection(new AuthorCircumfixDefinition().RelatedObjects(_cache,
        new AuthorCircumfixIntent("ka", "an", Vern(_cache), "having", Anal(_cache), Swapped)));

    [Fact]
    public void AnAffixProcessNamesTheDirection() => AssertNamesTheDirection(new AuthorAffixProcessDefinition().RelatedObjects(_cache,
        new AuthorAffixProcessIntent(MorphType(MoMorphTypeTags.kguidMorphInfix), "having", Anal(_cache), Swapped, [], [])));

    [Fact]
    public void ANewMorphoSyntaxAnalysisNamesTheDirection()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "mi", _noun);
        AssertNamesTheDirection(new AddMorphoSyntaxAnalysisDefinition().RelatedObjects(_cache,
            new AddMorphoSyntaxAnalysisIntent(entry, Swapped)));
    }

    [Fact]
    public void ChangingAnAffixToDerivationalNamesTheDirectionAndOtherKindsNameNone()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "mi", _adjective);
        var definition = new ChangeAffixKindDefinition();
        AssertNamesTheDirection(definition.RelatedObjects(_cache,
            new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational, _noun)));
        Assert.Empty(definition.RelatedObjects(_cache, new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Unclassified))!);
    }

    [Fact]
    public void AnAffixWhoseFromCategoryHasNoStemWarnsInTheDryRun()
    {
        AddStem(_cache, "kan", _noun);
        var warning = Assert.Single(Warnings(ScratchDryRun.Of(_cache, Proposal(Prefix(_adjective, _noun)))));
        Assert.Contains("Adjective -> Noun", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAffixWhoseFromCategoryHasAStemDoesNotWarn()
    {
        AddStem(_cache, "kan", _noun);
        Assert.Empty(Warnings(ScratchDryRun.Of(_cache, Proposal(Prefix(_noun, _adjective)))));
    }

    [Fact]
    public void AStemOfASubcategoryOrADerivationThatMakesTheCategoryFeedsTheAffix()
    {
        IPartOfSpeech subcategory = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            subcategory = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            Get<IPartOfSpeech>(_cache, _noun).SubPossibilitiesOS.Add(subcategory);
            subcategory.Name.set_String(_cache.DefaultAnalWs, "Common noun");
        });
        var common = Id(subcategory);
        AddStem(_cache, "kan", common);
        Assert.Empty(Warnings(ScratchDryRun.Of(_cache, Proposal(Prefix(_noun, _adjective)))));

        // Nothing is a Verb stem, but a derivation makes Verb from Noun, so a Verb-fed affix is reachable.
        var verb = AddCategory(_cache, "Verb");
        ApplyOps(_cache, Prefix(_noun, verb));
        Assert.Empty(Warnings(ScratchDryRun.Of(_cache, Proposal(Prefix(verb, _adjective)))));
    }
}
