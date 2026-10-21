using SIL.Motif.App;
using Avalonia.Controls;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SizeMatrixDeleteStoreLookupTests
{
    [Fact]
    public void FindsTheSameActionAfterItsCaptionChanges()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var button = new Button { Name = "DeleteStoreButton", Content = "Delete this file and reopen" };
            var beforeCaptionChange = SizeMatrixScreenshots.FindStoreDeletionButton([button]);

            button.Content = MotifAppStrings.DeleteMotifsStoreAndReopen;
            var afterCaptionChange = SizeMatrixScreenshots.FindStoreDeletionButton([button]);

            Assert.Same(button, beforeCaptionChange);
            Assert.Same(beforeCaptionChange, afterCaptionChange);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}
