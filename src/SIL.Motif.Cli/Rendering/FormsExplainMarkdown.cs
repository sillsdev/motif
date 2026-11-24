using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// The Markdown form of <c>motif forms explain</c>: one section per context with a table of surface, allomorphs and the
/// rules fired in order, and a collapsed block per surface with every step's before and after. It prints what the
/// response holds and nothing more, in the response's order.
/// </summary>
internal static class FormsExplainMarkdown
{
    public static string Render(FormsExplainResponse response)
    {
        var text = new StringBuilder();
        text.Append("# Forms of ").AppendLine(response.Word ?? response.Morpheme ?? "the morpheme");
        text.AppendLine();
        text.AppendLine($"Against {response.TracedAgainst}. Grammar `{response.GrammarHash}`. PanGloss {response.PanglossVersion}. " +
            $"Schema `{response.Schema}`.");
        if (!response.Complete)
        {
            text.AppendLine();
            text.AppendLine(response.StopReason == "context_rejected"
                ? "> **incomplete: context_rejected.** PanGloss refused at least one context, listed under Not explained, so " +
                  "its forms are missing below. Do not read a missing form as impossible."
                : $"> **incomplete: {response.StopReason}.** PanGloss stopped before it finished, so the forms " +
                  "below are only those found first. A form missing here may still be generated; do not read it as impossible.");
        }
        for (var index = 0; index < response.Contexts.Count; index++)
            Context(text, response.Contexts[index], index + 1, response.Contexts.Count);
        if (response.Skipped is { Count: > 0 } skipped)
        {
            text.AppendLine();
            text.AppendLine("## Not explained");
            text.AppendLine();
            foreach (var item in skipped) text.AppendLine($"- {Inline(item.Reading)}: {Inline(item.Reason)}");
        }
        return text.ToString();
    }

    private static void Context(StringBuilder text, FormsExplainContext context, int number, int count)
    {
        text.AppendLine();
        var label = string.Join(" + ", context.Morphemes.Select(morpheme => morpheme.Form ?? morpheme.EntryGuid));
        text.AppendLine(count == 1 ? $"## {Inline(label)}" : $"## Context {number.ToString(CultureInfo.InvariantCulture)}: {Inline(label)}");
        text.AppendLine();
        text.AppendLine("Morphemes: " + string.Join("; ", context.Morphemes.Select(morpheme =>
            $"{Inline(morpheme.Form ?? "(no form)")} (`{morpheme.EntryGuid}:{morpheme.MsaGuid}`, {morpheme.MsaClass})")));
        if (!context.Complete)
        {
            text.AppendLine();
            text.AppendLine($"> **incomplete: {context.StopReason}.** This context stopped early.");
        }
        if (context.Surfaces.Count == 0)
        {
            text.AppendLine();
            text.AppendLine(context.Complete ? "No form is generated here." : "No form was found before it stopped.");
            return;
        }
        text.AppendLine();
        text.AppendLine("| Surface | Allomorphs | Rules fired |");
        text.AppendLine("| --- | --- | --- |");
        foreach (var surface in context.Surfaces)
            text.AppendLine($"| {Cell(surface.Surface)} | {Cell(string.Join(", ", surface.Allomorphs.Select(Shape)))} | " +
                $"{Cell(RulesFired(surface))} |");
        foreach (var surface in context.Surfaces) Details(text, surface);
    }

    private static string RulesFired(FormsExplainSurface surface)
    {
        if (surface.Steps.Count > 0) return string.Join(", ", surface.Steps.Select(step => RuleLabel(step.Rule)));
        if (surface.Alternatives.Count > 0)
            return string.Join(" / ", surface.Alternatives.Select((steps, index) =>
                $"derivation {(index + 1).ToString(CultureInfo.InvariantCulture)}: " + string.Join(", ", steps.Select(step => RuleLabel(step.Rule)))));
        return surface.StepsUnresolved ? "rule steps unresolved" : "none recorded";
    }

    private static void Steps(StringBuilder text, IReadOnlyList<FormsExplainStep> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var subrule = step.Subrule is { } sub ? $", subrule {sub.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            text.AppendLine($"{(index + 1).ToString(CultureInfo.InvariantCulture)}. **{Inline(RuleLabel(step.Rule))}** " +
                $"({Inline(step.Rule.Kind)}, {Inline(step.Stratum)}{subrule}): {Code(step.Input)} -> {Code(step.Output)}");
        }
    }

    private static void Details(StringBuilder text, FormsExplainSurface surface)
    {
        text.AppendLine();
        text.AppendLine("<details>");
        text.AppendLine($"<summary>{Html(surface.Surface)} ({Html(surface.Seed)})</summary>");
        text.AppendLine();
        text.AppendLine("Allomorphs: " + string.Join(", ", surface.Allomorphs.Select(allomorph =>
            $"{Inline(allomorph.Role ?? "morpheme")} {Code(Shape(allomorph))}")));
        if (surface.Derivations > 1) text.AppendLine($"Reached by {surface.Derivations.ToString(CultureInfo.InvariantCulture)} derivations.");
        text.AppendLine();
        if (surface.Steps.Count > 0) Steps(text, surface.Steps);
        else if (surface.Alternatives.Count > 0)
            for (var index = 0; index < surface.Alternatives.Count; index++)
            {
                text.AppendLine($"Derivation {(index + 1).ToString(CultureInfo.InvariantCulture)} of {surface.Alternatives.Count.ToString(CultureInfo.InvariantCulture)}:");
                text.AppendLine();
                Steps(text, surface.Alternatives[index]);
                text.AppendLine();
            }
        else text.AppendLine(surface.StepsUnresolved
            ? "**rule steps unresolved**: PanGloss could not prove which rules made this surface, so it gave none."
            : "No steps were recorded.");
        if (surface.TraceTruncated)
        {
            text.AppendLine();
            text.AppendLine("The trace cap cut these steps short.");
        }
        text.AppendLine();
        text.AppendLine("</details>");
    }

    // An affix is a rule with no allomorph, so it reads by its rule name.
    private static string Shape(FormsExplainAllomorph allomorph) =>
        allomorph.Shape is null ? allomorph.Label ?? allomorph.RuleName ?? "unnamed affix" : allomorph.Shape.Length == 0 ? "(empty)" : allomorph.Shape;

    private static string RuleLabel(FormsExplainRule rule) => rule.Name ?? rule.Label ?? rule.Id ?? rule.Guid ?? rule.Kind;

    private static string Cell(string value) => Inline(value).Replace("|", "\\|", StringComparison.Ordinal);

    private static string Inline(string value) => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Html(string value) =>
        Inline(value).Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Code(string value)
    {
        var clean = Inline(value);
        return clean.Contains('`', StringComparison.Ordinal) ? "`` " + clean + " ``" : "`" + clean + "`";
    }
}
