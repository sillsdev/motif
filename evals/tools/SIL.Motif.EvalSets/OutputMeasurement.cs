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
    internal static async Task<object> RunAsync(string language, string start, string proposalPath,
        string outputRoot, string wordsRoot)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(LexicalSenseOperationKinds).Module.ModuleHandle);
        var json = await File.ReadAllTextAsync(proposalPath);
        var build = EvaluationProjectBuilder.Build(language, outputRoot, start);
        var loader = new FwDataProjectLoader();
        using var scratch = DryRunScratch.Adopt(loader.LoadScratchCache(build.ProjectPath), "independent task input");
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
            parserRows = ParseMorphEvidence.Read(completed.MorphologyOutput ?? "", words);
        }
        return new { proposal = projection, proposalFailure, lexiconBefore, lexiconAfter, parserRows };
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
