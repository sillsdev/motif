using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

internal static class DeclaredTrialGatePolicy
{
    internal static string Digest(ProjectConfiguration configuration) => Hash(new
    {
        sets = configuration.ReviewWordSets.Where(set => configuration.GateWordSets!.Contains(set.Id, StringComparer.Ordinal))
            .OrderBy(set => set.Id, StringComparer.Ordinal).Select(set => new { set.Id, set.Role, set.Selection, set.Manifest }),
        gainGate = configuration.GainGate,
        blockOn = configuration.ParsimonyBlockOn.Order(StringComparer.Ordinal),
        fold = configuration.ParsimonyFold,
    });

    internal static TrialGateDeclaration? Capture(ProjectConfiguration configuration, FrozenReviewInputs inputs)
    {
        if (configuration.GateWordSets is not { } ids) return null;
        var sets = inputs.Sets.Where(set => ids.Contains(set.Id, StringComparer.Ordinal)).OrderBy(set => set.Id, StringComparer.Ordinal)
            .Select(set => new TrialGateSet(set.Id, set.SourceDigest, set.MembershipDigest, set.ExpectationDigest)).ToArray();
        if (sets.Length != ids.Count) throw new InvalidOperationException("A required gate set was omitted from capture.");
        var cases = inputs.Cases.Where(item => item.Memberships.Any(member => ids.Contains(member.SetId, StringComparer.Ordinal)));
        return new TrialGateDeclaration(Digest(configuration), Hash(new { sets, cases }), sets);
    }

    internal static bool Current(MotifDatabase database, ProjectLocator project, ProjectConfiguration configuration,
        TrialGateDeclaration declaration)
    {
        if (configuration.GateWordSets is not { } ids || Digest(configuration) != declaration.PolicyDigest ||
            !ids.ToHashSet(StringComparer.Ordinal).SetEquals(declaration.WordSets.Select(set => set.Id))) return false;
        var configurationPath = ProjectConfigurationReader.PathFor(project);
        foreach (var pin in declaration.WordSets)
        {
            var set = configuration.ReviewWordSets.Single(item => item.Id == pin.Id);
            string digest;
            if (set.Manifest is not null)
                digest = ReviewWordSetManifestReader.Read(set.ResolveManifestPath(configurationPath)!, set.Role).SourceDigest;
            else
            {
                var selection = new NamedSelectionRepository(database).Get(set.Selection!);
                if (selection is null) return false;
                digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(selection)));
            }
            if (digest != pin.SourceDigest) return false;
        }
        return true;
    }

    private static string Hash<T>(T value) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, MotifJson.CreateOptions())));
}
