using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public sealed class ReviewReportExpandedTests
{
    [Fact]
    public void EveryRowKindRoundTripsStrictly()
    {
        Type[] types = [typeof(WordRow), typeof(TokenRow), typeof(CellRow), typeof(DiagnosticRow),
            typeof(FindingRow), typeof(EffectRow), typeof(EvidenceRow), typeof(ReadingRow),
            typeof(ExpectationRow), typeof(SourceRow), typeof(SetRow), typeof(RuleWorkRow)];
        foreach (var type in types)
        {
            var row = (ReviewRow)Create(type);
            if (row is WordRow word)
                row = word with { SamplePositions = [(TokenRow)Create(typeof(TokenRow))], SamplePositionsTotal = 1 };
            var json = JsonSerializer.Serialize(row, ReviewJson.Options);
            Assert.Equal(type, JsonSerializer.Deserialize<ReviewRow>(json, ReviewJson.Options)!.GetType());
            var invalid = JsonNode.Parse(json)!.AsObject();
            invalid["extra"] = 1;
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReviewRow>(invalid.ToJsonString(), ReviewJson.Options));
            AssertSchema(json, "ReviewRow");
        }
    }

    [Fact]
    public void AffectedPositionCountsMatchTheSchema()
    {
        var group = new CauseGroup("no-parse", 1, "words", [], 1,
            new ReadTarget(TargetKind.Resource, "detail", "motif://detail", null, null)) { SamplePositionsTotal = 6 };
        AssertSchema(JsonSerializer.Serialize(group, ReviewJson.Options), "CauseGroup");
    }

    [Fact]
    public void EveryFlagFactsKindRoundTripsStrictly()
    {
        Type[] types = [typeof(NewWordFacts), typeof(ReadingLossFacts), typeof(NegativeFacts),
            typeof(WorkChange), typeof(NewFindingFacts)];
        foreach (var type in types)
        {
            var facts = (FlagFacts)Create(type);
            var json = JsonSerializer.Serialize(facts, ReviewJson.Options);
            Assert.Equal(type, JsonSerializer.Deserialize<FlagFacts>(json, ReviewJson.Options)!.GetType());
            AssertSchema(json, "FlagFacts");
        }
    }

    [Fact]
    public void EveryResultStateValidatesAgainstSchema()
    {
        var report = CreateReport();
        TrialReviewResult[] values = [new NoTrialResult("p", []), new PendingTrialResult("p", "j", "running", null, []), new AvailableTrialResult(report)];
        foreach (var value in values)
        {
            var json = JsonSerializer.Serialize(value, ReviewJson.Options);
            Assert.Equal(value.GetType(), JsonSerializer.Deserialize<TrialReviewResult>(json, ReviewJson.Options)!.GetType());
            AssertSchema(json, "TrialReviewResult");
        }
    }

    [Fact]
    public void BaselineTokenRequiresEveryFieldAndRejectsUnknownFields()
    {
        var json = JsonSerializer.Serialize<TrialReviewResult>(new AvailableTrialResult(CreateReport()), ReviewJson.Options);
        var node = JsonNode.Parse(json)!.AsObject();
        var baseline = node["report"]!["pins"]!["baselineToken"]!.AsObject();
        baseline.Remove("capturedHostSessionId");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrialReviewResult>(node.ToJsonString(), ReviewJson.Options));
        baseline["capturedHostSessionId"] = null;
        baseline["unknown"] = 1;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrialReviewResult>(node.ToJsonString(), ReviewJson.Options));
    }

    [Fact]
    public void ExistingRefusalEnvelopeValidatesAgainstSchema()
    {
        AssertSchema("{\"ok\":false,\"code\":\"invalid\",\"reason\":\"InvalidArgument\",\"message\":\"Invalid selector\",\"facts\":{},\"next\":\"Fix the selector\"}", "RefusalEnvelope");
    }

    [Fact]
    public void SchemaKeepsTheSpecifiedSummaryAndRowFields()
    {
        var expected = new Dictionary<string, string>
        {
            ["ReportView"] = "schema schemaVersion reportId reportDigest detail pins freshness summary flagEvidence limitations detailAvailability sections page word trace links",
            ["ReviewSummary"] = "notice flags incomplete wordforms approved sampledTokens paradigmCells controls negatives parserWork warnings parsimony effects sectionCounts gains ruleAttempts",
            ["FlagFact"] = "id text wordCount readingCount evidenceState sectionIds sampleEventIds facts",
            ["WorkChange"] = "kind percentChange wordCount scopeDigest comparison reasonCodes",
            ["RuleWorkRow"] = "rowKind kind key identityQuality scope name status percentChange unavailableReason",
            ["WordRow"] = "rowKind caseKey wordKey word writingSystemId before after flagIds causes setIds membershipsTotal expectationsTotal work attribution links samplePositions samplePositionsTotal",
            ["TokenRow"] = "rowKind caseId wordKey occurrenceId weight sourceLocator before after links",
            ["CellRow"] = "rowKind setId cellId lexemeId features variantCount before after links",
            ["DiagnosticRow"] = "rowKind eventId diagnosticKey code change severity beforeSeverity subjectIds description attribution links",
            ["FindingRow"] = "rowKind eventId measureId findingId change group unit tier disposition evidenceDigest links",
            ["EffectRow"] = "rowKind operationId summary detail",
            ["EvidenceRow"] = "rowKind id kind pins limitations",
            ["ReadingRow"] = "rowKind identityDigest side identityState morphs observed expectationIds sourceRefs",
            ["ExpectationRow"] = "rowKind id assertion reference actorId revisionId readingIdentityDigest context sourceRefs before after",
            ["TraceNode"] = "id parentId kind label reasonCode objectRefs children detailTruncated",
            ["SourceRow"] = "rowKind id citation locator excerpt excerptComplete actorId",
            ["SetRow"] = "rowKind id name role membershipDigest expectationDigest memberships words expectations links",
        };
        var definitions = ReadSchema()["$defs"]!;
        foreach (var (name, names) in expected)
        {
            var properties = definitions[name]!["properties"]!.AsObject().Select(item => item.Key).Order();
            Assert.Equal(names.Split(' ').Order(), properties);
        }
    }

    [Fact]
    public void EveryPublicEnumRejectsNumericAndUnknownValues()
    {
        var types = typeof(ReviewFlagId).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ReviewFlagId).Namespace && type.IsEnum);
        foreach (var type in types)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("0", type, ReviewJson.Options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("\"not-a-value\"", type, ReviewJson.Options));
        }
    }

    [Fact]
    public void SelectorsAreValidatedBeforeWork()
    {
        var read = new ReviewReadRequest("project", "proposal");
        Assert.Null(read.Validate());
        Assert.Throws<ArgumentException>(() => new ReviewReportId(" "));
        Assert.NotNull((read with { ReportId = " " }).Validate());
        Assert.Equal(DetailLevel.Summary, read.EffectiveDetail);
        Assert.Equal(DetailLevel.Failures, (read with { Section = "warnings" }).EffectiveDetail);
        Assert.Equal(DetailLevel.Full, (read with { Case = "case" }).EffectiveDetail);
        Assert.NotNull((read with { JobId = "job", ReportId = "report" }).Validate());
        Assert.NotNull((read with { Word = "word", Case = "case" }).Validate());
        Assert.NotNull((read with { Trace = true }).Validate());
        Assert.NotNull((read with { Word = "word", Detail = DetailLevel.Summary }).Validate());
        Assert.NotNull((read with { Cursor = "cursor" }).Validate());
        Assert.NotNull((read with { Detail = DetailLevel.Full, Limit = 201 }).Validate());
        Assert.Null((read with { Word = "word", Trace = true, Filter = "all" }).Validate());
    }

    [Fact]
    public void PublicContractHasNoVerdictAuthorizationOrRawCostFields()
    {
        var schema = ReadSchema();
        string[] forbidden = ["verdict", "passed", "blocked", "score", "acceptable", "canApply", "reportClean", "elapsedMs", "baselineWork", "trialWork"];
        foreach (var definition in schema["$defs"]!.AsObject())
        {
            var properties = definition.Value?["properties"] as JsonObject;
            if (properties is null) continue;
            foreach (var key in forbidden) Assert.False(properties.ContainsKey(key), $"{definition.Key}.{key}");
        }
    }

    [Fact]
    public void AReportWrittenWithoutGainsReadsWithNullGains()
    {
        var report = CreateReport();
        Assert.Null(report.Summary.Gains);
        var json = JsonSerializer.Serialize(report, ReviewJson.Options);
        Assert.DoesNotContain("\"gains\"", json, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<ReportView>(json, ReviewJson.Options)!.Summary.Gains);
        var withGains = report with { Summary = report.Summary with
            { Gains = GainSummary.Create(1, 2, GrammarSizeDelta.None, GainGatePolicy.Default, false) } };
        var round = JsonSerializer.Deserialize<ReportView>(JsonSerializer.Serialize(withGains, ReviewJson.Options), ReviewJson.Options)!;
        Assert.Equal(withGains.Summary.Gains, round.Summary.Gains);
    }

    [Fact]
    public void AReportWrittenWithoutRuleAttemptsReadsWithNullAndOneWithThemRoundTrips()
    {
        var report = CreateReport();
        var json = JsonSerializer.Serialize(report, ReviewJson.Options);
        Assert.DoesNotContain("\"ruleAttempts\"", json, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<ReportView>(json, ReviewJson.Options)!.Summary.RuleAttempts);
        var summary = new RuleAttemptSummary(5, 7, [new RuleAttemptGroupRow("shape", 6, ["a", "b"], false)], true, true);
        var with = report with { Summary = report.Summary with { RuleAttempts = summary } };
        var round = JsonSerializer.Deserialize<ReportView>(JsonSerializer.Serialize(with, ReviewJson.Options), ReviewJson.Options)!;
        Assert.Equal("shape", Assert.Single(round.Summary.RuleAttempts!.Groups).ShapeKey);
        Assert.True(round.Summary.RuleAttempts.Hold);
    }

    private static ReportView CreateReport()
    {
        var report = (ReportView)Create(typeof(ReportView));
        return report with
        {
            Schema = "motif.proposal-review-report", SchemaVersion = 1,
            Summary = report.Summary with
            {
                Notice = "No red flags means only 'not bad', never 'good'.",
                Incomplete = report.Summary.Incomplete with { Label = "Incomplete" },
            },
            FlagEvidence = Enum.GetValues<ReviewFlagId>()
                .Select(id => new FlagEvidence(id, EvidenceState.Unavailable, [], 0)).ToArray(),
        };
    }

    private static object Create(Type type)
    {
        if (type == typeof(BaselineToken)) return new BaselineToken("project", "sha256:" + new string('a', 64), "v1",
            "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        if (type == typeof(string)) return "test";
        if (type == typeof(long)) return 1L;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(ReadTarget)) return new ReadTarget(TargetKind.Resource, "test", "motif://test", null, null);
        if (type == typeof(EvidencePin)) return Create(typeof(SetEvidencePin));
        if (type == typeof(FlagFacts)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        if (type == typeof(WorkChange)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        if (type == typeof(RuleWorkRow)) return new RuleWorkRow("rule", "id", "authored", null, "name", "paired", 25, null);
        var constructor = type.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(p =>
            Nullable.GetUnderlyingType(p.ParameterType) is not null ||
            new NullabilityInfoContext().Create(p).ReadState == NullabilityState.Nullable
                ? null : Create(p.ParameterType)).ToArray());
    }

    private static JsonObject ReadSchema()
    {
        using var stream = typeof(ReportView).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.proposal-review-report.schema.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static void AssertSchema(string json, string definition)
    {
        var schema = ReadSchema();
        schema["$id"] = "https://example.test/review-schema/" + Guid.NewGuid();
        schema["$ref"] = "#/$defs/" + definition;
        using var doc = JsonDocument.Parse(json);
        var result = JsonSchema.FromText(schema.ToJsonString()).Evaluate(doc.RootElement);
        Assert.True(result.IsValid, result.ToString());
    }
}
