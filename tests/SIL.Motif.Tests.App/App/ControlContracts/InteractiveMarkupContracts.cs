using System.Xml;
using System.Xml.Linq;

namespace SIL.Motif.Tests.App;

internal sealed record InteractiveMarkupControl(
    string Source,
    string XamlNamespace,
    string TypeName,
    int Line,
    InteractiveControlFamily? Family,
    IReadOnlyList<string> Triggers);

internal sealed record InteractiveBehaviorCase(Type TestClass, string MethodName);

internal static class InteractiveMarkupContracts
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string ViewsNamespace = "clr-namespace:SIL.Motif.App.Views";
    private const string ControlsNamespace = "clr-namespace:SIL.Motif.App.Controls";

    private static readonly HashSet<InteractiveControlFamily> Inputs =
    [
        InteractiveControlFamily.Action,
        InteractiveControlFamily.Link,
        InteractiveControlFamily.Filter,
        InteractiveControlFamily.TextEntry,
        InteractiveControlFamily.Choice,
        InteractiveControlFamily.Check,
        InteractiveControlFamily.Radio,
        InteractiveControlFamily.Disclosure,
        InteractiveControlFamily.List,
        InteractiveControlFamily.MatrixCell,
        InteractiveControlFamily.Grid,
        InteractiveControlFamily.Tree,
        InteractiveControlFamily.Morpheme,
    ];

    private static readonly HashSet<string> ActionProperties = new(StringComparer.Ordinal)
    {
        "Command", "Click", "KeyDown", "PointerPressed", "PointerReleased", "Tapped", "DoubleTapped",
        "TextChanged", "SelectionChanged", "Checked", "Unchecked", "ValueChanged",
    };

    private static readonly IReadOnlyDictionary<(string Namespace, string TypeName), InteractiveControlFamily> Families =
        new Dictionary<(string Namespace, string TypeName), InteractiveControlFamily>
        {
            [(AvaloniaNamespace, "Button")] = InteractiveControlFamily.Action,
            [(AvaloniaNamespace, "HyperlinkButton")] = InteractiveControlFamily.Link,
            [(AvaloniaNamespace, "SplitButton")] = InteractiveControlFamily.Action,
            [(AvaloniaNamespace, "MenuItem")] = InteractiveControlFamily.Action,
            [(AvaloniaNamespace, "TextBox")] = InteractiveControlFamily.TextEntry,
            [(AvaloniaNamespace, "ComboBox")] = InteractiveControlFamily.Choice,
            [(AvaloniaNamespace, "NumericUpDown")] = InteractiveControlFamily.TextEntry,
            [(AvaloniaNamespace, "CheckBox")] = InteractiveControlFamily.Check,
            [(AvaloniaNamespace, "RadioButton")] = InteractiveControlFamily.Radio,
            [(AvaloniaNamespace, "Expander")] = InteractiveControlFamily.Disclosure,
            [(AvaloniaNamespace, "ScrollViewer")] = InteractiveControlFamily.ScrollViewport,
            [(AvaloniaNamespace, "ListBox")] = InteractiveControlFamily.List,
            [(AvaloniaNamespace, "DataGrid")] = InteractiveControlFamily.Grid,
            [(AvaloniaNamespace, "TreeView")] = InteractiveControlFamily.Tree,
            [(ViewsNamespace, "MatrixCell")] = InteractiveControlFamily.MatrixCell,
            [(ViewsNamespace, "FilterChip")] = InteractiveControlFamily.Filter,
            [(ViewsNamespace, "CopyableTextBlock")] = InteractiveControlFamily.SelectableText,
            [(ViewsNamespace, "MarkGlyph")] = InteractiveControlFamily.Mark,
            [(ViewsNamespace, "TraceNotationToken")] = InteractiveControlFamily.Action,
            [(ViewsNamespace, "MorphemeRow")] = InteractiveControlFamily.Morpheme,
            [(ControlsNamespace, "OpinionMark")] = InteractiveControlFamily.Mark,
            [(ControlsNamespace, "UnreadMark")] = InteractiveControlFamily.Mark,
            [(AvaloniaNamespace, "Border")] = InteractiveControlFamily.FocusableSurface,
        };

    public static IReadOnlyDictionary<InteractiveControlFamily, IReadOnlyList<InteractiveBehaviorCase>> BehaviorCases { get; } =
        new Dictionary<InteractiveControlFamily, IReadOnlyList<InteractiveBehaviorCase>>
        {
            [InteractiveControlFamily.Action] = [new(typeof(CommandAvailabilityContractTests),
                nameof(CommandAvailabilityContractTests.EveryDiscoveredCommandHasAnAuthoredContract))],
            [InteractiveControlFamily.Link] =
            [
                new(typeof(WarningsPageWordsTests),
                    nameof(WarningsPageWordsTests.ANamedEntryShowsItsLexiconEditLinkAndAnExplicitMissingAdviceLine)),
                new(typeof(InteractionCueTests),
                    nameof(InteractionCueTests.AnOutcomeLegendLinkIsVisibleAtRestAndUnderlinesUnderThePointer)),
            ],
            [InteractiveControlFamily.Filter] = [new(typeof(ResultsInTextViewModelTests),
                nameof(ResultsInTextViewModelTests.NeedsALookFilterKeepsWordsWithAvailableMarkingActions))],
            [InteractiveControlFamily.TextEntry] = [new(typeof(MainWindowSmokeTests),
                nameof(MainWindowSmokeTests.EveryInputAndButtonHasAnAccessibleName))],
            [InteractiveControlFamily.Choice] = [new(typeof(KeyboardInteractionContractTests),
                nameof(KeyboardInteractionContractTests.WordCardArrowsRespectOccurrenceBoundariesAndChildInputs))],
            [InteractiveControlFamily.Check] = [new(typeof(TextsListsPanelTests),
                nameof(TextsListsPanelTests.TickingARowTicksTheWordForTheListsHandoff))],
            [InteractiveControlFamily.Disclosure] = [new(typeof(TryWordPageTests),
                nameof(TryWordPageTests.ExpandedFilteredNotationFitsInsideTheFullTreeViewport))],
            [InteractiveControlFamily.List] = [new(typeof(TextsListsPanelTests),
                nameof(TextsListsPanelTests.TickingARowTicksTheWordForTheListsHandoff))],
            [InteractiveControlFamily.MatrixCell] = [new(typeof(KeyboardInteractionContractTests),
                nameof(KeyboardInteractionContractTests.PointerAndKeyboardSelectionRespectTheSameCellGuards))],
            [InteractiveControlFamily.Grid] = [new(typeof(MainWindowSmokeTests),
                nameof(MainWindowSmokeTests.ResultTablesRenderSelectableDynamicCells))],
            [InteractiveControlFamily.Tree] = [new(typeof(TryWordPageTests),
                nameof(TryWordPageTests.ASelectedOccurrenceSurvivesBothMountedTreesAndFiltering))],
            [InteractiveControlFamily.Morpheme] = [new(typeof(WordRowControlTests),
                nameof(WordRowControlTests.InTheOpenCardLinksWaitForHoverOrKeyboardFocus))],
            [InteractiveControlFamily.FocusableSurface] = [new(typeof(KeyboardInteractionContractTests),
                nameof(KeyboardInteractionContractTests.ClosingTheCardReturnsFocusToItsOccurrence))],
            [InteractiveControlFamily.Occurrence] = [new(typeof(KeyboardInteractionContractTests),
                nameof(KeyboardInteractionContractTests.WordCardArrowsRespectOccurrenceBoundariesAndChildInputs))],
        };

    public static IReadOnlyList<string> TypesForFamily(InteractiveControlFamily family) =>
        Families.Where(pair => pair.Value == family)
            .Select(pair => $"{pair.Key.Namespace}/{pair.Key.TypeName}")
            .Concat(family == InteractiveControlFamily.Occurrence
                ? ["https://github.com/avaloniaui/StackPanel with PointerPressed"]
                : [])
            .ToArray();

    public static IReadOnlyList<InteractiveMarkupControl> DiscoverFile(string path, string source)
    {
        using var reader = XmlReader.Create(path, Settings());
        return Discover(source, reader);
    }

    public static IReadOnlyList<InteractiveMarkupControl> DiscoverMarkup(string source, string markup)
    {
        using var text = new StringReader(markup);
        using var reader = XmlReader.Create(text, Settings());
        return Discover(source, reader);
    }

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
    };

    private static IReadOnlyList<InteractiveMarkupControl> Discover(string source, XmlReader reader)
    {
        var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        return document.Descendants().Select(element => ReadControl(source, element))
            .Where(control => control is not null).Cast<InteractiveMarkupControl>().ToArray();
    }

    private static InteractiveMarkupControl? ReadControl(string source, XElement element)
    {
        var xamlNamespace = CanonicalNamespace(element.Name.NamespaceName);
        var typeName = element.Name.LocalName;
        if (element.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value is { } className)
        {
            var separator = className.LastIndexOf('.');
            if (separator > 0)
            {
                xamlNamespace = "clr-namespace:" + className[..separator];
                typeName = className[(separator + 1)..];
            }
        }

        var triggers = element.Attributes()
            .Where(attribute => ActionProperties.Contains(attribute.Name.LocalName) ||
                attribute.Name.LocalName is "Focusable" or "IsTabStop" &&
                string.Equals(attribute.Value, "True", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Name.LocalName).Distinct(StringComparer.Ordinal).ToArray();
        var type = (xamlNamespace, typeName);
        var hasFamily = Families.TryGetValue(type, out var family);
        if (!hasFamily && type == (AvaloniaNamespace, "StackPanel") && triggers.Contains("PointerPressed"))
        {
            family = InteractiveControlFamily.Occurrence;
            hasFamily = true;
        }

        var familyIsInput = hasFamily && Inputs.Contains(family);
        if (!familyIsInput && triggers.Length == 0) return null;

        return new InteractiveMarkupControl(source, xamlNamespace, typeName,
            (element as IXmlLineInfo)?.LineNumber ?? 0, hasFamily ? family : null, triggers);
    }

    private static string CanonicalNamespace(string xamlNamespace) => xamlNamespace.StartsWith("using:", StringComparison.Ordinal)
        ? "clr-namespace:" + xamlNamespace["using:".Length..]
        : xamlNamespace;
}
