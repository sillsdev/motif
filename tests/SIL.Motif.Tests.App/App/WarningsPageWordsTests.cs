using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Warnings page's words against the seeded check: plain meanings and FieldWorks' names on screen, the
/// parser's own sentence only in its small quoted line, and a FieldWorks link on every object a finding names.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsPageWordsTests
{
    // The parser's and Motif's internal words, and raw warning codes such as hc-partial-morpheme.
    private static readonly Regex EngineWords = new(
        @"Parser finding|Not named by the parser|From grammar check|Findings never block|Assessment|Stored for this Baseline|" +
        @"Reload grammar|\bMSA\b|stratum|bucket|\b[a-z]+\.[a-z-]+\.[a-z-]+\b|\bhc-[a-z-]+",
        RegexOptions.CultureInvariant);

    private readonly AvaloniaHeadlessFixture _avalonia;

    public WarningsPageWordsTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public async Task TheSeededCheckShowsPlainWordsAndLinksEveryNamedObjectToFieldWorks()
    {
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse(SeededGrammarFindings.All(), HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");
        Assert.True(grammar.ShowFindings);

        _avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1240, Height = 1400 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var visible = panel.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
                    .Where(block => !block.GetVisualAncestors().OfType<WrapPanel>().Any(line => line.Classes.Contains("parserLine")))
                    .Select(block => block.Text!)
                    .ToList();
                Assert.Contains("Environment couldn't be read", visible);
                Assert.Contains("Stem with no category", visible);
                Assert.Contains("Grammar-wide", visible);
                Assert.Contains("no single place", visible);
                Assert.Contains("Check the grammar again", panel.GetVisualDescendants().OfType<Button>()
                    .Select(button => button.Content as string));
                Assert.DoesNotContain(visible, text => EngineWords.IsMatch(text));

                var links = panel.GetVisualDescendants().OfType<HyperlinkButton>()
                    .Where(link => link.Classes.Contains("warningObjectLink") && link.IsEffectivelyVisible)
                    .ToList();
                Assert.Equal(SeededGrammarFindings.LinkedSubjectCount, links.Count);
                Assert.All(links, link => Assert.Equal("silfw", link.NavigateUri!.Scheme));
                // An environment is written with "_", which a button would otherwise read as its access key.
                Assert.Contains(links.SelectMany(link => link.GetVisualDescendants().OfType<TextBlock>())
                    .Where(block => block is not AccessText), block => block.Text == "e2 (/ _ [C])");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
