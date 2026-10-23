using Avalonia;
using Avalonia.Controls;
using SIL.Motif.App;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WindowZoomPolicyTests
{
    [Fact]
    public void ZoomStepsMutateTheSharedTransformWithoutAnAppWideResourceChange()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var app = Application.Current!;
            try
            {
                WindowZoomPolicy.Apply(app, ZoomPolicy.DefaultPercent);
                Assert.Same(WindowZoomPolicy.Transform, app.Resources[WindowZoomPolicy.TransformResourceKey]);
                var resourceChanges = 0;
                ((Avalonia.Controls.IResourceHost)app).ResourcesChanged += (_, _) => resourceChanges++;
                var host = new LayoutTransformControl
                {
                    LayoutTransform = WindowZoomPolicy.Transform,
                    Child = new Border { Width = 100, Height = 40 },
                };

                var window = new Window { Width = 1000, Height = 800, Content = host };
                window.Show();
                foreach (var percent in new[] { 80, 90, 110, 125, 150, ZoomPolicy.DefaultPercent })
                {
                    WindowZoomPolicy.Apply(app, percent);
                    window.UpdateLayout();
                    Assert.Equal(percent / 100d, WindowZoomPolicy.Transform.ScaleX);
                    Assert.Equal(percent / 100d, WindowZoomPolicy.Transform.ScaleY);
                    Assert.InRange(host.DesiredSize.Width, percent - 1.5, percent + 1.5);
                    Assert.Same(WindowZoomPolicy.Transform, app.Resources[WindowZoomPolicy.TransformResourceKey]);
                }

                window.Close();
                Assert.Equal(0, resourceChanges);
            }
            finally
            {
                WindowZoomPolicy.Apply(app, ZoomPolicy.DefaultPercent);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}
