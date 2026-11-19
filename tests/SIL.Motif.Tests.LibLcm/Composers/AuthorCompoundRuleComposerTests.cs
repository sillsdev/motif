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
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AuthorCompoundRuleComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IPartOfSpeech _noun;
    private readonly IPartOfSpeech _verb;

    public AuthorCompoundRuleComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _noun = NewCategory("Noun");
        _verb = NewCategory("Verb");
    }

    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    [Fact]
    public void AnEndocentricRuleIsCreatedThroughDryRunAndApplyWithItsMembersAndTheRightHeadByDefault()
    {
        SeedStem("aaa", _noun);
        var proposal = Proposal(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, "N+N", Id(_noun), Id(_noun), true, null)));
        var dry = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoMorphDataCompoundRules, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Single(effect.After);
        Assert.Equal("endocentric: Noun + Noun makes a Noun (the head's category); the right member is the head",
            effect.Preview!.Value.GetProperty("compounds").GetString());
        Assert.Empty(_cache.LangProject.MorphologicalDataOA.CompoundRulesOS);

        Assert.StartsWith("Compound rule: endocentric:", Assert.Single(proposal.Operations).Rationale, StringComparison.Ordinal);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Contains(ProjectAppliedLog.ReadAll(_cache), record => record.ProposalId == proposal.ProposalId.ToGuid());

        var rule = Assert.IsAssignableFrom<IMoEndoCompound>(Assert.Single(_cache.LangProject.MorphologicalDataOA.CompoundRulesOS));
        Assert.True(rule.HeadLast);
        Assert.False(rule.Disabled);
        Assert.Equal(_noun.Guid, rule.LeftMsaOA.PartOfSpeechRA.Guid);
        Assert.Equal(_noun.Guid, rule.RightMsaOA.PartOfSpeechRA.Guid);
        Assert.NotNull(rule.OverridingMsaOA);
        Assert.Null(rule.OverridingMsaOA.PartOfSpeechRA);
        Assert.Equal("N+N", rule.Name.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal(CanonicalId.Parse(effect.After.Single().Value).ToGuid(), rule.Guid);
    }

    [Fact]
    public void ALeftHeadedRuleWithAResultOverrideKeepsBothAndSaysSo()
    {
        SeedStem("aaa", _noun);
        SeedStem("bbb", _verb);
        var proposal = Proposal(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_verb), Id(_noun), false, Id(_noun))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("endocentric: Verb + Noun makes a Noun (overriding the head's category); the left member is the head",
            Assert.Single(dry.ExpectedEffects).Preview!.Value.GetProperty("compounds").GetString());
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        var rule = Assert.IsAssignableFrom<IMoEndoCompound>(Assert.Single(_cache.LangProject.MorphologicalDataOA.CompoundRulesOS));
        Assert.False(rule.HeadLast);
        Assert.Equal(_verb.Guid, rule.LeftMsaOA.PartOfSpeechRA.Guid);
        Assert.Equal(_noun.Guid, rule.OverridingMsaOA.PartOfSpeechRA.Guid);
    }

    [Fact]
    public void AnExocentricRuleOwnsItsResultCategoryAndIsAppendedAfterAnExistingRule()
    {
        SeedStem("aaa", _noun);
        SeedStem("bbb", _verb);
        Apply(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), true, null)));
        var proposal = Proposal(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Exocentric, null, Id(_verb), Id(_noun), true, Id(_noun))));
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("exocentric: Verb + Noun makes a Noun; neither member is the head",
            Assert.Single(dry.ExpectedEffects).Preview!.Value.GetProperty("compounds").GetString());
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        var rules = _cache.LangProject.MorphologicalDataOA.CompoundRulesOS;
        Assert.Equal(2, rules.Count);
        var exo = Assert.IsAssignableFrom<IMoExoCompound>(rules[1]);
        Assert.Equal(_noun.Guid, exo.ToMsaOA.PartOfSpeechRA.Guid);
        Assert.Equal(_verb.Guid, exo.LeftMsaOA.PartOfSpeechRA.Guid);
        Assert.False(exo.Disabled);
    }

    [Fact]
    public void TheDryRunWarnsWhenNoEntryHasAStemInAMemberCategoryAndNotWhenOneDoes()
    {
        SeedStem("aaa", _noun);
        var warnings = Warnings(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_verb), true, null)));
        var warning = Assert.Single(warnings);
        Assert.Contains("right category 'Verb'", warning, StringComparison.Ordinal);
        SeedStem("bbb", _verb);
        Assert.Empty(Warnings(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_verb), true, null))));
    }

    [Fact]
    public void ADuplicateRuleAndAnUnknownCategoryAreRefusedWithTheirCodes()
    {
        SeedStem("aaa", _noun);
        Apply(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), true, null)));
        Assert.Equal(RefusalCodes.CompoundRuleDuplicate, Code(() => AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, "again", Id(_noun), Id(_noun), true, null))));
        Assert.Equal(RefusalCodes.CompoundRuleDuplicate, Code(() => AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), true, Id(_noun)))));
        Assert.NotEmpty(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), false, null)));
        Assert.Equal(RefusalCodes.CompoundRuleInvalidCategory, Code(() => AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), CanonicalId.Mint(), true, null))));
        Assert.Equal(RefusalCodes.CompoundRuleInvalidCategory, Code(() => AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Exocentric, null, Id(_noun), Id(_noun), true, Id(_cache.LangProject.MorphologicalDataOA)))));
    }

    [Fact]
    public void ARuleCreatedAfterTheDryRunInvalidatesIt()
    {
        var proposal = Proposal(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), true, null)));
        var dry = ScratchDryRun.Of(_cache, proposal);
        Apply(AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, Id(_noun), Id(_noun), true, null)));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester"));
    }

    [Fact]
    public void TheParserReadsTheClosedIntentAndRefusesWhatItCannotHold()
    {
        string Json(object value) => JsonSerializer.Serialize(value);
        AuthorCompoundRuleIntent Parse(object value) =>
            AuthorCompoundRuleIntentParser.Parse(JsonDocument.Parse(Json(value)).RootElement);
        var left = Id(_noun).Value;
        var right = Id(_verb).Value;
        Assert.True(Parse(new { kind = "endocentric", left, right }).HeadLast);
        Assert.False(Parse(new { kind = "endocentric", left, right, head = "left" }).HeadLast);
        Assert.Equal(right, Parse(new { kind = "exocentric", left, right, result = right }).Result!.Value.Value);
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "exocentric", left, right }));
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "exocentric", left, right, result = right, head = "left" }));
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "endocentric", left, right, head = "middle" }));
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "coordinate", left, right }));
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "endocentric", left, right, linker = "x" }));
        Assert.Throws<ContractParseException>(() => Parse(new { kind = "endocentric", left }));
    }

    private IReadOnlyList<string> Warnings(IReadOnlyList<OperationEnvelope> operations)
    {
        var preview = Assert.Single(ScratchDryRun.Of(_cache, Proposal(operations)).ExpectedEffects).Preview!.Value;
        return preview.TryGetProperty("warnings", out var warnings)
            ? warnings.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
    }

    private IPartOfSpeech NewCategory(string name)
    {
        IPartOfSpeech pos = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            pos = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return pos;
    }

    private IPartOfSpeech SeedStem(string form, IPartOfSpeech category)
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            services.GetInstance<ILexEntryFactory>().Create(stem, TsStringUtils.MakeString(form, _cache.DefaultVernWs),
                "gloss " + form, new SandboxGenericMSA { MsaType = MsaType.kStem, MainPOS = category });
        });
        return category;
    }

    private void Apply(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "tester");
    }

    private static string Code(Action action) => Assert.ThrowsAny<ProposalRefusalException>(action).Code;
    private static CanonicalId Id(ICmObject item) => CanonicalId.FromGuid(item.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
