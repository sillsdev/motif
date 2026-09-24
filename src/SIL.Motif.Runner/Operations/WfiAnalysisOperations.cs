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
    public const string CreateAnalysis = "analysis/wfiWordform/createAnalysis";

    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var kind in new[] { AddRefEvaluations, RemoveRefEvaluations, CreateAnalysis })
            OperationKindRegistry.Register(kind);
        OperationHandlerRegistry.Register(AddRefEvaluations, new HumanEvaluationHandler(true));
        OperationHandlerRegistry.Register(RemoveRefEvaluations, new HumanEvaluationHandler(false));
        OperationHandlerRegistry.Register(CreateAnalysis, new CreateAnalysisHandler());
    }
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
        ReferenceCollectionFieldSnapshotting.ReadAlternatives(analysis.EvaluationsRC);
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
            if (form is null && string.IsNullOrEmpty(morph.TryGetProperty("guessedString", out var guessValue)
                    ? guessValue.GetString() : null))
                throw new ContractParseException($"'{kind}' requires a form or guessed string for each morph.");
            CanonicalId? inflType = morph.TryGetProperty("inflType", out var infl) ?
                ClosedPayloadParsing.GetRequiredCanonicalId(morph, "inflType", kind) : null;
            string? guessed = morph.TryGetProperty("guessedString", out var guess) ?
                ClosedPayloadParsing.GetRequiredString(morph, "guessedString", kind) : null;
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
        ReferenceCollectionFieldSnapshotting.ReadAlternatives(wordform.AnalysesOC);
}
