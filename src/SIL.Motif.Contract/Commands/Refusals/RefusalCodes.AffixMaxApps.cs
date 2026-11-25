namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>An affix repeat count is not a whole number from 1 to the cap.</summary>
    public const string AffixMaxAppsInvalidCount = "affix-max-apps.invalid-count";
    /// <summary>The id given for an affix repeat count is not an affix's morphosyntactic analysis.</summary>
    public const string AffixMaxAppsNotAffixMsa = "affix-max-apps.not-affix-msa";
    /// <summary>The project's repeat count for the affix is not the one the intent expected.</summary>
    public const string AffixMaxAppsStale = "affix-max-apps.stale";
}
