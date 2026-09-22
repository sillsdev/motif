using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.WritingSystems;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>Adds host-owned capture evidence while preserving every parser field.</summary>
internal static class TraceDiagnosticCapture
{
    internal static WordTraceResponse Attach(WordTraceResponse response, BaselineRecord baseline, ProjectLocator project)
    {
        if (string.IsNullOrEmpty(response.DiagnosticJson)) return response;
        using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var container = cache.ServiceLocator.WritingSystems;
        var inventory = WritingSystemInventoryReader.Read(cache);
        var definitions = container.CurrentVernacularWritingSystems.Concat(container.CurrentAnalysisWritingSystems).ToLookup(ws => ws.Id);
        TraceWritingSystem Describe(GrammarWritingSystem ws, bool vernacular)
        {
            var definition = definitions[ws.Id].FirstOrDefault();
            return new TraceWritingSystem(ws.Id, ws.Name, vernacular, ws.IsDefaultVernacular,
                definition is null ? null : definition.RightToLeftScript ? "rtl" : "ltr", definition?.DefaultFontName);
        }
        var systems = inventory.Vernacular.Select(ws => Describe(ws, true))
            .Concat(inventory.Analysis.Select(ws => Describe(ws, false))).ToArray();
        var capture = new TraceHostCapture(baseline.Token.ProjectIdentity, response.GrammarHash,
            response.GrammarHashSemantics, baseline.Token.BundleDigest, DateTimeOffset.UtcNow,
            response.ElapsedMs, systems);
        var projectMatches = StringComparer.Ordinal.Equals(baseline.Token.ProjectIdentity, project.FieldWorksProjectIdentity);
        var comparison = new TraceProvenanceComparison(projectMatches ? "match" : "mismatch",
            "unknown", "unknown", false,
            projectMatches
                ? "Recorded baseline evidence is shown. Current project grammar and writing systems have not been compared."
                : "The captured baseline belongs to a different project; live navigation is unavailable.")
        {
            CanNavigate = projectMatches,
        };
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var projectName = Path.GetFileNameWithoutExtension(project.FullFwDataPath);
        TraceMorph Resolve(TraceMorph morph)
        {
            string? link = null;
            if (projectMatches && morph.IdentityQuality == "authored" &&
                Guid.TryParse(morph.EntryId ?? morph.FormId ?? morph.MsaId, out var id) &&
                repository.TryGetObject(id, out var found))
                link = FieldWorksLinks.For(cache, projectName, found);
            return morph with { FieldWorksLink = link };
        }
        TraceStep ResolveStep(TraceStep step) => step with
        {
            AttemptedMorphs = step.AttemptedMorphs.Select(Resolve).ToArray(),
            Children = step.Children.Select(ResolveStep).ToArray(),
        };
        var json = JsonNode.Parse(response.DiagnosticJson, documentOptions: new JsonDocumentOptions { MaxDepth = 512 })!.AsObject();
        var host = json["hostCapture"] as JsonObject ?? new JsonObject();
        var captured = JsonSerializer.SerializeToNode(capture, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        foreach (var field in captured) host[field.Key] = field.Value?.DeepClone();
        if (json["hostCapture"] is not JsonObject) json["hostCapture"] = host;
        return response with
        {
            HostCapture = capture,
            Provenance = comparison,
            DiagnosticJson = json.ToJsonString(new JsonSerializerOptions { MaxDepth = 512 }),
            Analyses = response.Analyses.Select(analysis => analysis with { Morphs = analysis.Morphs.Select(Resolve).ToArray() }).ToArray(),
            Candidates = response.Candidates.Select(candidate => candidate with
            {
                RichMorphs = candidate.RichMorphs.Select(Resolve).ToArray(),
                Steps = candidate.Steps.Select(ResolveStep).ToArray(),
            }).ToArray(),
            Root = ResolveStep(response.Root),
        };
    }

    internal static TraceProvenanceComparison Compare(TraceHostCapture? recorded, TraceHostCapture? current)
    {
        string CompareValue(string? left, string? right) => string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)
            ? "unknown" : StringComparer.Ordinal.Equals(left, right) ? "match" : "mismatch";
        var project = CompareValue(recorded?.ProjectIdentity, current?.ProjectIdentity);
        var sameHashKind = !string.IsNullOrEmpty(recorded?.GrammarHashSemantics) &&
            StringComparer.Ordinal.Equals(recorded.GrammarHashSemantics, current?.GrammarHashSemantics);
        var grammar = sameHashKind ? CompareValue(recorded?.GrammarHash, current?.GrammarHash) : "unknown";
        var writingSystems = recorded is null || current is null || recorded.WritingSystems.Count == 0 || current.WritingSystems.Count == 0
            ? "unknown" : recorded.WritingSystems.Select(ws => (ws.Id, ws.IsVernacular, ws.IsDefault))
                .SequenceEqual(current.WritingSystems.Select(ws => (ws.Id, ws.IsVernacular, ws.IsDefault))) ? "match" : "mismatch";
        var compatible = project == "match" && grammar == "match" && writingSystems == "match";
        return new TraceProvenanceComparison(project, grammar, writingSystems, compatible,
            compatible ? "" : $"Recorded evidence retained. Project: {project}; grammar: {grammar}; writing systems: {writingSystems}. Live navigation is unavailable.");
    }
}
