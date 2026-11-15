using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalParsimonyPanelTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private const string Proposal = "proposal/one";

    public static TheoryData<string, string> States => new()
    {
        { "Not Set Up", "not-set-up" }, { "Not Yet Run", "not-yet-run" }, { "Queued", "queued" },
        { "Running", "running" }, { "Ready", "ready" }, { "Out of Date", "stale" }, { "Did Not Finish", "failed" },
    };

    private static ProposalParsimonyStatus StatusFor(string name) => name switch
    {
        "not-set-up" => ProposalParsimonyStatusFixtures.NotSetUp(Proposal),
        "not-yet-run" => ProposalParsimonyStatusFixtures.NotYetRun(Proposal),
        "queued" => ProposalParsimonyStatusFixtures.Waiting(Proposal),
        "running" => ProposalParsimonyStatusFixtures.Running(Proposal),
        "ready" => ProposalParsimonyStatusFixtures.Ready(Proposal),
        "stale" => ProposalParsimonyStatusFixtures.Stale(Proposal, ProposalParsimonyStaleReason.BaselineChanged),
        _ => ProposalParsimonyStatusFixtures.Failed(Proposal, ProposalParsimonyFailureCategory.TimedOut,
            code: "status-skipped-timeout"),
    };

    [Theory]
    [MemberData(nameof(States))]
    public void RetainedComponentShowsEachStoredState(string label, string name)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalParsimonyStatusIs(StatusFor(name));
            var model = new ProposalParsimonyViewModel(fake.ReadProposalParsimonyStatusAsync,
                (_, _) => Task.FromCanceled(new CancellationToken(canceled: true)), fake.ReadParsimonyReportAsync,
                fake.ReadParsimonyViewAsync);
            var panel = new ProposalParsimonyDetails { DataContext = model };
            var window = new MotifTestWindow { Width = 500, Height = 720, Content = panel };
            try
            {
                _ = model.Watch(Project, Proposal);
                await Wait.UntilAsync(() => model.Status is not null, "The stored Parsimony status was not read.");
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal(label, panel.FindControl<TextBlock>("ParsimonyState")!.Text);
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Parsimony");
            }
            finally
            {
                model.Stop();
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }
}
