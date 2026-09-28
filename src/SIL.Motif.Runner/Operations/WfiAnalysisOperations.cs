using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>Operation kinds for human opinions and parser candidates in the analysis Manifest.</summary>
public static class WfiAnalysisOperationKinds
{
    public const string AddRefEvaluations = "analysis/wfiAnalysis/addRefEvaluations";
    public const string RemoveRefEvaluations = "analysis/wfiAnalysis/removeRefEvaluations";
    public const string DeleteAnalysis = "analysis/wfiAnalysis/delete";
    public const string CreateAnalysis = "analysis/wfiWordform/createAnalyses";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var kind in new[] { AddRefEvaluations, RemoveRefEvaluations, DeleteAnalysis, CreateAnalysis })
            OperationKindRegistry.Register(kind);
        OperationHandlerRegistry.Register(AddRefEvaluations, new HumanEvaluationHandler(true));
        OperationHandlerRegistry.Register(RemoveRefEvaluations, new HumanEvaluationHandler(false));
        OperationHandlerRegistry.Register(DeleteAnalysis, new DeleteAnalysisHandler());
        OperationHandlerRegistry.Register(CreateAnalysis, new CreateAnalysisHandler());
    }
#pragma warning restore CA2255
}

/// <summary>The closed payload naming the default human agent's positive or negative evaluation.</summary>
public static class HumanEvaluationPayload
{
    public const string Approves = "defaultUserApproves";
    public const string Disapproves = "defaultUserDisapproves";

    public static string Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["member"], kind);
        var member = ClosedPayloadParsing.GetRequiredString(after, "member", kind);
        if (member != Approves && member != Disapproves)
            throw new ContractParseException($"'{kind}' operation 'after.member' must name a default user evaluation.");
        return member;
    }
}

internal sealed class HumanEvaluationHandler(bool add) : IOperationHandler
{
    private string Kind => add ? WfiAnalysisOperationKinds.AddRefEvaluations
        : WfiAnalysisOperationKinds.RemoveRefEvaluations;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var member = HumanEvaluationPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{Kind}' operation requires 'after'."), Kind);
        var (id, analysis) = TargetResolution.Resolve<IWfiAnalysis>(cache, operation, Kind);
        touchedTargets.Add(id);
        var before = Read(analysis);
        var agent = cache.LangProject.DefaultUserAgent;
        var evaluation = member == HumanEvaluationPayload.Approves ? agent.ApprovesOA : agent.DisapprovesOA;
        if (add)
            agent.SetEvaluation(analysis, member == HumanEvaluationPayload.Approves ? Opinions.approves : Opinions.disapproves);
        else if (evaluation is not null)
        {
            analysis.EvaluationsRC.Remove(evaluation);
        }
        return new ExpectedEffect(id, SnapshotFields.WfiAnalysisEvaluations, before, Read(analysis));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        HumanEvaluationPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{Kind}' operation requires 'after'."), Kind);
        var (id, analysis) = TargetResolution.Resolve<IWfiAnalysis>(cache, operation, Kind);
        var current = Read(analysis);
        return new ExpectedEffect(id, SnapshotFields.WfiAnalysisEvaluations, current, current);
    }

    private static IReadOnlyDictionary<string, string> Read(IWfiAnalysis analysis) =>
        AnalysisFieldSnapshots.Read(analysis).AlternativesFields[SnapshotFields.WfiAnalysisEvaluations];
}

/// <summary>The closed empty payload for deleting one stored analysis.</summary>
public static class DeleteAnalysisPayload
{
    public static void Parse(JsonElement after)
    {
        var kind = WfiAnalysisOperationKinds.DeleteAnalysis;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, [], kind);
    }
}

