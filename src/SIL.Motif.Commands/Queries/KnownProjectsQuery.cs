using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;

namespace SIL.Motif.Commands.Queries;

/// <summary>One Known project ready to display in a project picker.</summary>
public sealed record KnownProjectSummary(string FullFwDataPath, DateTimeOffset LastSeenUtc);

/// <summary>
/// Lists the machine store's Known projects for the application's project picker. Read-only and outside
/// the command catalog: it changes nothing on the store it lists from and has no CLI verb, so the parity
/// test that pins catalogued commands against CLI verbs does not enumerate it.
/// </summary>
public static class KnownProjectsQuery
{
    /// <summary>Lists Known projects, most-recently-seen first, resolving the real installation's managed root.</summary>
    public static IReadOnlyList<KnownProjectSummary> List() => List(RunnerOptions.ResolveRoot());

    /// <summary>Lists Known projects under an explicitly supplied managed root, for a test's own disposable root.</summary>
    public static IReadOnlyList<KnownProjectSummary> List(string managedRoot)
    {
        using var machine = MachineDatabase.Open(managedRoot);
        var registry = new KnownProjectRegistry(machine);
        var kept = new List<KnownProjectSummary>();
        foreach (var known in registry.List())
        {
            // A project whose file is gone cannot be picked, so it is forgotten rather than merely skipped.
            if (!File.Exists(known.FullFwDataPath))
            {
                registry.Forget(known.WorkspaceKey);
                continue;
            }
            kept.Add(new KnownProjectSummary(known.FullFwDataPath, known.LastSeenUtc));
        }
        return kept.OrderByDescending(project => project.LastSeenUtc).ToList();
    }
}
