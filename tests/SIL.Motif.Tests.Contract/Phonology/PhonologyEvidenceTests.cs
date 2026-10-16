using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Phonology;

public sealed class PhonologyEvidenceTests
{
    private static readonly JsonSerializerOptions Options = PhonologyJson.CreateOptions();

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("\"Agree\"")]
    [InlineData("\"unknown\"")]
    public void ClassificationsRequireExactNamedStringTags(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowDisposition>(json, Options));

    [Fact]
    public void UnavailableBranchesRefuseMeasuredFieldsAndMissingReasons()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
            "{\"kind\":\"unavailable\",\"reason\":\"absentProducer\",\"detail\":\"absent\",\"steps\":0}", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
            "{\"kind\":\"unavailable\",\"detail\":\"absent\"}", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((PhonologyRowCompletion)100, Options));
    }

    [Fact]
    public void EnumFieldsAndWireNamesStayClosedWithBothSerializerOptionSets()
    {
        var binding = new PhonologyInvocationBinding.Unavailable(
            PhonologyInvocationReason.ProducerUnavailable, "No executable.");
        var summary = PhonologyRowSummary.Unavailable.For(
            PhonologyInvocationReason.ProducerUnavailable, "No executable.");
        var context = new PhonologyGenerationContext.HcXmlEntry("entry");
        var row = new PhonologyRowEvidence.Produced(context, null, "root", [],
            new PhonologyLimits(1, 1, 10), PhonologyRowDisposition.Unavailable,
            PhonologyRowCompletion.NotStarted, [false], null, PhonologyRowReason.UnknownEntry,
            0, 0, false, false, [], [], []);
        foreach (var options in new[] { Options, MotifJson.CreateOptions() })
        {
            var bindingJson = JsonSerializer.Serialize<PhonologyInvocationBinding>(binding, options);
            Assert.Contains("\"kind\":\"unavailable\",\"reason\":\"producerUnavailable\"", bindingJson);
            Assert.IsType<PhonologyInvocationBinding.Unavailable>(
                JsonSerializer.Deserialize<PhonologyInvocationBinding>(bindingJson, options));
            foreach (var invalidReason in new[] { "1", "\"ProducerUnavailable\"", "\"unknown\"" })
            {
                var invalid = bindingJson.Replace("\"producerUnavailable\"", invalidReason, StringComparison.Ordinal);
                Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyInvocationBinding>(invalid, options));
            }

            var summaryJson = JsonSerializer.Serialize<PhonologyRowSummary>(summary, options);
            Assert.Contains("\"disposition\":\"unavailable\"", summaryJson);
            Assert.Contains("\"reason\":\"producerUnavailable\"", summaryJson);
            Assert.IsType<PhonologyRowSummary.Unavailable>(
                JsonSerializer.Deserialize<PhonologyRowSummary>(summaryJson, options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowSummary>(
                summaryJson.Replace("\"disposition\":\"unavailable\"", "\"disposition\":\"agree\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowSummary>(
                summaryJson.Replace("\"disposition\":\"unavailable\",", "", StringComparison.Ordinal), options));

            var rowJson = JsonSerializer.Serialize<PhonologyRowEvidence>(row, options);
            Assert.Contains("\"disposition\":\"unavailable\"", rowJson);
            Assert.Contains("\"completion\":\"notStarted\"", rowJson);
            Assert.Contains("\"reason\":\"unknownEntry\"", rowJson);
            var recovered = Assert.IsType<PhonologyRowEvidence.Produced>(
                JsonSerializer.Deserialize<PhonologyRowEvidence>(rowJson, options));
            Assert.Equal(row.Disposition, recovered.Disposition);
            Assert.Equal(row.Completion, recovered.Completion);
            Assert.Equal(row.Reason, recovered.Reason);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"disposition\":\"unavailable\"", "\"disposition\":1",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"disposition\":\"unavailable\"", "\"disposition\":\"Unavailable\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"disposition\":\"unavailable\"", "\"disposition\":\"unknown\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"completion\":\"notStarted\"", "\"completion\":1",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"completion\":\"notStarted\"", "\"completion\":\"NotStarted\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"completion\":\"notStarted\"", "\"completion\":\"unknown\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"reason\":\"unknownEntry\"", "\"reason\":1",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"reason\":\"unknownEntry\"", "\"reason\":\"UnknownEntry\"",
                    StringComparison.Ordinal), options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhonologyRowEvidence>(
                rowJson.Replace("\"reason\":\"unknownEntry\"", "\"reason\":\"unknown\"",
                    StringComparison.Ordinal), options));
        }
    }

    [Fact]
    public void MaximumEscapedRowRetainsThreeCompleteRuleListsWithinActualCSharpDetailBound()
    {
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", 256));
        var rules = Enumerable.Range(0, 1024).Select(index => astral[..^2] + char.ConvertFromUtf32(0x10000 + index)).ToArray();
        var expected = Enumerable.Range(0, 16).Select(index => astral[..^2] + (char)('a' + index)).ToArray();
        var context = new PhonologyGenerationContext.HcXmlEntry(astral);
        var example = new PhonologyExample(new string('"', 128), astral, expected, context,
            new CitationView(new string('"', 300), new string('\\', 120)));
        var limits = new PhonologyLimits(ulong.MaxValue, ulong.MaxValue, uint.MaxValue);
        var row = new PhonologyRowEvidence.Produced(context, context, astral, expected, limits,
            PhonologyRowDisposition.Unavailable, PhonologyRowCompletion.Incomplete,
            Enumerable.Repeat(true, 16).ToArray(), astral, PhonologyRowReason.Timeout, ulong.MaxValue,
            ulong.MaxValue, true, true, rules, rules, rules);
        var revision = new PhonologyRevisionReference("set/" + new string('a', 22), "revision/" + new string('a', 22),
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64), 500);
        var baseline = new BaselineToken("project", "sha256:" + new string('c', 64), "projection",
            "2026-01-01T00:00:00Z", "sha256:" + new string('d', 64));
        var binding = new PhonologyInvocationBinding.Produced("sha256:" + new string('a', 64),
            "sha256:" + new string('b', 64), "sha256:" + new string('c', 64), new string('"', 256),
            "sha256:" + new string('d', 64), "sha256:" + new string('e', 64), 32 * 1024 * 1024,
            ulong.MaxValue, 0, 0, 500);
        var header = new PhonologyAssessmentHeader("assessment/" + new string('a', 22), "pangloss",
            "2026-01-01T00:00:00Z", new PhonologyExampleSetHeader(revision, new string('"', 128),
                "2026-01-01T00:00:00Z", null), new PhonologyBaselineSource(baseline), limits, binding);
        var response = new PhonologyExampleResultResponse(header, new PhonologyExampleDetail(499, example, row));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        Assert.InRange(bytes.Length, 9 * 1024 * 1024, 32 * 1024 * 1024);
        var recovered = JsonSerializer.Deserialize<PhonologyExampleResultResponse>(bytes, Options)!;
        var evidence = Assert.IsType<PhonologyRowEvidence.Produced>(recovered.Result.Evidence);
        Assert.Equal(rules, evidence.OrderedRuleIds);
        Assert.Equal(rules, evidence.VisitedRuleIds);
        Assert.Equal(rules, evidence.AppliedRuleIds);
    }
}