internal sealed class DeleteAnalysisHandler : IOperationHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation,
        List<CanonicalId> touchedTargets)
    {
        DeleteAnalysisPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{WfiAnalysisOperationKinds.DeleteAnalysis}' operation requires 'after'."));
        var (analysisId, analysis) = TargetResolution.Resolve<IWfiAnalysis>(
            cache, operation, WfiAnalysisOperationKinds.DeleteAnalysis);
        var wordform = analysis.Owner as IWfiWordform ?? throw new ContractParseException(
            $"'{WfiAnalysisOperationKinds.DeleteAnalysis}' target must be owned by a wordform.");
        var wordformId = CanonicalId.FromGuid(wordform.Guid);
        var before = Read(wordform);
        AddUses(before, analysisId, ReadTextUses(cache, analysisId, wordform));
        touchedTargets.Add(analysisId);

        analysis.Delete();

        return new ExpectedEffect(wordformId, SnapshotFields.WfiWordformAnalyses, before, Read(wordform));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        DeleteAnalysisPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{WfiAnalysisOperationKinds.DeleteAnalysis}' operation requires 'after'."));
        var (analysisId, analysis) = TargetResolution.Resolve<IWfiAnalysis>(
            cache, operation, WfiAnalysisOperationKinds.DeleteAnalysis);
        var wordform = analysis.Owner as IWfiWordform ?? throw new ContractParseException(
            $"'{WfiAnalysisOperationKinds.DeleteAnalysis}' target must be owned by a wordform.");
        var current = Read(wordform);
        AddUses(current, analysisId, ReadTextUses(cache, analysisId, wordform));
        return new ExpectedEffect(CanonicalId.FromGuid(wordform.Guid), SnapshotFields.WfiWordformAnalyses,
            current, current);
    }

    private static Dictionary<string, string> Read(IWfiWordform wordform) => AnalysisFieldSnapshots.Read(wordform)
        .AlternativesFields[SnapshotFields.WfiWordformAnalyses].ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal);

    private static void AddUses(Dictionary<string, string> analyses, CanonicalId analysisId,
        IReadOnlyList<AnalysisTextUse> uses)
    {
        if (!analyses.TryGetValue(analysisId.Value, out var content))
            throw new ContractParseException("The analysis is no longer owned by its wordform.");
        using var document = JsonDocument.Parse(content);
        analyses[analysisId.Value] = JsonSerializer.Serialize(new
        {
            analysis = document.RootElement.Clone(),
            uses,
        }, JsonOptions);
    }

    private static IReadOnlyList<AnalysisTextUse> ReadTextUses(LcmCache cache, CanonicalId analysisId,
        IWfiWordform wordform)
    {
        var wordformId = CanonicalId.FromGuid(wordform.Guid).Value;
        var wordformForm = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
        var uses = new List<AnalysisTextUse>();
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances())
        {
            if (text.ContentsOA is not { } contents) continue;
            foreach (var paragraphObject in contents.ParagraphsOS)
            {
                if (paragraphObject is not IStTxtPara paragraph) continue;
                foreach (var segment in paragraph.SegmentsOS)
                for (var index = 0; index < segment.AnalysesRS.Count; index++)
                {
                    if (segment.AnalysesRS[index].Guid != analysisId.ToGuid()) continue;
                    uses.Add(new AnalysisTextUse(
                        CanonicalId.FromGuid(text.Guid).Value,
                        text.Name.get_String(cache.DefaultAnalWs)?.Text ?? "",
                        CanonicalId.FromGuid(paragraph.Guid).Value,
                        CanonicalId.FromGuid(segment.Guid).Value,
                        index,
                        wordformId,
                        wordformForm,
                        "LibLCM removes the reference; it adds no wordform fallback. " +
                        "Later analyses keep their order and shift left."));
                }
            }
        }
        return uses
            .OrderBy(use => use.TextId, StringComparer.Ordinal)
            .ThenBy(use => use.ParagraphId, StringComparer.Ordinal)
            .ThenBy(use => use.SegmentId, StringComparer.Ordinal)
            .ThenBy(use => use.Index)
            .ToArray();
    }

    private sealed record AnalysisTextUse(string TextId, string TextName, string ParagraphId, string SegmentId,
        int Index, string WordformId, string Wordform, string After);
}

/// <summary>One ordered morph bundle in a new parser candidate.</summary>
public sealed record CandidateMorph(CanonicalId? Form, CanonicalId? Msa, CanonicalId? InflType, string? GuessedString);

