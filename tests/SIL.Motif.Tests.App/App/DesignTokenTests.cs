using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Styling;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the App's three token layers: every Intent key resolves in the light and the dark theme, a theme's
/// brush is its own and not the other theme's captured at load, an Intent alias of a Semi role is that role's
/// own brush, a component style takes its colours from Intent keys and its sizes from Intent keys or its own
/// Component keys aliasing primitives, and no view names a primitive. An unknown key in a <c>DynamicResource</c> is not an
/// error — the setter silently does nothing — so resolution is checked here rather than by eye.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DesignTokenTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly ThemeVariant[] Variants = [ThemeVariant.Light, ThemeVariant.Dark];

    private readonly AvaloniaHeadlessFixture _avalonia;

    public DesignTokenTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void EveryIntentKeyResolvesInBothThemeVariants()
    {
        var keys = IntentKeys();
        Assert.Contains("Intent.Approved.Fill", keys);
        Assert.Contains("Intent.Space.Compact", keys);

        _avalonia.Invoke(() =>
        {
            foreach (var variant in Variants)
            foreach (var key in keys)
                Assert.True(Application.Current!.TryGetResource(key, variant, out var value) && value is not null,
                    $"{key} does not resolve in {variant}.");
        });
    }

    [Fact]
    public void BothThemeDictionariesDeclareTheSameIntentKeys()
    {
        var themed = ThemedIntentKeys();
        Assert.NotEmpty(themed["Light"]);
        Assert.Equal(themed["Light"].Order(), themed["Dark"].Order());
    }

    [Fact]
    public void EachThemeResolvesItsOwnBrushNotTheOtherThemesCapturedAtLoad()
    {
        var keys = ThemedIntentKeys()["Light"];

        _avalonia.Invoke(() =>
        {
            foreach (var key in keys)
            {
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out var light));
                Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var dark));
                Assert.False(ReferenceEquals(light, dark), $"{key} is one object in both themes.");
            }
        });
    }

    [Theory]
    [InlineData("Intent.TextMuted", "SemiColorText2")]
    [InlineData("Intent.Border", "SemiColorBorder")]
    [InlineData("Intent.Danger", "SemiColorDanger")]
    [InlineData("Intent.Focus", "SemiColorFocusBorder")]
    [InlineData("Intent.Surface", "SemiColorBackground0")]
    public void AnIntentAliasOfASemiRoleIsThatRolesBrushInEachTheme(string intent, string semi)
    {
        _avalonia.Invoke(() =>
        {
            foreach (var variant in Variants)
            {
                Assert.True(Application.Current!.TryGetResource(intent, variant, out var ours));
                Assert.True(Application.Current!.TryGetResource(semi, variant, out var theirs));
                Assert.Same(theirs, ours);
            }
        });
    }

    [Fact]
    public void AnIntentSpacingAliasOfSemisScaleCarriesSemisValue()
    {
        _avalonia.Invoke(() =>
        {
            Assert.True(Application.Current!.TryGetResource("Intent.Space.Compact", null, out var ours));
            Assert.True(Application.Current!.TryGetResource("SemiSpacingExtraTight", null, out var theirs));
            Assert.Equal(theirs, ours);
        });
    }

    [Fact]
    public void EveryColourAComponentStyleSetsNamesAnIntentKey()
    {
        var setters = ComponentSetters().Where(setter => ColourProperties.Contains(setter.Property)).ToList();
        Assert.NotEmpty(setters);
        Assert.All(setters, setter => Assert.True(Regex.IsMatch(setter.Value, @"^\{DynamicResource Intent\.[\w.]+\}$"),
            $"{setter.File} sets {setter.Property} to {setter.Value}; a component's colours come from Intent keys only."));
    }

    [Fact]
    public void EverySizeAComponentStyleSetsNamesAnIntentOrComponentKey()
    {
        var setters = ComponentSetters().Where(setter => SizeProperties.Contains(setter.Property)).ToList();
        Assert.NotEmpty(setters);
        Assert.All(setters, setter => Assert.True(
            Regex.IsMatch(setter.Value, @"^\{DynamicResource (Intent|Component)\.[\w.]+\}$"),
            $"{setter.File} sets {setter.Property} to {setter.Value}; a size or gap comes from an Intent or Component key."));
    }

    [Fact]
    public void AComponentFileDeclaresOnlyItsOwnKeysAndAliasesPrimitivesForThem()
    {
        var declared = 0;
        foreach (var file in ComponentFiles())
        {
            var component = Path.GetFileNameWithoutExtension(file);
            foreach (var element in XDocument.Load(file).Descendants().Where(element => element.Attribute(Xaml + "Key") is not null))
            {
                declared++;
                var key = (string)element.Attribute(Xaml + "Key")!;
                Assert.StartsWith($"Component.{component}.", key);
                Assert.Equal("StaticResource", element.Name.LocalName);
                Assert.StartsWith("Primitive.", (string?)element.Attribute("ResourceKey") ?? "");
            }
        }
        Assert.True(declared > 0, "No component file declares a Component key.");
    }

    [Fact]
    public void EveryKeyAComponentStyleNamesResolvesInBothThemeVariants()
    {
        var named = ComponentFiles().SelectMany(file => ResourceKeysNamedIn(File.ReadAllText(file))
            .Where(key => !key.StartsWith("Primitive.", StringComparison.Ordinal))).Distinct().ToList();
        Assert.Contains("Component.Sidebar.FooterMargin", named);
        Assert.Contains("Intent.Clear", named);

        _avalonia.Invoke(() =>
        {
            foreach (var variant in Variants)
            foreach (var key in named)
                Assert.True(Application.Current!.TryGetResource(key, variant, out var value) && value is not null,
                    $"{key} is missing in {variant}.");
        });
    }

    [Fact]
    public void NoViewNamesAPrimitiveKey()
    {
        var views = Path.Combine(AppDirectory(), "Views");
        var named = Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => ResourceKeysNamedIn(File.ReadAllText(path)).Select(key => (File: Path.GetFileName(path), Key: key)))
            .Where(use => use.Key.StartsWith("Primitive.", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(named);
    }

    [Fact]
    public void EveryIntentKeyAViewNamesResolves()
    {
        var keys = AppSources("*.axaml")
            .SelectMany(text => ResourceKeysNamedIn(text).Where(key => key.StartsWith("Intent.", StringComparison.Ordinal)))
            .Distinct()
            .ToList();
        Assert.Contains("Intent.Candidate.Fill", keys);

        _avalonia.Invoke(() =>
        {
            foreach (var variant in Variants)
            foreach (var key in keys)
                Assert.True(Application.Current!.TryGetResource(key, variant, out _), $"{key} is missing in {variant}.");
        });
    }

    [Fact]
    public void EveryComponentFileIsIncludedByTheApplication()
    {
        var app = File.ReadAllText(Path.Combine(AppDirectory(), "App.axaml"));
        foreach (var file in ComponentFiles())
            Assert.Contains($"Tokens/Components/{Path.GetFileName(file)}\"", app);
    }

    [Fact]
    public void NoResourceKeyKeepsTheRetiredMotifPrefix()
    {
        var retired = AppSources("*.axaml").Concat(AppSources("*.cs"))
            .SelectMany(text => Regex.Matches(text, @"(?:Resource\s+|x:Key=""|Resource\(\s*"")(Motif[A-Z]\w*)")
                .Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.Empty(retired);
    }

    [Fact]
    public void TheIntentFileHeaderListsEveryIntentKey()
    {
        var text = File.ReadAllText(IntentFile());
        var header = text[..text.IndexOf("<ResourceDictionary", StringComparison.Ordinal)];
        foreach (var key in IntentKeys())
            Assert.Matches($@"(?m)^\s*{Regex.Escape(key)}\s", header);
    }

    private static readonly HashSet<string> ColourProperties = ["Background", "Foreground", "BorderBrush", "Fill", "Stroke"];

    private static readonly HashSet<string> SizeProperties =
    [
        "Padding", "Margin", "Spacing", "FontSize", "CornerRadius", "BorderThickness", "Width", "Height", "MinWidth",
        "MinHeight", "MaxWidth", "MaxHeight", "StrokeThickness", "Opacity",
    ];

    private static IEnumerable<(string File, string Property, string Value)> ComponentSetters() =>
        ComponentFiles().SelectMany(file => XDocument.Load(file).Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => (Path.GetFileName(file), (string)element.Attribute("Property")!, (string)element.Attribute("Value")!)));

    private static IEnumerable<string> ResourceKeysNamedIn(string text) =>
        Regex.Matches(text, @"(?:Dynamic|Static)Resource\s+(?:ResourceKey=)?([\w.]+)(?!:)").Select(match => match.Groups[1].Value);

    private static List<string> IntentKeys()
    {
        var document = XDocument.Load(IntentFile());
        return document.Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .Where(key => key.StartsWith("Intent.", StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, List<string>> ThemedIntentKeys()
    {
        var document = XDocument.Load(IntentFile());
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary" &&
                (string?)element.Attribute(Xaml + "Key") is "Light" or "Dark")
            .ToDictionary(
                element => (string)element.Attribute(Xaml + "Key")!,
                element => element.Elements().Select(child => (string)child.Attribute(Xaml + "Key")!).ToList());
    }

    private static string IntentFile() => Path.Combine(AppDirectory(), "Tokens", "Intent.axaml");

    private static string[] ComponentFiles() =>
        Directory.GetFiles(Path.Combine(AppDirectory(), "Tokens", "Components"), "*.axaml");

    private static IEnumerable<string> AppSources(string pattern) =>
        Directory.EnumerateFiles(AppDirectory(), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText);

    private static string AppDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "SIL.Motif.App");
    }
}
