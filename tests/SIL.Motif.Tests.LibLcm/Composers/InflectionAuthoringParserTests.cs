using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InflectionAuthoringParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AStemNameRequiresItsAuthoredFeatureRegionInThePinnedParser()
    {
        using var cache = pristine.NewScratch();
        foreach (var symbol in "amotifb") Apply(cache,
            AuthorPhonemeComposer.Build(cache, new(symbol.ToString(), [symbol.ToString()])));
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var featureProposal = Apply(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("Number", ws, [new("Plural", "pl"), new("Singular", "sg")], "num")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Value(string text) => values.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);
        var plural = Value("Plural");
        var singular = Value("Singular");
        var nounClass = Created(Apply(cache, AuthorInflectionClassComposer.Build(cache,
            new(category, "Class Seven", ws, "seven"))), PartOfSpeechInflectionClassesOperationKinds.Create);
        var stemName = Created(Apply(cache, AuthorStemNameComposer.Build(cache,
            new(category, "Plural stem", ws, [[new(feature, plural)]]))), PartOfSpeechStemNamesOperationKinds.Create);
        var entry = Apply(cache, AuthorLexicalEntryComposer.Build(cache,
            new("mama", vern, CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), "MAMA", ws,
                new(LexicalEntryMsaKind.Stem, category, InflectionClass: nounClass))));
        var form = Created(entry, LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var msa = Created(entry, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var structure = Created(Apply(cache, AuthorFeatureStructureComposer.Build(cache, new(msa))),
            MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
        var specification = Created(Apply(cache, AuthorFeatureValueComposer.Build(cache, new(structure, feature, plural))),
            FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs);
        Apply(cache, [new(CanonicalId.Mint(), MoStemAllomorphStemNameOperationKinds.SetStemName, target: form,
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { @ref = stemName.Value }))]);
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var matched = await tracer.TraceAsync(cache.ProjectId.Path, "mama", CancellationToken.None);
        Assert.True(matched is PanGlossTraceOutcome.Completed, matched.ToString());
        Assert.NotEqual("-", ((PanGlossTraceOutcome.Completed)matched).Summary.Signature);
        Apply(cache, [new(CanonicalId.Mint(), FsClosedValueValueOperationKinds.SetValue, target: specification,
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { @ref = singular.Value }))]);
        new FwDataProjectLoader().Save(cache);
        var mismatch = await tracer.TraceAsync(cache.ProjectId.Path, "mama", CancellationToken.None);
        Assert.True(mismatch is PanGlossTraceOutcome.Completed, mismatch.ToString());
        Assert.Equal("-", ((PanGlossTraceOutcome.Completed)mismatch).Summary.Signature);
    }

    private static Proposal Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string>
            { ["lists"] = "1.0", ["grammar"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "synthetic-inflection-parser-tests");
        return proposal;
    }

    private static CanonicalId Created(Proposal proposal, string kind) =>
        Assert.Single(proposal.Operations, op => op.Kind == kind).EntityId!.Value;
}
