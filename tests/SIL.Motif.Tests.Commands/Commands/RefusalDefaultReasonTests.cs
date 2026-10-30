using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Generator;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A spec's <see cref="RefusalSpec.DefaultReason"/> is read back from the source: every refusal construction that
/// names a registered code beside a <see cref="FailureReason"/>, directly or through a helper that hard-codes one,
/// must agree with it. The scan is textual, so it is a floor under the registry, not a proof.
/// </summary>
public sealed class RefusalDefaultReasonTests
{
    // Codes whose call sites differ: the spec holds the most common reason, and each split is named here.
    private const string PerSite = "the reason follows the check that failed at each site; the spec holds the most common";

    private static readonly Dictionary<string, (string Reasons, string Why)> Split = new(StringComparer.Ordinal)
    {
        ["baseline.text-words-unreadable"] = ("Refused+StoreInconsistent", PerSite),
        ["comparison.assessment-not-found"] = ("InvalidArgument+NotFound", PerSite),
        ["draft.invalid"] = ("InvalidArgument+Refused", PerSite),
        ["handoff.trace-unavailable"] = ("InvalidArgument+Refused", PerSite),
        ["operation.invalid-dependency"] = ("InvalidArgument+Refused", PerSite),
        ["operation.invalid-id"] = ("InvalidArgument+Refused", PerSite),
        ["parsimony.invalid-assessment"] = ("InvalidArgument+Refused", PerSite),
        ["parsimony.report-bundle-mismatch"] = ("Refused+StoreInconsistent", PerSite),
        ["parsimony.report-not-found"] = ("NotFound+Refused", PerSite),
        ["parsimony.result-invalid"] = ("Refused+StoreInconsistent", PerSite),
        ["project.not-found"] = ("InvalidArgument+NotFound", PerSite),
        ["project.store-io"] = ("Busy+Refused", "Busy when SQLite reports the storage unavailable, Refused when Motif cannot delete its file"),
        ["proposal.inconsistent"] = ("Refused+StoreInconsistent", PerSite),
        ["proposal.not-found"] = ("NotFound+Refused", PerSite),
        ["report.assessment-not-found"] = ("InvalidArgument+NotFound", PerSite),
        ["selection.empty"] = ("InvalidArgument+Refused", PerSite),
        ["stats.no-assessment"] = ("InvalidArgument+NotFound", PerSite),
        ["store.unsupported"] = ("Refused+StoreInconsistent", PerSite),
    };

