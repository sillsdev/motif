using System.Diagnostics;

namespace SIL.Motif.Host.PanGloss;

internal static class PanGlossProcessEnvironment
{
    private static readonly string[] AllowedNames = ["SystemRoot", "PATH", "TEMP", "TMP"];

    internal static void Configure(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Clear();
        foreach (var name in AllowedNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null) startInfo.Environment[name] = value;
        }
    }
}
