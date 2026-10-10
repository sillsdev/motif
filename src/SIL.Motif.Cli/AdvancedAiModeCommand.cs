using SIL.Motif.Commands.Preferences;

namespace SIL.Motif.Cli;

internal static class AdvancedAiModeCommand
{
    private const string Usage = "Usage: motif settings advanced-ai on|off";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || args[0] != "advanced-ai" || args[1] is not "on" and not "off")
        {
            error.WriteLine(Usage);
            return 1;
        }

        var enabled = args[1] == "on";
        try
        {
            FileAdvancedAiModePreferenceStore.ForInstallation().SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            error.WriteLine("Could not save the Advanced AI mode preference: " + exception.Message);
            return 4;
        }

        output.WriteLine(enabled ? "Advanced AI mode is on." : "Advanced AI mode is off.");
        return 0;
    }
}
