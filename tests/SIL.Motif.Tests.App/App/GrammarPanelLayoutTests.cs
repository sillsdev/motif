using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GrammarPanelLayoutTests
{
    [Fact]
    public void WarningFiltersSortAndRecheckFitTheAvailablePageWidth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var grammar = new GrammarViewModel(new FakeCommandClient());
            grammar.LoadStored("grammar-layout.fwdata", new GrammarCheckResponse(
            [
                new(GrammarDiagnosticLevel.Warning, "Example warning", [], [], "Example warning")
                    { Code = "example.warning", Group = "Example warning" },
                new(GrammarDiagnosticLevel.Information, "Example information", [], [], "Example information")
                    { Code = "example.info", Group = "Example information" },
            ], true));
            var window = new Window { Content = new GrammarPanel(grammar), Width = 520, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(grammar.ShowFindings);
                LayoutAssertions.AssertCurrent(window);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}
