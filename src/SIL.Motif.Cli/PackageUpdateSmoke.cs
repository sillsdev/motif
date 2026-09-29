using SIL.Motif.Host.Installation;
using Velopack;

namespace SIL.Motif.Cli;

internal static class PackageUpdateSmoke
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: motif --update-smoke <local-feed> <channel> <expected-version>");
            return 1;
        }

        using var updateLease = MotifUpdateGate.TryAcquireForUpdate();
        if (updateLease is null)
        {
            Console.Error.WriteLine("Motif cannot update while a Worker job, Assessment, Handoff, or pending apply is running.");
            return 3;
        }

        try
        {
            var feedDirectory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(feedDirectory))
                throw new DirectoryNotFoundException("The update feed does not exist: " + feedDirectory);

            var manager = new UpdateManager(feedDirectory, new UpdateOptions { ExplicitChannel = args[2] });
            if (!manager.IsInstalled)
                throw new InvalidOperationException("Velopack could not locate this installed Motif package.");

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The local feed did not offer an update.");
            var targetVersion = update.TargetFullRelease.Version.ToString();
            if (!string.Equals(targetVersion, args[3], StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The local feed offered " + targetVersion + ", expected " + args[3] + ".");

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            Console.Error.WriteLine("Applying Motif update " + targetVersion + ".");
            manager.ApplyUpdatesAndExit(update.TargetFullRelease);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Motif update smoke failed: " + exception.Message);
            return 1;
        }
    }
}
