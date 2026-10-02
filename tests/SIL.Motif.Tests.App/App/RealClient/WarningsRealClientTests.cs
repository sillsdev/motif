using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class WarningsRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CheckingTheGrammarStoresTheResultOverviewThenShows()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var request = new GrammarCheckRequest(project.FwDataPath);
        var before = await project.Client.ReadStoredGrammarCheckAsync(request, CancellationToken.None);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.Null(before.Value!.Check);

        var checkedGrammar = await project.Client.CheckGrammarAsync(request, CancellationToken.None);
        Assert.True(checkedGrammar.Succeeded, checkedGrammar.Refusal?.Message);
        Assert.NotEmpty(checkedGrammar.Value!.Findings);

        var stored = await project.Client.ReadStoredGrammarCheckAsync(request, CancellationToken.None);
        var overview = await project.Client.OverviewAsync(new OverviewRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(checkedGrammar.Value.Findings.Select(finding => (finding.Code, finding.Text)),
            stored.Value!.Check!.Findings.Select(finding => (finding.Code, finding.Text)));
        Assert.Equal(checkedGrammar.Value.Findings.Count, overview.Value!.Warnings!.Count);
        Assert.Single(project.Invocations(), invocation => invocation == "grammar-health");
    }

    [Fact]
    public async Task RowsUseTheGroupAndDescriptionPanGlossReports()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var grammar = new GrammarViewModel(project.Client);

        await grammar.SetProjectAsync(project.FwDataPath);

        Assert.True(grammar.ShowFindings);
        var row = grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>().First();
        Assert.Equal(row.Warning.Group ?? string.Empty, row.PanGlossTitle);
        Assert.Equal(row.Warning.Description, row.Message);
    }
}
