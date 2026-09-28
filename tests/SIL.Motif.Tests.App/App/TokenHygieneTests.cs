using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the design-token gate that <c>build.ps1</c> runs: each kind of raw styling value fails in the layer that
/// forbids it, the forms that are not styling pass, and an allowlist entry excuses only what it names.
/// </summary>
public sealed class TokenHygieneTests
{
    private const string View = "src/SIL.Motif.App/Views/Sample.axaml";
    private const string ViewCode = "src/SIL.Motif.App/Views/Sample.cs";
    private const string Component = "src/SIL.Motif.App/Tokens/Components/Sample.axaml";
    private const string Intent = "src/SIL.Motif.App/Tokens/Intent.axaml";
    private const string Primitives = "src/SIL.Motif.App/Tokens/Primitives.axaml";

    [Theory]
    [InlineData("Margin=\"12,0,0,0\"")]
    [InlineData("Padding=\"10,4\"")]
    [InlineData("Spacing=\"8\"")]
    [InlineData("RowSpacing=\"8\"")]
    [InlineData("ColumnSpacing=\"8\"")]
    [InlineData("FontSize=\"13\"")]
    [InlineData("CornerRadius=\"6\"")]
    [InlineData("BorderThickness=\"1\"")]
    [InlineData("StrokeThickness=\"1.5\"")]
    [InlineData("Width=\"28\"")]
    [InlineData("Height=\"80\"")]
    [InlineData("MinWidth=\"260\"")]
    [InlineData("MinHeight=\"260\"")]
    [InlineData("MaxWidth=\"260\"")]
    [InlineData("MaxHeight=\"260\"")]
    [InlineData("ColumnDefinitions=\"*,340\"")]
    [InlineData("RowDefinitions=\"Auto,24,*\"")]
    public void ALiteralSizeInAViewFails(string attribute)
    {
        Assert.Equal(["literal-size"], Categories(View, Element(attribute)));
        Assert.Equal(["literal-size"], Categories(View, Setter(attribute)));
    }

    [Theory]
    [InlineData("Background=\"Transparent\"")]
    [InlineData("Foreground=\"White\"")]
    [InlineData("BorderBrush=\"#DDD\"")]
    [InlineData("Fill=\"#FF8800\"")]
    [InlineData("Stroke=\"Red\"")]
    public void ALiteralColourInAViewFails(string attribute)
    {
        Assert.Equal(["literal-colour"], Categories(View, Element(attribute)));
        Assert.Equal(["literal-colour"], Categories(View, Setter(attribute)));
    }

    [Fact]
    public void AHexColourOnAnyPropertyFails() =>
        Assert.Equal(["literal-colour"], Categories(View, Element("Tag=\"#12AB34\"")));

    [Fact]
    public void ABrushWrittenAsAPropertyElementFails()
    {
        var xaml = Document("<Border><Border.Background><SolidColorBrush Color=\"#FFF\" /></Border.Background></Border>");
        Assert.Equal(["literal-colour"], Categories(View, xaml));
    }

    [Fact]
    public void ARawValueDeclaredAsAViewResourceFails()
    {
        var xaml = Document("<Border><Border.Resources><Thickness x:Key=\"Gap\">4</Thickness></Border.Resources></Border>");
        Assert.Equal(["literal-size"], Categories(View, xaml));
    }

    [Fact]
    public void ARawValueResourceIsReportedAsWritten()
    {
        var xaml = Styles("<Styles.Resources><Thickness x:Key=\"Component.Sample.Gap\">4,3</Thickness></Styles.Resources>");
        Assert.Equal("<Thickness>4,3</Thickness>", Assert.Single(TokenHygiene.ScanFile(Component, xaml)).Detail);
    }

    [Theory]
    [InlineData("Margin=\"{DynamicResource Primitive.Inset.0_0_8_0}\"")]
    [InlineData("Tag=\"{StaticResource Primitive.Space.4}\"")]
    public void AViewNamingAPrimitiveFails(string attribute) =>
        Assert.Equal(["primitive-in-view"], Categories(View, Element(attribute)));

