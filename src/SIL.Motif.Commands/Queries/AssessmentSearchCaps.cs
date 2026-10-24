using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Commands.Queries;

internal static class AssessmentSearchCaps
{
    internal static bool Match(StepCap before, StepCap after) =>
        StringComparer.Ordinal.Equals(before.ToArgument(), after.ToArgument()) &&
        StringComparer.Ordinal.Equals(before.ToWorkCapArgument(), after.ToWorkCapArgument());
}
