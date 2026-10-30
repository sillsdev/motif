using System.Text.Json;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Builds a failed job's <c>ResultJson</c>: the user-facing message plus the exception's type and stack for diagnosis.</summary>
internal static class JobFailureDetail
{
    /// <summary>
    /// Returns JSON whose <c>detail</c> is <paramref name="exception"/>'s message (unchanged for users), with
    /// <c>exceptionType</c> and the full <c>exception</c> text (type, message, stack, inner exceptions) beside it.
    /// </summary>
    public static string Serialize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return JsonSerializer.Serialize(new
        {
            detail = exception.Message,
            exceptionType = exception.GetType().FullName,
            exception = exception.ToString(),
        });
    }
}