    [Theory]
    [InlineData("Foreground=\"{DynamicResource SemiColorText2}\"")]
    [InlineData("Margin=\"{DynamicResource SemiSpacingTight}\"")]
    public void AViewNamingASemiKeyForStylingFails(string attribute) =>
        Assert.Equal(["wrong-layer"], Categories(View, Element(attribute)));

    [Theory]
    [InlineData("Foreground=\"{StaticResource Intent.Text}\"")]
    [InlineData("Background=\"{StaticResource Component.Card.Fill}\"")]
    public void AViewTakingAColourByStaticResourceFails(string attribute) =>
        Assert.Equal(["wrong-layer"], Categories(View, Element(attribute)));

    [Fact]
    public void AStaticValueOnAStylingPropertyInAViewFails() =>
        Assert.Equal(["not-a-token"], Categories(View, Element("Width=\"{x:Static local:Sizes.Wide}\"")));

    [Theory]
    [InlineData("Margin=\"0\"")]
    [InlineData("Padding=\"0,0,0,0\"")]
    [InlineData("ColumnDefinitions=\"Auto,*,2*,0.5*\"")]
    [InlineData("Width=\"Auto\"")]
    [InlineData("Background=\"{DynamicResource Intent.Surface}\"")]
    [InlineData("Margin=\"{DynamicResource Component.Card.Padding}\"")]
    [InlineData("FontSize=\"{StaticResource Intent.Type.Body}\"")]
    [InlineData("Width=\"{Binding ColumnWidth}\"")]
    [InlineData("Foreground=\"{TemplateBinding Foreground}\"")]
    [InlineData("Background=\"{x:Null}\"")]
    [InlineData("Theme=\"{DynamicResource BorderlessButton}\"")]
    [InlineData("Data=\"M 0 0 L 24 24 Z\"")]
    [InlineData("Grid.Column=\"2\"")]
    [InlineData("Opacity=\"0.5\"")]
    public void WhatIsNotALiteralStylingValuePassesInAView(string attribute) =>
        Assert.Empty(Categories(View, Element(attribute)));

    [Fact]
    public void AnXmlCommentQuotingABannedValueIsNotScanned() =>
        Assert.Empty(Categories(View, Document("<!-- Margin=\"12\" Background=\"White\" --><Border />")));

    [Theory]
    [InlineData("var brush = new SolidColorBrush(Color.FromRgb(1, 2, 3));", "literal-colour")]
    [InlineData("block.Foreground = Brushes.White;", "literal-colour")]
    [InlineData("var fill = Color.Parse(\"#C62828\");", "literal-colour")]
    [InlineData("var hex = \"#FFAA00\";", "literal-colour")]
    [InlineData("Margin = new Thickness(0, 0, 6, 0),", "literal-size")]
    [InlineData("CornerRadius = new CornerRadius(3) };", "literal-size")]
    [InlineData("var bar = new Grid { Height = 14 };", "literal-size")]
    [InlineData("Spacing = 8;", "literal-size")]
    [InlineData("FontSize = 12.5,", "literal-size")]
    [InlineData("style.Setters.Add(new Setter(Border.PaddingProperty, 4));", "literal-size")]
    [InlineData("var column = new GridLength(160);", "literal-size")]
    [InlineData("Padding = new Thickness(0, 0, last ? 0 : 12, 0),", "literal-size")]
    [InlineData("var radius = new CornerRadius(compact ? 2 : 3);", "literal-size")]
    [InlineData("var bar = new Grid { Height = ShowLegend ? 14 : 10, ClipToBounds = true };", "literal-size")]
    [InlineData("FontSize = wide ? size : 12;", "literal-size")]
    [InlineData("Width = 12 + 8;", "literal-size")]
    [InlineData("var bar = new Grid { Height = baseline * 2, ClipToBounds = true };", "literal-size")]
    [InlineData("FontSize = SizeFor(12),", "literal-size")]
    [InlineData("Margin = new Thickness(Math.Max(4, inset));", "literal-size")]
    [InlineData("var radius = new CornerRadius(Math.Min(radius, 3));", "literal-size")]
    [InlineData("var stop = new GradientStop { Color = new Color(255, 255, 0, 0) };", "literal-colour")]
    [InlineData("var tint = Color.FromHsv(120, 0.5, 0.5);", "literal-colour")]
    [InlineData("var column = new GridLength(wide ? 220 : 160);", "literal-size")]
    [InlineData("this.GetResourceObservable(\"Primitive.Space.4\");", "primitive-in-view")]
    [InlineData("this.GetResourceObservable(\"SemiColorText2\");", "wrong-layer")]
    public void AVisualLiteralInViewCodeFails(string line, string category) =>
        Assert.Contains(category, Categories(ViewCode, line));

