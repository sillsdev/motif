using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SIL.Motif.Help;

/// <summary>The operating system used to resolve platform-neutral shortcut gestures.</summary>
public enum ShortcutPlatform
{
    /// <summary>Windows modifier and key names.</summary>
    Windows,

    /// <summary>Linux modifier and key names.</summary>
    Linux,

    /// <summary>macOS modifier and key names.</summary>
    MacOS,
}

/// <summary>Modifiers declared by a keyboard shortcut.</summary>
[Flags]
public enum ShortcutModifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,

    /// <summary>The platform's primary modifier: Control on Windows and Linux, Command on macOS.</summary>
    Primary = 1,

    /// <summary>The Control key independent of platform.</summary>
    Control = 2,

    /// <summary>The Alt key independent of platform.</summary>
    Alt = 4,

    /// <summary>The Shift key.</summary>
    Shift = 8,

    /// <summary>The Meta or Command key independent of platform.</summary>
    Meta = 16,
}

/// <summary>A platform-neutral key code used by the shortcut catalog.</summary>
public enum ShortcutKey
{
    /// <summary>Left arrow.</summary>
    Left,
    /// <summary>Right arrow.</summary>
    Right,
    /// <summary>Up arrow.</summary>
    Up,
    /// <summary>Down arrow.</summary>
    Down,
    /// <summary>Home.</summary>
    Home,
    /// <summary>End.</summary>
    End,
    /// <summary>Page Up.</summary>
    PageUp,
    /// <summary>Page Down.</summary>
    PageDown,
    /// <summary>Enter.</summary>
    Enter,
    /// <summary>Space.</summary>
    Space,
    /// <summary>Escape.</summary>
    Escape,
    /// <summary>Tab.</summary>
    Tab,
    /// <summary>F1.</summary>
    F1,
    /// <summary>The A key.</summary>
    A,
    /// <summary>The D key.</summary>
    D,
    /// <summary>The U key.</summary>
    U,
    /// <summary>The C key.</summary>
    C,
    /// <summary>The Y key.</summary>
    Y,
    /// <summary>The Z key.</summary>
    Z,
    /// <summary>The F key.</summary>
    F,
    /// <summary>The key that produces a question mark with Shift on the current layout.</summary>
    OemQuestion,
    /// <summary>The comma key.</summary>
    OemComma,
    /// <summary>The equals/plus key.</summary>
    OemPlus,
    /// <summary>The numeric keypad plus key.</summary>
    Add,
    /// <summary>The minus key.</summary>
    OemMinus,
    /// <summary>The numeric keypad minus key.</summary>
    Subtract,
    /// <summary>The top-row zero key.</summary>
    D0,
    /// <summary>The numeric keypad zero key.</summary>
    NumPad0,
    /// <summary>Delete.</summary>
    Delete,
    /// <summary>The S key.</summary>
    S,
    /// <summary>The P key.</summary>
    P,
    /// <summary>F10.</summary>
    F10,
}

/// <summary>The product surface where a shortcut is available.</summary>
public enum ShortcutScope
{
    /// <summary>Window-level actions.</summary>
    Window,
    /// <summary>The Analyze texts reader.</summary>
    TextReader,
    /// <summary>The Word list.</summary>
    WordList,
    /// <summary>Lists.</summary>
    Lists,
    /// <summary>The comparison Matrix.</summary>
    Matrix,
    /// <summary>A Matrix preview.</summary>
    MiniMatrix,
    /// <summary>A morpheme row.</summary>
    Morpheme,
    /// <summary>The Try a Word page.</summary>
    TryWord,
    /// <summary>The Review changes page.</summary>
    ReviewChanges,
    /// <summary>The keyboard-navigable diagnostic tree.</summary>
    DiagnosticTree,
    /// <summary>The Settings popup.</summary>
    Settings,
}

/// <summary>The focus condition required before a shortcut can run.</summary>
public enum ShortcutFocusRule
{
    /// <summary>The shortcut may run regardless of focused control.</summary>
    Always,
    /// <summary>The shortcut runs only when focus is outside text-entry controls.</summary>
    OutsideTextInput,
    /// <summary>The shortcut acts on the currently focused item.</summary>
    FocusedItem,
}

