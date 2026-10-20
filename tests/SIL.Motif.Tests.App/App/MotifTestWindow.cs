using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using System;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A test window that is a Motif root: it merges <c>Tokens/Scoped/ControlHeights.axaml</c> into its own resources,
/// as Motif's real windows do, so hosted controls measure at Motif's heights. A bare <see cref="Window"/> is
/// outside Motif and gets Semi's defaults.
/// </summary>
internal sealed class MotifTestWindow : Window
{
    public MotifTestWindow() => MergeControlHeights(this);

    protected override Type StyleKeyOverride => typeof(Window);

    internal static void MergeControlHeights(Window window) =>
        window.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://SIL.Motif.App/"))
        {
            Source = new Uri("avares://SIL.Motif.App/Tokens/Scoped/ControlHeights.axaml"),
        });
}
