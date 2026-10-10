using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.SampleProjects;

namespace SIL.Motif.EvalSets;

internal static class OutputMeasurement
{
    internal static async Task<object> RunAsync(string language, string start, string proposalPath,
        string outputRoot, string wordsRoot)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(LexicalSenseOperationKinds).Module.ModuleHandle);
        var json = await File.ReadAllTextAsync(proposalPath);
        var proposal = json.Trim() == "null" ? null : ProposalJsonParser.Parse(json);
        if (proposal is not null && (proposal.Operations.Count > 1024 || proposal.Requires.Count > 0))
            throw new InvalidDataException("The output exceeds the task's Proposal size or dependency boundary.");
        var build = EvaluationProjectBuilder.Build(language, outputRoot, start);
        if (proposal is not null)
        {
            var loader = new FwDataProjectLoader();
            using var scratch = DryRunScratch.Adopt(loader.LoadScratchCache(build.ProjectPath), "independent task input");
            ProposalDryRunner.Run(scratch, proposal);
            loader.Save(scratch.PeekCache());
        }
        var operations = proposal?.Operations.Select(operation => new ProposalOperationView(
            operation.OperationId.Value, operation.Kind, operation.Target?.Value, operation.EntityId?.Value,
            operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray(),
            operation.After?.GetRawText())).ToArray() ?? [];
        var projection = new { proposalId = proposal?.ProposalId.Value, operations };
        var parserPath = PanGlossExecutable.TryLocate()
            ?? throw new InvalidOperationException(PanGlossExecutable.NotFoundMessage);
        var words = File.ReadAllLines(Path.Combine(wordsRoot, "words", "heldout.txt"))
            .Concat(File.ReadAllLines(Path.Combine(wordsRoot, "words", "negative.txt")))
            .Select(word => word.Trim()).Where(word => word.Length > 0).ToArray();
        if (words.Length == 0 || words.Distinct(StringComparer.Ordinal).Count() != words.Length)
            throw new InvalidDataException("Measurement words must be nonempty and unique.");
        using var invoker = new PanGlossInvoker(parserPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(build.ProjectPath, words, TimeSpan.FromSeconds(1)) { CollectAnalyses = true },
            "output-measurement", timeout.Token, TimeSpan.FromMinutes(10));
        if (outcome is not PanGlossOutcome.Completed completed)
            throw new InvalidOperationException(outcome.Message);
        var rows = ParseMorphEvidence.Read(completed.MorphologyOutput ?? "", words);
        return new { proposal = projection, parserRows = rows };
    }
}