/// <summary>A platform-neutral key and modifier combination.</summary>
/// <param name="Key">The key code.</param>
/// <param name="Modifiers">The exact modifier set.</param>
public sealed record ShortcutGesture(ShortcutKey Key, ShortcutModifiers Modifiers);

/// <summary>A registered action and the gesture that invokes it.</summary>
/// <param name="Id">The stable binding identifier.</param>
/// <param name="ActionId">The stable App action identifier.</param>
/// <param name="Gesture">The key and exact modifiers.</param>
/// <param name="FocusRule">The required focus state.</param>
public sealed record ShortcutBinding(
    string Id,
    string ActionId,
    ShortcutGesture Gesture,
    ShortcutFocusRule FocusRule);

/// <summary>A localized shortcut title, group, scope list, and one or more gestures.</summary>
/// <param name="Id">The stable shortcut row identifier.</param>
/// <param name="GroupCode">The localized UI code for its group heading.</param>
/// <param name="TitleCode">The localized UI code for its action text.</param>
/// <param name="Scopes">The product surfaces where its bindings are available.</param>
/// <param name="Bindings">The gestures that invoke the action.</param>
public sealed record ShortcutDefinition(
    string Id,
    string GroupCode,
    string TitleCode,
    IReadOnlyList<ShortcutScope> Scopes,
    IReadOnlyList<ShortcutBinding> Bindings);

/// <summary>The shared, validated catalog of keyboard shortcuts and their Help rendering.</summary>
public static class KeyboardShortcutCatalog
{
    private const string EnglishLocale = "en";
    private static readonly IReadOnlyList<ShortcutDefinition> Catalog = ReadCatalog();

    /// <summary>Every supported shortcut definition in display order.</summary>
    public static IReadOnlyList<ShortcutDefinition> Entries => Catalog;

    /// <summary>Formats a gesture using the platform and localized key names.</summary>
    /// <param name="gesture">The neutral gesture.</param>
    /// <param name="platform">The platform used to resolve Primary.</param>
    /// <param name="culture">The culture used for key names.</param>
    public static string FormatGesture(ShortcutGesture gesture, ShortcutPlatform platform, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(culture);
        var resources = ReadShortcutHelp(culture.TwoLetterISOLanguageName);
        var modifiers = ResolveModifiers(gesture.Modifiers, platform);
        var parts = new List<string>();
        foreach (var modifier in new[]
                 {
                     ShortcutModifiers.Control, ShortcutModifiers.Meta, ShortcutModifiers.Alt, ShortcutModifiers.Shift,
                 })
        {
            if (modifiers.HasFlag(modifier))
                parts.Add(resources.ModifierNames.GetValueOrDefault(modifier.ToString()) ?? modifier.ToString());
        }

        var key = FormatKeyName(gesture.Key, culture);
        parts.Add(key);
        return string.Join(resources.ModifierSeparator ?? "+", parts);
    }

    /// <summary>Formats one key name with localized Help resources.</summary>
    /// <param name="key">The neutral key code.</param>
    /// <param name="culture">The culture used for the name.</param>
    public static string FormatKeyName(ShortcutKey key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var names = ReadShortcutHelp(culture.TwoLetterISOLanguageName).KeyNames;
        return names.GetValueOrDefault(key.ToString()) ?? key.ToString();
    }

    /// <summary>Formats the key label shown in a shortcut list.</summary>
    /// <param name="key">The neutral key code.</param>
    /// <param name="culture">The culture used for the label.</param>
    public static string FormatKeyLabel(ShortcutKey key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var labels = ReadShortcutHelp(culture.TwoLetterISOLanguageName).KeyLabels;
        return labels.GetValueOrDefault(key.ToString()) ?? FormatKeyName(key, culture);
    }

    /// <summary>Formats the key labels for one row with the localized alternative separator.</summary>
    /// <param name="keys">The neutral key codes shown on the row.</param>
    /// <param name="culture">The culture used for names and the separator.</param>
    public static string FormatKeyLabels(IEnumerable<ShortcutKey> keys, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(culture);
        var resources = ReadShortcutHelp(culture.TwoLetterISOLanguageName);
        return string.Join(resources.AlternativeSeparator ?? " or ", keys
            .Select(key => FormatKeyLabel(key, culture)).Distinct(StringComparer.Ordinal));
    }

