using System.Text.Json.Nodes;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WarningReadableTitleTests
{
    private const string SubjectGuid = "00000000-0000-0000-0000-000000000104";
    private const string ReadableDescription = "Item \"kuma (dog)\" needs attention.";

    [Fact]
    public void InspectorDescriptionUsesTheSameCapturedSubjectNameAsTheTable()
    {
        var warning = ReadCapturedFinding();
        var row = new GrammarWarningRowViewModel(warning);

        Assert.Equal("kuma", Assert.Single(warning.Subject).Title);
        Assert.Equal(ReadableDescription, row.TableProblemText);
        var detail = Assert.Single(row.Details);
        Assert.Equal(ReadableDescription, detail.Description);
        Assert.False(detail.HasProblemParts);
        Assert.Equal($"Item \"{SubjectGuid}\" needs attention.", warning.Description);
    }

    [Fact]
    public void ACanonicalObjectIdDoesNotHideItsSeparatelyCapturedSubjectGuidName()
    {
        var warning = ReadCapturedFinding();
        var subject = Assert.Single(warning.Subject);
        warning = warning with
        {
            Subject =
            [
                subject with { ObjectId = CanonicalId.FromGuid(Guid.Parse(SubjectGuid)).Value },
            ],
        };
        var row = new GrammarWarningRowViewModel(warning);

        Assert.Equal(SubjectGuid, Assert.Single(warning.Subject).SubjectGuid);
        Assert.Equal(ReadableDescription, row.TableProblemText);
    }

    [Fact]
    public void AdviceAndDisplayedPartsUseNamesWithoutChangingCapturedEvidence()
    {
        var warning = ReadCapturedFinding();
        warning = warning with
        {
            Explanation = $"Compare {SubjectGuid}.",
            Guidance = $"Review {SubjectGuid}.",
            Subject = [.. warning.Subject,
                new GrammarWarningPart($"Related {SubjectGuid}.", GrammarWarningPartRole.Text)],
        };
        var row = new GrammarWarningRowViewModel(warning);
        var detail = Assert.Single(row.Details);

        Assert.Equal("Compare kuma (dog).", detail.Explanation);
        Assert.Equal("Review kuma (dog).", detail.Guidance);
        Assert.Equal("Compare kuma (dog).", row.PanGlossExplanation);
        Assert.Equal("Review kuma (dog).", row.PanGlossGuidance);
        Assert.Equal("Related kuma (dog).", Assert.Single(detail.SubjectParts).Text);
        Assert.Equal(ReadableDescription, Assert.Single(detail.ProblemParts).Text);
        Assert.False(detail.HasProblemParts);
        Assert.False(row.HasProblemParts);
        Assert.Same(warning, row.Warning);
        Assert.Same(warning, detail.Warning);
        Assert.Equal($"Compare {SubjectGuid}.", warning.Explanation);
        Assert.Equal($"Review {SubjectGuid}.", warning.Guidance);
        Assert.Equal(warning.Subject[0].FieldWorksLink, Assert.Single(detail.EntryLinks).Address.OriginalString);
    }

    [Fact]
    public void AnOwningNavigationGuidDoesNotBorrowTheSubjectsName()
    {
        const string ownerGuid = "11111111-1111-1111-1111-111111111111";
        var ownerLink = $"silfw://localhost/link?tool=lexiconEdit&guid={ownerGuid}";
        var warning = ReadCapturedFinding();
        var subject = Assert.Single(warning.Subject);
        warning = warning with
        {
            Description = $"Item {SubjectGuid} has owner {ownerGuid}.",
            Subject = [subject with
            {
                FieldWorksKind = "MoStemAllomorph",
                SourceClass = "MoStemAllomorph",
                FieldWorksGuid = ownerGuid,
                FieldWorksLink = ownerLink,
            }],
        };
        var row = new GrammarWarningRowViewModel(warning);

        Assert.Equal("Item kuma (dog) has owner an unnamed item.", row.TableProblemText);
        Assert.Equal(row.TableProblemText, Assert.Single(row.Details).Description);
        Assert.Equal(ownerGuid, Assert.Single(row.SubjectParts).FieldWorksGuid);
        Assert.Equal(ownerLink, Assert.Single(row.SubjectParts).FieldWorksLink);
        Assert.Equal($"Item {SubjectGuid} has owner {ownerGuid}.", warning.Description);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000104")]
    [InlineData("")]
    public void AReadableCapturedTitleSuppliesAnIdentifierOnlyOrMissingPartText(string capturedText)
    {
        var warning = ReadCapturedFinding();
        var subject = Assert.Single(warning.Subject);
        warning = warning with { Subject = [subject with { Text = capturedText }] };
        var row = new GrammarWarningRowViewModel(warning);

        Assert.Equal(ReadableDescription, row.TableProblemText);
        Assert.Equal("kuma (dog)", row.TableItemName);
        Assert.Equal("kuma (dog)", Assert.Single(row.SubjectParts).Text);
        Assert.Equal(subject.ObjectId, Assert.Single(row.SubjectParts).ObjectId);
        Assert.Equal(subject.SubjectGuid, Assert.Single(row.SubjectParts).SubjectGuid);
        Assert.Equal(subject.FieldWorksLink, Assert.Single(row.SubjectParts).FieldWorksLink);
        Assert.Equal(capturedText, Assert.Single(warning.Subject).Text);
    }

    private static GrammarWarning ReadCapturedFinding()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "schema-v4-producer.json");
        var report = JsonNode.Parse(File.ReadAllText(path))!;
        report["diagnostics"]![0]!["description"] = $"Item \"{SubjectGuid}\" needs attention.";
        return GrammarHealthReportReader.ReadResponse(report.ToJsonString()).Findings[0];
    }
}
