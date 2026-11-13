using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Commands.Parsimony;

public sealed class ParsimonyAdmissionTests
{
    private const string Known = "B-adhoc-is-slot-order";
    private const ParsimonyEvidenceScopeKind Scope = ParsimonyEvidenceScopeKind.ProjectApproved;

    public static TheoryData<string?, ParsimonyEvidenceScopeKind?, string[]?, bool, string, FailureReason, string>
        Refusals => new()
    {
        { "no-such-measure", Scope, null, true, "parsimony.invalid-measure", FailureReason.InvalidArgument,
            "Unknown Parsimony measure 'no-such-measure'." },
        { "", Scope, null, true, "parsimony.invalid-measure", FailureReason.InvalidArgument,
            "Unknown Parsimony measure ''." },
        { null, Scope, null, true, "parsimony.invalid-measure", FailureReason.InvalidArgument,
            "Unknown Parsimony measure ''." },
        { Known, Scope, null, false, "parsimony.measure-unavailable", FailureReason.Refused,
            $"Parsimony measure '{Known}' is catalogued but not supported by this build." },
        { Known, (ParsimonyEvidenceScopeKind)99, null, true, "parsimony.invalid-scope",
            FailureReason.InvalidArgument,
            "Evidence scope must be 'default-selection' or 'project-approved'." },
        { Known, null, null, true, "parsimony.invalid-scope", FailureReason.InvalidArgument,
            "Evidence scope must be 'default-selection' or 'project-approved'." },
        { Known, Scope, new[] { "a", " " }, true, "parsimony.invalid-assessment", FailureReason.InvalidArgument,
            "Assessment references must be nonblank and unique." },
        { Known, Scope, new[] { "a", "a" }, true, "parsimony.invalid-assessment", FailureReason.InvalidArgument,
            "Assessment references must be nonblank and unique." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryInadmissibleRequestIsRefusedWithItsCodeReasonAndWords(string? measureId,
        ParsimonyEvidenceScopeKind? scope, string[]? assessmentIds, bool supported, string code,
        FailureReason reason, string message)
    {
        var refusal = ParsimonyAdmission.Check(measureId, scope, assessmentIds, _ => supported);

        Assert.NotNull(refusal);
        Assert.Equal(code, refusal.Code);
        Assert.Equal(reason, refusal.Reason);
        Assert.Equal(message, refusal.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a")]
    public void AnAdmissibleRequestIsNotRefused(string? assessmentId)
    {
        string[]? ids = assessmentId is null ? null : [assessmentId, "b"];

        Assert.Null(ParsimonyAdmission.Check(Known, Scope, ids, _ => true));
        Assert.Null(ParsimonyAdmission.Check(Known, ParsimonyEvidenceScopeKind.DefaultSelection, ids));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankDryRunIdIsRefusedAsTheDryRunTheCandidateRequires(string? dryRunJobId)
    {
        var refusal = ParsimonyAdmission.CheckDryRunJobId(dryRunJobId);

        Assert.NotNull(refusal);
        Assert.Equal("parsimony.dry-run-required", refusal.Code);
        Assert.Equal(ParsimonyAdmission.DryRunRequiredMessage, refusal.Message);
        Assert.Null(ParsimonyAdmission.CheckDryRunJobId("job/x"));
    }

    [Fact]
    public void GroupsExpandToTheTwelveGrammarAndNineTextMeasures()
    {
        Assert.Equal(12, ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar).Count);
        Assert.Equal(9, ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Text).Count);
        Assert.Equal(ParsimonyAdmission.SupportedMeasureIds(), ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.All));
        Assert.Equal(["grammar", "text", "all"], ParsimonyGroupRequests.Names);
        Assert.All(ParsimonyGroupRequests.Names, name =>
        {
            Assert.True(ParsimonyGroupRequests.TryParse(name, out var group));
            Assert.Equal(name, ParsimonyGroupRequests.Wire(group));
        });
        Assert.False(ParsimonyGroupRequests.TryParse("both", out _));
    }

    [Fact]
    public void ANamedMeasureOutsideTheGroupIsRefused()
    {
        var refusal = ParsimonyAdmission.ResolveGroup(ParsimonyGroupRequest.Grammar, Known, out var none);

        Assert.NotNull(refusal);
        Assert.Equal("parsimony.measure-outside-group", refusal.Code);
        Assert.Equal($"Parsimony measure '{Known}' is not in the grammar group.", refusal.Message);
        Assert.Empty(none);
        Assert.Null(ParsimonyAdmission.ResolveGroup(ParsimonyGroupRequest.All, Known, out var one));
        Assert.Equal([Known], one);
        Assert.Null(ParsimonyAdmission.ResolveGroup(ParsimonyGroupRequest.Text, null, out var whole));
        Assert.Equal(9, whole.Count);
        Assert.Equal("parsimony.invalid-group",
            ParsimonyAdmission.ResolveGroup((ParsimonyGroupRequest)9, null, out _)!.Code);
    }

    [Fact]
    public void RefusalCodesKeepTheirWireSpelling()
    {
        Assert.Equal("parsimony.measure-unavailable", ParsimonyRefusalCodes.MeasureUnavailable);
        Assert.Equal("parsimony.retirement-expectations-unavailable",
            ParsimonyRefusalCodes.RetirementExpectationsUnavailable);
        Assert.All(typeof(ParsimonyRefusalCodes).GetFields(), field =>
            Assert.Equal("parsimony." + System.Text.RegularExpressions.Regex
                .Replace(field.Name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant(),
                (string)field.GetRawConstantValue()!));
    }
}
