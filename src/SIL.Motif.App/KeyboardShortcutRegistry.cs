using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.Help;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

internal enum KeyboardShortcutScope
{
    Window,
    Settings,
    TextReader,
    WordList,
    Lists,
    Matrix,
    ReviewChanges,
    MiniMatrix,
    Morpheme,
    DiagnosticTree,
}

internal enum KeyboardShortcutBehavior
{
    PreviousWord,
    NextWord,
    PreviousTreeItem,
    NextTreeItem,
    PreviousLine,
    NextLine,
    OpenWordCard,
    CloseWordCard,
    PreviousRow,
    NextRow,
    PreviousScreen,
    NextScreen,
    FirstItem,
    LastItem,
    Approve,
    Disapprove,
    Unknown,
    Copy,
    Undo,
    Redo,
    FocusWordSearch,
    ShowShortcuts,
    OpenSettings,
    ZoomIn,
    ZoomOut,
    ResetZoom,
    CommitParsingLimits,
    CancelParsingLimits,
    PreviousThemeChoice,
    NextThemeChoice,
    FocusCloseButton,
    FocusSettingsGroups,
    ShowHelp,
    Back,
    ToggleMatrixCell,
    InspectMorpheme,
}

public sealed record KeyboardShortcutEntry(string Key, string Gesture, string Scope, string Action);

[AttributeUsage(AttributeTargets.Method)]
internal sealed class KeyboardShortcutHandlerAttribute(params string[] actionScopes) : Attribute
{
    public IReadOnlyList<string> ActionScopes { get; } = actionScopes;
}

internal sealed record KeyboardShortcutBinding(
    KeyboardShortcutEntry Row,
    Key Key,
    KeyModifiers Modifiers,
    KeyboardShortcutScope Scope,
    KeyboardShortcutBehavior Behavior,
    ShortcutFocusRule FocusRule,
    string DefinitionId,
    string ActionId,
    string BindingId,
    ShortcutGesture Gesture);

public static class KeyboardShortcutRegistry
{
    private static readonly ShortcutPlatform CurrentPlatform = OperatingSystem.IsMacOS()
        ? ShortcutPlatform.MacOS
        : OperatingSystem.IsWindows() ? ShortcutPlatform.Windows : ShortcutPlatform.Linux;
    private static readonly CultureInfo CurrentCulture = CultureInfo.CurrentUICulture;
    private static readonly HelpCatalog Help = HelpCatalog.Load(CurrentCulture);
    private static readonly IReadOnlyDictionary<ShortcutKey, Key> AvaloniaKeys = new Dictionary<ShortcutKey, Key>
    {
        [ShortcutKey.Left] = Key.Left,
        [ShortcutKey.Right] = Key.Right,
        [ShortcutKey.Up] = Key.Up,
        [ShortcutKey.Down] = Key.Down,
        [ShortcutKey.Home] = Key.Home,
        [ShortcutKey.End] = Key.End,
        [ShortcutKey.PageUp] = Key.PageUp,
        [ShortcutKey.PageDown] = Key.PageDown,
        [ShortcutKey.Enter] = Key.Enter,
        [ShortcutKey.Space] = Key.Space,
        [ShortcutKey.Escape] = Key.Escape,
        [ShortcutKey.Tab] = Key.Tab,
        [ShortcutKey.F1] = Key.F1,
        [ShortcutKey.A] = Key.A,
        [ShortcutKey.D] = Key.D,
        [ShortcutKey.U] = Key.U,
        [ShortcutKey.C] = Key.C,
        [ShortcutKey.Y] = Key.Y,
        [ShortcutKey.Z] = Key.Z,
        [ShortcutKey.F] = Key.F,
        [ShortcutKey.OemQuestion] = Key.OemQuestion,
        [ShortcutKey.OemComma] = Key.OemComma,
        [ShortcutKey.OemPlus] = Key.OemPlus,
        [ShortcutKey.Add] = Key.Add,
        [ShortcutKey.OemMinus] = Key.OemMinus,
        [ShortcutKey.Subtract] = Key.Subtract,
        [ShortcutKey.D0] = Key.D0,
        [ShortcutKey.NumPad0] = Key.NumPad0,
    };
    private static readonly IReadOnlyDictionary<Key, ShortcutKey> ShortcutKeys =
        AvaloniaKeys.ToDictionary(pair => pair.Value, pair => pair.Key);

    internal static IReadOnlyList<KeyboardShortcutBinding> Bindings { get; } = CreateBindings();

    public static string Title => UiTitle("shortcut-page-title");

    public static IReadOnlyList<KeyboardShortcutEntry> Entries { get; } = Bindings
        .GroupBy(binding => (binding.DefinitionId, binding.Scope))
        .Select(group => group.First().Row)
        .ToArray();

