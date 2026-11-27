using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli;

/// <summary>Reads the sibling flags of <c>trial</c>.</summary>
internal static class TrialSiblingsCli
{
    private static readonly string[] Options = ["siblings", "corpus-types", "siblings-max", "siblings-pattern"];

    /// <summary>
    /// The request when siblings are asked for, null when they are not or when <paramref name="refusal"/> is set. Any sibling
    /// option asks for siblings, and siblings need a corpus-types file.
    /// </summary>
    internal static TrialSiblingsRequest? Read(IReadOnlyDictionary<string, string> flags, out Refusal? refusal)
    {
        refusal = null;
        var asked = Options.Any(flags.ContainsKey);
        if (flags.ContainsKey("no-siblings"))
        {
            if (asked) refusal = Invalid("--no-siblings opts out of siblings and cannot be combined with " +
                string.Join(", ", Options.Where(flags.ContainsKey).Select(name => "--" + name)) + ".");
            return null;
        }
        if (!asked) return null;
        var path = flags.GetValueOrDefault("corpus-types");
        if (path is null or "" or "true")
        {
            refusal = Invalid("Siblings are sampled from a corpus-types file, and none was given. " +
                "Next: pass --corpus-types <file>, or leave the sibling options out.");
            return null;
        }
        var max = TrialSiblingSampler.DefaultMax;
        if (flags.TryGetValue("siblings-max", out var text) &&
            (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out max) || max < 1))
        {
            refusal = Invalid("--siblings-max must be a whole number of one or more.");
            return null;
        }
        var pattern = flags.GetValueOrDefault("siblings-pattern");
        if (pattern is "true" or "")
        {
            refusal = Invalid("--siblings-pattern needs a regular expression.");
            return null;
        }
        return new TrialSiblingsRequest(path, max, pattern);
    }

    private static Refusal Invalid(string message) => new(RefusalCodes.TrialSiblingsInvalid, FailureReason.InvalidArgument, message);
}
