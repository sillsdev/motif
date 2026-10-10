using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract.Contract;

public sealed class ParsimonyReportContractsTests
{
    private const string InputsJson = """
        {
          "bundleId":"bundle/1",
          "baselineToken":{
            "projectIdentity":"project/1",
            "semanticSnapshotDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "projectionVersion":"1",
            "capturedUtc":"2026-10-05T00:00:00Z",
            "bundleDigest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
          },
          "inputKind":"baseline",
          "candidate":null,
          "modelFingerprint":"model/1",
          "grammarFacts":{"schemaVersion":1,"sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},
          "evidence":{"schemaVersion":1,"sha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"},
          "selectionSha256":null,
          "expectationRevisionSha256":null,
          "assessmentIds":[],
          "evidenceScope":"default-selection"
        }
        """;

    [Fact]
    public void ParsimonyInputsRejectUnknownFields()
    {
        var json = InputsJson.Replace("\"assessmentIds\":[]", "\"assessmentIds\":[],\"unexpected\":true", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParsimonyReportInputs>(
            json, MotifJson.CreateOptions()));
    }

    [Fact]
    public void ParsimonyInputsRejectDuplicateFields()
    {
        var json = InputsJson.Replace(
            "\"bundleId\":\"bundle/1\"",
            "\"bundleId\":\"bundle/1\",\"bundleId\":\"bundle/2\"",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParsimonyReportInputs>(
            json, MotifJson.CreateOptions()));
    }

    [Fact]
    public void ParsimonyInputsRoundTripTheirFrozenArtifactBindings()
    {
        var actual = JsonSerializer.Deserialize<ParsimonyReportInputs>(InputsJson, MotifJson.CreateOptions());

        Assert.NotNull(actual);
        Assert.Equal("bundle/1", actual.BundleId);
        Assert.Equal("baseline", actual.InputKind);
        Assert.Equal(1, actual.GrammarFacts.SchemaVersion);
        Assert.Equal(new string('c', 64), actual.GrammarFacts.Sha256);
        Assert.Empty(actual.AssessmentIds);
        Assert.Equal(ParsimonyEvidenceScopeKind.DefaultSelection, actual.EvidenceScope);
    }

    [Fact]
    public void ParsimonyInputsKeepTheSourceAndTranslatedExpectationsAndTheirMappingDigest()
    {
        var baseline = new BaselineToken("project/1", "sha256:" + new string('a', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64));
        var original = Frozen(baseline, "sha256:" + new string('c', 64));
        var translated = Frozen(baseline, "sha256:" + new string('d', 64));
        var mapping = new RetirementExpectationMapping(CanonicalId.Mint().Value, CanonicalId.Mint().Value,
            null, "whole", CanonicalId.Mint().Value);
        var translation = new RetirementExpectationTranslation(original, translated, [mapping],
            [new RetirementBundleTextEffect(CanonicalId.Mint().Value,
                new Dictionary<string, string>(), new Dictionary<string, string>())],
            RetirementExpectationTranslationCodec.ComputeMappingDigest([mapping]),
            "sha256:" + new string('e', 64), "sha256:" + new string('f', 64),
            "sha256:" + new string('9', 64));
        var inputs = new ParsimonyReportInputs("bundle-1", baseline, "baseline", null, "model/1",
            new ParsimonyArtifactDigest(1, new string('1', 64)),
            new ParsimonyArtifactDigest(1, new string('2', 64)), null, null, [],
            ParsimonyEvidenceScopeKind.DefaultSelection, translation);

        var json = JsonSerializer.Serialize(inputs, MotifJson.CreateOptions());
        var actual = JsonSerializer.Deserialize<ParsimonyReportInputs>(json, MotifJson.CreateOptions());

        Assert.NotNull(actual);
        Assert.Equal(original.ManifestDigest, actual.RetirementExpectationTranslation!.Original.ManifestDigest);
        Assert.Equal(translated.ManifestDigest, actual.RetirementExpectationTranslation.Translated.ManifestDigest);
        Assert.Equal(translation.MappingDigest, actual.RetirementExpectationTranslation.MappingDigest);
        Assert.Equal(translation.DryRunFootprintDigest,
            actual.RetirementExpectationTranslation.DryRunFootprintDigest);
    }

    [Fact]
    public void ReportResponseRoundTripsWithoutAnAssessmentLink()
    {
        var expected = new ReportResponse("report/1", null, "parsimony", "advisory findings");

        var json = JsonSerializer.Serialize(expected, MotifJson.CreateOptions());
        var actual = JsonSerializer.Deserialize<ReportResponse>(json, MotifJson.CreateOptions());

        Assert.NotNull(actual);
        Assert.Null(actual.AssessmentId);
        Assert.Equal(expected.ReportId, actual.ReportId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Text, actual.Text);
    }

    private static FrozenExpectationSet Frozen(BaselineToken baseline, string digest) => new(
        baseline, digest, digest, "frozen-expectations/v1/test", [], [], []);
}