    /// <summary>Builds tooltip copy from a registered row's localized action and current-platform gesture.</summary>
    /// <param name="entry">The catalog row displayed by the caller.</param>
    public static string TooltipFor(KeyboardShortcutEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Entries.Contains(entry))
            throw new ArgumentException("The shortcut row must come from the keyboard shortcut catalog.", nameof(entry));
        return $"{entry.Action} ({entry.Gesture})";
    }

    internal static KeyboardShortcutBinding? Find(
        KeyboardShortcutScope scope, Key key, KeyModifiers modifiers, ShortcutPlatform? platform = null,
        IReadOnlyCollection<KeyboardShortcutBehavior>? targetBehaviors = null)
    {
        if (!ShortcutKeys.TryGetValue(key, out var neutralKey)) return null;
        var selectedPlatform = platform ?? CurrentPlatform;
        var actualModifiers = NormalizeModifiers(modifiers);
        var candidates = Bindings.Where(binding => binding.Scope == scope
            && binding.Gesture.Key == neutralKey
            && KeyboardShortcutCatalog.ResolveModifiers(binding.Gesture.Modifiers, selectedPlatform) == actualModifiers);
        if (targetBehaviors is not null)
            candidates = candidates.Where(binding => targetBehaviors.Contains(binding.Behavior));
        var matches = candidates.Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static bool Allows(KeyboardShortcutBinding binding, bool isTextInput, bool hasFocusedItem) =>
        binding.FocusRule switch
        {
            ShortcutFocusRule.Always => true,
            ShortcutFocusRule.OutsideTextInput => !isTextInput,
            ShortcutFocusRule.FocusedItem => !isTextInput && hasFocusedItem,
            _ => false,
        };

    internal static bool IsTextInput(object? source) => source is Control control &&
        control.GetSelfAndVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown
            || ancestor is ComboBox { IsEditable: true });

    internal static string? CopyTextFor(object? source)
    {
        if (source is not Control control) return null;
        var ancestors = control.GetSelfAndVisualAncestors().OfType<Control>().ToArray();
        var root = TopLevel.GetTopLevel(control);
        var selectedBlocks = ancestors.OfType<SelectableTextBlock>().Concat(
            root?.GetVisualDescendants().OfType<SelectableTextBlock>() ?? []);
        var selected = selectedBlocks
            .Select(block => block.SelectedText).FirstOrDefault(text => !string.IsNullOrEmpty(text));
        if (selected is not null) return selected;
        foreach (var ancestor in ancestors)
        {
            if (ancestor is Controls.WordPresentation.WordRow { Data: { } data }) return data.Facts.Word;
            var word = ancestor.DataContext switch
            {
                ResultsTokenViewModel token when token.IsWord => token.Form,
                WordRowViewModel wordRow => wordRow.Word,
                ReviewListEntryViewModel entry => entry.Change?.Word,
                ChangeViewModel change => change.Word,
                _ => null,
            };
            if (!string.IsNullOrEmpty(word)) return word;
        }
        return null;
    }

    private static IReadOnlyList<KeyboardShortcutBinding> CreateBindings()
    {
        var result = new List<KeyboardShortcutBinding>();
        foreach (var definition in KeyboardShortcutCatalog.Entries)
        foreach (var scope in definition.Scopes)
        {
            var appScope = ToAppScope(scope);
            var row = new KeyboardShortcutEntry(
                KeyboardShortcutCatalog.FormatKeyLabels(definition.Bindings.Select(binding => binding.Gesture.Key),
                    CurrentCulture),
                KeyboardShortcutCatalog.FormatAlternatives(
                    definition.Bindings.Select(binding => binding.Gesture), CurrentPlatform, CurrentCulture),
                UiTitle(KeyboardShortcutCatalog.ScopeCode(scope)),
                UiTitle(definition.TitleCode));
            foreach (var registered in definition.Bindings)
            {
                if (!Enum.TryParse<KeyboardShortcutBehavior>(registered.ActionId, ignoreCase: false, out var behavior))
                    throw new InvalidOperationException($"Keyboard shortcut action '{registered.ActionId}' has no App behavior.");
                if (!AvaloniaKeys.TryGetValue(registered.Gesture.Key, out var key))
                    throw new InvalidOperationException($"Keyboard shortcut key '{registered.Gesture.Key}' has no Avalonia mapping.");

                var modifiers = ToAvaloniaModifiers(
                    KeyboardShortcutCatalog.ResolveModifiers(registered.Gesture.Modifiers, CurrentPlatform));
                result.Add(new KeyboardShortcutBinding(
                    row, key, modifiers, appScope, behavior, registered.FocusRule, definition.Id, registered.ActionId,
                    registered.Id, registered.Gesture));
            }
        }

        return result;
    }

    private static string UiTitle(string code) => Help.Find(HelpEntryKind.Ui, code)?.Title
        ?? throw new InvalidOperationException($"Keyboard shortcut UI text '{code}' is missing.");

    private static KeyboardShortcutScope ToAppScope(ShortcutScope scope) => scope switch
    {
        ShortcutScope.Window => KeyboardShortcutScope.Window,
        ShortcutScope.Settings => KeyboardShortcutScope.Settings,
        ShortcutScope.TextReader => KeyboardShortcutScope.TextReader,
        ShortcutScope.WordList => KeyboardShortcutScope.WordList,
        ShortcutScope.Lists => KeyboardShortcutScope.Lists,
        ShortcutScope.Matrix => KeyboardShortcutScope.Matrix,
        ShortcutScope.ReviewChanges => KeyboardShortcutScope.ReviewChanges,
        ShortcutScope.DiagnosticTree => KeyboardShortcutScope.DiagnosticTree,
        ShortcutScope.MiniMatrix => KeyboardShortcutScope.MiniMatrix,
        ShortcutScope.Morpheme => KeyboardShortcutScope.Morpheme,
        _ => throw new InvalidOperationException($"Shortcut scope '{scope}' has no App route."),
    };

    private static ShortcutModifiers NormalizeModifiers(KeyModifiers modifiers)
    {
        var result = ShortcutModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= ShortcutModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= ShortcutModifiers.Meta;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= ShortcutModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= ShortcutModifiers.Shift;
        return result;
    }

    private static KeyModifiers ToAvaloniaModifiers(ShortcutModifiers modifiers)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(ShortcutModifiers.Control)) result |= KeyModifiers.Control;
        if (modifiers.HasFlag(ShortcutModifiers.Meta)) result |= KeyModifiers.Meta;
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) result |= KeyModifiers.Shift;
        return result;
    }

}
