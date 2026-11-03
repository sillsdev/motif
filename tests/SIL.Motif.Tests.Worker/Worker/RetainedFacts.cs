using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Config;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;

namespace SIL.Motif.Tests.Worker;

// A build discards its grammar facts; tests that read what PanGloss wrote ask the pipeline to keep a copy.
internal static class RetainedFacts
{
    public static EvidenceBundleChecks Checks(Action<string>? factsIntegrity = null) =>
        Retaining(EvidenceBundleChecks.Create(factsIntegrity));

    public static EvidenceBundleChecks Retaining(EvidenceBundleChecks checks) => checks with
    {
        BeforeFactsDiscard = (facts, staging) => File.Copy(facts, Path.Combine(staging, "grammar-facts.sqlite")),
    };

    public static ParsimonyJobHandler Handler(MotifDatabase database, BaselineRepository baselines, string projectKey,
        ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker,
        IEvidencePublicationHooks? publicationHooks = null, Action<string>? factsIntegrity = null) =>
        new(database, baselines, projectKey, project, options, invoker, publicationHooks, Checks(factsIntegrity),
            FileDigestCache.Shared);

    public static ParsimonyCandidateEvidenceBuilder Builder(MotifDatabase database, string projectKey,
        ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker, ProjectLaneRegistry lanes,
        Action<string>? factsIntegrity = null) =>
        new(database, projectKey, project, options, invoker, lanes, Checks(factsIntegrity), FileDigestCache.Shared);
}