    // Codes the scan cannot resolve: the reason is computed, passed through a variable, or chosen in another layer.
    private static readonly Dictionary<string, string> NotDeterminable = new(StringComparer.Ordinal)
    {
        ["command.advanced-ai-mode-required"] = "reason not determinable statically: chosen by the caller of the availability facts, not where the code is spelled",
        ["command.developer-commands-required"] = "reason not determinable statically: chosen by the caller of the availability facts, not where the code is spelled",
        ["machine-store.inconsistent"] = "reason not determinable statically: a window problem report, which carries no FailureReason",
        ["preferences.refused"] = "reason not determinable statically: a window preference problem, which carries no FailureReason",
        ["preferences.unavailable"] = "reason not determinable statically: a window preference problem, which carries no FailureReason",
        ["phonology-examples.assessment-not-found"] = "reason not determinable statically: the code is chosen by a conditional; both arms use NotFound",
        ["phonology-examples.example-not-found"] = "reason not determinable statically: the code is chosen by a conditional; both arms use NotFound",
        ["project.tag-invalid"] = "reason not determinable statically: code and reason are selected from ProjectTagProblem",
        ["project.tag-taken"] = "reason not determinable statically: code and reason are selected from ProjectTagProblem",
        ["project.refused"] = "reason not determinable statically: the reason is computed from the exception by ReasonFor",
        ["clitic-attachment.invalid-category"] = "reason not determinable statically: CliticAttachmentRefusal carries Refused for each code",
        ["clitic-attachment.not-clitic"] = "reason not determinable statically: CliticAttachmentRefusal carries Refused for each code",
        ["clitic-attachment.stale-source"] = "reason not determinable statically: CliticAttachmentRefusal carries Refused for each code",
        ["adhoc-prohibition.invalid-morpheme"] = "reason not determinable statically: the ad hoc prohibition checks carry Refused for each code",
        ["adhoc-prohibition.invalid-allomorph"] = "reason not determinable statically: the ad hoc prohibition checks carry Refused for each code",
        ["adhoc-prohibition.self"] = "reason not determinable statically: the ad hoc prohibition checks carry Refused for each code",
        ["adhoc-prohibition.duplicate"] = "reason not determinable statically: the ad hoc prohibition checks carry Refused for each code",
        ["adhoc-prohibition.no-morphology-data"] = "reason not determinable statically: the ad hoc prohibition checks carry Refused for each code",
        ["variant.invalid-entry"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.already-linked"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.cycle"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.invalid-component"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.invalid-reference"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.invalid-type"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.stale-reference"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["variant.self-link"] = "reason not determinable statically: the variant link helper carries Refused for each code",
        ["window.check"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
        ["window.diagnostic-not-written"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
        ["job.parser-unavailable"] = "reason not determinable statically: a failure code a Worker records on a job, not a refusal",
        ["window.diagnostic-unreadable"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
        ["window.no-project"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
        ["window.open-failed"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
        ["window.open-proposal-failed"] = "reason not determinable statically: a window-only code, which carries no FailureReason",
    };

    private static readonly Regex CodeToken = new(
        "\"(?<lit>[a-z][a-z-]*(?:\\.[a-z0-9-]+)+)\"|\\b(?:\\w+Codes|C)\\.(?<name>\\w+)", RegexOptions.None);

    private static readonly Regex Reason = new("FailureReason\\.(?<reason>\\w+)", RegexOptions.None);

    [Fact]
    public void EverySpecsDefaultReasonIsTheOneItsCallSitesUse()
    {
        var observed = Observed();
        var problems = new List<string>();
        var undetermined = new List<string>();
        foreach (var (code, spec) in RefusalCatalog.Specs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!observed.TryGetValue(code, out var counts))
            {
                if (!NotDeterminable.ContainsKey(code)) undetermined.Add(code);
                continue;
            }

            if (NotDeterminable.ContainsKey(code)) problems.Add(code + " is listed as not determinable but the scan found " + Show(counts));
            var top = counts.Values.Max();
            var leaders = counts.Where(pair => pair.Value == top).Select(pair => pair.Key).ToList();
            if (!leaders.Contains(spec.DefaultReason.ToString()))
                problems.Add(code + " is " + spec.DefaultReason + " but its call sites say " + Show(counts));
            var isSplit = counts.Count > 1;
            if (isSplit != Split.ContainsKey(code))
                problems.Add(code + (isSplit ? " has split call sites " + Show(counts) + " with no allowlist entry"
                    : " is allowlisted as split but its call sites agree on " + Show(counts)));
            else if (isSplit && Split[code].Reasons != string.Join("+", counts.Keys.Order(StringComparer.Ordinal)))
                problems.Add(code + " split changed to " + Show(counts));
        }

        Assert.True(problems.Count == 0, "\n" + string.Join("\n", problems));
        Assert.True(undetermined.Count == 0, "Not determinable and not listed:\n" + string.Join("\n", undetermined));
        Assert.DoesNotContain(NotDeterminable.Keys, code => !RefusalCatalog.Specs.ContainsKey(code));
    }

    private static string Show(Dictionary<string, int> counts) =>
        string.Join("+", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "x" + pair.Value));

    // Registered code -> reason -> number of call sites naming it.
    private static Dictionary<string, Dictionary<string, int>> Observed()
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in new[] { typeof(RefusalCodes), typeof(HostRefusalCodes), typeof(ParsimonyRefusalCodes) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
                byName.Add(field.Name, (string)field.GetRawConstantValue()!);
        var registered = byName.Values.ToHashSet(StringComparer.Ordinal);
        var catalog = Path.Combine("Contract", "Commands", "RefusalCatalog.cs");
        var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        void Vote(string? code, string reason)
        {
            if (code is null) return;
            result.TryAdd(code, new Dictionary<string, int>(StringComparer.Ordinal));
            result[code][reason] = result[code].GetValueOrDefault(reason) + 1;
        }

        string? Resolve(Match token) => token.Groups["lit"].Success
            ? registered.Contains(token.Groups["lit"].Value) ? token.Groups["lit"].Value : null
            : byName.GetValueOrDefault(token.Groups["name"].Value);

        var src = Path.Combine(RepoPaths.FindRepoRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains(sep + "obj" + sep) || file.EndsWith(catalog, StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(file);

            // A code spelled before a reason in the same statement, or a reason followed by `code: ...`.
            foreach (Match reason in Reason.Matches(text))
            {
                var before = text[Math.Max(0, reason.Index - 250)..reason.Index];
                before = before[(before.LastIndexOfAny([';', '{', '}']) + 1)..];
                foreach (Match token in CodeToken.Matches(before)) Vote(Resolve(token), reason.Groups["reason"].Value);
                var after = text.Substring(reason.Index + reason.Length, Math.Min(160, text.Length - reason.Index - reason.Length));
                var named = Regex.Match(after, "^[^;{}]*?\\bcode:\\s*(?<tok>\"[^\"]+\"|\\w+Codes\\.\\w+)");
                if (named.Success && CodeToken.Match(named.Groups["tok"].Value) is { Success: true } hit)
                    Vote(Resolve(hit), reason.Groups["reason"].Value);
            }

            foreach (var (helper, helperReason) in Helpers(text))
                foreach (Match call in Regex.Matches(text, "\\b" + helper + "(?:<[^>()]*>)?\\("))
                {
                    var args = text.Substring(call.Index + call.Length, Math.Min(200, text.Length - call.Index - call.Length));
                    var cut = args.IndexOf(';');
                    if (cut >= 0) args = args[..cut];
                    var commas = args.Select((ch, i) => (ch, i)).Where(p => p.ch == ',').Take(2).ToList();
                    if (commas.Count == 2) args = args[..commas[1].i];
                    foreach (Match token in CodeToken.Matches(args)) Vote(Resolve(token), helperReason);
                }

            // The tool argument exception defaults to InvalidArgument unless the call names a reason.
            foreach (Match call in Regex.Matches(text, "new ToolArgumentException\\("))
            {
                var statement = text.Substring(call.Index, Math.Min(400, text.Length - call.Index));
                statement = statement[..(statement.IndexOf(';') is var end and >= 0 ? end : statement.Length)];
                var first = CodeToken.Match(statement);
                if (first.Success)
                    Vote(Resolve(first), Reason.Match(statement) is { Success: true } named
                        ? named.Groups["reason"].Value
                        : nameof(FailureReason.InvalidArgument));
            }
        }

        return result;
    }

    // Methods in one file that take a code and construct a Refusal with a fixed reason.
    private static IEnumerable<(string Name, string Reason)> Helpers(string text)
    {
        foreach (Match parameter in Regex.Matches(text, "string (?<code>\\w*[cC]ode)\\b"))
        {
            var head = text[Math.Max(0, parameter.Index - 300)..parameter.Index];
            var open = Regex.Matches(head, "(?<name>\\w+)(?:<[^>()]*>)?\\(");
            if (open.Count == 0) continue;
            var name = open[^1].Groups["name"].Value;
            var body = text.Substring(parameter.Index, Math.Min(700, text.Length - parameter.Index));
            var made = Regex.Match(body, "new Refusal\\(\\s*" + parameter.Groups["code"].Value + "\\s*,\\s*FailureReason\\.(?<reason>\\w+)");
            if (!made.Success) continue;
            if (Regex.IsMatch(body[..made.Index], "\\n    (public|private|internal)\\s[^\\n]*\\(")) continue;
            yield return (name, made.Groups["reason"].Value);
        }
    }
}