    [Theory]
    [InlineData("var column = new StackPanel { Spacing = 0 };")]
    [InlineData("Padding = new Thickness(0),")]
    [InlineData("var share = new GridLength(2, GridUnitType.Star);")]
    [InlineData("var share = new GridLength(count, GridUnitType.Star);")]
    [InlineData("var inset = new Thickness(0, 0, last ? 0 : gap, 0);")]
    [InlineData("Width = collapsed ? 0 : available;")]
    [InlineData("var open = IsOpen ? 1 : 0;")]
    [InlineData("if (e.NewSize.Width < 760) Compact();")]
    [InlineData("if (Width == 14) return;")]
    [InlineData("Width = Math.Max(bounds.Width, MinWidth);")]
    [InlineData("var x = Width * 2;")]
    [InlineData("// Margin = new Thickness(0, 0, 6, 0) is what the design shows.")]
    [InlineData("/* Brushes.White */ var ok = true;")]
    [InlineData("var url = \"https://example.org/#FFAA00x\"; // not a colour")]
    [InlineData("this.GetResourceObservable(\"Intent.Candidate.Fill\");")]
    [InlineData("Height = Math.Max(0, available - used);")]
    [InlineData("var width = Bounds.Width - 2 * r;")]
    [InlineData("public double Width => Bounds.Width;")]
    [InlineData("Padding = new Thickness(Math.Max(0, inset));")]
    [InlineData("var share = new GridLength(Math.Max(1, count), GridUnitType.Star);")]
    public void ViewCodeWithoutAVisualLiteralPasses(string line) =>
        Assert.Empty(Categories(ViewCode, line));

    [Fact]
    public void AMultiLineBlockCommentInViewCodeIsNotScanned() =>
        Assert.Empty(Categories(ViewCode, "/*\n Brushes.White\n Spacing = 8;\n*/\nvar ok = true;"));

    [Theory]
    [InlineData("<Setter Property=\"Background\" Value=\"Transparent\" />", "literal-colour")]
    [InlineData("<Setter Property=\"Background\" Value=\"{DynamicResource Primitive.Color.Blue50}\" />", "wrong-layer")]
    [InlineData("<Setter Property=\"Background\" Value=\"{StaticResource Intent.Surface}\" />", "wrong-layer")]
    [InlineData("<Setter Property=\"Foreground\" Value=\"{DynamicResource SemiColorText2}\" />", "wrong-layer")]
    [InlineData("<Setter Property=\"Margin\" Value=\"4\" />", "literal-size")]
    [InlineData("<Setter Property=\"FontSize\" Value=\"{DynamicResource SemiFontSizeSmall}\" />", "wrong-layer")]
    public void AComponentStyleWithARawOrForeignValueFails(string setter, string category) =>
        Assert.Equal([category], Categories(Component, Styles($"<Style Selector=\"Border.sample\">{setter}</Style>")));

