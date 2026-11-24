using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads a why-not report into Motif's bounded response. Fields it has no use for are left unread, and a report whose
/// shape contradicts the candidates asked is refused rather than repaired.
/// </summary>
internal static class WhyNotProjection
{
    /// <summary>What the response says when a failure carries no FieldWorks guid.</summary>
    internal const string NoFieldWorksTarget = WhyNotTarget.NoFieldWorksTarget;

    private static readonly HashSet<string> Results = ["accepted", "rejected", "unknown"];

    internal static bool TryProject(
        string report, string word, string tracedAgainst, IReadOnlyList<WhyNotCandidate> candidates,
        IReadOnlyList<string> answerableKinds, out WhyNotResponse? response, out string error)
    {
        response = null;
        try
        {
            using var document = JsonDocument.Parse(report);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "schema") != PanGlossInterfaceVersions.WhyNotSchemaId)
                return Fail($"the report's schema is not {PanGlossInterfaceVersions.WhyNotSchemaId}.", out error);
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() != candidates.Count)
                return Fail($"the report does not hold exactly {candidates.Count.ToString(CultureInfo.InvariantCulture)} result(s).", out error);
            var cut = new Cuts();
            var shown = new List<WhyNotCandidateResult>(candidates.Count);
            var index = 0;
            foreach (var result in results.EnumerateArray())
            {
                var mapped = Candidate(result, index, candidates[index].Spelling, cut, out var problem);
                if (mapped is null) return Fail($"result {index.ToString(CultureInfo.InvariantCulture)}: {problem}", out error);
                shown.Add(mapped);
                index++;
            }
            int? best = null;
            if (root.TryGetProperty("best", out var bestElement) && bestElement.ValueKind == JsonValueKind.Number)
            {
                if (!bestElement.TryGetInt32(out var value) || value < 0 || value >= shown.Count)
                    return Fail("the report's best index is outside its results.", out error);
                best = value;
            }
            response = new WhyNotResponse(word, tracedAgainst, PanGlossInterfaceVersions.WhyNotSchemaId, answerableKinds, shown,
                best, new WhyNotBound(shown.Count, cut.Morphs, cut.Sentences));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Fail("it is not valid JSON of the expected shape: " + exception.Message, out error);
        }
    }

    private sealed class Cuts
    {
        public int Morphs;
        public int Sentences;
    }

    private static WhyNotCandidateResult? Candidate(
        JsonElement result, int index, string spelling, Cuts cut, out string problem)
    {
        problem = string.Empty;
        var kind = Text(result, "result");
        if (kind is null || !Results.Contains(kind))
        {
            problem = "its result is not accepted, rejected or unknown.";
            return null;
        }
        switch (kind)
        {
            case "accepted":
            {
                WhyNotAcceptance? acceptance = null;
                if (result.TryGetProperty("analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object &&
                    analysis.TryGetProperty("analysis", out var derivation) && derivation.ValueKind == JsonValueKind.Object)
                {
                    var morphs = Morphs(derivation, "morphs", cut, out var omitted);
                    acceptance = new WhyNotAcceptance(morphs, omitted, Text(derivation, "category"));
                }
                if (acceptance is null)
                {
                    problem = "it is accepted but carries no analysis.";
                    return null;
                }
                return new WhyNotCandidateResult(index, spelling, kind, acceptance, null, null, null);
            }
            case "rejected":
            {
                if (!result.TryGetProperty("failure", out var failure) || failure.ValueKind != JsonValueKind.Object ||
                    Text(failure, "kind") is not { } failureKind || Text(failure, "plain") is not { } plain)
                {
                    problem = "it is rejected but names no failure kind and sentence.";
                    return null;
                }
                var rejection = Rejection(failure, failureKind, plain, cut);
                if (failureKind == "environment_unmet" && rejection.Environment is null && rejection.Target is null)
                {
                    problem = "it is an unmet environment but names neither the environment nor the object.";
                    return null;
                }
                return new WhyNotCandidateResult(index, spelling, kind, null, rejection, null, null);
            }
            default:
            {
                if (Text(result, "stop") is not { } stop)
                {
                    problem = "it is unknown but gives no stop reason.";
                    return null;
                }
                return new WhyNotCandidateResult(index, spelling, kind, null, null, stop,
                    Text(result, "stop_note") is { } note ? Shorten(note, cut) : null);
            }
        }
    }

    private static WhyNotRejection Rejection(JsonElement failure, string kind, string plain, Cuts cut)
    {
        WhyNotTarget? target = null;
        if (failure.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && Text(obj, "type") is { } type)
        {
            var guid = Text(obj, "guid");
            target = new WhyNotTarget(type, Text(obj, "id"), guid, guid ?? NoFieldWorksTarget);
        }
        WhyNotEnvironment? environment = null;
        if (failure.TryGetProperty("environment", out var env) && env.ValueKind == JsonValueKind.Object &&
            Text(env, "allomorph_id") is { } allomorphId)
        {
            string? left = null, right = null;
            if (failure.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object)
            {
                left = Text(context, "left") is { } l ? Tail(l) : null;
                right = Text(context, "right") is { } r ? Head(r) : null;
            }
            var guid = Text(env, "guid");
            var choices = new List<WhyNotEnvironmentChoice>();
            if (failure.TryGetProperty("environments", out var many) && many.ValueKind == JsonValueKind.Array)
                foreach (var choice in many.EnumerateArray())
                {
                    if (choices.Count == WhyNotLimits.MaxMorphs)
                    {
                        cut.Morphs++;
                        continue;
                    }
                    choices.Add(new WhyNotEnvironmentChoice(Int(choice, "index"), Text(choice, "guid"), Text(choice, "text")));
                }
            var pointer = choices.Count > 0
                ? "see environments: " + choices.Count.ToString(CultureInfo.InvariantCulture) + " targets"
                : guid ?? NoFieldWorksTarget;
            environment = new WhyNotEnvironment(allomorphId, Int(env, "index"), left, right, guid, pointer,
                Text(env, "text") is { } envText ? Shorten(envText, cut) : null, choices);
        }
        var features = new List<WhyNotFeature>();
        var featuresOmitted = 0;
        if (failure.TryGetProperty("features", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var feature in list.EnumerateArray())
            {
                if (features.Count == WhyNotLimits.MaxFeatures)
                {
                    featuresOmitted++;
                    continue;
                }
                features.Add(new WhyNotFeature(Text(feature, "path") ?? string.Empty, Text(feature, "required") ?? string.Empty,
                    Text(feature, "actual") ?? string.Empty));
            }
        var members = Morphs(failure, "members", cut, out var membersOmitted);
        return new WhyNotRejection(kind, Shorten(plain, cut), Int(failure, "position"), Text(failure, "provenance"),
            Morphs(failure, "morphs", cut, out var omitted), omitted, target, environment, Text(failure, "stem_name"),
            features, featuresOmitted, Int(failure, "alternatives"),
            members, membersOmitted, Text(failure, "gate"));
    }

    private static IReadOnlyList<WhyNotMorph> Morphs(JsonElement parent, string name, Cuts cut, out int omitted)
    {
        omitted = 0;
        var morphs = new List<WhyNotMorph>();
        if (!parent.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return morphs;
        foreach (var morph in list.EnumerateArray())
        {
            if (Text(morph, "morpheme") is not { } id) continue;
            string? entry = null, msa = null;
            if (morph.TryGetProperty("guid", out var identity) && identity.ValueKind == JsonValueKind.Object)
            {
                entry = Text(identity, "entry_guid");
                msa = Text(identity, "msa_guid");
            }
            if (morphs.Count == WhyNotLimits.MaxMorphs)
            {
                omitted++;
                cut.Morphs++;
                continue;
            }
            morphs.Add(new WhyNotMorph(id, Text(morph, "allomorph"), entry, msa, Text(morph, "form_guid")));
        }
        return morphs;
    }

    private static string Shorten(string text, Cuts cut)
    {
        if (text.Length <= WhyNotLimits.MaxSentence) return text;
        cut.Sentences++;
        var length = WhyNotLimits.MaxSentence - 1;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length] + "…";
    }

    private static string Tail(string text) =>
        text.Length <= WhyNotLimits.MaxContext ? text : "…" + text[^WhyNotLimits.MaxContext..];

    private static string Head(string text) =>
        text.Length <= WhyNotLimits.MaxContext ? text : text[..WhyNotLimits.MaxContext] + "…";

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
