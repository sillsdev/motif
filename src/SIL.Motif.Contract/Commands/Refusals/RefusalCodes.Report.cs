namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A report named an Assessment that is not in the store.</summary>
    public const string ReportAssessmentNotFound = "report.assessment-not-found";
    /// <summary>A report kind is not one the product defines.</summary>
    public const string ReportInvalidKind = "report.invalid-kind";
    /// <summary>A report could not be produced from the named Assessment.</summary>
    public const string ReportRefused = "report.refused";
}
