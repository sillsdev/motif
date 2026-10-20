using System.Linq;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Model.AppliedLog;

namespace SIL.Motif.Commands;

/// <summary>The one check every Apply front door makes of the mode and message before it reads or changes anything.</summary>
public static class ApplyAttributionCheck
{
    /// <summary>Why <paramref name="attribution"/> cannot be applied with, or <see langword="null"/> when it can.</summary>
    public static Refusal? Refusal(ApplyAttribution? attribution)
    {
        attribution ??= new ApplyAttribution();
        if (!ApplyModes.IsKnown(attribution.Mode))
            return new Refusal(RefusalCodes.ApplyMessageInvalid, FailureReason.InvalidArgument,
                $"Apply mode '{attribution.Mode}' is not 'person' or 'autonomous'.", null);
        var message = attribution.Message?.Trim();
        if (attribution.Mode == ApplyModes.Autonomous && string.IsNullOrEmpty(message))
            return new Refusal(RefusalCodes.ApplyMessageRequired, FailureReason.InvalidArgument,
                "An autonomous Apply needs a --message saying what it did and why. Add one and apply again.", null);
        if (message is not null &&
            (message.Length > AppliedLogFormat.MaxDescriptionLength || message.Any(char.IsControl)))
            return new Refusal(RefusalCodes.ApplyMessageInvalid, FailureReason.InvalidArgument,
                $"The message must be one line of at most {AppliedLogFormat.MaxDescriptionLength} characters.", null);
        return null;
    }
}
