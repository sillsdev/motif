using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Explains a recorded refusal without inferring an unrecorded character or grammar repair.</summary>
public static class ParserRefusals
{
    public const string Title = "PanGloss can't read it";
    public const string ListExplanation = "PanGloss refused these words; each word carries its recorded reason.";
    public static ParserRefusal? Of(ParseWordEvidence? morphology, string? outcome = null) =>
        morphology?.Refusal ?? (morphology?.InvalidShape == true
            ? InvalidShape
            : outcome == "skipped" ? new("skipped", "PanGloss refused this word; no reason was recorded.") : null);

    public static ParserRefusal InvalidShape { get; } = new("invalidShape",
        "PanGloss can't read this word with this project's phonemes (invalidShape).");
}
