using System;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Contract.HumanJudgments;

public sealed class HumanJudgmentContractTests
{
    private const string Id = "AAECAwQFBgcICQoLDA0ODw";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void KeepWithoutReasonOrProvenanceRoundTripsAsOneReadableValue()
    {
        var value = Keep();
        var physical = HumanJudgmentCodec.Format(value);
        Assert.StartsWith("Keep “plural forms” for “Unconditioned allomorph”. [motif-human-judgment:v1:", physical);
        var read = HumanJudgmentCodec.Parse(physical, Id, Id);
        Assert.Null(read.Reason);
        Assert.Null(read.Actor);
        Assert.Null(read.JudgedAtUtc);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.LogicalDigest(read));
        Assert.Equal(ParsimonyDispositionKind.Keep, Assert.IsType<DispositionJudgment>(read.Body).Disposition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Reason: \"a quote\"\n第二行 [motif-human-judgment:v1:literal] café")]
    public void QuotedReasonHasOneEditableSourceAndStableNfdDigest(string? reason)
    {
        var value = Keep() with { Reason = reason };
        var physical = HumanJudgmentCodec.Format(value);
        var decoded = DecodeMarker(physical);
        Assert.False(decoded.ContainsKey("reason"));
        var read = HumanJudgmentCodec.Parse(physical, Id, Id);
        Assert.Equal(string.IsNullOrEmpty(reason) ? null : reason.Normalize(NormalizationForm.FormD), read.Reason);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.LogicalDigest(read));
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(value),
            HumanJudgmentCodec.LogicalDigest(value with { Reason = reason?.Normalize(NormalizationForm.FormD) }));
        var markerAt = physical.LastIndexOf(" [motif-human-judgment:v1:", StringComparison.Ordinal);
        var edited = "Keep “plural forms” for “Unconditioned allomorph”. Reason: \"new reason\"" + physical[markerAt..];
        var afterEdit = HumanJudgmentCodec.Parse(edited, Id, Id);
        Assert.Equal("new reason", afterEdit.Reason);
        Assert.NotEqual(HumanJudgmentCodec.LogicalDigest(read), HumanJudgmentCodec.LogicalDigest(afterEdit));
        Assert.Equal(Digest, Assert.IsType<DispositionJudgment>(afterEdit.Body).EvidenceDigest);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("format")]
    [InlineData("readableFormat")]
    [InlineData("unknown-root")]
    [InlineData("unknown-body")]
    [InlineData("unknown-subject")]
    [InlineData("unknown-actor")]
    [InlineData("unknown-role")]
    [InlineData("numeric-disposition")]
    [InlineData("missing-body")]
    [InlineData("missing-format")]
    [InlineData("missing-measure")]
    [InlineData("null-members")]
    public void UnknownOrIncompleteSemanticValuesRefuse(string fault)
    {
        var node = JsonNode.Parse(HumanJudgmentCodec.ToJson(Keep()))!.AsObject();
        switch (fault)
        {
            case "version": node["version"] = 2; break;
            case "format": node["format"] = "other"; break;
            case "readableFormat": node["readableFormat"] = "en-v2"; break;
            case "unknown-root": node["extra"] = true; break;
            case "unknown-body": node["body"]!["extra"] = true; break;
            case "unknown-subject": node["body"]!["subject"]!["kind"] = "guessed-object"; break;
            case "unknown-actor": node["actor"] = new JsonObject { ["kind"] = "login" }; break;
            case "unknown-role": node["body"]!["subject"] = JsonNode.Parse(
                "{\"kind\":\"computed-group\",\"measureId\":\"R-allo-unconditioned\",\"role\":\"unknown\",\"members\":[]}"); break;
            case "numeric-disposition": node["body"]!["disposition"] = 1; break;
            case "missing-body": node.Remove("body"); break;
            case "missing-format": node.Remove("format"); break;
            case "missing-measure": node["body"]!.AsObject().Remove("measureId"); break;
            case "null-members": node["body"]!["subject"] = JsonNode.Parse(
                "{\"kind\":\"computed-group\",\"measureId\":\"R-allo-unconditioned\",\"role\":\"duplicate-pair\",\"members\":null}"); break;
        }
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(node.ToJsonString(), Id, Id));
    }

    [Fact]
    public void DuplicateJsonKeysAndSecondEncodedReasonRefuse()
    {
        var json = HumanJudgmentCodec.ToJson(Keep());
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(json.Insert(1, "\"version\":1,"), Id, Id));
        var physical = HumanJudgmentCodec.Format(Keep());
        var payload = DecodeMarker(physical);
        payload["reason"] = "secret reason";
        var changed = WithMarker(physical, payload);
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(changed, Id, Id));
    }

    [Fact]
    public void UnknownReadableFormatIsRefusedWithItsMarker()
    {
        var physical = HumanJudgmentCodec.Format(Keep());
        var payload = DecodeMarker(physical);
        payload["readableFormat"] = "en-v2";

        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(WithMarker(physical, payload), Id, Id));
    }

    [Fact]
    public void MarkerAndVisibleDecisionMustAgreeAndStrictEncodingIsRequired()
    {
        var physical = HumanJudgmentCodec.Format(Keep());
        foreach (var changed in new[]
        {
            physical.Replace("Keep “", "Fix “", StringComparison.Ordinal),
            physical[..^1], physical[..^1] + "=]", physical + physical,
            physical.Replace("motif-human-judgment:v1:", "motif-human-judgment:v2:", StringComparison.Ordinal),
            physical.Replace(". [motif", ". Reason: unquoted [motif", StringComparison.Ordinal),
            physical.Replace(". [motif", ". Reason: null [motif", StringComparison.Ordinal)
        }) Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(changed, Id, Id));
        var start = physical.LastIndexOf(" [motif-human-judgment:v1:", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(
            physical[..start] + " [motif-human-judgment:v1:_w]", Id, Id));
    }

    [Fact]
    public void PortableIdentityUsesNetworkGuidBytesAndActualContainingIdentities()
    {
        Assert.Equal(Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"), CanonicalId.Parse(Id).ToGuid());
        var physical = HumanJudgmentCodec.Format(Keep());
        var other = "EBESExQVFhcYGRobHB0eHw";
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(physical, other, Id));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Parse(physical, Id, other));
        Assert.NotNull(HumanJudgmentCodec.Parse(physical, "project_" + Id, "notebook_" + Id));
    }

    [Fact]
    public void ComputedMembersAreSetsButEdgesAndReadingMorphsKeepTheirDeclaredRoles()
    {
        var a = new JudgmentObject("MoAffixAllomorph", Id);
        var b = new JudgmentObject("MoAffixAllomorph", "EBESExQVFhcYGRobHB0eHw");
        var group = new GroupJudgmentSubject("R-allo-unconditioned", JudgmentGroupRole.AlternationFamily, [a, b]);
        Assert.Equal(HumanJudgmentCodec.SubjectKey(group), HumanJudgmentCodec.SubjectKey(group with { Members = [b, a] }));
        Assert.NotEqual(HumanJudgmentCodec.SubjectKey(group), HumanJudgmentCodec.SubjectKey(group with { Members = [a] }));
        Assert.Equal(HumanJudgmentCodec.SubjectKey(new ObjectJudgmentSubject(a)),
            HumanJudgmentCodec.SubjectKey(new ObjectJudgmentSubject(a with { Id = "form_" + Id })));
        var edge = new EdgeJudgmentSubject(JudgmentEdgeRole.Precedence, a, a, b);
        Assert.NotEqual(HumanJudgmentCodec.SubjectKey(edge), HumanJudgmentCodec.SubjectKey(edge with { From = b, To = a }));
        var body = Assert.IsType<DispositionJudgment>(Keep().Body) with { Subject = group with { Members = [a, a] } };
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = body }));
        var reading = new ReadingNegativeTarget([Morph(Id), Morph(b.Id)]);
        var negative = Negative(reading);
        var swapped = negative with { Body = Assert.IsType<ReviewedNegativeJudgment>(negative.Body) with
            { Target = new ReadingNegativeTarget(reading.Morphs.Reverse().ToArray()) } };
        Assert.NotEqual(HumanJudgmentCodec.LogicalDigest(negative), HumanJudgmentCodec.LogicalDigest(swapped));
        Assert.Contains("This reading", HumanJudgmentCodec.Format(negative));
    }

    [Fact]
    public void WordCaseDispositionNamesOneExactMeasureFinding()
    {
        var subject = new ParsimonyFindingJudgmentSubject("R-word-negative-accepted", "finding:case-17");
        var judgment = Keep() with
        {
            Body = Assert.IsType<DispositionJudgment>(Keep().Body) with
            {
                Subject = subject,
                MeasureId = subject.MeasureId,
            },
        };

        var decoded = HumanJudgmentCodec.ParseJson(HumanJudgmentCodec.ToJson(judgment), Id, Id);
        Assert.Equal(HumanJudgmentCodec.SubjectKey(subject),
            HumanJudgmentCodec.SubjectKey(Assert.IsType<DispositionJudgment>(decoded.Body).Subject));
        Assert.NotEqual(HumanJudgmentCodec.SubjectKey(subject), HumanJudgmentCodec.SubjectKey(
            subject with { FindingId = "finding:case-18" }));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(judgment with
        {
            Body = Assert.IsType<DispositionJudgment>(judgment.Body) with { MeasureId = "other-measure" },
        }));
    }

    [Fact]
    public void ClosedNegativeRetractionAndAskCannotMasqueradeAsAnotherKind()
    {
        var surface = Negative(new SurfaceNegativeTarget());
        Assert.Null(Assert.IsType<ReviewedNegativeJudgment>(surface.Body).WordformId);
        Assert.NotNull(HumanJudgmentCodec.Parse(HumanJudgmentCodec.Format(surface), Id, Id));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(surface with { Actor = new(JudgmentActorKind.Agent) }));
        var reading = Negative(new ReadingNegativeTarget([Morph(Id) with { Identity = new ParseMorph(null, Id, null, null) }]));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(reading));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = new RetractionJudgment() }));
        var previous = new JudgmentPredecessor("EBESExQVFhcYGRobHB0eHw", Digest);
        var retract = Keep() with { Body = new RetractionJudgment(), Replaces = [previous] };
        Assert.IsType<RetractionJudgment>(HumanJudgmentCodec.Parse(HumanJudgmentCodec.Format(retract), Id, Id).Body);
        var ask = Assert.IsType<DispositionJudgment>(Keep().Body) with { Disposition = ParsimonyDispositionKind.Ask };
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = ask }));
        Assert.NotNull(HumanJudgmentCodec.Parse(HumanJudgmentCodec.Format(Keep() with { Body = ask with { Question = "Which dialect?" } }), Id, Id));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with
            { Replaces = [previous, new JudgmentPredecessor("ICEiIyQlJicoKSorLC0uLw", Digest)] }));
    }

    [Fact]
    public void BoundsRefuseAndNeverTruncateIdentityOrReason()
    {
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Reason = new string('é', 4097) }));
        var body = Assert.IsType<DispositionJudgment>(Keep().Body) with { SubjectCaption = "first\nsecond" };
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = body }));
    }

    [Fact]
    public void IndependentPortableVectorsMatchExactReadableValuesAndDigests()
    {
        using var stream = typeof(HumanJudgment).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.human-judgment.vectors.json");
        Assert.NotNull(stream);
        using var vectors = JsonDocument.Parse(stream);
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            var value = HumanJudgmentCodec.ParseJson(vector.GetProperty("value").GetRawText(), Id, Id);
            Assert.Equal(vector.GetProperty("physical").GetString(), HumanJudgmentCodec.Format(value));
            Assert.Equal(vector.GetProperty("digest").GetString(), HumanJudgmentCodec.LogicalDigest(value));
            Assert.Equal(HumanJudgmentCodec.ToJson(value), HumanJudgmentCodec.ToJson(
                HumanJudgmentCodec.Parse(vector.GetProperty("physical").GetString()!, Id, Id)));
        }
    }

    [Theory]
    [InlineData("KEEP")]
    [InlineData("Keep")]
    [InlineData("unknown")]
    public void WireDispositionHasNoCaseOrNameAliases(string wire)
    {
        var json = JsonNode.Parse(HumanJudgmentCodec.ToJson(Keep()))!;
        json["body"]!["disposition"] = wire;
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(json.ToJsonString(), Id, Id));
    }

    [Fact]
    public void ConflictResolutionNamesDistinctHeadsAndIsExplicitInReadableText()
    {
        var heads = new[]
        {
            new JudgmentPredecessor("EBESExQVFhcYGRobHB0eHw", Digest),
            new JudgmentPredecessor("ICEiIyQlJicoKSorLC0uLw", Digest)
        };
        var resolution = Keep() with { Replaces = heads, ResolvesConflict = true };
        Assert.StartsWith("Resolve conflicting revisions: Keep", HumanJudgmentCodec.Format(resolution));
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(resolution), HumanJudgmentCodec.LogicalDigest(
            resolution with { Replaces = heads.Reverse().ToArray() }));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(resolution with { Replaces = [heads[0]] }));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(resolution with
            { Replaces = [heads[0], heads[0] with { RevisionId = "prefix_" + heads[0].RevisionId }] }));
    }

    [Fact]
    public void SchemaIsClosedAndStorageForbidsASecondReasonSource()
    {
        using var stream = typeof(HumanJudgment).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.human-judgment.schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);
        var definitions = document.RootElement.GetProperty("$defs");
        using var logical = JsonDocument.Parse(HumanJudgmentCodec.ToJson(Keep()));
        var envelope = definitions.GetProperty("HumanJudgment");
        Assert.False(envelope.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(envelope.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
            logical.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.False(definitions.GetProperty("StorageEnvelope").GetProperty("properties").TryGetProperty("reason", out _));
        foreach (var name in new[] { "Object", "ObjectSubject", "ProjectSubject", "EdgeSubject", "GroupSubject",
            "ParsimonyFindingSubject",
            "Disposition", "MorphIdentity", "Morph", "SurfaceTarget", "ReadingTarget", "Negative", "Retraction",
            "Actor", "Source", "Predecessor", "StorageEnvelope" })
            Assert.False(definitions.GetProperty(name).GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void ReadableFormatIsASharedVersionedResource()
    {
        using var resource = typeof(HumanJudgment).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.HumanJudgments.ReadableFormats.en-v1.json");
        Assert.NotNull(resource);
        using var format = JsonDocument.Parse(resource);
        Assert.Equal("en-v1", format.RootElement.GetProperty("format").GetString());
        Assert.Equal("Keep", format.RootElement.GetProperty("keep").GetString());
    }

    [Fact]
    public void ActorAndEdgeWireRolesHaveNoCaseAliases()
    {
        var value = Keep() with { Actor = new(JudgmentActorKind.Human) };
        var node = JsonNode.Parse(HumanJudgmentCodec.ToJson(value))!;
        node["actor"]!["kind"] = "Human";
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(node.ToJsonString(), Id, Id));
        var disposition = Assert.IsType<DispositionJudgment>(value.Body);
        var item = new JudgmentObject("MoAffixAllomorph", Id);
        value = value with { Body = disposition with { Subject = new EdgeJudgmentSubject(JudgmentEdgeRole.Precedence, item, item, item) } };
        node = JsonNode.Parse(HumanJudgmentCodec.ToJson(value))!;
        node["body"]!["subject"]!["role"] = "Precedence";
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(node.ToJsonString(), Id, Id));
    }

    private static NegativeJudgmentMorph Morph(string form) => new(new ParseMorph(form, Id, null, null), null, "-ra", "plural");
    private static HumanJudgment Negative(NegativeJudgmentTarget target) => Keep() with
    {
        Actor = new(JudgmentActorKind.Human),
        Body = new ReviewedNegativeJudgment(Id, "qaa", "tará", "standard dialect", target)
    };

    private static JsonObject DecodeMarker(string physical)
    {
        const string marker = " [motif-human-judgment:v1:";
        var start = physical.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return JsonNode.Parse(Encoding.UTF8.GetString(Base64Url.Decode(physical[start..^1])))!.AsObject();
    }

    private static string WithMarker(string physical, JsonObject payload)
    {
        const string marker = " [motif-human-judgment:v1:";
        var start = physical.LastIndexOf(marker, StringComparison.Ordinal);
        return physical[..start] + marker + Base64Url.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString())) + "]";
    }

    private static HumanJudgment Keep() => new(
        Id, Id, Id, Array.Empty<JudgmentPredecessor>(),
        new DispositionJudgment(new ObjectJudgmentSubject(new JudgmentObject("MoAffixAllomorph", Id)),
            "R-allo-unconditioned", ParsimonyDispositionKind.Keep, Digest, "detector-v1/threshold-v1/projection-v1",
            "plural forms", "Unconditioned allomorph", null));
}
