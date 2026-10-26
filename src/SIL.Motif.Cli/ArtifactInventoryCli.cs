using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Diagnostics;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Cli;

internal static class ArtifactInventoryCli
{
    private const string UsageLine =
        "Usage: motif store artifacts --root <worker-root> --stores <stores.json> --json";

    internal static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Count >= 2 && args[0] == "store" && args[1] == "artifacts";

    internal static int Run(string[] args, TextWriter output, TextWriter error, string? developerCommands)
    {
        var asJson = args.Contains("--json", StringComparer.Ordinal);
        var descriptor = CommandCatalog.All.Single(command => command.Name == "store artifacts");
        var policy = CommandSurfacePolicy.FromEnvironment(developerCommands, advancedAiModeEnabled: false);
        if (!policy.IsAvailable(descriptor))
        {
            var availability = CommandAvailabilityFacts.For(descriptor);
            return Fail(CommandAvailabilityText.Render(availability), asJson, error, FailureReason.Refused,
                availability.RefusalCode);
        }

        var parsed = Parse(args.Skip(2).ToArray());
        if (parsed.Error is not null)
            return Fail(parsed.Error, asJson, error, FailureReason.InvalidArgument);

        if (!asJson)
            return Fail("The artifact inventory writes its structured report only with --json.",
                false, error, FailureReason.InvalidArgument);

        try
        {
            var inventory = ArtifactRetentionCommands.Inventory(
                new ArtifactInventoryRequest(parsed.RootPath!, parsed.StoresPath!));
            output.WriteLine(ProjectionJson.Serialize(inventory));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or NotSupportedException or ArgumentException or System.Security.SecurityException or
            System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return Fail(exception.Message, true, error, FailureReason.InvalidArgument);
        }
    }

    private static ParsedArguments Parse(IReadOnlyList<string> args)
    {
        string? root = null;
        string? stores = null;
        for (var index = 0; index < args.Count; index++)
        {
            var flag = args[index];
            if (flag == "--json") continue;
            if (flag is not ("--root" or "--stores"))
                return ParsedArguments.Failed($"Unrecognized artifact inventory argument '{flag}'.");
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                return ParsedArguments.Failed($"{flag} needs a value.");
            var value = args[++index];
            if (flag == "--root")
            {
                if (root is not null) return ParsedArguments.Failed("--root may be supplied once.");
                root = value;
            }
            else
            {
                if (stores is not null) return ParsedArguments.Failed("--stores may be supplied once.");
                stores = value;
            }
        }

        if (root is null || stores is null)
            return ParsedArguments.Failed(UsageLine);
        return new ParsedArguments(root, stores, null);
    }

    private static int Fail(string message, bool asJson, TextWriter error,
        FailureReason reason, string? code = null)
    {
        var exitCode = FailureEnvelope.ExitCodeFor(reason);
        if (asJson)
            error.WriteLine(ProjectionJson.Serialize(new FailureEnvelope(reason, message, code: code)));
        else
        {
            error.WriteLine(message);
            error.WriteLine(UsageLine);
        }
        return exitCode;
    }

    private sealed record ParsedArguments(string? RootPath, string? StoresPath, string? Error)
    {
        public static ParsedArguments Failed(string message) => new(null, null, message);
    }
}
