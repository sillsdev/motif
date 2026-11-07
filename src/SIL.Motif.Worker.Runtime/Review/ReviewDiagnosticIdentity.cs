using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Creates a locale-independent diagnostic identity from stable producer facts.</summary>
public static class ReviewDiagnosticIdentity
{
    /// <summary>Projects a stored grammar diagnostic without using localized labels as identity.</summary>
    public static ReviewDiagnostic FromGrammarWarning(GrammarWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var subjects = new List<string>();
        var qualifiers = new List<string>();
        var unresolved = warning.Subject is null;
        foreach (var part in warning.Subject ?? [])
        {
            if (part is null) { unresolved = true; continue; }
            if (part.Role == GrammarWarningPartRole.Value ||
                (part.Role == GrammarWarningPartRole.Text && part.Status == GrammarSubjectStatus.Object))
            { unresolved = true; continue; }
            if (part.Role is not (GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing)) continue;
            var id = new[] { part.SubjectGuid, part.FieldWorksGuid, part.ObjectId }
                .Select(value => Guid.TryParse(value, out var guid) ? guid : Guid.Empty)
                .FirstOrDefault(guid => guid != Guid.Empty);
            if (id == Guid.Empty) { unresolved = true; continue; }
            subjects.Add(id.ToString("D"));
            if (!string.IsNullOrWhiteSpace(part.Field)) qualifiers.Add("field:" + id.ToString("D") + ":" + part.Field);
            if (!string.IsNullOrWhiteSpace(part.SourceClass)) qualifiers.Add("class:" + id.ToString("D") + ":" + part.SourceClass);
        }
        var origin = warning.Origin switch
        {
            GrammarFindingOrigin.Check => "check", GrammarFindingOrigin.Import => "import", _ => "",
        };
        var severity = warning.Severity switch
        {
            GrammarDiagnosticLevel.Error => "error", GrammarDiagnosticLevel.Warning => "warning",
            GrammarDiagnosticLevel.Information => "info", _ => "unknown",
        };
        return new ReviewDiagnostic(null, origin, warning.Code ?? "", unresolved ? null : subjects,
            qualifiers, severity);
    }

    /// <summary>Returns false when a diagnostic's stable subject identity is unavailable.</summary>
    public static bool TryCreate(ReviewDiagnostic diagnostic, out string identity)
    {
        identity = string.Empty;
        if (string.IsNullOrWhiteSpace(diagnostic.Origin)) return false;
        if (!string.IsNullOrWhiteSpace(diagnostic.ProducerId))
        {
            identity = "producer:" + JsonSerializer.Serialize(new[] { diagnostic.Origin, diagnostic.ProducerId });
            return true;
        }
        if (string.IsNullOrWhiteSpace(diagnostic.Origin) || string.IsNullOrWhiteSpace(diagnostic.Code) ||
            diagnostic.SubjectIds is null || diagnostic.SubjectIds.Any(string.IsNullOrWhiteSpace) ||
            diagnostic.Qualifiers is null || diagnostic.Qualifiers.Any(string.IsNullOrWhiteSpace)) return false;
        var canonical = JsonSerializer.Serialize(new[] { new[] { "v1", diagnostic.Origin, diagnostic.Code },
            diagnostic.SubjectIds.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray(),
            diagnostic.Qualifiers.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray() });
        identity = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return true;
    }
}
