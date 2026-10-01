using System.Text.RegularExpressions;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// Pins that a refusal the real commands write about an Assessment reaches the window in the window's words, with
/// the facts a problem report needs kept beneath them.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed partial class WindowRefusalRealClientTests(PristineProjectFixture pristine)
{
    [GeneratedRegex(@"\bassess\w*", RegexOptions.IgnoreCase)]
    private static partial Regex CliWord();

    [Fact]
    public async Task AMissingAssessmentRefusalKeepsItsIdButNotTheCommandsWords()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var loaded = await project.Client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);

        var refused = await project.Client.AcceptNewSetAsync(new AcceptNewSetRequest(project.FwDataPath,
            MotifProductVersion.CurrentText, loaded.Value!.Revision, "assessment/never-recorded", Selection: true),
            CancellationToken.None);

        Assert.Equal("change.assessment-missing", refused.Refusal?.Code);
        Assert.Contains("Assessment", refused.Refusal!.Message, StringComparison.Ordinal);
        var shown = WindowRefusal.From(refused.Refusal);
        var fact = "assessmentId: assessment/never-recorded";
        Assert.DoesNotMatch(CliWord(), shown.Sentence);
        Assert.Contains(fact, shown.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotMatch(CliWord(), (shown.Details ?? string.Empty).Replace(fact, string.Empty, StringComparison.Ordinal));
    }
}
