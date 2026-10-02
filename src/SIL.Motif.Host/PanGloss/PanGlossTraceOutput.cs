namespace SIL.Motif.Host.PanGloss;

/// <summary>Maps current diagnostic format errors to invocation outcomes without losing their messages.</summary>
internal static class PanGlossTraceOutput
{
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
