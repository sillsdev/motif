using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Contract;

public sealed class HumanAgreementFactsTests
{
    [Fact]
    public void FactsHavePinnedJsonCanonicalFormAndDigest()
    {
        var facts = new HumanAgreementFacts(
            HumanAgreementEvidenceAvailability.Available,
            HumanAgreementSearchCompletion.Complete,
            [],
            HumanAgreementApprovalState.ApprovedUnique,
            1,
            1,
            1,
            [],
            [],
            ["EAAAAAAAAAAAAAAAAAAAAQ"],
            [],
            [],
            [],
            [],
            HumanAgreementSpellingAcceptance.NotApplicable,
            []);
        var json = JsonSerializer.Serialize(facts, MotifJson.CreateOptions());
        const string expectedJson = "{\"Availability\":\"available\",\"Completion\":\"complete\",\"Limitations\":[],\"ApprovedState\":\"approved-unique\",\"ProducedIdentityCount\":1,\"ApprovedExpectedMorphologyCount\":1,\"MatchedApprovedMorphologyCount\":1,\"ObservedUnmatchedExpectedAnalysisIds\":[],\"ProvenMissingApprovedAnalysisIds\":[],\"MatchedApprovedAnalysisIds\":[\"EAAAAAAAAAAAAAAAAAAAAQ\"],\"MatchedDisapprovedAnalysisIds\":[],\"MatchedUnknownAnalysisIds\":[],\"UnmatchedProducedIdentities\":[],\"Conflicts\":[],\"SpellingAcceptance\":\"not-applicable\",\"NegativeEvidence\":[]}";
        const string expectedCanonical = "{\"ApprovedExpectedMorphologyCount\":1,\"ApprovedState\":\"approved-unique\",\"Availability\":\"available\",\"Completion\":\"complete\",\"Conflicts\":[],\"Limitations\":[],\"MatchedApprovedAnalysisIds\":[\"EAAAAAAAAAAAAAAAAAAAAQ\"],\"MatchedApprovedMorphologyCount\":1,\"MatchedDisapprovedAnalysisIds\":[],\"MatchedUnknownAnalysisIds\":[],\"NegativeEvidence\":[],\"ObservedUnmatchedExpectedAnalysisIds\":[],\"ProducedIdentityCount\":1,\"ProvenMissingApprovedAnalysisIds\":[],\"SpellingAcceptance\":\"not-applicable\",\"UnmatchedProducedIdentities\":[]}";

        Assert.Equal(expectedJson, json);
        var canonical = CanonicalJson.Canonicalize(json);
        Assert.Equal(expectedCanonical, canonical);
        Assert.Equal("sha256:62f128e69d220a8331e9c9561b07f410a5ec60852b68c9f16297b439d227351d",
            IntentDigest.Sha256Of(Encoding.UTF8.GetBytes(canonical)));
    }

    [Fact]
    public void TypedFactsRoundTripWithClosedEnumValues()
    {
        var idA = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000001")).Value;
        var idB = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000002")).Value;
        var facts = new HumanAgreementFacts(
            HumanAgreementEvidenceAvailability.PartiallyAvailable,
            HumanAgreementSearchCompletion.Incomplete,
            [HumanAgreementLimitation.Capped, HumanAgreementLimitation.ProjectionUnavailable],
            HumanAgreementApprovalState.ConflictingDecisions,
            1,
            1,
            1,
            [idA],
            [],
            [idA],
            [idB],
            [],
            [],
            [new HumanAgreementConflict(HumanAgreementConflictKind.OpposedOpinions, [idA, idB])],
            HumanAgreementSpellingAcceptance.AcceptedMisspelling,
            [new HumanAgreementNegativeEvidence(HumanAgreementNegativeEvidenceKind.DisapprovedParse, [idB])]);
        var options = MotifJson.CreateOptions();

        var restored = JsonSerializer.Deserialize<HumanAgreementFacts>(JsonSerializer.Serialize(facts, options), options);

        Assert.NotNull(restored);
        Assert.Equal(HumanAgreementEvidenceAvailability.PartiallyAvailable, restored.Availability);
        Assert.Equal(HumanAgreementSearchCompletion.Incomplete, restored.Completion);
        Assert.Equal(HumanAgreementApprovalState.ConflictingDecisions, restored.ApprovedState);
        Assert.Equal(HumanAgreementLimitation.Capped, restored.Limitations[0]);
        Assert.Equal(HumanAgreementConflictKind.OpposedOpinions, restored.Conflicts[0].Kind);
        Assert.Equal(HumanAgreementNegativeEvidenceKind.DisapprovedParse, restored.NegativeEvidence[0].Kind);
        Assert.Equal(HumanAgreementSpellingAcceptance.AcceptedMisspelling, restored.SpellingAcceptance);
    }

    [Theory]
    [InlineData("\"unknown-state\"")]
    [InlineData("\"approved-unique-extra\"")]
    [InlineData("0")]
    [InlineData("3")]
    public void EnumStatesRejectUnknownNamesAndAllNumericWireValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HumanAgreementApprovalState>(
            json, new JsonSerializerOptions()));
    }

    [Theory]
    [InlineData("{\"Availability\":0}")]
    [InlineData("{\"Completion\":\"finished\"}")]
    [InlineData("{\"ApprovedState\":3}")]
    [InlineData("{\"Limitations\":[0]}")]
    [InlineData("{\"SpellingAcceptance\":0}")]
    [InlineData("{\"Conflicts\":[{\"Kind\":0,\"AnalysisIds\":[]}]}")]
    [InlineData("{\"NegativeEvidence\":[{\"Kind\":0,\"AnalysisIds\":[]}]}")]
    public void FactPropertiesUseScopedStrictEnumConverters(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HumanAgreementFacts>(
            json, MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData(HumanAgreementApprovalState.ApprovedUnique, "approved-unique")]
    [InlineData(HumanAgreementApprovalState.ApprovedWithAlternatives, "approved-with-alternatives")]
    [InlineData(HumanAgreementApprovalState.NoApprovedMatch, "no-approved-match")]
    [InlineData(HumanAgreementApprovalState.NoParse, "no-parse")]
    public void ApprovalStateWireNamesArePinned(HumanAgreementApprovalState value, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData(HumanAgreementNegativeEvidenceKind.DisapprovedParse, "disapproved-parse")]
    [InlineData(HumanAgreementNegativeEvidenceKind.MisspelledParse, "misspelled-parse")]
    public void NegativeEvidenceWireNamesArePinned(HumanAgreementNegativeEvidenceKind value, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, MotifJson.CreateOptions()));
    }
}
