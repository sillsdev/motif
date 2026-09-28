namespace SIL.Motif.Contract.Responses;

/// <summary>The closed wire values that describe whether one collected change still fits.</summary>
public static class ChangeFitStatus
{
    public const string Fits = "fits";
    public const string Uncertain = "uncertain";
    public const string NoLongerFits = "no-longer-fits";
}
