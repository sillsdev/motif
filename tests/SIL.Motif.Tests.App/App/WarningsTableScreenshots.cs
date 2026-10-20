using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsTableScreenshots
{
    [ScreenshotFact]
    public void CaptureWarningsAtFourWidths()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var grammar = new GrammarViewModel(new FakeCommandClient());
            var warnings = new[]
            {
                new GrammarWarning(GrammarDiagnosticLevel.Error, "Unreadable environment",
                    [new("environment", GrammarWarningPartRole.Text),
                     new("e2 (/ _ [C])", GrammarWarningPartRole.Object, "11111111-1111-1111-1111-111111111111", "PhEnvironment")],
                    [new("cannot be read", GrammarWarningPartRole.Text)], "error: environment cannot be read")
                {
                    Code = "grammar.environment.unreadable",
                    Group = "Unreadable environment",
                    Description = "PanGloss cannot read this environment.",
                },
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unreachable phonological rule",
                    [new("phonological rule", GrammarWarningPartRole.Text),
                     new("C -> V", GrammarWarningPartRole.Object, "22222222-2222-2222-2222-222222222222", "PhPhonRule")],
                    [new("is not reached", GrammarWarningPartRole.Text)], "warning: rule is not reached")
                {
                    Code = "grammar.rule.unreachable",
                    Group = "Unreachable rule",
                    Description = "PanGloss did not reach this rule.",
                },
                new GrammarWarning(GrammarDiagnosticLevel.Information, "Unused affix",
                    [new("affix", GrammarWarningPartRole.Text),
                     new("-pa", GrammarWarningPartRole.Object, "33333333-3333-3333-3333-333333333333", "MoAffixForm")],
                    [new("is not used", GrammarWarningPartRole.Text)], "info: affix is not used")
                {
                    Code = "grammar.affix.unused",
                    Group = "Unused affix",
                    Description = "PanGloss did not use this affix.",
                },
            };
            grammar.LoadStored("warnings-capture.fwdata", new GrammarCheckResponse(warnings, HasBaseline: true));
            grammar.Warnings.MostYourWordsFirst = false;
            var panel = new GrammarPanel(grammar);
            var window = new MotifTestWindow { Content = panel, Width = 640, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var selected = grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>().First();
                grammar.Warnings.OpenDetailsCommand.Execute(selected);
                PageScreenshots.Settle(window);

                foreach (var width in new[] { 640, 840, 1040, 1240 })
                {
                    window.Width = width;
                    PageScreenshots.Settle(window);
                    Assert.True(panel.Bounds.Width <= width);
                    var path = Path.Combine(folder, $"warnings-table-{width}.png");
                    using var frame = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException($"No frame rendered for {path}.");
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(2));
    }
}
