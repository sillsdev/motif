using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A synthetic project whose one Draft holds an operation kind the product does not know, so reading it as a
/// Proposal fails the contract parse, with a Trial recorded against that Draft.
/// </summary>
public sealed class UnreadableProposalProject : IDisposable
{
    public const string UnknownKind = "lexical/lexSense/nope";

    private readonly string _directory;

    private UnreadableProposalProject(string directory, string projectPath, string proposalId, string draftName)
    {
        _directory = directory;
        ProjectPath = projectPath;
        ProposalId = proposalId;
        DraftName = draftName;
    }

    public string DirectoryPath => _directory;

    public string ProjectPath { get; }

    public string ProposalId { get; }

    public string DraftName { get; }

    public static UnreadableProposalProject Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-unreadable-proposal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(projectPath, "synthetic project marker");
        var proposalId = CanonicalId.Mint("proposal/").Value;
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.Mint("lexSense/").Value;
        var readableJson = ProposalJson(proposalId, operationId, target, "lexical/lexSense/setGloss");
        var draftJson = ProposalJson(proposalId, operationId, target, UnknownKind);
        var jobInput = JsonSerializer.Serialize(new
        {
            proposalJson = readableJson,
            scope = (string?)null,
            words = new[] { "cat" },
            allWords = false,
            baselineAssessmentIds = (string[]?)null,
            limits = (object?)null,
        });
        var seeded = ProjectStoreCommand.Run(projectPath, "1.0", (database, locator) =>
        {
            new ProposalRepository(database).CreateDraft("unreadable", CanonicalId.Parse(proposalId), draftJson);
            var jobs = new JobRepository(database);
            var seedJob = jobs.Create("job/seed", ProjectWorkspaceKey.Compute(locator), JobCommands.TrialKind, jobInput,
                "2026-09-24T11:00:00Z");
            jobs.Transition(seedJob.JobId, JobStatus.Running);
            return CommandOutcome<string>.Success("seeded");
        });
        if (!seeded.Succeeded) throw new InvalidOperationException(seeded.Refusal?.Message);
        return new UnreadableProposalProject(directory, projectPath, proposalId, "unreadable");
    }

    private static string ProposalJson(string proposalId, string operationId, string target, string kind) =>
        JsonSerializer.Serialize(new
        {
            proposalId,
            contractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" },
            operations = new[]
            {
                new { operationId, kind, target, after = new Dictionary<string, string> { ["ws"] = "en", ["text"] = "x" } },
            },
        });

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
