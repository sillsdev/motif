using System.Diagnostics;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

internal static class PanGlossProcessEnvironment
{
    private static readonly string[] AllowedNames = ["SystemRoot", "PATH", "TEMP", "TMP"];

    /// <summary>
    /// PanGloss writes UTF-8 to its standard streams regardless of the console code page, so a redirected
    /// stream decoded any other way turns every non-ASCII word into mojibake.
    /// </summary>
    internal static readonly Encoding StreamEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal static void Configure(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Clear();
        foreach (var name in AllowedNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null) startInfo.Environment[name] = value;
        }
        startInfo.StandardOutputEncoding = StreamEncoding;
        startInfo.StandardErrorEncoding = StreamEncoding;
    }
}