    /// <summary>Formats alternative gestures as one localized row value.</summary>
    /// <param name="gestures">The bindings that perform the same row action.</param>
    /// <param name="platform">The platform used to resolve Primary.</param>
    /// <param name="culture">The culture used for key names and the alternative separator.</param>
    public static string FormatAlternatives(
        IEnumerable<ShortcutGesture> gestures, ShortcutPlatform platform, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        ArgumentNullException.ThrowIfNull(culture);
        var resources = ReadShortcutHelp(culture.TwoLetterISOLanguageName);
        var separator = resources.AlternativeSeparator ?? " or ";
        return string.Join(separator, gestures.Select(gesture => FormatGesture(gesture, platform, culture)));
    }

    /// <summary>Resolves Primary and returns the exact explicit modifier set for a platform.</summary>
    /// <param name="modifiers">The declared modifiers.</param>
    /// <param name="platform">The platform that defines Primary.</param>
    public static ShortcutModifiers ResolveModifiers(ShortcutModifiers modifiers, ShortcutPlatform platform)
    {
        var result = modifiers & ~ShortcutModifiers.Primary;
        if (modifiers.HasFlag(ShortcutModifiers.Primary))
            result |= platform == ShortcutPlatform.MacOS ? ShortcutModifiers.Meta : ShortcutModifiers.Control;
        return result;
    }

    /// <summary>Renders the complete keyboard shortcuts Guide page from the catalog and localized resources.</summary>
    /// <param name="culture">The culture to render, or the current UI culture when omitted.</param>
    public static string RenderHelpPage(CultureInfo? culture = null)
    {
        var selectedCulture = culture ?? CultureInfo.CurrentUICulture;
        var locale = NormalizeLocale(selectedCulture.TwoLetterISOLanguageName);
        var titles = ReadUiTitles(locale);
        var resources = ReadShortcutHelp(locale);
        var introduction = Required(resources.Introduction, "introduction");
        var keyHeading = Required(resources.KeyHeading, "keyHeading");
        var windowsHeading = Required(resources.WindowsHeading, "windowsHeading");
        var macHeading = Required(resources.MacHeading, "macHeading");
        var scopeHeading = Required(resources.ScopeHeading, "scopeHeading");
        var actionHeading = Required(resources.ActionHeading, "actionHeading");
        var lines = new List<string>
        {
            $"# {EscapeCell(FindUiTitle(titles, "shortcut-page-title"))}",
            "",
            introduction,
        };

        foreach (var scope in Catalog.SelectMany(definition => definition.Scopes).Distinct())
        {
            lines.Add("");
            lines.Add($"## {EscapeCell(FindUiTitle(titles, ScopeCode(scope)))}");
            lines.Add("");
            lines.Add($"| {keyHeading} | {windowsHeading} | {macHeading} | {scopeHeading} | {actionHeading} |");
            lines.Add("| --- | --- | --- | --- | --- |");
            foreach (var definition in Catalog.Where(definition => definition.Scopes.Contains(scope)))
            {
                var key = FormatKeyLabels(definition.Bindings.Select(binding => binding.Gesture.Key), selectedCulture);
                var winGesture = FormatAlternatives(definition.Bindings.Select(binding => binding.Gesture),
                    ShortcutPlatform.Windows, selectedCulture);
                var macGesture = FormatAlternatives(definition.Bindings.Select(binding => binding.Gesture),
                    ShortcutPlatform.MacOS, selectedCulture);
                var action = FindUiTitle(titles, definition.TitleCode);
                var scopeText = FindUiTitle(titles, ScopeCode(scope));
                lines.Add($"| {EscapeCell(key)} | {EscapeCell(winGesture)} | {EscapeCell(macGesture)} | {EscapeCell(scopeText)} | {EscapeCell(action)} |");
            }
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Returns the UI metadata code used to title a scope.</summary>
    /// <param name="scope">The catalog scope.</param>
    public static string ScopeCode(ShortcutScope scope) => $"shortcut-scope-{scope switch
    {
        ShortcutScope.TextReader => "analyze-texts",
        ShortcutScope.WordList => "word-list",
        ShortcutScope.MiniMatrix => "matrix-preview",
        ShortcutScope.TryWord => "try-a-word",
        ShortcutScope.ReviewChanges => "review-changes",
        _ => ToKebabCase(scope.ToString()),
    }}";

    private static string FindUiTitle(IReadOnlyDictionary<string, string> titles, string code) =>
        titles.GetValueOrDefault(code)
        ?? throw new InvalidDataException($"Keyboard shortcut UI text '{code}' is missing.");

    private static string Required(string? value, string property) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Keyboard shortcut Help resource '{property}' is missing.")
            : value;

    private static string EscapeCell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string ToKebabCase(string value) => string.Concat(value.Select((character, index) =>
        char.IsUpper(character) && index > 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));

    private static IReadOnlyList<ShortcutDefinition> ReadCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            string.Equals(name.Replace('\\', '/'), "help/shortcuts.json", StringComparison.Ordinal));
        if (resourceName is null)
            throw new InvalidDataException("The keyboard shortcut catalog is missing.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        var definitions = new List<ShortcutDefinition>();
        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            var scopes = entry.GetProperty("scopes").EnumerateArray()
                .Select(value => Enum.Parse<ShortcutScope>(value.GetString()!, ignoreCase: false)).ToArray();
            var bindings = entry.GetProperty("bindings").EnumerateArray().Select(binding =>
            {
                var modifiersText = binding.GetProperty("modifiers").GetString()!;
                var modifiers = modifiersText == "None"
                    ? ShortcutModifiers.None
                    : modifiersText.Split('|').Aggregate(ShortcutModifiers.None,
                        (current, value) => current | Enum.Parse<ShortcutModifiers>(value, ignoreCase: false));
                return new ShortcutBinding(
                    binding.GetProperty("id").GetString()!,
                    binding.GetProperty("actionId").GetString()!,
                    new ShortcutGesture(
                        Enum.Parse<ShortcutKey>(binding.GetProperty("key").GetString()!, ignoreCase: false), modifiers),
                    Enum.Parse<ShortcutFocusRule>(binding.GetProperty("focusRule").GetString()!, ignoreCase: false));
            }).ToArray();
            definitions.Add(new ShortcutDefinition(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("groupCode").GetString()!,
                entry.GetProperty("titleCode").GetString()!,
                scopes,
                bindings));
        }

