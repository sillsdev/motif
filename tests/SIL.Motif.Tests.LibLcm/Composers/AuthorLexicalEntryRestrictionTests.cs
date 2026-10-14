using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// An affix entry authored through <see cref="AuthorLexicalEntryComposer"/> can be restricted to inflection
/// classes and environments, and every refusal leaves the project untouched.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorLexicalEntryRestrictionTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public AuthorLexicalEntryRestrictionTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private AuthorLexicalEntryIntent Prefix(LexicalEntryMsa msa, IReadOnlyList<CanonicalId>? classes = null,
        IReadOnlyList<CanonicalId>? environments = null) => new("m", Vern(_cache),
        MorphType(MoMorphTypeTags.kguidMorphPrefix), "CL1", Anal(_cache), msa, null, classes, environments);

    private AuthorLexicalEntryIntent Stem(IReadOnlyList<CanonicalId>? classes = null,
        IReadOnlyList<CanonicalId>? environments = null) => new("tu", Vern(_cache),
        MorphType(MoMorphTypeTags.kguidMorphStem), "person", Anal(_cache),
        new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _category), null, classes, environments);

    [Fact]
    public void ClassRestrictedPrefix_AppliesAndReadsBackAfterReload()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var environment = AddEnvironment(_cache, "before vowel");
        var intent = Prefix(new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category), [class12], [environment]);
        var proposal = Proposal(AuthorLexicalEntryComposer.Build(_cache, intent).Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "restriction-tests");
        var entryId = Created(proposal, LexDbEntriesOperationKinds.Create);
        var entry = Get<ILexEntry>(_cache, entryId);
        var form = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        Assert.Equal([class12.ToGuid()], form.InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal([environment.ToGuid()], form.PhoneEnvRC.Select(item => item.Guid));
        Assert.Null(Assert.IsAssignableFrom<IMoInflAffMsa>(Assert.Single(entry.MorphoSyntaxAnalysesOC)).InflFeatsOA);
        var digest = BaselineSemanticDigest.Compute(_cache);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloadedForm = Assert.IsAssignableFrom<IMoAffixAllomorph>(Get<ILexEntry>(reopened, entryId).LexemeFormOA);
        Assert.Equal([class12.ToGuid()], reloadedForm.InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal([environment.ToGuid()], reloadedForm.PhoneEnvRC.Select(item => item.Guid));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void EveryDependentOperationDeclaresTheCreateItTargets()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var environment = AddEnvironment(_cache, "before vowel");
        var operations = AuthorLexicalEntryComposer.Build(_cache,
            Prefix(new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category), [class12], [environment]));
        var creators = operations.Where(operation => operation.EntityId is not null)
            .ToDictionary(operation => operation.EntityId!.Value);
        foreach (var operation in operations.Where(operation => operation.Target is { } target && creators.ContainsKey(target)))
            Assert.Contains(creators[operation.Target!.Value].OperationId,
                operation.DependsOn.Select(dependency => dependency.OperationId));
        Assert.Single(operations, operation => operation.Kind == MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses);
        Assert.Single(operations, operation => operation.Kind == MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv);
    }

    [Fact]
    public void AStemCanBeRestrictedToEnvironmentsButNotToClasses()
    {
        var environment = AddEnvironment(_cache, "after vowel");
        var proposal = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, Stem(environments: [environment])));
        var form = Assert.IsAssignableFrom<IMoStemAllomorph>(
            Get<ILexEntry>(_cache, Created(proposal, LexDbEntriesOperationKinds.Create)).LexemeFormOA);
        Assert.Equal([environment.ToGuid()], form.PhoneEnvRC.Select(item => item.Guid));

        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            AuthorLexicalEntryComposer.Build(_cache, Stem(classes: [class12])));
        Assert.Contains("only an affix form takes 'inflectionClasses'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassesFromAnAncestorCategoryAreAccepted_AndClassesOfAForeignCategoryAreRefused()
    {
        var parent = AddCategory(_cache, "Parent");
        var child = Created(ApplyOps(_cache, AuthorPartOfSpeechComposer.Build(_cache,
            new("Child", Anal(_cache), Parent: parent))), CmPossibilitySubPossibilitiesOperationKinds.Create);
        var inherited = AddInflectionClass(_cache, parent, "inherited");
        var foreign = AddInflectionClass(_cache, _category, "foreign");
        var inflectional = new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, child);

        var proposal = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, Prefix(inflectional, [inherited])));
        Assert.Single(Assert.IsAssignableFrom<IMoAffixAllomorph>(
            Get<ILexEntry>(_cache, Created(proposal, LexDbEntriesOperationKinds.Create)).LexemeFormOA).InflectionClassesRC);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            AuthorLexicalEntryComposer.Build(_cache, Prefix(inflectional, [foreign])));
        Assert.Contains("does not belong to the category or one of its ancestors", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectIntentsWithMalformedRestrictionsAreRefusedBeforeAnythingIsAuthored()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var environment = AddEnvironment(_cache, "before vowel");
        var msa = new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category);
        var count = _cache.LangProject.LexDbOA.Entries.Count();
        AuthorLexicalEntryIntent[] refused =
        [
            Prefix(msa, [], null), Prefix(msa, null, []), Prefix(msa, [class12, class12]),
            Prefix(msa, null, [environment, environment]), Prefix(msa, [CanonicalId.Mint()]),
            Prefix(msa, [environment]), Prefix(msa, null, [class12]), Prefix(msa with { InflectionClass = class12 }),
        ];
        foreach (var intent in refused)
            Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache, intent));
        Assert.Equal(count, _cache.LangProject.LexDbOA.Entries.Count());
    }

    [Theory]
    [InlineData("""{"inflectionClasses":[]}""")]
    [InlineData("""{"environments":[]}""")]
    [InlineData("""{"inflectionClasses":["CLASS","CLASS"]}""")]
    [InlineData("""{"environments":"ENV"}""")]
    [InlineData("""{"inflectionClasses":[1]}""")]
    [InlineData("""{"inflectionClass":"CLASS"}""")]
    [InlineData("""{"msaExtra":true}""")]
    public void IntentParser_RefusesMalformedTopLevelRestrictions(string extra)
    {
        var id = CanonicalId.Mint().Value;
        var original = BasePrefixJson(id, "\"kind\":\"inflectional\",\"category\":\"" + id + "\"");
        var json = original[..^1] + "," + extra.Trim('{', '}').Replace("CLASS", id).Replace("ENV", id) + "}";
        using var document = JsonDocument.Parse(json);

        Assert.ThrowsAny<Exception>(() => AuthorLexicalEntryIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void IntentParser_RefusesDeferredFeaturesAndReadsAValidRestriction()
    {
        var id = CanonicalId.Mint().Value;
        using var deferred = JsonDocument.Parse(BasePrefixJson(id,
            "\"kind\":\"inflectional\",\"category\":\"" + id + "\",\"inflectionFeatures\":[]"));
        Assert.ThrowsAny<Exception>(() => AuthorLexicalEntryIntentParser.Parse(deferred.RootElement));
        using var valid = JsonDocument.Parse($$$"""
            {"form":"m","formWs":"sw","morphType":"{{{id}}}","gloss":"CL1","analysisWs":"en",
             "inflectionClasses":["{{{id}}}"],"environments":["{{{id}}}"],
             "msa":{"kind":"inflectional","category":"{{{id}}}"}}
            """);
        var intent = AuthorLexicalEntryIntentParser.Parse(valid.RootElement);
        Assert.Equal([id], intent.InflectionClasses!.Select(item => item.Value));
        Assert.Equal([id], intent.Environments!.Select(item => item.Value));
    }

    [Fact]
    public void FailureAfterTheLastOperationRemovesTheWholeRestrictedEntry()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var proposal = Proposal(AuthorLexicalEntryComposer.Build(_cache, Prefix(new LexicalEntryMsa(
            LexicalEntryMsaKind.Inflectional, _category), [class12])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "restriction-tests", string.Empty, afterOperation: (index, _) =>
            {
                if (index == proposal.Operations.Count) throw new InvalidOperationException("injected after every write");
            }));

        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
        Assert.All(proposal.Operations.Where(operation => operation.EntityId is not null),
            operation => Assert.False(Exists(_cache, operation.EntityId!.Value)));
    }

    [Fact]
    public void ADerivationalAffixCanBeRestrictedToTheClassesOfItsInputCategory()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");

        var derivational = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, Prefix(new LexicalEntryMsa(
            LexicalEntryMsaKind.Derivational, _category, _category), [class12])));

        Assert.Single(Assert.IsAssignableFrom<IMoAffixAllomorph>(
            Get<ILexEntry>(_cache, Created(derivational, LexDbEntriesOperationKinds.Create)).LexemeFormOA).InflectionClassesRC);
    }

    [Theory]
    [InlineData("classes")]
    [InlineData("environments")]
    [InlineData("slots")]
    public void RestrictionsRefuseDuplicateDecodedIdentitiesEvenWhenSpelledDifferently(string field)
    {
        var classId = AddInflectionClass(_cache, _category, "class");
        var environment = AddEnvironment(_cache, "environment");
        var slot = Created(ApplyOps(_cache, AuthorAffixSlotComposer.Build(_cache,
            new(_category, "slot", Anal(_cache), false))), PartOfSpeechAffixSlotsOperationKinds.Create);
        var msa = new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category);
        var intent = field switch
        {
            "classes" => Prefix(msa, [classId, Alias(classId)]),
            "environments" => Prefix(msa, environments: [environment, Alias(environment)]),
            _ => Prefix(msa with { Slots = [slot, Alias(slot)] }),
        };
        Assert.Throws<InvalidOperationException>(() => AuthorLexicalEntryComposer.Build(_cache, intent));
    }

    private static CanonicalId Alias(CanonicalId id)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var suffix = id.Suffix;
        var alternative = suffix[..^1] + alphabet[alphabet.IndexOf(suffix[^1]) + 1];
        var alias = CanonicalId.Parse("authored:" + alternative);
        Assert.Equal(id.ToGuid(), alias.ToGuid());
        return alias;
    }

    private static string BasePrefixJson(string id, string msa) => "{\"form\":\"m\",\"formWs\":\"sw\",\"morphType\":\"" + id +
        "\",\"gloss\":\"CL1\",\"analysisWs\":\"en\",\"msa\":{" + msa + "}}";
}
