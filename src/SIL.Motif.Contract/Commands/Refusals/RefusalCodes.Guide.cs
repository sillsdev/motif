namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>Two lists share the Grammar Guide name, or two records share an abbreviation.</summary>
    public const string GdrAmbiguous = "guide.ambiguous";
    /// <summary>The exact Baseline file is missing, so no Grammar Guide section can be read.</summary>
    public const string GdrBaselineUnavailable = "guide.baseline-unavailable";
    /// <summary>The current Baseline is not the one the request named.</summary>
    public const string GdrBaselineChanged = "guide.baseline-changed";
    /// <summary>A style in the project is based on itself, so no style can be resolved.</summary>
    public const string GdrStyleCycle = "guide.style-cycle";
    /// <summary>A project path for a link names more than one object.</summary>
    public const string GdrLinkAmbiguous = "guide.link-ambiguous";
    /// <summary>A project path for a link names an object no FieldWorks tool opens.</summary>
    public const string GdrLinkNoTool = "guide.link-no-tool";
    /// <summary>A project path for a link names nothing in the project.</summary>
    public const string GdrLinkNotFound = "guide.link-not-found";
    /// <summary>A link's project path is not a path to an object.</summary>
    public const string GdrLinkPathInvalid = "guide.link-path-invalid";
    /// <summary>A guide show request named no section.</summary>
    public const string GdrEmptyId = "guide.empty-id";
    /// <summary>The project has no Baseline to read the Grammar Guide from.</summary>
    public const string GdrNoBaseline = "guide.no-baseline";
    /// <summary>No Grammar Guide section has that id or number.</summary>
    public const string GdrNotFound = "guide.not-found";
    public const string GdrRecordMoved = "guide.record-moved";
    /// <summary>The title given for a new Grammar Guide section is empty, spans lines or names an unknown writing system.</summary>
    public const string GdrTitleInvalid = "guide.title-invalid";
    /// <summary>The project's Guide list still has its old name, which Motif reads but does not add to.</summary>
    public const string GdrListNotWritable = "guide.list-not-writable";
    /// <summary>The Markdown given for a Grammar Guide section cannot be read.</summary>
    public const string GdrMarkdownInvalid = "guide.markdown-invalid";
    /// <summary>A Grammar Guide judgement call is missing its option or its reason.</summary>
    public const string GdrDecisionInvalid = "guide.decision-invalid";
    /// <summary>A row of a batch of judgement calls cannot be read or staged, so the whole batch was refused and nothing was staged.</summary>
    public const string GdrDecisionsInvalid = "guide.decisions-invalid";
}
