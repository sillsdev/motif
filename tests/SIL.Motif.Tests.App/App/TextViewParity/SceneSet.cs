using SIL.Motif.Tests.App;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// One window per sample for a whole test class: the first test to ask opens it, every test gets it reset,
/// and the class's end closes it. xUnit builds a new test object per test, so the windows live here.
/// </summary>
public abstract class SceneSet : IDisposable
{
    private readonly Dictionary<string, TextViewScene> _scenes = new();

    internal abstract TextViewScene Open(TextViewSample sample);

    /// <summary>The reset scene for the sample called <paramref name="name"/>; call on the Avalonia thread.</summary>
    internal TextViewScene Get(string name)
    {
        if (!_scenes.TryGetValue(name, out var scene))
            _scenes[name] = scene = Open(TextViewSample.Named(name));
        scene.Reset();
        return scene;
    }

    public void Dispose() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        foreach (var scene in _scenes.Values) scene.Dispose();
        _scenes.Clear();
    });
}

/// <summary>A reader line of four words, wide enough for three of them on its first row.</summary>
public sealed class LineScenes : SceneSet
{
    internal override TextViewScene Open(TextViewSample sample) => TextViewScene.Line(sample, 4, 1100);
}

/// <summary>One word strip alone in its window.</summary>
public sealed class WordScenes : SceneSet
{
    internal override TextViewScene Open(TextViewSample sample) => TextViewScene.SingleWord(sample);
}

/// <summary>One word card alone in its window.</summary>
public sealed class CardScenes : SceneSet
{
    internal override TextViewScene Open(TextViewSample sample) => TextViewScene.Card(sample);
}
