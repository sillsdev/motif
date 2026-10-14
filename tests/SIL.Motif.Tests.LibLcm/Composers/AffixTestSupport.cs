using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>Builds, applies and reads the small grammars the affix restriction tests share.</summary>
internal static class AffixTestSupport
{
    public static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lists"] = "1.0", ["lexical"] = "1.0", ["grammar"] = "1.0" },
        CanonicalId.Mint(), null, operations);

    /// <summary>Dry-runs the operations on a copy, then applies them to the caller's project.</summary>
    public static Proposal ApplyOps(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "affix-tests");
        return proposal;
    }

    public static CanonicalId Created(Proposal proposal, string kind) =>
        Assert.Single(proposal.Operations, operation => operation.Kind == kind).EntityId!.Value;

    public static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);

    public static string Vern(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);

    public static string Anal(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);

    public static CanonicalId MorphType(Guid guid) => CanonicalId.FromGuid(guid);

    public static T Get<T>(LcmCache cache, CanonicalId id) where T : class, ICmObject =>
        (T)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    public static bool Exists(LcmCache cache, CanonicalId id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(id.ToGuid());

    public static CanonicalId AddInflectionClass(LcmCache cache, CanonicalId category, string name) =>
        Created(ApplyOps(cache, AuthorInflectionClassComposer.Build(cache, new(category, name, Anal(cache)))),
            PartOfSpeechInflectionClassesOperationKinds.Create);

    /// <summary>Adds an entry for an inflectional prefix of the category and returns its msa and lexeme form.</summary>
    public static (CanonicalId Entry, CanonicalId Msa, CanonicalId Form) AddInflectionalPrefix(
        LcmCache cache, string form, CanonicalId category)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    public static (CanonicalId Entry, CanonicalId Msa, CanonicalId Form) AddStem(
        LcmCache cache, string form, CanonicalId category, CanonicalId? inflectionClass = null)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphStem), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category, InflectionClass: inflectionClass))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    /// <summary>Creates a bare environment directly; composer tests need its identity, not its pattern.</summary>
    public static CanonicalId AddEnvironment(LcmCache cache, string name)
    {
        IPhEnvironment environment = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.Name.set_String(cache.DefaultAnalWs, name);
        });
        return Id(environment);
    }

    public static CanonicalId AddCategory(LcmCache cache, string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, name);
        });
        return Id(category);
    }
}
