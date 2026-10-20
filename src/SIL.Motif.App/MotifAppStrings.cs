namespace SIL.Motif.App;

/// <summary>
/// The window's localized text. The neutral MotifAppStrings.resx holds the English source and translations
/// would ship as satellite assemblies, as FwAvaloniaStrings does in FieldWorks. A language change takes effect on
/// restart because <c>{x:Static}</c> reads once. Automation ids stay constants in <see cref="AutomationIds"/>.
/// </summary>
public static class MotifAppStrings
{
    private static readonly System.Resources.ResourceManager Resources =
        new("SIL.Motif.App.MotifAppStrings", typeof(MotifAppStrings).Assembly);

    // Falls back to the id so a missing entry shows in the window; MotifAppStringsTests pins every accessor.
    private static string Text(string stringId) => Resources.GetString(stringId) ?? stringId;

    /// <summary>Accessible name of the project menu button in the top bar.</summary>
    public static string ProjectMenuAccessibleName => Text("MotifApp.ProjectMenuAccessibleName");

    /// <summary>Command in the project menu: pick another project file. The ellipsis means it asks for more input.</summary>
    public static string SelectNew => Text("MotifApp.SelectNew");

    /// <summary>Detail line under the Select New command.</summary>
    public static string SelectNewDetail => Text("MotifApp.SelectNewDetail");

    /// <summary>Accessible name of the Select New command.</summary>
    public static string SelectNewAccessibleName => Text("MotifApp.SelectNewAccessibleName");

    /// <summary>Command in the project menu that opens the list of recent projects.</summary>
    public static string OpenRecent => Text("MotifApp.OpenRecent");

    /// <summary>Accessible name of the Open Recent command.</summary>
    public static string OpenRecentAccessibleName => Text("MotifApp.OpenRecentAccessibleName");

    /// <summary>Command in the project menu that opens the project configuration.</summary>
    public static string Configure => Text("MotifApp.Configure");

    /// <summary>Accessible name of the Configure command.</summary>
    public static string ConfigureAccessibleName => Text("MotifApp.ConfigureAccessibleName");

    /// <summary>Command in the project menu that opens the writing system guide.</summary>
    public static string WritingSystemsAndFonts => Text("MotifApp.WritingSystemsAndFonts");

    /// <summary>Detail line under the Writing Systems and Fonts command.</summary>
    public static string WritingSystemsAndFontsDetail => Text("MotifApp.WritingSystemsAndFontsDetail");

    /// <summary>Accessible name of the Writing Systems and Fonts command.</summary>
    public static string WritingSystemsAndFontsAccessibleName => Text("MotifApp.WritingSystemsAndFontsAccessibleName");

    /// <summary>Top bar button that shows what a refresh changed.</summary>
    public static string SeeWhatChanged => Text("MotifApp.SeeWhatChanged");

    /// <summary>Accessible name of the See What Changed button.</summary>
    public static string SeeWhatChangedAccessibleName => Text("MotifApp.SeeWhatChangedAccessibleName");

    /// <summary>Top bar button that parses the words the user chose.</summary>
    public static string ParseAllWords => Text("MotifApp.ParseAllWords");

    /// <summary>Accessible name of the Parse All Words button.</summary>
    public static string ParseAllWordsAccessibleName => Text("MotifApp.ParseAllWordsAccessibleName");

    /// <summary>Tooltip of the Parse All Words button.</summary>
    public static string ParseAllWordsTip => Text("MotifApp.ParseAllWordsTip");

    /// <summary>Top bar button that opens the choice of what to parse.</summary>
    public static string ChooseWhatToParse => Text("MotifApp.ChooseWhatToParse");

    /// <summary>Accessible name of the Choose What to Parse button.</summary>
    public static string ChooseWhatToParseAccessibleName => Text("MotifApp.ChooseWhatToParseAccessibleName");

    /// <summary>Top bar button that captures a new Baseline.</summary>
    public static string Refresh => Text("MotifApp.Refresh");

