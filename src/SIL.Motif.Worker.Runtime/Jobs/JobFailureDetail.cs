using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Assess;

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
            code = Code(exception),
            detail = exception.Message,
            exceptionType = exception.GetType().FullName,
            exception = exception.ToString(),
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    internal static JobFailureCategory? Category(Exception exception) => exception switch
    {
        OutOfMemoryException => JobFailureCategory.OutOfMemory,
        AssessorUnavailableException { MemoryKill: not null } => JobFailureCategory.OutOfMemory,
        AssessorUnavailableException { ParserRefusal.FailureCategory: JobFailureCategory.ParserKilled } => JobFailureCategory.ParserKilled,
        AssessorUnavailableException { ParserRefusal.FailureCategory: JobFailureCategory.OutOfMemory } => JobFailureCategory.OutOfMemory,
        _ => null,
    };

    internal static string? Code(Exception exception) => Category(exception) switch
    {
        JobFailureCategory.ParserKilled => RefusalCodes.AssessParserKilled,
        JobFailureCategory.OutOfMemory => RefusalCodes.AssessOutOfMemory,
        _ => null,
    };
}