    [Fact]
    public void AComponentKeyAliasingAPrimitiveColourFails() =>
        Assert.Equal(["wrong-layer"], Categories(Component,
            Styles("<Styles.Resources><StaticResource x:Key=\"Component.Sample.Ink\" ResourceKey=\"Primitive.Color.Blue50\" /></Styles.Resources>")));

    [Theory]
    [InlineData("<Setter Property=\"Background\" Value=\"{DynamicResource Intent.Surface}\" />")]
    [InlineData("<Setter Property=\"Margin\" Value=\"{DynamicResource Primitive.Inset.0_0_8_0}\" />")]
    [InlineData("<Setter Property=\"Margin\" Value=\"{DynamicResource Component.Sample.Gap}\" />")]
    [InlineData("<Setter Property=\"FontSize\" Value=\"{DynamicResource Intent.Type.Body}\" />")]
    [InlineData("<Setter Property=\"HorizontalAlignment\" Value=\"Right\" />")]
    public void AComponentStyleFromTokensPasses(string setter) =>
        Assert.Empty(Categories(Component, Styles($"<Style Selector=\"Border.sample\">{setter}</Style>")));

    [Fact]
    public void AComponentKeyAliasingAPrimitiveSizePasses() =>
        Assert.Empty(Categories(Component,
            Styles("<Styles.Resources><StaticResource x:Key=\"Component.Sample.Gap\" ResourceKey=\"Primitive.Space.4\" /></Styles.Resources>")));

    [Theory]
    [InlineData("<SolidColorBrush x:Key=\"Intent.Sample\" Color=\"#FFFFFF\" />", "literal-value")]
    [InlineData("<x:Double x:Key=\"Intent.Sample\">4</x:Double>", "literal-value")]
    [InlineData("<StaticResource x:Key=\"Intent.Sample\" ResourceKey=\"Intent.Surface\" />", "wrong-layer")]
    [InlineData("<StaticResource x:Key=\"Intent.Sample\" ResourceKey=\"Component.Card.Padding\" />", "wrong-layer")]
    [InlineData("<SolidColorBrush x:Key=\"Intent.Sample\" Color=\"{StaticResource Component.Card.Ink}\" />", "wrong-layer")]
    public void AnIntentKeyWithARawValueOrAHigherLayerFails(string declaration, string category) =>
        Assert.Equal([category], Categories(Intent, Dictionary(declaration)));

    [Theory]
    [InlineData("<StaticResource x:Key=\"Intent.Sample\" ResourceKey=\"Primitive.Space.4\" />")]
    [InlineData("<StaticResource x:Key=\"Intent.Sample\" ResourceKey=\"SemiColorText2\" />")]
    [InlineData("<SolidColorBrush x:Key=\"Intent.Sample\" Color=\"{StaticResource Primitive.Color.Cyan50}\" />")]
    public void AnIntentKeyNamingAPrimitiveOrASemiRolePasses(string declaration) =>
        Assert.Empty(Categories(Intent, Dictionary(declaration)));

    [Fact]
    public void ThePrimitiveFileMayHoldRawValues() =>
        Assert.Empty(Categories(Primitives, Dictionary("<Color x:Key=\"Primitive.Color.Red\">#FF0000</Color><x:Double x:Key=\"Primitive.Space.4\">4</x:Double>")));

    [Theory]
    [InlineData("src/SIL.Motif.App/App.axaml")]
    [InlineData("src/SIL.Motif.App/ViewModels/Sample.cs")]
    [InlineData("src/SIL.Motif.App/Views/bin/Debug/Sample.axaml")]
    public void FilesOutsideTheLayersAreNotScanned(string path) =>
        Assert.Empty(Categories(path, Element("Margin=\"12\" Background=\"White\"")));

    [Fact]
    public void AnAllowlistEntryExcusesTheLineItNames()
    {
        var file = new SourceFile(View, Document("<Canvas Width=\"24\" Height=\"24\" />"));
        var allowlist = new[] { new Allowance(View, "<Canvas Width=\"24\"", "a drawing area, not a size") };
        Assert.Empty(TokenHygiene.Check([file], allowlist));
    }

