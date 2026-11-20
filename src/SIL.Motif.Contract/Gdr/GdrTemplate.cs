namespace SIL.Motif.Contract.Gdr;

/// <summary>The fixed parts of a Grammar Guide section, in template order.</summary>
public static class GdrTemplate
{
    public const string HowItWorks = "How it works";
    public const string Examples = "Examples";
    public const string GroupingCommands = "Grouping commands";
    public const string Exceptions = "Exceptions";
    public const string Decisions = "Decisions";
    public const string OpenQuestions = "Open questions";
    public const string InTheGrammar = "In the grammar";

    /// <summary>The template headings in the order a section lists them.</summary>
    public static IReadOnlyList<string> Headings { get; } =
        [HowItWorks, Examples, GroupingCommands, Exceptions, Decisions, OpenQuestions, InTheGrammar];

    /// <summary>The info string that tags a query definition block.</summary>
    public const string QueryBlockTag = "motif-query";

    /// <summary>The info string that tags a display directive block.</summary>
    public const string ShowBlockTag = "motif-show";
}
