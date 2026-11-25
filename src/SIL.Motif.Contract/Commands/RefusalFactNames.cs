namespace SIL.Motif.Contract.Commands;

/// <summary>
/// Names of <see cref="Refusal.Facts"/> entries that a caller branches on, not only displays.
/// </summary>
public static class RefusalFactNames
{
    /// <summary>
    /// Present, with the value <c>true</c>, on a parser-unavailable refusal whose cause is that no PanGloss
    /// executable was found. Absent when PanGloss was found but could not be run.
    /// </summary>
    public const string ParserNotFound = "parserNotFound";

    /// <summary>The full path of a refused Motif store file, on a <see cref="RefusalCodes.StoreOtherVersion"/> refusal.</summary>
    public const string StorePath = "storePath";

    /// <summary>The project file a refused store is registered to, on a <see cref="RefusalCodes.StoreOtherProject"/> refusal.</summary>
    public const string RegisteredProjectPath = "registeredProjectPath";

    /// <summary>
    /// Present, with the file's path, on a project-store refusal whose cause is that the <c>.use.lock</c> file
    /// beside the Motif store is missing, as when the store was copied or moved without it.
    /// </summary>
    public const string StoreLockMissing = "storeLockMissing";

    /// <summary>Present, as "true", on a store.other-version refusal whose store is older and a normal open upgrades it.</summary>
    public const string StoreUpgradable = "storeUpgradable";

    /// <summary>Present, as "true", on a store.other-version refusal whose store a newer Motif made: never reset or deleted; install the newer Motif or restore a pre-upgrade backup.</summary>
    public const string StoreNewer = "storeNewer";

    /// <summary>Fact key for the parser refusal code on a grammarcheck.not-run refusal.</summary>
    public const string LastParserRefusalCode = "lastParserRefusalCode";

    /// <summary>Fact key for the parser refusal reason on a grammarcheck.not-run refusal.</summary>
    public const string LastParserRefusalReason = "lastParserRefusalReason";

    /// <summary>Fact key for the parser refusal message on a grammarcheck.not-run refusal.</summary>
    public const string LastParserRefusalMessage = "lastParserRefusalMessage";

    /// <summary>Prefix for parser fact keys on a grammarcheck.not-run refusal.</summary>
    public const string LastParserRefusalFactPrefix = "lastParserRefusal.fact.";
}
