using SIL.Motif.Commands.Catalog;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Cli;

/// <summary>Maps a verb with its flags and positionals to the catalog command name the surface gate checks.</summary>
internal static class CliCommandNames
{
    internal static string Resolve(string verb, IReadOnlyDictionary<string, string> flags,
        IReadOnlyList<string> positionals)
    {
        if (verb == "proposal" && positionals.Count >= 2 && positionals[0] == "picture" &&
            positionals[1] is "add" or "remove" or "show")
            return "proposal picture " + positionals[1];
        if (verb == "proposal" && positionals.Count >= 2 && positionals[0] == "comment" &&
            positionals[1] is "add" or "edit" or "delete")
            return "proposal comment " + positionals[1];
        if (verb == "proposal" && positionals.FirstOrDefault() is "trial" or "review" or "looked-at" or "rationale" or "note")
            return "proposal " + positionals[0];
        if (verb == "parsimony")
        {
            if (positionals.SequenceEqual(new[] { "expectations" })) return "parsimony expectations";
            if (positionals.Count == 2 && positionals[0] == "negative" && positionals[1] == "confirm")
                return "parsimony negative confirm";
            if (positionals.Count == 2 && positionals[0] == "negative" && positionals[1] == "retract")
                return "parsimony negative retract";
            if (positionals.Count == 2 && positionals[0] == "revise") return "parsimony revise";
            if (positionals.Count == 2 && positionals[0] == "retract") return "parsimony retract";
            if (positionals.FirstOrDefault() == "show") return "parsimony show";
            if (positionals.FirstOrDefault() == "latest") return "parsimony latest";
            if (positionals.FirstOrDefault() == "retirement-review") return "parsimony retirement-review";
            if (positionals.FirstOrDefault() == "measures") return "parsimony measures";
            if (positionals.FirstOrDefault() == "view") return "parsimony view";
            if (positionals.FirstOrDefault() == "record-types") return "parsimony record-types";
            if (positionals.FirstOrDefault() == "dispose") return "parsimony dispose";
            if (flags.ContainsKey("group") && !flags.ContainsKey("dry-run")) return "parsimony --group";
            if (flags.ContainsKey("wait"))
                return flags.GetValueOrDefault("measure") == ParsimonyAdmission.AllMeasures && !flags.ContainsKey("dry-run")
                    ? "parsimony --measure all --wait" : "parsimony --wait";
            return "parsimony";
        }
        if (verb == "apply" && flags.ContainsKey("all-pending"))
            return "apply --all-pending";
        if (verb == "delete") return "delete --proposal";
        if (verb == "list" && positionals.SequenceEqual(new[] { "requests" })) return "list requests";
        if (verb == "request" && positionals.Count > 0) return "request " + positionals[0];
        if (verb is "reject" or "reopen" && flags.ContainsKey("proposal")) return verb + " --proposal";
        if (verb == "apply" && flags.ContainsKey("proposal"))
            return "apply --proposal";
        if (verb == "trial" && flags.ContainsKey("pending"))
            return "trial --pending";
        if (verb == "phonology-examples" && positionals.FirstOrDefault() is "save" or "list" or "show" or "check" or "results")
            return verb + " " + positionals[0];
        if (verb == "project" && positionals.SequenceEqual(new[] { "tag" }) && flags.ContainsKey("set"))
            return "project tag --set";
        if (verb == "current" && positionals.SequenceEqual(new[] { "set" }) && flags.ContainsKey("clear"))
            return "current set --clear";
        if (verb is "config" or "baseline" or "gdr" or "source" or "current" or "grammar" or "jobs" or "project" or "selection" or "texts" or "setup" or "store" or "word" or "difference" or "evidence")
        {
            var candidate = positionals.Count > 0 ? verb + " " + positionals[0] : verb;
            if (CommandCatalog.All.Any(command => command.Name == candidate)) return candidate;
            return verb;
        }

        if (verb == "report" && flags.ContainsKey("list-kinds"))
            return "report --list-kinds";
        if (verb == "trace" && flags.ContainsKey("load"))
            return "trace --load";
        if (verb is "dry-run" or "trial" && flags.ContainsKey("wait"))
            return verb + " --wait";
        return verb;
    }
}
