using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.HandAnalysis;

/// <summary>
/// The one place a frozen-approval Reading identity string (<c>form/msa/inflType/guess</c> joined by " + ") meets a
/// <see cref="ReadingSignature"/>. A string that does not resolve to one verified signature is refused, never guessed.
/// </summary>
public sealed class ReadingIdentityMap(ReadingSignatureReader reader)
{
    /// <summary>Resolves the string against the project to a verified signature.</summary>
    public CommandOutcome<ReadingSignature> ToSignature(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return Mismatch("The Reading identity is empty.");
        var morphs = new List<ParseMorph>();
        foreach (var part in identity.Split(" + "))
        {
            var fields = part.Split('/');
            if (fields.Length != 4) return Mismatch("A morph identity does not have form, MSA, inflection type and guess fields.");
            if (fields[3].Length > 0) return Mismatch("A guessed morph has no verified identity.");
            morphs.Add(new ParseMorph(Field(fields[0]), Field(fields[1]), Field(fields[2]), null));
        }

        return reader.ReadVerified(new ParseAnalysis(morphs)) is { } signature
            ? CommandOutcome<ReadingSignature>.Success(signature)
            : Mismatch("The morphs do not all verify against the project.");
    }

    /// <summary>Resolves the string and requires the same morphemes as the signature, ignoring senses.</summary>
    public CommandOutcome<ReadingSignature> Match(string? identity, ReadingSignature signature)
    {
        var mapped = ToSignature(identity);
        return mapped.Value is { } value && value.SenseFree() != signature.SenseFree()
            ? Mismatch("The Reading identity and the signature name different morphemes.")
            : mapped;
    }

    private static string? Field(string value) => value.Length == 0 ? null : value;

    private static CommandOutcome<ReadingSignature> Mismatch(string reason) =>
        CommandOutcome<ReadingSignature>.Refused(new Refusal(RefusalCodes.HandAnalysisIdentityMismatch,
            FailureReason.Refused, reason));
}