/// <summary>The closed payload for creating a parser candidate under a wordform.</summary>
public static class CreateAnalysisPayload
{
    public static IReadOnlyList<CandidateMorph> Parse(JsonElement after)
    {
        var kind = WfiAnalysisOperationKinds.CreateAnalysis;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["morphs"], kind);
        if (!after.TryGetProperty("morphs", out var morphs) || morphs.ValueKind != JsonValueKind.Array ||
            morphs.GetArrayLength() == 0)
            throw new ContractParseException($"'{kind}' operation requires a nonempty 'after.morphs' array.");
        var result = new List<CandidateMorph>();
        foreach (var morph in morphs.EnumerateArray())
        {
            ClosedPayloadParsing.RequireObject(morph, kind);
            ClosedPayloadParsing.RejectUnknownProperties(morph, ["form", "msa", "inflType", "guessedString"], kind);
            CanonicalId? form = morph.TryGetProperty("form", out var formElement) && formElement.ValueKind != JsonValueKind.Null
                ? ClosedPayloadParsing.GetRequiredCanonicalId(morph, "form", kind) : null;
            CanonicalId? msa = morph.TryGetProperty("msa", out var msaElement) && msaElement.ValueKind != JsonValueKind.Null
                ? ClosedPayloadParsing.GetRequiredCanonicalId(morph, "msa", kind) : null;
            CanonicalId? inflType = morph.TryGetProperty("inflType", out var infl) && infl.ValueKind != JsonValueKind.Null ?
                ClosedPayloadParsing.GetRequiredCanonicalId(morph, "inflType", kind) : null;
            string? guessed = morph.TryGetProperty("guessedString", out var guess) && guess.ValueKind != JsonValueKind.Null ?
                ClosedPayloadParsing.GetRequiredString(morph, "guessedString", kind) : null;
            if (form is null && string.IsNullOrEmpty(guessed))
                throw new ContractParseException($"'{kind}' requires a form or guessed string for each morph.");
            result.Add(new CandidateMorph(form, msa, inflType, guessed));
        }
        return result;
    }
}

internal sealed class CreateAnalysisHandler : IOperationHandler
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        const string kind = WfiAnalysisOperationKinds.CreateAnalysis;
        var morphs = CreateAnalysisPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{kind}' operation requires 'after'."));
        var entityId = operation.EntityId ?? throw new ContractParseException($"'{kind}' operation requires 'entityId'.");
        var (id, wordform) = TargetResolution.Resolve<IWfiWordform>(cache, operation, kind);
        var resolved = morphs.Select(morph => (
            Form: morph.Form is { } form ? ReferenceFieldLowering.Resolve<IMoForm>(cache, form, kind) : null,
            Msa: morph.Msa is { } msa ? ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, msa, kind) : null,
            InflType: morph.InflType is { } infl ?
                ReferenceFieldLowering.Resolve<ILexEntryInflType>(cache, infl, kind) : null,
            morph.GuessedString)).ToArray();
        touchedTargets.Add(id);
        var before = Read(wordform);
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create(entityId.ToGuid());
        wordform.AnalysesOC.Add(analysis);
        foreach (var morph in resolved)
        {
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = morph.Form;
            bundle.MsaRA = morph.Msa;
            bundle.InflTypeRA = morph.InflType;
            if (morph.GuessedString is not null)
                bundle.Form.set_String(cache.DefaultVernWs,
                    SIL.LCModel.Core.Text.TsStringUtils.MakeString(morph.GuessedString, cache.DefaultVernWs));
        }
        cache.LangProject.DefaultParserAgent.SetEvaluation(analysis, Opinions.approves);
        return new ExpectedEffect(id, SnapshotFields.WfiWordformAnalyses, before, Read(wordform));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        CreateAnalysisPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{WfiAnalysisOperationKinds.CreateAnalysis}' operation requires 'after'."));
        var (id, wordform) = TargetResolution.Resolve<IWfiWordform>(cache, operation, WfiAnalysisOperationKinds.CreateAnalysis);
        var current = Read(wordform);
        return new ExpectedEffect(id, SnapshotFields.WfiWordformAnalyses, current, current);
    }

    private static IReadOnlyDictionary<string, string> Read(IWfiWordform wordform) =>
        AnalysisFieldSnapshots.Read(wordform).AlternativesFields[SnapshotFields.WfiWordformAnalyses];
}
