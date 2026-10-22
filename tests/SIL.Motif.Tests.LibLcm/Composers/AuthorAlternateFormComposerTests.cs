using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
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
/// Alternate forms authored with <see cref="AuthorAlternateFormComposer"/>: each carries its own conditions,
/// and malformed intents leave the project as it was.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorAlternateFormComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public AuthorAlternateFormComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private AuthorAlternateFormIntent Prefix(CanonicalId entry, string form = "mw",
        IReadOnlyList<CanonicalId>? environments = null, IReadOnlyList<CanonicalId>? classes = null,
        Placement? placement = null) => new(entry, form, Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix),
        environments, classes, placement);

    [Fact]
    public void ARestrictedAffixAlternate_LeavesTheLexemeFormAloneAndCopiesNothingFromIt()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var class34 = AddInflectionClass(_cache, _category, "3/4");
        var environment = AddEnvironment(_cache, "before vowel");
        var entryProposal = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent(
            "m", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "CL1", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category), null, [class34], null)));
        var entry = Created(entryProposal, LexDbEntriesOperationKinds.Create);
        var lexemeForm = Get<ILexEntry>(_cache, entry).LexemeFormOA!;
        var senses = Get<ILexEntry>(_cache, entry).SensesOS.Count;
        var msas = Get<ILexEntry>(_cache, entry).MorphoSyntaxAnalysesOC.Count;
        var entries = _cache.LangProject.LexDbOA.Entries.Count();

        var operations = AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mw", [environment], [class12]));
        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "alternate-form-tests");

        var reloadedEntry = Get<ILexEntry>(_cache, entry);
        var alternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(reloadedEntry.AlternateFormsOS));
        Assert.Equal(Created(proposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).ToGuid(), alternate.Guid);
        Assert.Equal([class12.ToGuid()], alternate.InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal([environment.ToGuid()], alternate.PhoneEnvRC.Select(item => item.Guid));
        var incumbent = Assert.IsAssignableFrom<IMoAffixAllomorph>(reloadedEntry.LexemeFormOA);
        Assert.Equal(lexemeForm.Guid, incumbent.Guid);
        Assert.Equal([class34.ToGuid()], incumbent.InflectionClassesRC.Select(item => item.Guid));
        Assert.Empty(incumbent.PhoneEnvRC);
        Assert.Equal(senses, reloadedEntry.SensesOS.Count);
        Assert.Equal(msas, reloadedEntry.MorphoSyntaxAnalysesOC.Count);
        Assert.Equal(entries, _cache.LangProject.LexDbOA.Entries.Count());

        var digest = BaselineSemanticDigest.Compute(_cache);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloaded = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(Get<ILexEntry>(reopened, entry).AlternateFormsOS));
        Assert.Equal([class12.ToGuid()], reloaded.InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void EveryLinkDependsOnTheFormCreate_AndTheCreateNamesThePlacement()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var environment = AddEnvironment(_cache, "before vowel");
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mu")));
        var existing = Id(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));

        var operations = AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mw", [environment], [class12], new Placement(null, existing)));

        var create = Assert.Single(operations, operation =>
            operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        Assert.Equal(new Placement(null, existing), create.Placement);
        Assert.Equal(3, operations.Count);
        foreach (var link in operations.Where(operation => operation != create))
        {
            Assert.Equal(create.EntityId, link.Target);
            Assert.Equal([create.OperationId], link.DependsOn.Select(dependency => dependency.OperationId));
        }
    }

    [Fact]
    public void AStemAlternate_TakesEnvironmentsButNotInflectionClasses()
    {
        var environment = AddEnvironment(_cache, "after vowel");
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var (entry, _, _) = AddStem(_cache, "tu", _category, class12);
        var stem = new AuthorAlternateFormIntent(entry, "tw", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphStem),
            [environment]);

        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, stem));

        var alternate = Assert.IsAssignableFrom<IMoStemAllomorph>(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));
        Assert.Equal([environment.ToGuid()], alternate.PhoneEnvRC.Select(item => item.Guid));
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            AuthorAlternateFormComposer.Build(_cache, stem with { Environments = null, InflectionClasses = [class12] }));
        Assert.Contains("only an affix form takes 'inflectionClasses'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAffixAlternateOfADerivationalAffixRefusesInflectionClassesNamingTheRestrictionComposer()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var derived = AddCategory(_cache, "Adjective");
        var created = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("mi", Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "mi", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, derived))));
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        var intent = new AuthorAlternateFormIntent(entry, "me", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix));

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            AuthorAlternateFormComposer.Build(_cache, intent with { InflectionClasses = [class12] }));

        Assert.Contains("compose-edit-derivational-affix-restriction", refusal.Message, StringComparison.Ordinal);
        Assert.NotEmpty(AuthorAlternateFormComposer.Build(_cache, intent));
    }

    [Fact]
    public void AnAffixAlternateUsesInflectionClassesForTheInflectionalMsaOfAMixedEntry()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var derived = AddCategory(_cache, "Adjective");
        var created = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("mi", Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "mi", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, derived))));
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var inflectional = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            Get<ILexEntry>(_cache, entry).MorphoSyntaxAnalysesOC.Add(inflectional);
            inflectional.PartOfSpeechRA = Get<IPartOfSpeech>(_cache, _category);
        });

        var operations = AuthorAlternateFormComposer.Build(_cache, Prefix(entry, classes: [class12]));
        ApplyOps(_cache, operations);

        var alternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));
        Assert.Equal([class12.ToGuid()], alternate.InflectionClassesRC.Select(item => item.Guid));
    }

    [Fact]
    public void AnEnvironmentNamingAnUnknownNaturalClassIsRefusedAndOneNamingAnAbbreviationIsAccepted()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var byName = EnvironmentWithPattern("/ [Vowels] _");
        var byAbbreviation = EnvironmentWithPattern("/ [V] _");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var vowels = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(vowels);
            vowels.Abbreviation.set_String(_cache.DefaultAnalWs, "V");
            vowels.Name.set_String(_cache.DefaultAnalWs, "Vowels");
        });

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mw", [byName])));
        Assert.Contains("natural class 'Vowels'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("abbreviation", refusal.Message, StringComparison.Ordinal);
        Assert.NotEmpty(AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mw", [byAbbreviation])));
    }

    [Fact]
    public void AnOrderThatLetsAnUnrestrictedFormShadowARestrictedOneIsRefusedWithTheNextStep()
    {
        var environment = AddEnvironment(_cache, "after vowel");
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mu")));
        var plain = Id(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));
        var digest = BaselineSemanticDigest.Compute(_cache);

        var behind = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mw", [environment], placement: new Placement(plain, null))));
        Assert.Contains("never be selected", behind.Message, StringComparison.Ordinal);
        Assert.Contains("Place it before 'mu'", behind.Message, StringComparison.Ordinal);
        Assert.NotEmpty(AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mw", [environment], placement: new Placement(null, plain))));

        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mw", [environment], placement: new Placement(null, plain))));
        var restricted = Id(Get<ILexEntry>(_cache, entry).AlternateFormsOS[0]);
        var ahead = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mx", placement: new Placement(null, restricted))));
        Assert.Contains("stop it from ever being selected", ahead.Message, StringComparison.Ordinal);
        Assert.NotEmpty(AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mx", placement: new Placement(plain, null))));
        Assert.NotEqual(digest, BaselineSemanticDigest.Compute(_cache));
    }

    private CanonicalId EnvironmentWithPattern(string pattern)
    {
        IPhEnvironment environment = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            environment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = SIL.LCModel.Core.Text.TsStringUtils.MakeString(pattern, _cache.DefaultVernWs);
        });
        return CanonicalId.FromGuid(environment.Guid);
    }

    [Fact]
    public void KindMismatchesAndUnsupportedEntriesAreRefused()
    {
        var (affixEntry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var (stemEntry, stemMsa, _) = AddStem(_cache, "tu", _category);
        var processEntry = AddEntryWithAffixProcess();
        var noForm = AddEntryWithoutLexemeForm();

        var stemOnAffix = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache,
            new(affixEntry, "x", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphStem))));
        var affixOnStem = Assert.Throws<InvalidOperationException>(() =>
            AuthorAlternateFormComposer.Build(_cache, Prefix(stemEntry)));
        var process = Assert.Throws<InvalidOperationException>(() =>
            AuthorAlternateFormComposer.Build(_cache, Prefix(processEntry)));
        var empty = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, Prefix(noForm)));

        Assert.Contains("makes a stem form but the entry's lexeme form is an affix", stemOnAffix.Message, StringComparison.Ordinal);
        Assert.Contains("makes an affix form but the entry's lexeme form is a stem", affixOnStem.Message, StringComparison.Ordinal);
        Assert.Contains("affix process", process.Message, StringComparison.Ordinal);
        Assert.Contains("no stem or affix lexeme form", empty.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, Prefix(stemMsa)));
        Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, Prefix(CanonicalId.Mint())));
    }

    [Fact]
    public void BadFormsWritingSystemsAndConditionsAreRefusedBeforeAnythingIsAuthored()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var foreignClass = AddInflectionClass(_cache, AddCategory(_cache, "Other"), "other");
        var environment = AddEnvironment(_cache, "before vowel");
        var digest = BaselineSemanticDigest.Compute(_cache);

        AuthorAlternateFormIntent[] refused =
        [
            Prefix(entry, " "),
            Prefix(entry) with { FormWritingSystem = "zz-notaws" },
            Prefix(entry) with { FormWritingSystem = Anal(_cache) == Vern(_cache) ? "zz" : Anal(_cache) },
            Prefix(entry) with { MorphType = CanonicalId.Mint() },
            Prefix(entry, environments: []),
            Prefix(entry, classes: []),
            Prefix(entry, environments: [environment, environment]),
            Prefix(entry, classes: [class12, class12]),
            Prefix(entry, environments: [class12]),
            Prefix(entry, classes: [environment]),
            Prefix(entry, classes: [foreignClass]),
            Prefix(entry, environments: [CanonicalId.Mint()]),
            Prefix(entry, placement: new Placement(CanonicalId.Mint(), null)),
        ];
        foreach (var intent in refused)
            Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, intent));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }

    [Fact]
    public void PlacementIsRequiredOnceTheSequenceIsOccupied_AndMustBeAdjacent()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mw")));
        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mu",
            placement: new Placement(Id(Get<ILexEntry>(_cache, entry).AlternateFormsOS[0]), null))));
        var forms = Get<ILexEntry>(_cache, entry).AlternateFormsOS.Select(Id).ToArray();

        var missing = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mi")));
        var gap = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mi", placement: new Placement(forms[0], forms[0]))));
        var leapfrog = Assert.Throws<InvalidOperationException>(() => AuthorAlternateFormComposer.Build(_cache,
            Prefix(entry, "mi", placement: new Placement(forms[1], forms[0]))));

        Assert.Contains("requires placement", missing.Message, StringComparison.Ordinal);
        Assert.Contains("not adjacent", gap.Message, StringComparison.Ordinal);
        Assert.Contains("not adjacent", leapfrog.Message, StringComparison.Ordinal);
        ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mi", placement: new Placement(null, forms[0]))));
        Assert.Equal(forms[0], Id(Get<ILexEntry>(_cache, entry).AlternateFormsOS[1]));
    }

    [Fact]
    public void FailureAfterTheLinksRemovesTheForm()
    {
        var class12 = AddInflectionClass(_cache, _category, "1/2");
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var proposal = Proposal(AuthorAlternateFormComposer.Build(_cache, Prefix(entry, classes: [class12])));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "alternate-form-tests", string.Empty, afterOperation: (_, operation) =>
            {
                if (operation.Kind == MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses)
                    throw new InvalidOperationException("injected after the link");
            }));

        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }

    [Theory]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","placement":{}}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","placement":{"after":"ID","extra":"ID"}}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","gloss":"x"}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw"}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","environments":[]}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","inflectionClasses":["ID","ID"]}""")]
    [InlineData("""{"entry":"ID","form":"mw","formWs":"sw","morphType":"ID","environments":["ID",5]}""")]
    [InlineData("""{"entry":"ID","form":7,"formWs":"sw","morphType":"ID"}""")]
    [InlineData("""{"entry":"not an id","form":"mw","formWs":"sw","morphType":"ID"}""")]
    public void IntentParser_IsClosed(string json)
    {
        using var document = JsonDocument.Parse(json.Replace("ID", CanonicalId.Mint().Value));

        Assert.ThrowsAny<Exception>(() => AuthorAlternateFormIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void IntentParser_ReadsEveryField()
    {
        var (entry, type, env, cls, after, before) = (CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint(),
            CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint());
        using var document = JsonDocument.Parse($$$"""
            {"entry":"{{{entry.Value}}}","form":"mw","formWs":"sw","morphType":"{{{type.Value}}}","environments":["{{{env.Value}}}"],
             "inflectionClasses":["{{{cls.Value}}}"],"placement":{"after":"{{{after.Value}}}","before":"{{{before.Value}}}"}}
            """);

        var intent = AuthorAlternateFormIntentParser.Parse(document.RootElement);

        Assert.Equal(new AuthorAlternateFormIntent(entry, "mw", "sw", type, [env], [cls], new Placement(after, before)).Placement,
            intent.Placement);
        Assert.Equal([env], intent.Environments);
        Assert.Equal([cls], intent.InflectionClasses);
        Assert.Equal(entry, intent.Entry);
    }

    private CanonicalId AddEntryWithAffixProcess()
    {
        ILexEntry entry = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entry.LexemeFormOA = _cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
        });
        return Id(entry);
    }

    private CanonicalId AddEntryWithoutLexemeForm()
    {
        ILexEntry entry = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create());
        return Id(entry);
    }
    [Fact]
    public void PlacementNormalizesDecodedIdentityAndPersistsTheGap()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var first = ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache, Prefix(entry)));
        var firstId = Created(first, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        var anchor = CanonicalId.FromGuid(firstId.ToGuid(), "form:");
        var operations = AuthorAlternateFormComposer.Build(_cache, Prefix(entry, "mu", placement: new Placement(anchor, null)));
        Assert.Equal(firstId.Value, operations[0].Placement!.After!.Value.Value);
        ApplyOps(_cache, operations);
        Assert.Equal([firstId.ToGuid(), operations[0].EntityId!.Value.ToGuid()],
            Get<ILexEntry>(_cache, entry).AlternateFormsOS.Select(form => form.Guid));
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.Equal([firstId.ToGuid(), operations[0].EntityId!.Value.ToGuid()],
            Get<ILexEntry>(reopened, entry).AlternateFormsOS.Select(form => form.Guid));
        Assert.Equal(firstId.Value, operations[0].Placement!.After!.Value.Value);

    }

}
