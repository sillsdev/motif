using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class WhyNotRenderer
{
    public static string Render(WhyNotResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Why not, for {response.Word} against {response.TracedAgainst}: {response.Candidates.Count} candidate(s).");
        text.AppendLine(response.AnswerableKinds.Count == 0
            ? "This PanGloss declares no failure kinds it can answer, so every failure comes back unknown."
            : "This PanGloss can name these failure kinds: " + string.Join(", ", response.AnswerableKinds) +
              ". Any other failure comes back unknown.");
        foreach (var candidate in response.Candidates)
        {
            text.AppendLine();
            text.AppendLine($"{(candidate.Index + 1).ToString(CultureInfo.InvariantCulture)}. {candidate.Candidate}: {candidate.Result}" +
                (response.Best == candidate.Index ? " (best)" : ""));
            if (candidate.Acceptance is { } accepted) Accepted(text, accepted);
            if (candidate.Rejection is { } rejection) Rejected(text, rejection);
            if (candidate.Stop is { } stop)
                text.AppendLine($"   Could not tell ({stop}).{(candidate.StopNote is { } note ? " " + note : "")}");
        }
        if (response.Bound.MorphsOmitted > 0 || response.Bound.SentencesShortened > 0)
            text.AppendLine($"\nLeft out here: {N(response.Bound.MorphsOmitted)} morph(s), {N(response.Bound.SentencesShortened)} sentence(s) cut short; see --json.");
        return text.ToString();
    }

    private static void Accepted(StringBuilder text, WhyNotAcceptance accepted)
    {
        if (accepted.Morphs.Count == 0) return;
        text.AppendLine("   Parses as " + Morphs(accepted.Morphs, accepted.MorphsOmitted) +
            (accepted.Category is { } category ? $", category {category}." : "."));
    }

    private static void Rejected(StringBuilder text, WhyNotRejection rejection)
    {
        var place = rejection.Position is { } position ? $" at position {position.ToString(CultureInfo.InvariantCulture)}" : " about the whole word";
        text.AppendLine($"   {rejection.Kind}{place}" + (rejection.Provenance is { } provenance ? $" ({provenance})." : "."));
        text.AppendLine("   " + rejection.Plain);
        if (rejection.Gate is { } gate) text.AppendLine("   Gate: " + gate + ".");
        if (rejection.Morphs.Count > 0) text.AppendLine("   Morphs: " + Morphs(rejection.Morphs, rejection.MorphsOmitted));
        foreach (var morph in rejection.Morphs.Where(m => m.EntryGuid is not null || m.MsaGuid is not null || m.FormGuid is not null))
            text.AppendLine($"   FieldWorks {morph.Morpheme}: {Guids(morph)}.");
        if (rejection.Target is { } target)
            text.AppendLine($"   Object: {target.Type} {target.Id ?? "(no grammar id)"}; FieldWorks: {target.FieldWorksTarget}" +
                (target.FieldWorksGuid is null && target.Type == "rule" ? " (the parser could not tell which rule fired)" : "") + ".");
        if (rejection.Environment is { } environment) Environment(text, environment);
        if (rejection.Members is { Count: > 0 } members)
        {
            text.AppendLine("   The prohibition names:");
            foreach (var member in members)
                text.AppendLine($"     {Morphs([member], 0)}; FieldWorks: {Guids(member)}.");
            if (rejection.MembersOmitted > 0) text.AppendLine($"     and {N(rejection.MembersOmitted)} more member(s); see --json.");
        }
        if (rejection.StemName is { } stemName) text.AppendLine("   Stem name: " + stemName + ".");
        foreach (var feature in rejection.Features)
            text.AppendLine($"   Feature {feature.Path}: required {feature.Required}, had {feature.Actual}.");
        if (rejection.FeaturesOmitted > 0) text.AppendLine($"   and {N(rejection.FeaturesOmitted)} more feature clash(es); see --json.");
        if (rejection.Alternatives is { } alternatives)
            text.AppendLine($"   {alternatives.ToString(CultureInfo.InvariantCulture)} other derivation(s) of this sequence also fail.");
    }

    private static void Environment(StringBuilder text, WhyNotEnvironment environment)
    {
        if (environment.Choices is { Count: > 0 } choices)
        {
            text.AppendLine($"   None of the {N(choices.Count)} environments of allomorph {environment.AllomorphId} is met:");
            foreach (var choice in choices)
                text.AppendLine($"     environment{(choice.Index is { } at ? " " + at.ToString(CultureInfo.InvariantCulture) : "")}" +
                    $" {choice.Text ?? "(no text)"}; FieldWorks: {choice.FieldWorksGuid ?? WhyNotTarget.NoFieldWorksTarget}.");
            return;
        }
        text.AppendLine($"   Environment{(environment.Index is { } index ? " " + index.ToString(CultureInfo.InvariantCulture) : "")}" +
            (environment.Text is { } authored ? " " + authored : "") +
            $" of allomorph {environment.AllomorphId}" +
            (environment.Left is not null || environment.Right is not null
                ? $", checked around [{environment.Left}|{environment.Right}]" : "") +
            $"; FieldWorks: {environment.FieldWorksTarget}.");
    }

    private static string Guids(WhyNotMorph morph)
    {
        var parts = new List<string>();
        if (morph.EntryGuid is { } entry) parts.Add("entry " + entry);
        if (morph.MsaGuid is { } msa) parts.Add("analysis " + msa);
        if (morph.FormGuid is { } form) parts.Add("form " + form);
        return parts.Count == 0 ? WhyNotTarget.NoFieldWorksTarget : string.Join(", ", parts);
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Morphs(IReadOnlyList<WhyNotMorph> morphs, int omitted) =>
        string.Join(" + ", morphs.Select(m => m.Allomorph is null ? m.Morpheme : $"{m.Morpheme} ({m.Allomorph})")) +
        (omitted > 0 ? $" and {N(omitted)} more" : "");
}