        Validate(definitions);
        return definitions;
    }

    private static void Validate(IReadOnlyList<ShortcutDefinition> definitions)
    {
        var definitionIds = new HashSet<string>(StringComparer.Ordinal);
        var bindingIds = new HashSet<string>(StringComparer.Ordinal);
        var gestures = new HashSet<(ShortcutScope Scope, ShortcutGesture Gesture, string ActionId)>();
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || !definitionIds.Add(definition.Id))
                throw new InvalidDataException($"Keyboard shortcut id '{definition.Id}' is empty or duplicated.");
            if (string.IsNullOrWhiteSpace(definition.GroupCode) || string.IsNullOrWhiteSpace(definition.TitleCode))
                throw new InvalidDataException($"Keyboard shortcut '{definition.Id}' needs localized group and title codes.");
            if (definition.Scopes.Count == 0 || definition.Scopes.Distinct().Count() != definition.Scopes.Count)
                throw new InvalidDataException($"Keyboard shortcut '{definition.Id}' needs unique scopes.");
            if (definition.Bindings.Count == 0)
                throw new InvalidDataException($"Keyboard shortcut '{definition.Id}' needs at least one binding.");
            foreach (var binding in definition.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Id) || !bindingIds.Add(binding.Id))
                    throw new InvalidDataException($"Keyboard shortcut binding id '{binding.Id}' is empty or duplicated.");
                if (string.IsNullOrWhiteSpace(binding.ActionId))
                    throw new InvalidDataException($"Keyboard shortcut binding '{binding.Id}' needs an action id.");
                if (binding.Gesture.Modifiers.HasFlag(ShortcutModifiers.Primary)
                    && (binding.Gesture.Modifiers.HasFlag(ShortcutModifiers.Control)
                        || binding.Gesture.Modifiers.HasFlag(ShortcutModifiers.Meta)))
                    throw new InvalidDataException($"Keyboard shortcut binding '{binding.Id}' combines Primary with an explicit platform modifier.");
                foreach (var scope in definition.Scopes)
                {
                    if (!gestures.Add((scope, binding.Gesture, binding.ActionId)))
                        throw new InvalidDataException($"Shortcut '{binding.Gesture}' repeats action '{binding.ActionId}' in scope '{scope}'.");
                }
            }
        }
        var titles = ReadUiTitles(EnglishLocale);
        var requiredCodes = definitions.SelectMany(definition => new[] { definition.GroupCode, definition.TitleCode })
            .Concat(definitions.SelectMany(definition => definition.Scopes).Distinct().Select(ScopeCode))
            .Append("shortcut-page-title");
        foreach (var code in requiredCodes.Distinct(StringComparer.Ordinal))
        {
            if (!titles.ContainsKey(code))
                throw new InvalidDataException($"Keyboard shortcut UI text '{code}' is missing from English metadata.");
        }
    }

    private static ShortcutHelpResources ReadShortcutHelp(string locale)
    {
        locale = NormalizeLocale(locale);
        var english = ReadShortcutHelpResource(EnglishLocale);
        if (locale == EnglishLocale)
            return english;
        var localized = ReadShortcutHelpResource(locale);
        return english with
        {
            Introduction = localized.Introduction ?? english.Introduction,
            KeyHeading = localized.KeyHeading ?? english.KeyHeading,
            WindowsHeading = localized.WindowsHeading ?? english.WindowsHeading,
            MacHeading = localized.MacHeading ?? english.MacHeading,
            ScopeHeading = localized.ScopeHeading ?? english.ScopeHeading,
            ActionHeading = localized.ActionHeading ?? english.ActionHeading,
            ModifierSeparator = localized.ModifierSeparator ?? english.ModifierSeparator,
            AlternativeSeparator = localized.AlternativeSeparator ?? english.AlternativeSeparator,
            KeyNames = english.KeyNames.Concat(localized.KeyNames).GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal),
            KeyLabels = english.KeyLabels.Concat(localized.KeyLabels).GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal),
            ModifierNames = english.ModifierNames.Concat(localized.ModifierNames).GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal),
        };
    }

    private static ShortcutHelpResources ReadShortcutHelpResource(string locale)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            string.Equals(name.Replace('\\', '/'), $"help/{locale}/shortcut-help.json", StringComparison.Ordinal));
        if (resourceName is null)
            return ShortcutHelpResources.Empty;
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new ShortcutHelpResources(
            OptionalString(root, "introduction"),
            OptionalString(root, "keyHeading"),
            OptionalString(root, "windowsHeading"),
            OptionalString(root, "macHeading"),
            OptionalString(root, "scopeHeading"),
            OptionalString(root, "actionHeading"),
            OptionalString(root, "modifierSeparator"),
            OptionalString(root, "alternativeSeparator"),
            ReadStringMap(root, "keyNames"),
            ReadStringMap(root, "keyLabels"),
            ReadStringMap(root, "modifierNames"));
    }

    private static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string property) =>
        root.TryGetProperty(property, out var values)
            ? values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static string NormalizeLocale(string locale) =>
        string.IsNullOrWhiteSpace(locale) || locale == "iv" ? EnglishLocale : locale;

    private static Dictionary<string, string> ReadUiTitles(string locale)
    {
        var english = ReadUiTitlesResource(EnglishLocale);
        if (locale == EnglishLocale)
            return english;
        return english.Concat(ReadUiTitlesResource(locale))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadUiTitlesResource(string locale)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            string.Equals(name.Replace('\\', '/'), $"help/{locale}/ui.json", StringComparison.Ordinal));
        if (resourceName is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToDictionary(
            item => item.Name, item => item.Value.GetProperty("title").GetString()!, StringComparer.Ordinal);
    }

    private sealed record ShortcutHelpResources(
        string? Introduction,
        string? KeyHeading,
        string? WindowsHeading,
        string? MacHeading,
        string? ScopeHeading,
        string? ActionHeading,
        string? ModifierSeparator,
        string? AlternativeSeparator,
        Dictionary<string, string> KeyNames,
        Dictionary<string, string> KeyLabels,
        Dictionary<string, string> ModifierNames)
    {
        public static ShortcutHelpResources Empty { get; } = new(
            null, null, null, null, null, null, null, null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
