using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalDocumentLayoutTests
{
    [Theory]
    [InlineData(640, 150)]
    [InlineData(1280, 100)]
    public void CommentsAndApplyRemainReachableWithLongRtlText(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = WorkspaceContextTests.NewContext(fake);
            await context.OpenProjectAsync("project.fwdata");
            var documents = new OpenProposalDocuments(context);
            var model = documents.Open("one");
            await model.RefreshAsync();
            var page = new ProposalDocument { DataContext = model };
            var window = new MotifTestWindow
            {
                Width = width, Height = 600,
                Content = new ScrollViewer { Content = new LayoutTransformControl
                    {
                        Child = page,
                        LayoutTransform = new Avalonia.Media.ScaleTransform(zoom / 100.0, zoom / 100.0),
                    }, HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            };
            try
            {
                page.FlowDirection = Avalonia.Media.FlowDirection.RightToLeft;
                window.Show();
                model.AddCommentCommand.Execute(null);
                var editor = Assert.Single(model.Comments);
                editor.Document.Paragraphs[0].Runs[0].Text = string.Concat(Enumerable.Repeat("كلمة طويلة ", 80));
                PageScreenshots.Settle(window);
                var apply = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalPrepareApply);
                apply.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.True(apply.IsEffectivelyVisible);
                var bounds = LayoutAssertions.BoundsIn(apply, window);
                Assert.True(bounds.Width > 0 && bounds.Height > 0);
                Assert.InRange(bounds.Left, -LayoutAssertions.Tolerance, width + LayoutAssertions.Tolerance);
                Assert.InRange(bounds.Right, -LayoutAssertions.Tolerance, width + LayoutAssertions.Tolerance);
                Assert.InRange(bounds.Top, -LayoutAssertions.Tolerance, window.ClientSize.Height + LayoutAssertions.Tolerance);
                Assert.InRange(bounds.Bottom, -LayoutAssertions.Tolerance, window.ClientSize.Height + LayoutAssertions.Tolerance);
                var comments = page.FindControl<ProgressiveItemsControl>("ProposalComments")!;
                comments.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.True(comments.IsEffectivelyVisible);
                var commentBounds = LayoutAssertions.BoundsIn(comments, window);
                Assert.True(commentBounds.Bottom > 0 && commentBounds.Top < window.ClientSize.Height);
                Assert.Same(editor, Assert.Single(model.Comments));
            }
            finally
            {
                window.Close();
                CancelEditors(model);
                await documents.StopAsync();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TurningDiagnosticsOffRemovesTheDocumentSlotAndRejectsHiddenContent()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = WorkspaceContextTests.NewContext(fake);
            await context.OpenProjectAsync("project.fwdata");
            var documents = new OpenProposalDocuments(context);
            var document = documents.Open("one");
            await document.RefreshAsync();
            var diagnostic = new TextBlock { Text = "Diagnostic evidence" };
            document.DiagnosticsContent = diagnostic;
            Assert.Null(document.DiagnosticsContent);
            context.Diagnostics = true;
            document.DiagnosticsContent = diagnostic;
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 700, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Contains(diagnostic, page.GetVisualDescendants());
                context.Diagnostics = false;
                PageScreenshots.Settle(window);
                Assert.Null(document.DiagnosticsContent);
                Assert.DoesNotContain(diagnostic, page.GetVisualDescendants());
                Assert.Empty(fake.ProposalParsimonyStatusRequests);
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AClosedCommentBecomesReadOnlyAndKeepsTheTypedText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
            await context.OpenProjectAsync("project.fwdata");
            var documents = new OpenProposalDocuments(context);
            var document = documents.Open("one");
            await document.RefreshAsync();
            document.AddCommentCommand.Execute(null);
            var editor = Assert.Single(document.Comments);
            editor.Document.Paragraphs[0].Runs[0].Text = "Unsaved discussion";
            var view = new RationaleEditor { DataContext = editor };
            var window = new MotifTestWindow { Width = 800, Height = 600, Content = view };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.False(view.Field!.IsReadOnly);
                document.Card = new ProposalCardViewModel(new("one", "One", "rejected", ProposalGroup.SetAside,
                    false, [], TrialStanding.Current, null, []));
                PageScreenshots.Settle(window);
                Assert.True(view.Field.IsReadOnly);
                Assert.Equal(MotifAppStrings.ProposalDeclined, document.StatusText);
                Assert.Equal("Unsaved discussion", editor.Text());
                Assert.False(editor.SaveCommand.CanExecute(null));
            }
            finally { window.Close(); await documents.StopAsync(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    private static void CancelEditors(ProposalDocumentViewModel model)
    {
        foreach (var editor in model.Comments.ToArray()) editor.CancelCommand.Execute(null);
    }
}
