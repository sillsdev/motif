namespace SIL.Motif.Host.PanGloss;

/// <summary>Compatibility adapter for the original internal v1 reader.</summary>
internal static class PanGlossTraceOutput
{
    internal const string Schema = PanGlossTraceDiagnosticReader.SchemaV1;

    internal static bool TryParse(
        string standardOutput, out string signature, out PanGlossTraceNode? root, out PanGlossTraceDetails? details)
    {
        if (TryRead(standardOutput, out var document, out _))
        {
            signature = document!.Signature;
            root = document.Root;
            details = document.Details;
            return true;
        }

        signature = string.Empty;
        root = null;
        details = null;
        return false;
    }

    internal static bool TryRead(
        string standardOutput, out PanGlossTraceDiagnosticDocument? document, out string error)
    {
        try
        {
            document = PanGlossTraceDiagnosticReader.Read(standardOutput);
            error = string.Empty;
            return true;
        }
        catch (PanGlossTraceDiagnosticFormatException exception)
        {
            document = null;
            error = exception.Message;
            return false;
        }
    }
}
