using SIL.Motif.Contract;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Identifies Motif's staged parser input contracts.</summary>
/// <remarks>
/// Increment a format's version when Motif changes the contract by which it prepares that parser input.
/// These versions describe Motif's staging, not the parser's wire schema or the source application's schema.
/// </remarks>
public static class GrammarExportFormats
{
    /// <summary>The current contract for staging a FieldWorks project snapshot.</summary>
    public static readonly GrammarExportFormat FwData = new("motif-fwdata-snapshot", 1);

    /// <summary>The current contract for staging an HC XML grammar.</summary>
    public static readonly GrammarExportFormat HcXml = new("motif-hcxml-snapshot", 1);

    /// <summary>Returns the known staging contract for a grammar path, or null for an unknown format.</summary>
    public static GrammarExportFormat? ForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".fwdata" => FwData,
        ".xml" => HcXml,
        _ => null,
    };
}
