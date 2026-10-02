using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;
using WordRowFact = SIL.Motif.Contract.Responses.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Warnings page's quiet rows, opened finding details and named FieldWorks links.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsPageWordsTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public WarningsPageWordsTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public async Task ARowOpensPanGlossGuidanceAndHidesItsFieldWorksLinkUntilHoverOrFocus()
    {
        var word = new ObjectUseWord(new WordRowFact("walikata", WordRowOutcome.NoParse, "Lost", WordRowTone.Problem)
        {
            Opinion = "Approved",
            Places = 2,
            PanGlossReadingAvailability = WordRowReadingAvailability.NotRequested,
        });
        var warning = new GrammarWarning(
            GrammarDiagnosticLevel.Warning,
            "Invalid environment",
            [new GrammarWarningPart("e2 (/ _ [C])", GrammarWarningPartRole.Object, "g", "PhEnvironment",
                "silfw://localhost/link?tool=EnvironmentEdit")
            {
                Status = GrammarSubjectStatus.Object,
                FieldWorksTool = "EnvironmentEdit",
                LinkStatus = FieldWorksLinkStatus.Available,
            },
            new GrammarWarningPart("missing referenced entry", GrammarWarningPartRole.Object, "g2", "LexEntry")
            {
                Status = GrammarSubjectStatus.UnresolvedReference,
                Field = "Entry",
                LinkStatus = FieldWorksLinkStatus.Unavailable,
                LinkReason = FieldWorksLinkReason.UnresolvedReference,
            },
            new GrammarWarningPart("default phonological settings", GrammarWarningPartRole.Value)
            {
                Status = GrammarSubjectStatus.ProjectSettings,
                Field = "Phonology",
                LinkStatus = FieldWorksLinkStatus.Unavailable,
                LinkReason = FieldWorksLinkReason.ProjectSettings,
            }],
            [new GrammarWarningPart("The environment cannot be read.", GrammarWarningPartRole.Text)],
            "warning: grammar.environment.invalid: the environment cannot be read")
        {
            Group = "Invalid phonological environment",
            Code = "grammar.environment.invalid",
            Title = "Environment cannot be read",
            Description = "The environment description is distinct from the producer's explanation.",
            Explanation = "PanGloss explains that this environment cannot be read by the parser.",
            Guidance = "In FieldWorks, check the named environment and the allomorphs that use it.",
            FieldWorksPlaces = [new("EnvironmentEdit", "Representation")],
            YourWords = new WarningWords(WarningWordsMatch.Identity, [word], [])
            {
                Paths = [WarningWordsPath.ThroughAllomorphs],
            },
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        _avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var controls = Assert.Single(panel.GetVisualDescendants().OfType<StackPanel>(),
                    item => item.Classes.Contains("warningControls"));
                Assert.NotEmpty(controls.Children);
                Assert.Equal(Avalonia.Layout.Orientation.Horizontal, controls.Orientation);
                Assert.Contains(panel.GetVisualDescendants().OfType<ToggleButton>(), toggle =>
                    Equals(toggle.Content, "Touch your words · 1 word"));
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "1 of your words");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "The environment description is distinct from the producer's explanation.");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "In FieldWorks, check the named environment and the allomorphs that use it.");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "PanGloss explains that this environment cannot be read by the parser.");

                var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(grammar.Warnings.Rows));
                row.ToggleOpenCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var handoff = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
                    button => ReferenceEquals(button.Command, grammar.Warnings.HandOffCommand));
                Assert.Equal("AI Handoff for this warning", handoff.Content);
                window.MouseMove(new Avalonia.Point(-100, -100));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "The environment description is distinct from the producer's explanation.");
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "In FieldWorks, check the named environment and the allomorphs that use it.");
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "PanGloss explains that this environment cannot be read by the parser.");
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "What to do in FieldWorks");
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Your words that use e2 (/ _ [C])");
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "After you fix it");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text is "PanGloss description" or "PanGloss explanation" or
                        "PanGloss guidance" or "FieldWorks places" or "Motif workflow");

                var rowBorder = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("warningRow"));
                Assert.False(rowBorder.IsPointerOver);
                Assert.False(rowBorder.IsKeyboardFocusWithin);
                var link = Assert.Single(panel.GetVisualDescendants().OfType<HyperlinkButton>(),
                    button => button.Classes.Contains("warningObjectLink"));
                Assert.Contains("revealControl", link.Classes);
                Assert.Contains("revealOnHover", link.Classes);
                Assert.Contains("revealLink", link.Classes);
                Assert.Equal(0, link.Opacity);
                Assert.False(link.IsHitTestVisible);

                var hoverTitle = Assert.Single(panel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Classes.Contains("warningHoverTitle"));
                Assert.False(hoverTitle.IsEffectivelyVisible);
                var hoverPoint = rowBorder.TranslatePoint(
                    new Avalonia.Point(rowBorder.Bounds.Width / 2, rowBorder.Bounds.Height / 2), window)!.Value;
                window.MouseMove(hoverPoint);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(1, link.Opacity);
                Assert.True(link.IsHitTestVisible);
                Assert.True(hoverTitle.IsEffectivelyVisible);

                window.MouseMove(new Avalonia.Point(window.Width - 5, window.Height - 5));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(0, link.Opacity);
                Assert.False(link.IsHitTestVisible);
                Assert.False(hoverTitle.IsEffectivelyVisible);
                link.Focus(NavigationMethod.Tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(1, link.Opacity);
                Assert.True(link.IsHitTestVisible);
                Assert.True(hoverTitle.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task OpeningANarrowWarningShowsTheCompleteDescriptionAndSeparateProblemParts()
    {
        var description = "PanGloss inspected the named template. " + new string('x', 360) +
            " The failing field is the required person-number feature at the end of this description.";
        var problem = "Person-number feature is missing; this is PanGloss problem evidence.";
        var warning = new GrammarWarning(
            GrammarDiagnosticLevel.Error,
            "Incomplete template",
            [new GrammarWarningPart("Verb template", GrammarWarningPartRole.Object,
                "77777777-7777-7777-7777-777777777777", "MoInflAffixTemplate",
                "silfw://localhost/link?tool=InflAffixTemplateEdit")],
            [new GrammarWarningPart(problem, GrammarWarningPartRole.Text)],
            "error: grammar.template.incomplete")
        {
            Code = "grammar.template.incomplete",
            Group = "Incomplete template",
            Description = description,
            Guidance = null,
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync("/tmp/warnings-long-description.fwdata");

        _avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(grammar.Warnings.Rows));
                Assert.Null(row.Warning.Guidance);
                Assert.Equal(description, row.Message);
                Assert.Equal([problem], row.ProblemParts.Select(part => part.Text));
                row.ToggleOpenCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var fullDescription = Assert.Single(panel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Classes.Contains("warningFullDescription"));
                Assert.True(fullDescription.IsEffectivelyVisible);
                Assert.Equal(description, fullDescription.Text);
                Assert.Equal(TextWrapping.Wrap, fullDescription.TextWrapping);
                Assert.True(fullDescription.Bounds.Height > 30);
                Assert.Contains(panel.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Classes.Contains("warningPart") && text.Text == problem);
                Assert.True(row.HasProblemParts);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