    /// <summary>Accessible name of the Refresh button.</summary>
    public static string RefreshAccessibleName => Text("MotifApp.RefreshAccessibleName");

    /// <summary>Tooltip of the Refresh button. Baseline is a Motif glossary term.</summary>
    public static string RefreshTip => Text("MotifApp.RefreshTip");

    /// <summary>Status bar notice that Motif writes only when the user presses Apply.</summary>
    public static string TechDemoStatus => Text("MotifApp.TechDemoStatus");

    /// <summary>Tooltip of the status bar tech demo notice.</summary>
    public static string TechDemoStatusTip => Text("MotifApp.TechDemoStatusTip");

    /// <summary>Status bar button shown when the window is too narrow for the full notice.</summary>
    public static string TechDemoCompact => Text("MotifApp.TechDemoCompact");

    /// <summary>Tooltip of the compact Tech Demo button.</summary>
    public static string TechDemoCompactTip => Text("MotifApp.TechDemoCompactTip");

    /// <summary>Command that opens the problem report.</summary>
    public static string ReportProblem => Text("MotifApp.ReportProblem");

    /// <summary>Accessible name of the Report a Problem command.</summary>
    public static string ReportProblemAccessibleName => Text("MotifApp.ReportProblemAccessibleName");

    /// <summary>Accessible name of the status bar changes count button.</summary>
    public static string ReviewChangesAccessibleName => Text("MotifApp.ReviewChangesAccessibleName");

    /// <summary>Accessible name of the status bar warnings count button.</summary>
    public static string OpenWarningsAccessibleName => Text("MotifApp.OpenWarningsAccessibleName");

    /// <summary>Plural pattern: the words after the status bar changes count. Keeps its leading space.</summary>
    public static string StatusChangesNounPattern => Text("MotifApp.StatusChangesNounPattern");

    /// <summary>Plural pattern: the word after the status bar warnings count. Keeps its leading space.</summary>
    public static string StatusWarningsNounPattern => Text("MotifApp.StatusWarningsNounPattern");

    /// <summary>Accessible name of the status bar parse progress button.</summary>
    public static string SeeParseProgressAccessibleName => Text("MotifApp.SeeParseProgressAccessibleName");

    /// <summary>Status bar label shown while parsing when the window is narrow.</summary>
    public static string Parsing => Text("MotifApp.Parsing");

    /// <summary>Accessible name of the parse progress text.</summary>
    public static string ParseProgressDetailsAccessibleName => Text("MotifApp.ParseProgressDetailsAccessibleName");

    /// <summary>Accessible name of the parse progress bar.</summary>
    public static string ParseAllWordsProgressAccessibleName => Text("MotifApp.ParseAllWordsProgressAccessibleName");

    /// <summary>Button that cancels parsing.</summary>
    public static string Cancel => Text("MotifApp.Cancel");

    /// <summary>Tooltip of the Cancel button while parsing.</summary>
    public static string CancelParsingTip => Text("MotifApp.CancelParsingTip");

    /// <summary>Accessible name of the Cancel button while parsing.</summary>
    public static string CancelParsingAccessibleName => Text("MotifApp.CancelParsingAccessibleName");

    /// <summary>Accessible name of the Report a Problem button when parsing stalls.</summary>
    public static string ReportParseProblemAccessibleName => Text("MotifApp.ReportParseProblemAccessibleName");

    /// <summary>Header of the expander listing words whose parse stopped.</summary>
    public static string SeeStoppedWords => Text("MotifApp.SeeStoppedWords");

    /// <summary>Accessible name of the stopped words expander.</summary>
    public static string SeeStoppedWordsAccessibleName => Text("MotifApp.SeeStoppedWordsAccessibleName");

    /// <summary>Accessible name of the status bar line that announces keyboard shortcut results.</summary>
    public static string KeyboardShortcutStatusAccessibleName => Text("MotifApp.KeyboardShortcutStatusAccessibleName");
}
