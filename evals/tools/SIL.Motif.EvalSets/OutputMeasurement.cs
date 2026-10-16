using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Projection;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.SampleProjects;

namespace SIL.Motif.EvalSets;

internal static class OutputMeasurement
{
    /// <summary>
    /// Applies the frozen Proposal to a copy of <paramref name="startProjectDirectory"/>, the start project the
    /// agent worked on. A rebuild would give new ids to the objects LibLCM creates without a stable id, such as
    /// an affix's MSA, so a Proposal that targets one could not apply.
    /// </summary>
    /// <param name="wordLists">
    /// The word lists under <c>words/</c> to parse; a list only some task graders read, such as <c>train</c>, is parsed
    /// only when the caller names it, so other tasks' parser rows stay what they were.
    /// </param>
    internal static async Task<object> RunAsync(string startProjectDirectory, string proposalPath,
        string outputRoot, string wordsRoot, IReadOnlyList<string>? wordLists = null)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(LexicalSenseOperationKinds).Module.ModuleHandle);
        var json = await File.ReadAllTextAsync(proposalPath);
        var projectPath = CopyStartProject(startProjectDirectory, Path.Combine(outputRoot, "project"));
        var loader = new FwDataProjectLoader();
        using var scratch = DryRunScratch.Adopt(loader.LoadScratchCache(projectPath), "independent task input");
        var lexiconBefore = ReadLexicon(scratch.PeekCache());
        Proposal? proposal = null;
        object? proposalFailure = null;
        try
        {
            proposal = json.Trim() == "null" ? null : ProposalJsonParser.Parse(json);
            if (proposal is not null && (proposal.Operations.Count > 1024 || proposal.Requires.Count > 0))
                throw new InvalidDataException("The Proposal exceeds its size or dependency boundary.");
            if (proposal is not null) ProposalDryRunner.Run(scratch, proposal);
        }
        catch (Exception exception) when (IsAgentProposalFailure(exception))
        {
            proposal = null;
            proposalFailure = new { code = exception.GetType().Name, evidence = exception.Message };
        }
        if (proposal is not null) loader.Save(scratch.PeekCache());
        var lexiconAfter = ReadLexicon(scratch.PeekCache());
        var operations = proposal?.Operations.Select(operation => new ProposalOperationView(
            operation.OperationId.Value, operation.Kind, operation.Target?.Value, operation.EntityId?.Value,
            operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray(),
            operation.After?.GetRawText())).ToArray() ?? [];
        var projection = new { proposalId = proposal?.ProposalId.Value, operations };
        object parserRows = Array.Empty<object>();
        if (proposalFailure is null)
        {
            var parserPath = PanGlossExecutable.TryLocate()
                ?? throw new InvalidOperationException(PanGlossExecutable.NotFoundMessage);
            var words = (wordLists ?? DefaultWordLists)
                .SelectMany(list => File.ReadAllLines(Path.Combine(wordsRoot, "words", list + ".txt")))
                .Select(word => word.Trim()).Where(word => word.Length > 0).ToArray();
            if (words.Length == 0 || words.Distinct(StringComparer.Ordinal).Count() != words.Length)
                throw new InvalidDataException("Measurement words must be nonempty and unique.");
            using var invoker = new PanGlossInvoker(parserPath);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Batch(projectPath, words, TimeSpan.FromSeconds(1)) { CollectAnalyses = true },
                "output-measurement", timeout.Token, TimeSpan.FromMinutes(10));
            if (outcome is not PanGlossOutcome.Completed completed)
                throw new InvalidOperationException(outcome.Message);
            parserRows = ParseMorphEvidence.Read(completed.MorphologyOutput ?? "", words);
        }
        return new { proposal = projection, proposalFailure, lexiconBefore, lexiconAfter, parserRows };
    }

    /// <summary>The word lists every task's graders read.</summary>
    internal static readonly IReadOnlyList<string> DefaultWordLists = ["heldout", "negative"];

    private static string CopyStartProject(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The trial's start project is missing: {source}");
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        return Directory.EnumerateFiles(destination, "*.fwdata", SearchOption.AllDirectories).Single();
    }

    private static bool IsAgentProposalFailure(Exception exception) => exception is
        ArgumentException or FormatException or InvalidDataException or InvalidOperationException or
        KeyNotFoundException or JsonException;

    private static object ReadLexicon(LcmCache cache) => new
    {
        defaultVernacularWritingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
        entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .OrderBy(entry => Id(entry), StringComparer.Ordinal)
            .Select(entry => new
            {
                id = Id(entry),
                headword = entry.HeadWord?.Text?.Normalize(System.Text.NormalizationForm.FormD),
                lexemeForm = entry.LexemeFormOA is null ? [] : ReadAlternatives(cache, entry.LexemeFormOA.Form),
                alternateForms = entry.AlternateFormsOS.Select(form => new
                {
                    id = Id(form),
                    forms = ReadAlternatives(cache, form.Form),
                }).ToArray(),
                senses = entry.AllSenses.Select(sense => new
                {
                    id = Id(sense),
                    glosses = ReadAlternatives(cache, sense.Gloss),
                    category = sense.MorphoSyntaxAnalysisRA?.ClassName,
                    categoryName = sense.MorphoSyntaxAnalysisRA switch
                    {
                        IMoStemMsa stem => WritingSystemTextReader.BestAnalysis(cache, stem.PartOfSpeechRA?.Name).Text,
                        IMoInflAffMsa affix => WritingSystemTextReader.BestAnalysis(cache, affix.PartOfSpeechRA?.Name).Text,
                        IMoDerivAffMsa derivation => WritingSystemTextReader.BestAnalysis(cache, derivation.FromPartOfSpeechRA?.Name).Text,
                        IMoDerivStepMsa step => WritingSystemTextReader.BestAnalysis(cache, step.PartOfSpeechRA?.Name).Text,
                        IMoUnclassifiedAffixMsa unclassified => WritingSystemTextReader.BestAnalysis(cache, unclassified.PartOfSpeechRA?.Name).Text,
                        _ => null,
                    },
                }).ToArray(),
            }).ToArray(),
    };

    private static object[] ReadAlternatives(LcmCache cache, IMultiAccessorBase value) =>
        value.AvailableWritingSystemIds.Order()
            .Select(ws => new
            {
                writingSystem = cache.WritingSystemFactory.GetStrFromWs(ws),
                text = value.get_String(ws)?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text ?? string.Empty,
            })
            .Where(alternative => alternative.text.Length > 0)
            .Cast<object>()
            .ToArray();

    private static string Id(ICmObject target) => CanonicalId.FromGuid(target.Guid).Value;
}
