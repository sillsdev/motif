using SIL.Motif.Contract.Responses;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.WritingSystems;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>Adds host-owned capture evidence while preserving every parser field.</summary>
internal static class TraceDiagnosticCapture
{
    internal static WordTraceResponse Attach(WordTraceResponse response, BaselineRecord baseline, ProjectLocator project,
        long? wallElapsedMs = null)
    {
        var baselineSource = new TraceBaselineSource(baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc,
            $"Saved FieldWorks project as of {baseline.SourceLastWriteUtc:O}; Baseline captured {baseline.Token.CapturedUtc}.");
        if (string.IsNullOrEmpty(response.DiagnosticJson)) return response with
        {
            HostCapture = new TraceHostCapture(baseline.Token.ProjectIdentity, response.GrammarHash,
                response.GrammarHashSemantics, baseline.Token.BundleDigest, DateTimeOffset.UtcNow, wallElapsedMs, [])
                { Baseline = baselineSource },
        };
        using var reader = BaselineReadCache.Open(baseline.FwDataPath);
        var cache = reader.Cache;
        var systems = WritingSystemDisplayReader.Read(cache).Select(ws => new TraceWritingSystem(
            ws.Id, ws.Name, ws.Kind == WritingSystemKind.Vernacular, ws.IsDefault,
            ws.RightToLeft ? "rtl" : "ltr", ws.FontFamily)
        {
            FontFeatures = ws.FontFeatures,
            StyleFonts = ws.StyleFonts,
            StyleSizes = ws.StyleSizes,
        }).ToArray();
        var capture = new TraceHostCapture(baseline.Token.ProjectIdentity, response.GrammarHash,
            response.GrammarHashSemantics, baseline.Token.BundleDigest, DateTimeOffset.UtcNow,
            wallElapsedMs, systems)
        {
            Baseline = baselineSource,
        };
        var navigation = SavedProjectNavigation.Read(project.FullFwDataPath, baseline.Token.ProjectIdentity);
        var baselineMatches = Guid.TryParse(baseline.Token.ProjectIdentity, out var baselineIdentity) &&
            baselineIdentity == cache.LangProject.Guid;
        var projectStatus = navigation.ProjectIdentityStatus == "unknown" ? "unknown"
            : baselineMatches ? navigation.ProjectIdentityStatus : "mismatch";
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
        TraceMorph Resolve(TraceMorph morph)
        {
            string? link = null;
            if (projectMatches && morph.IdentityQuality == "authored" &&
                Guid.TryParse(morph.EntryId ?? morph.FormId ?? morph.MsaId, out var id) &&
                repository.TryGetObject(id, out var found))
                link = navigation.TargetFor(cache, found)?.Link;
            return morph with { FieldWorksLink = link };
        }
        TraceStep ResolveStep(TraceStep step) => step with
        {
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
                Steps = candidate.Steps.Select(ResolveStep).ToArray(),
            };
        }
        var reading = TraceReadingBuilder.Summarize(response.Word, ResolveStep(response.Reading.Root),
            response.Reading.Attempts.Select(ResolveCandidate).ToArray(),
            response.Reading.Analyses.Select(analysis => analysis with { Morphs = analysis.Morphs.Select(Resolve).ToArray() }).ToArray());
        TraceRef Link(TraceRef reference)
        {
            if (reference.IdentityQuality != "authored" ||
                !Guid.TryParse(reference.Identity, out var id) || !repository.TryGetObject(id, out var found))
                return reference;
            reference = reference with { CapturedFieldWorksLabel = NameOf(found) };
            if (!comparison.CanNavigate) return reference;
            return reference with
            {
                FieldWorks = navigation.TargetFor(cache, found),
            };
        }
        reading = reading with { Refs = reading.Refs.Select(Link).ToArray() };
        capture = capture with
        {
            TraceLabels = reading.Refs.Where(reference => reference.CapturedFieldWorksLabel is not null)
                .Select(reference => new TraceCapturedLabel(reference.Id, reference.CapturedFieldWorksLabel!)).ToArray(),
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
            Reading = reading,
        };
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

    private static string SizesKey(System.Collections.Generic.IReadOnlyDictionary<string, double>? sizes) =>
        sizes is null ? string.Empty : SIL.Motif.Contract.Canonicalization.CanonicalJson.Canonicalize(
            JsonSerializer.Serialize(sizes));

    private static string FontsKey(IReadOnlyDictionary<string, WritingSystemStyleFont>? fonts) =>
        fonts is null ? string.Empty : SIL.Motif.Contract.Canonicalization.CanonicalJson.Canonicalize(
            JsonSerializer.Serialize(fonts));

    internal static TraceProvenanceComparison Compare(TraceHostCapture? recorded, TraceHostCapture? current)
    {
        string CompareValue(string? left, string? right) => string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)
            ? "unknown" : StringComparer.Ordinal.Equals(left, right) ? "match" : "mismatch";
        var project = string.IsNullOrEmpty(recorded?.ProjectIdentity) || string.IsNullOrEmpty(current?.ProjectIdentity)
            ? "unknown" : ObjectIdentity.Same(ObjectIdentity.Create("project", recorded.ProjectIdentity),
                ObjectIdentity.Create("project", current.ProjectIdentity)) ? "match" : "mismatch";
        var sameHashKind = !string.IsNullOrEmpty(recorded?.GrammarHashSemantics) &&
            StringComparer.Ordinal.Equals(recorded.GrammarHashSemantics, current?.GrammarHashSemantics);
        var grammar = sameHashKind ? CompareValue(recorded?.GrammarHash, current?.GrammarHash) : "unknown";
        var writingSystems = recorded is null || current is null || recorded.WritingSystems.Count == 0 || current.WritingSystems.Count == 0
            ? "unknown" : recorded.WritingSystems.Select(ws => (ws.Id, ws.IsVernacular, ws.IsDefault, ws.Direction, ws.Font, ws.FontFeatures,
                    SizesKey(ws.StyleSizes), FontsKey(ws.StyleFonts)))
                .SequenceEqual(current.WritingSystems.Select(ws => (ws.Id, ws.IsVernacular, ws.IsDefault, ws.Direction, ws.Font, ws.FontFeatures,
                    SizesKey(ws.StyleSizes), FontsKey(ws.StyleFonts)))) ? "match" : "mismatch";
        var compatible = project == "match" && grammar == "match" && writingSystems == "match";
        return new TraceProvenanceComparison(project, grammar, writingSystems, compatible,
            compatible ? "" : $"Recorded evidence retained. Project: {project}; grammar: {grammar}; writing systems: {writingSystems}. Live navigation is unavailable.");
    }
}
