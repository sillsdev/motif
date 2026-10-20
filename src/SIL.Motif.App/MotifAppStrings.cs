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

    public static string ProjectMenuAccessibleName => Text("MotifApp.ProjectMenuAccessibleName");

    public static string SelectNew => Text("MotifApp.SelectNew");

    public static string SelectNewDetail => Text("MotifApp.SelectNewDetail");

    public static string SelectNewAccessibleName => Text("MotifApp.SelectNewAccessibleName");

    public static string OpenRecent => Text("MotifApp.OpenRecent");

    public static string OpenRecentAccessibleName => Text("MotifApp.OpenRecentAccessibleName");

    public static string Configure => Text("MotifApp.Configure");

    public static string ConfigureAccessibleName => Text("MotifApp.ConfigureAccessibleName");

    public static string WritingSystemsAndFonts => Text("MotifApp.WritingSystemsAndFonts");

    public static string WritingSystemsAndFontsDetail => Text("MotifApp.WritingSystemsAndFontsDetail");

    public static string WritingSystemsAndFontsAccessibleName => Text("MotifApp.WritingSystemsAndFontsAccessibleName");

    public static string SeeWhatChanged => Text("MotifApp.SeeWhatChanged");

    public static string SeeWhatChangedAccessibleName => Text("MotifApp.SeeWhatChangedAccessibleName");

    public static string ParseAllWords => Text("MotifApp.ParseAllWords");

    public static string ParseAllWordsAccessibleName => Text("MotifApp.ParseAllWordsAccessibleName");

    public static string ParseAllWordsTip => Text("MotifApp.ParseAllWordsTip");

    public static string ChooseWhatToParse => Text("MotifApp.ChooseWhatToParse");

    public static string ChooseWhatToParseAccessibleName => Text("MotifApp.ChooseWhatToParseAccessibleName");

    public static string Refresh => Text("MotifApp.Refresh");

    public static string RefreshAccessibleName => Text("MotifApp.RefreshAccessibleName");

    public static string RefreshTip => Text("MotifApp.RefreshTip");

    public static string TechDemoStatus => Text("MotifApp.TechDemoStatus");

    public static string TechDemoStatusTip => Text("MotifApp.TechDemoStatusTip");

    public static string TechDemoCompact => Text("MotifApp.TechDemoCompact");

    public static string TechDemoCompactTip => Text("MotifApp.TechDemoCompactTip");

    public static string ReportProblem => Text("MotifApp.ReportProblem");

    public static string ReportProblemAccessibleName => Text("MotifApp.ReportProblemAccessibleName");

    public static string ReviewChangesAccessibleName => Text("MotifApp.ReviewChangesAccessibleName");

    public static string OpenWarningsAccessibleName => Text("MotifApp.OpenWarningsAccessibleName");

    public static string StatusChangesPattern => Text("MotifApp.StatusChangesPattern");

    public static string StatusWarningsPattern => Text("MotifApp.StatusWarningsPattern");

    public static string SeeParseProgressAccessibleName => Text("MotifApp.SeeParseProgressAccessibleName");

    public static string Parsing => Text("MotifApp.Parsing");

    public static string ParseProgressDetailsAccessibleName => Text("MotifApp.ParseProgressDetailsAccessibleName");

    public static string ParseAllWordsProgressAccessibleName => Text("MotifApp.ParseAllWordsProgressAccessibleName");

    public static string Cancel => Text("MotifApp.Cancel");

    public static string CancelParsingTip => Text("MotifApp.CancelParsingTip");

    public static string CancelParsingAccessibleName => Text("MotifApp.CancelParsingAccessibleName");

    public static string ReportParseProblemAccessibleName => Text("MotifApp.ReportParseProblemAccessibleName");

    public static string SeeStoppedWords => Text("MotifApp.SeeStoppedWords");

    public static string SeeStoppedWordsAccessibleName => Text("MotifApp.SeeStoppedWordsAccessibleName");

    public static string KeyboardShortcutStatusAccessibleName => Text("MotifApp.KeyboardShortcutStatusAccessibleName");
}
