using System.Xml;
using System.Xml.Linq;

namespace SIL.Motif.Runner.Operations;

/// <summary>Reads a Boolean flag inside the <c>&lt;HC&gt;</c> element of ParserParameters the way FieldWorks's HCLoader casts it.</summary>
internal static class ParserParametersFlag
{
    /// <summary>
    /// The flag's value, or <paramref name="whenAbsent"/> when the XML, its <c>&lt;HC&gt;</c> element or the flag is missing.
    /// The text is read with <see cref="XmlConvert.ToBoolean(string)"/> after trimming, so <c>0</c>, <c>1</c>, <c>true</c> and
    /// <c>false</c> are read and anything else throws <see cref="FormatException"/>, as the loader does.
    /// </summary>
    internal static bool Read(string? parserParameters, string flagName, bool whenAbsent)
    {
        if (string.IsNullOrWhiteSpace(parserParameters)) return whenAbsent;
        var flag = XElement.Parse(parserParameters).Element("HC")?.Element(flagName);
        return flag is null ? whenAbsent : XmlConvert.ToBoolean(flag.Value.Trim());
    }
}
