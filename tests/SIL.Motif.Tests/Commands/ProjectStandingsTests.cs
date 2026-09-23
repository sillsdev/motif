using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProjectStandingsTests
{
    [Theory]
    [InlineData(0, 0, 0, false, ProjectStanding.NotPresent)]
    [InlineData(1, 3, 2, false, ProjectStanding.Approved)]
    [InlineData(0, 3, 2, false, ProjectStanding.Candidate)]
    [InlineData(0, 0, 2, false, ProjectStanding.Rejected)]
    [InlineData(1, 0, 0, true, ProjectStanding.IncorrectSpelling)]
    public void TheBestStandingWinsAndAnIncorrectSpellingOverridesAll(
        int approved, int candidates, int rejected, bool incorrectSpelling, string expected) =>
        Assert.Equal(expected, ProjectStandings.Of(approved, candidates, rejected, incorrectSpelling));
}