    [Fact]
    public void AnAllowlistEntryExcusesNothingElse()
    {
        var file = new SourceFile(View, Document("<Canvas Width=\"24\" Height=\"24\" />\n<Border Margin=\"4\" />"));
        var allowlist = new[] { new Allowance(View, "<Canvas Width=\"24\"", "a drawing area, not a size") };
        Assert.Equal(["literal-size"], TokenHygiene.Check([file], allowlist).Select(v => v.Category));
    }

    [Fact]
    public void AnAllowlistEntryThatMatchesNothingFails()
    {
        var file = new SourceFile(View, Document("<Border />"));
        var allowlist = new[] { new Allowance(View, "<Canvas Width=\"24\"", "a drawing area, not a size") };
        Assert.Equal(["stale-allowlist"], TokenHygiene.Check([file], allowlist).Select(v => v.Category));
    }

    [Fact]
    public void EveryShippedAllowlistEntryGivesAReason() =>
        Assert.All(TokenHygiene.Allowlist, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.Snippet));

    [Fact]
    public void AViolationFailsTheRunUnlessItIsAdvisory()
    {
        using var repo = new ScratchRepo();
        repo.Write(View, Element("Margin=\"12\""));

        var output = new StringWriter();
        Assert.Equal(1, TokenHygiene.Run([], repo.Root, [], output));
        Assert.Contains("Sample.axaml:", output.ToString());
        Assert.Equal(0, TokenHygiene.Run(["-Advisory"], repo.Root, [], new StringWriter()));
    }

    [Fact]
    public void ACleanTreePasses()
    {
        using var repo = new ScratchRepo();
        repo.Write(View, Element("Margin=\"{DynamicResource Intent.Gap.AfterGroup}\""));
        Assert.Equal(0, TokenHygiene.Run([], repo.Root, [], new StringWriter()));
    }

    [Fact]
    public void AnEmptyScanIsRefusedRatherThanReportedClean()
    {
        using var repo = new ScratchRepo();
        Assert.Equal(2, TokenHygiene.Run([], repo.Root, [], new StringWriter()));
    }

    [Fact]
    public void TheRealViewsAreScannedFromTheRepository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var output = new StringWriter();
        TokenHygiene.Run(["-Advisory"], root.FullName, TokenHygiene.Allowlist, output);
        Assert.Matches(@"scanned [1-9]\d* file", output.ToString());
    }

    [Fact]
    public void TheBuildRunsTheGateAsAFailingStep()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var gate = File.ReadAllLines(Path.Combine(root.FullName, "build.ps1"))
            .Single(line => line.Contains("tools/TokenHygiene/token-hygiene.cs", StringComparison.Ordinal) && line.Contains("& dotnet", StringComparison.Ordinal));
        Assert.DoesNotContain("-Advisory", gate, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Categories(string path, string text) =>
        TokenHygiene.ScanFile(path, text).Select(v => v.Category).ToList();

    private static string Document(string body) =>
        "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n"
        + body + "\n</UserControl>";

    private static string Element(string attribute) => Document($"<Border {attribute} />");

    private static string Setter(string attribute)
    {
        var equals = attribute.IndexOf('=');
        var property = attribute[..equals];
        var value = attribute[(equals + 2)..^1];
        return Document($"<UserControl.Styles><Style Selector=\"Border\"><Setter Property=\"{property}\" Value=\"{value}\" /></Style></UserControl.Styles>");
    }

    private static string Styles(string body) =>
        "<Styles xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n"
        + body + "\n</Styles>";

    private static string Dictionary(string body) =>
        "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n"
        + body + "\n</ResourceDictionary>";

    private sealed class ScratchRepo : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("token-hygiene-").FullName;

        public void Write(string relative, string text)
        {
            var full = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
