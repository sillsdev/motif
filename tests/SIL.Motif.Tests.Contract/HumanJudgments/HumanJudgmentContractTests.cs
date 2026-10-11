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
    public void KeepWithoutReasonOrProvenanceRoundTripsAsSentenceDataAndId()
    {
        var value = Keep();
        var formatted = HumanJudgmentCodec.Format(value);
        Assert.Equal("Keep “plural forms” for “Unconditioned allomorph”.", formatted.Statement);
        Assert.StartsWith("{", formatted.Data);
        Assert.Contains("\"contentHash\":\"sha256:", formatted.Data);
        Assert.DoesNotContain("[motif-human-judgment", formatted.Data);
        Assert.Matches("^MJ-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", formatted.VisibleId);
        var reading = HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId, [formatted.Statement], Id, Id);
        Assert.Empty(reading.Warnings);
        var read = reading.Judgment;
        Assert.Null(read.Reason);
        Assert.Null(read.Actor);
        Assert.Null(read.JudgedAtUtc);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.LogicalDigest(read));
        Assert.Equal(ParsimonyDispositionKind.Keep, Assert.IsType<DispositionJudgment>(read.Body).Disposition);
    }

    [Fact]
    public void VisibleIdIsStableAcrossRevisionsAndDependsOnTheJudgmentIdOnly()
    {
        var first = HumanJudgmentCodec.Format(Keep()).VisibleId;
        var revision = HumanJudgmentCodec.Format(Keep() with { RevisionId = "EBESExQVFhcYGRobHB0eHw", Reason = "x" });
        Assert.Equal(first, revision.VisibleId);
        Assert.Equal(first, HumanJudgmentCodec.VisibleId(Id));
        Assert.Equal(first, HumanJudgmentCodec.VisibleId("judgment_" + Id));
        Assert.NotEqual(first, HumanJudgmentCodec.VisibleId("EBESExQVFhcYGRobHB0eHw"));
        Assert.Equal(HumanJudgmentCodec.NormalizeVisibleId(first),
            HumanJudgmentCodec.NormalizeVisibleId(" " + first.ToLowerInvariant().Replace("-", "") + " "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Reason: \"a quote\"\n第二行 [motif-human-judgment:v1:literal] café")]
    public void ReasonLivesOnlyInTheParagraphsAfterTheSentence(string? reason)
    {
        var value = Keep() with { Reason = reason };
        var formatted = HumanJudgmentCodec.Format(value);
        Assert.False(JsonNode.Parse(formatted.Data)!.AsObject().ContainsKey("reason"));
        var paragraphs = new[] { formatted.Statement }.Concat(HumanJudgmentCodec.ReasonParagraphs(reason)).ToArray();
        var read = HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId, paragraphs, Id, Id).Judgment;
        Assert.Equal(string.IsNullOrEmpty(reason) ? null : reason.Normalize(NormalizationForm.FormD), read.Reason);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.LogicalDigest(read));
        var edited = HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId,
            [formatted.Statement, "new reason"], Id, Id);
        Assert.Empty(edited.Warnings);
        Assert.Equal("new reason", edited.Judgment.Reason);
        Assert.NotEqual(HumanJudgmentCodec.LogicalDigest(read), HumanJudgmentCodec.LogicalDigest(edited.Judgment));
        Assert.Equal(Digest, Assert.IsType<DispositionJudgment>(edited.Judgment.Body).EvidenceDigest);
        var removed = HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId, [formatted.Statement], Id, Id);
        Assert.Null(removed.Judgment.Reason);
    }

    [Fact]
    public void EditedDataFailsTheHashButNeverThrowsAnythingElse()
    {
        var formatted = HumanJudgmentCodec.Format(Keep());
        var tampered = formatted.Data.Replace("\"keep\"", "\"defer\"", StringComparison.Ordinal);
        Assert.NotEqual(formatted.Data, tampered);
        var failure = Assert.Throws<HumanJudgmentFormatException>(() => HumanJudgmentCodec.ParseData(tampered, Id, Id));
        Assert.Equal(HumanJudgmentFailure.HashFailed, failure.Failure);
        Assert.Equal("hash failed on this record entry", failure.Message);

        var invalidValue = formatted.Data.Replace("\"keep\"", "\"nonsense\"", StringComparison.Ordinal);
        Assert.Equal(HumanJudgmentFailure.HashFailed, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(invalidValue, Id, Id)).Failure);

        var node = JsonNode.Parse(formatted.Data)!.AsObject();
        node.Remove("contentHash");
        Assert.Equal(HumanJudgmentFailure.HashFailed, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(node.ToJsonString(), Id, Id)).Failure);

        Assert.Equal(HumanJudgmentFailure.HashFailed, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(" " + formatted.Data, Id, Id)).Failure);
        Assert.Equal(HumanJudgmentFailure.HashFailed, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(JsonNode.Parse(formatted.Data)!.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }), Id, Id)).Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"format\":")]
    [InlineData("Keep “x”. [motif-human-judgment:v1:e30]")]
    public void DamagedDataIsReportedAsDamaged(string data)
    {
        Assert.Equal(HumanJudgmentFailure.Damaged, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(data, Id, Id)).Failure);
    }

    [Fact]
    public void ADataValueWithAValidHashButAnOpenEnvelopeIsDamaged()
    {
        var node = JsonNode.Parse(HumanJudgmentCodec.Format(Keep()).Data)!.AsObject();
        node["extra"] = true;
        Assert.Equal(HumanJudgmentFailure.Damaged, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(Reseal(node), Id, Id)).Failure);
    }

    [Fact]
    public void IdentityMustMatchTheContainingProjectAndRecord()
    {
        var data = HumanJudgmentCodec.Format(Keep()).Data;
        var other = "EBESExQVFhcYGRobHB0eHw";
        Assert.Equal(HumanJudgmentFailure.Identity, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(data, other, Id)).Failure);
        Assert.Equal(HumanJudgmentFailure.Identity, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(data, Id, other)).Failure);
        Assert.NotNull(HumanJudgmentCodec.ParseData(data, "project_" + Id, "notebook_" + Id));
    }

    [Fact]
    public void AnEditedIdOrSentenceWarnsAndTheDataStillDecides()
    {
        var formatted = HumanJudgmentCodec.Format(Keep() with { Reason = "because" });
        var wrongId = HumanJudgmentCodec.Read(formatted.Data, "MJ-0000-0000", [formatted.Statement, "because"], Id, Id);
        var idWarning = Assert.Single(wrongId.Warnings);
        Assert.Equal(HumanJudgmentWarningKind.IdMismatch, idWarning.Kind);
        Assert.Equal("The judgment ID on this record does not match its data", idWarning.Message);
        Assert.Equal("because", wrongId.Judgment.Reason);
        Assert.Equal(HumanJudgmentWarningKind.IdMismatch, Assert.Single(
            HumanJudgmentCodec.Read(formatted.Data, null, [formatted.Statement], Id, Id).Warnings).Kind);

        var sentence = HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId,
            ["Fix “plural forms” for “Unconditioned allomorph”.", "because"], Id, Id);
        var sentenceWarning = Assert.Single(sentence.Warnings);
        Assert.Equal(HumanJudgmentWarningKind.StatementEdited, sentenceWarning.Kind);
        Assert.Equal("The sentence on this record was edited; Motif uses its data", sentenceWarning.Message);
        Assert.Equal(ParsimonyDispositionKind.Keep, Assert.IsType<DispositionJudgment>(sentence.Judgment.Body).Disposition);
        Assert.Equal(HumanJudgmentWarningKind.StatementEdited, Assert.Single(
            HumanJudgmentCodec.Read(formatted.Data, formatted.VisibleId, [], Id, Id).Warnings).Kind);

        var both = HumanJudgmentCodec.Read(formatted.Data, "typo", ["edited"], Id, Id);
        Assert.Equal(2, both.Warnings.Count);
        Assert.Null(both.Judgment.Reason);
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
    public void DuplicateJsonKeysAndAReasonInsideTheDataRefuse()
    {
        var json = HumanJudgmentCodec.ToJson(Keep());
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.ParseJson(json.Insert(1, "\"version\":1,"), Id, Id));
        var node = JsonNode.Parse(HumanJudgmentCodec.Format(Keep()).Data)!.AsObject();
        node["reason"] = "secret reason";
        Assert.Equal(HumanJudgmentFailure.Damaged, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(Reseal(node), Id, Id)).Failure);
    }

    [Fact]
    public void AnOldMarkerValueIsDamagedData()
    {
        const string old = "Keep “plural forms” for “Unconditioned allomorph”. [motif-human-judgment:v1:e30]";
        Assert.Equal(HumanJudgmentFailure.Damaged, Assert.Throws<HumanJudgmentFormatException>(
            () => HumanJudgmentCodec.ParseData(old, Id, Id)).Failure);
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
        Assert.Contains("This reading", HumanJudgmentCodec.Format(negative).Statement);
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
        Assert.NotNull(HumanJudgmentCodec.ParseData(HumanJudgmentCodec.Format(surface).Data, Id, Id));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(surface with { Actor = new(JudgmentActorKind.Agent) }));
        var reading = Negative(new ReadingNegativeTarget([Morph(Id) with { Identity = new ParseMorph(null, Id, null, null) }]));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(reading));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = new RetractionJudgment() }));
        var previous = new JudgmentPredecessor("EBESExQVFhcYGRobHB0eHw", Digest);
        var retract = Keep() with { Body = new RetractionJudgment(), Replaces = [previous] };
        Assert.IsType<RetractionJudgment>(HumanJudgmentCodec.ParseData(HumanJudgmentCodec.Format(retract).Data, Id, Id).Body);
        var ask = Assert.IsType<DispositionJudgment>(Keep().Body) with { Disposition = ParsimonyDispositionKind.Ask };
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(Keep() with { Body = ask }));
        Assert.NotNull(HumanJudgmentCodec.ParseData(HumanJudgmentCodec.Format(Keep() with { Body = ask with { Question = "Which dialect?" } }).Data, Id, Id));
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
    public void IndependentPortableVectorsMatchExactStoredPartsAndDigests()
    {
        using var stream = typeof(HumanJudgment).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.human-judgment.vectors.json");
        Assert.NotNull(stream);
        using var vectors = JsonDocument.Parse(stream);
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            var value = HumanJudgmentCodec.ParseJson(vector.GetProperty("value").GetRawText(), Id, Id);
            var formatted = HumanJudgmentCodec.Format(value);
            Assert.Equal(vector.GetProperty("statement").GetString(), formatted.Statement);
            Assert.Equal(vector.GetProperty("data").GetString(), formatted.Data);
            Assert.Equal(vector.GetProperty("id").GetString(), formatted.VisibleId);
            Assert.Equal(vector.GetProperty("digest").GetString(), HumanJudgmentCodec.LogicalDigest(value));
            var paragraphs = new[] { vector.GetProperty("statement").GetString()! }
                .Concat(HumanJudgmentCodec.ReasonParagraphs(value.Reason)).ToArray();
            var reading = HumanJudgmentCodec.Read(vector.GetProperty("data").GetString()!,
                vector.GetProperty("id").GetString(), paragraphs, Id, Id);
            Assert.Empty(reading.Warnings);
            Assert.Equal(HumanJudgmentCodec.ToJson(value), HumanJudgmentCodec.ToJson(reading.Judgment));
        }
    }

    [Fact]
    public void HashFailureVectorsAreRefusedWithTheirNamedFailure()
    {
        using var stream = typeof(HumanJudgment).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.human-judgment.refusals.json");
        Assert.NotNull(stream);
        using var refusals = JsonDocument.Parse(stream);
        foreach (var refusal in refusals.RootElement.EnumerateArray())
        {
            var failure = Assert.Throws<HumanJudgmentFormatException>(
                () => HumanJudgmentCodec.ParseData(refusal.GetProperty("data").GetString()!, Id, Id));
            Assert.Equal(refusal.GetProperty("failure").GetString(), failure.Failure.ToString());
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
        Assert.StartsWith("Resolve conflicting revisions: Keep", HumanJudgmentCodec.Format(resolution).Statement);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(resolution), HumanJudgmentCodec.LogicalDigest(
            resolution with { Replaces = heads.Reverse().ToArray() }));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(resolution with { Replaces = [heads[0]] }));
        Assert.Throws<FormatException>(() => HumanJudgmentCodec.Format(resolution with
            { Replaces = [heads[0], heads[0] with { RevisionId = "prefix_" + heads[0].RevisionId }] }));
    }

    [Fact]
    public void SchemaIsClosedAndStorageForbidsAReasonButRequiresTheHash()
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
        var storage = definitions.GetProperty("StorageEnvelope");
        Assert.False(storage.GetProperty("properties").TryGetProperty("reason", out _));
        Assert.True(storage.GetProperty("properties").TryGetProperty("contentHash", out _));
        Assert.Contains("contentHash", storage.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
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

    private static string Reseal(JsonObject node)
    {
        node.Remove("contentHash");
        var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(SIL.Motif.Contract.Canonicalization.CanonicalJson.Canonicalize(node.ToJsonString()))));
        node["contentHash"] = hash;
        return SIL.Motif.Contract.Canonicalization.CanonicalJson.Canonicalize(node.ToJsonString());
    }

    private static HumanJudgment Keep() => new(
        Id, Id, Id, Array.Empty<JudgmentPredecessor>(),
        new DispositionJudgment(new ObjectJudgmentSubject(new JudgmentObject("MoAffixAllomorph", Id)),
            "R-allo-unconditioned", ParsimonyDispositionKind.Keep, Digest, "detector-v1/threshold-v1/projection-v1",
            "plural forms", "Unconditioned allomorph", null));
}
