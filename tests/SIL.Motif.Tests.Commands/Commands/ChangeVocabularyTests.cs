using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ChangeVocabularyTests
{
    [Fact]
    public void AKnownGrammarKindHasItsNounsAndTitle()
    {
        var rule = ChangeVocabulary.Of("grammar/phPhonData/createPhonRules");

        Assert.Equal("rule", rule.Key);
        Assert.Equal("1 rule", rule.Count(1));
        Assert.Equal("2 rules", rule.Count(2));
        Assert.Equal("Add a phonological rule", rule.Title);
        Assert.True(rule.ChangesGrammar);
    }

    [Theory]
    [InlineData("grammar/somethingNew/createWidgets", "Create widgets", true)]
    [InlineData("analysis/somethingNew/setColour", "Set colour", false)]
    [InlineData("", "Change", true)]
    public void AnUnknownKindIsAPlainChangeTitledFromItsName(string kind, string title, bool changesGrammar)
    {
        var change = ChangeVocabulary.Of(kind);

        Assert.Equal(ChangeVocabulary.GenericKey, change.Key);
        Assert.Equal("3 changes", change.Count(3));
        Assert.Equal(title, change.Title);
        Assert.Equal(changesGrammar, change.ChangesGrammar);
    }

    [Fact]
    public void AnEvaluationIsNamedByWhetherItApprovesOrDisapproves()
    {
        Assert.Equal("parse approved", Evaluation(HumanEvaluationPayload.Approves).Singular);
        Assert.Equal("parse disapproved", Evaluation(HumanEvaluationPayload.Disapproves).Singular);
        Assert.Equal("parse opinion", ChangeVocabulary.Of(WfiAnalysisOperationKinds.AddRefEvaluations).Singular);
        Assert.False(Evaluation(HumanEvaluationPayload.Approves).ChangesGrammar);
    }

    [Fact]
    public void ADraftOperationIsNamedAsTheSameFinalizedOperationIs()
    {
        var draft = new DraftOperation
        {
            Kind = WfiAnalysisOperationKinds.AddRefEvaluations,
            After = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["member"] = JsonSerializer.SerializeToElement(HumanEvaluationPayload.Disapproves),
            },
        };

        Assert.Equal(Evaluation(HumanEvaluationPayload.Disapproves), ChangeVocabulary.Of(draft));
    }

    [Theory]
    [InlineData("AuthorPhoneme", "ɸ", "Add phoneme ɸ")]
    [InlineData("AuthorPhoneme", null, "Add a phoneme")]
    [InlineData("AuthorNaturalClass", "fricatives", "Add natural class fricatives")]
    [InlineData("AuthorPhonologicalRule", " ", "Add a phonological rule")]
    [InlineData("AuthorWidget", "w", "Add widget w")]
    [InlineData("EditGadget", null, "Change gadget")]
    [InlineData("", null, "Change")]
    public void AComposerCallIsTitledInALinguistsWords(string composer, string? name, string title) =>
        Assert.Equal(title, ChangeVocabulary.ComposerTitle(composer, name));

    [Fact]
    public void ASummaryCountsNamedKindsAndNeverListsOperationNames()
    {
        var phoneme = ProposalReviewTests.Op("grammar/phPhonemeSet/createPhonemes");
        var name = ProposalReviewTests.Op("grammar/phPhoneme/setName");
        var codes = ProposalReviewTests.Op("grammar/phPhoneme/createCodes");

        Assert.Equal("1 phoneme", ChangeVocabulary.Summary([phoneme, name, codes]));
        Assert.Equal("2 changes", ChangeVocabulary.Summary([name, codes]));
        Assert.DoesNotContain("Set name", ChangeVocabulary.Summary([phoneme, name, codes]), StringComparison.Ordinal);
    }

    [Fact]
    public void AWordChangeIsTitledAfterItsWord()
    {
        Assert.Equal("cat: approve an analysis", ChangeVocabulary.WordTitle("cat", AnalysisChangeKinds.Approve));
        Assert.Equal("cat: mark incorrectly spelled",
            ChangeVocabulary.WordTitle("cat", AnalysisChangeKinds.IncorrectSpelling));
        Assert.Equal("cat: brand new action", ChangeVocabulary.WordTitle("cat", "brand-new-action"));
    }

    [Fact]
    public void AProposalCountsItsChangesByKindAndKnowsWhetherItChangesGrammar()
    {
        var wordOnly = Proposal(Operation(WfiAnalysisOperationKinds.AddRefEvaluations,
            new { member = HumanEvaluationPayload.Approves }), Operation(WfiAnalysisOperationKinds.DeleteAnalysis));
        var withRules = Proposal(Operation("grammar/phPhonData/createPhonRules"),
            Operation("grammar/phPhonData/createPhonRules"), Operation(WfiAnalysisOperationKinds.DeleteAnalysis));

        Assert.False(ChangeVocabulary.ChangesGrammar(wordOnly));
        Assert.True(ChangeVocabulary.ChangesGrammar(withRules));
        Assert.Equal(["2 rules", "1 parse removed"], ChangeVocabulary.Count(withRules).Select(kind => kind.Text));
    }

    [Fact]
    public void AGrammarChangeOrAWordChangeNamingNoWordIsTriedOnTheDefaultSelection()
    {
        Assert.Null(JobCommands.DerivedTrialWords(Proposal(Operation("grammar/phPhonData/createPhonRules"))));
        Assert.Null(JobCommands.DerivedTrialWords(Proposal(Operation(WfiAnalysisOperationKinds.DeleteAnalysis))));
    }

    private static ChangeKind Evaluation(string member) =>
        ChangeVocabulary.Of(Operation(WfiAnalysisOperationKinds.AddRefEvaluations, new { member }));

    private static OperationEnvelope Operation(string kind, object? after = null) =>
        new(CanonicalId.Mint(), kind, after: after is null ? null : JsonSerializer.SerializeToElement(after));

    private static Proposal Proposal(params OperationEnvelope[] operations) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal) { ["analysis"] = "1.0" }, CanonicalId.Mint(),
            null, operations);
}
