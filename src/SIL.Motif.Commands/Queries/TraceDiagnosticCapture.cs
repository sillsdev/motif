using SIL.Motif.Contract.Responses;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
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
        var sourceIdentity = ReadSourceIdentity(project.FullFwDataPath);
        var baselineMatches = Guid.TryParse(baseline.Token.ProjectIdentity, out var baselineIdentity) &&
            baselineIdentity == cache.LangProject.Guid;
        var projectStatus = sourceIdentity is null ? "unknown"
            : baselineMatches && sourceIdentity == baselineIdentity ? "match" : "mismatch";
        var projectMatches = projectStatus == "match";
        var comparison = new TraceProvenanceComparison(projectStatus,
            "unknown", "unknown", false,
            projectMatches
                ? "Recorded baseline evidence is shown. Current project grammar and writing systems have not been compared."
                : projectStatus == "unknown"
                    ? "Current project identity could not be verified; recorded baseline evidence is shown and live navigation is unavailable."
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
        string? RuleName(string? id, string? fallback) =>
            projectMatches && Guid.TryParse(id, out var guid) && repository.TryGetObject(guid, out var rule)
                ? NameOf(rule) ?? fallback
                : fallback;
        TraceStep ResolveStep(TraceStep step) => step with
        {
            Source = step.SourceIdentityKind is "morphRule" or "phonRule" or "compoundingRule" or "affixTemplate"
                ? RuleName(step.SourceIdentityId, step.Source) : step.Source,
            AttemptedMorphs = step.AttemptedMorphs.Select(Resolve).ToArray(),
            Children = step.Children.Select(ResolveStep).ToArray(),
        };
        TraceCandidate ResolveCandidate(TraceCandidate candidate)
        {
            var morphs = candidate.RichMorphs.Select(Resolve).ToArray();
            return candidate with
            {
                RichMorphs = morphs,
                Morphs = morphs.Length > 0 ? morphs.Select(TraceReadingBuilder.ToReadingMorph).ToArray() : candidate.Morphs,
                StoppedByRule = RuleName(candidate.StoppedByRuleId, candidate.StoppedByRule),
                Steps = candidate.Steps.Select(ResolveStep).ToArray(),
            };
        }
        var json = JsonNode.Parse(response.DiagnosticJson, documentOptions: new JsonDocumentOptions { MaxDepth = 512 })!.AsObject();
        var host = json["hostCapture"] as JsonObject ?? new JsonObject();
        var captured = JsonSerializer.SerializeToNode(capture, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        foreach (var field in captured) host[field.Key] = field.Value?.DeepClone();
        if (json["hostCapture"] is not JsonObject) json["hostCapture"] = host;
        var resolved = response with
        {
            HostCapture = capture,
            Provenance = comparison,
            DiagnosticJson = json.ToJsonString(new JsonSerializerOptions { MaxDepth = 512 }),
            Analyses = response.Analyses.Select(analysis => analysis with { Morphs = analysis.Morphs.Select(Resolve).ToArray() }).ToArray(),
            Candidates = response.Candidates.Select(ResolveCandidate).ToArray(),
            Root = ResolveStep(response.Root),
        };
        var reading = TraceReadingBuilder.Summarize(resolved.Word, resolved.Root, resolved.Candidates, resolved.Analyses);
        TraceRef Link(TraceRef reference)
        {
            if (!comparison.CanNavigate || reference.IdentityQuality != "authored" ||
                !Guid.TryParse(reference.Identity, out var id) || !repository.TryGetObject(id, out var found) ||
                FieldWorksLinks.TargetFor(cache, found) is not { } target)
                return reference;
            return reference with
            {
                FieldWorks = new TraceFieldWorksTarget(target.Tool, FieldWorksLinks.ToolName(target.Tool),
                    target.ObjectId.ToString("D"), FieldWorksLinks.ForTarget(projectName, target)!),
            };
        }
        return resolved with
        {
            Analyses = reading.Analyses,
            Reading = reading with { Refs = reading.Refs.Select(Link).ToArray() },
        };
    }

    // A saved-file read avoids taking the live project's LibLCM lock just to authorize navigation.
    private static Guid? ReadSourceIdentity(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            Guid? identity = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "rt" ||
                    reader.GetAttribute("class") != "LangProject") continue;
                if (identity is not null || !Guid.TryParse(reader.GetAttribute("guid"), out var guid)) return null;
                identity = guid;
            }
            return identity;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    // An affix rule is known by its entry, as FieldWorks shows it: headword, then the sense gloss if any.
    private static string? NameOf(ICmObject rule)
    {
        var msa = rule as IMoMorphSynAnalysis;
        for (var owner = rule; owner is not null; owner = owner.Owner)
        {
            if (owner is ILexEntry entry)
            {
                var headword = entry.HeadWord?.Text;
                var gloss = (msa is null ? entry.SensesOS.FirstOrDefault()
                        : entry.AllSenses.FirstOrDefault(sense => sense.MorphoSyntaxAnalysisRA == msa))
                    ?.Gloss.BestAnalysisAlternative.Text;
                return gloss is { Length: > 0 } && gloss != "***" ? $"{headword} ‘{gloss}’" : headword;
            }
        }
        // A rule's ShortName is its class description ("A PhRegularRule"), not the name the project gives it.
        var named = rule switch
        {
            IPhSegmentRule segment => segment.Name.BestAnalysisAlternative?.Text,
            IMoCompoundRule compound => compound.Name.BestAnalysisAlternative?.Text,
            _ => rule.ShortName,
        };
        return named is { Length: > 0 } name && name != "***" ? name : null;
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
