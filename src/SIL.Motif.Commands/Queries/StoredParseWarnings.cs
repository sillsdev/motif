using System.Text.RegularExpressions;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Preserves allomorph refusals recorded by Parse all words beside the whole-grammar check.</summary>
internal static partial class StoredParseWarnings
{
    internal static GrammarCheckResponse Merge(GrammarCheckResponse check, IEnumerable<string> lines)
    {
        var findings = new List<GrammarWarning>();
        foreach (var line in lines.Distinct(StringComparer.Ordinal))
        {
            var match = AllomorphRefusal().Match(line);
            if (!match.Success || !Guid.TryParse(match.Groups["id"].Value, out var guid)) continue;
            var form = match.Groups["form"].Value;
            var id = guid.ToString("D");
            var description = line["warning: ".Length..];
            var subject = new GrammarWarningPart(form, GrammarWarningPartRole.Object, id, "MoForm")
            {
                Title = form,
                SubjectGuid = id,
                Status = GrammarSubjectStatus.Object,
                Reach = new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [id], Spellings = [form] },
            };
            findings.Add(new GrammarWarning(GrammarDiagnosticLevel.Warning, "Parse all words", [subject], [], line)
            {
                Code = GrammarFindingCodes.ParseAllomorphUnsegmentable,
                Group = "Parse all words allomorphs",
                Title = "Parse all words · Allomorph could not be segmented",
                Description = description,
                Origin = GrammarFindingOrigin.Import,
                Scope = GrammarSubjectStatus.Object,
            });
        }
        return check with { Findings = check.Findings.Concat(findings).ToArray() };
    }

    internal static GrammarCheckResponse Merge(GrammarCheckResponse check, CurrentEvidenceSnapshot snapshot) =>
        Merge(check, (snapshot.MatchingAssessment?.Invocation?.GrammarWarningLines ?? [])
            .Concat(snapshot.RerunAssessments.SelectMany(run => run.Invocation?.GrammarWarningLines ?? [])));

    // A recorded GUID establishes the subject; a name-only stderr line cannot establish object identity.
    [GeneratedRegex("""^warning: Allomorph '(?<form>.+)' could not be segmented with this project's phonemes: allomorph "(?<id>[0-9a-fA-F-]{36})":""")]
    private static partial Regex AllomorphRefusal();
}
