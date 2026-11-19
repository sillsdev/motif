namespace SIL.Motif.Commands.Queries;

/// <summary>One sentence per stable reason code PanGloss records, so no front end invents a cause from a code.</summary>
public static class RejectionTraceSentences
{
    private static readonly Dictionary<string, string> ByReason = new(StringComparer.Ordinal)
    {
        ["invalid_shape"] = "The word's shape is not valid for this grammar, so the parser did not search.",
        ["lookup_completed"] = "The lexical lookup finished and produced root candidates.",
        ["lookup_no_seed"] = "The lexical lookup found no root for this spelling.",
        ["lookup_features"] = "A root was found, but its features did not unify with what the parse required.",
        ["required_mpr_features"] = "A required morphosyntactic-rule feature was not present on the root.",
        ["excluded_mpr_features"] = "An excluded morphosyntactic-rule feature was present on the root.",
        ["required_stem_name"] = "The stem name the rule requires was not the root's.",
        ["excluded_stem_name"] = "The stem name the rule excludes was the root's.",
        ["root_not_viable"] = "The root candidate could not start a valid analysis.",
        ["required_slot_unfilled"] = "A required template slot was left unfilled.",
        ["applicable_templates_not_applied"] = "An applicable affix template was not applied.",
        ["nonfinal_template_last"] = "A template that is not final came last in the word.",
        ["remaining_rules_in_stratum"] = "Morphology did not complete the rules the stratum still required.",
        ["remaining_analyzed_rules"] = "Morphology did not complete the rules the analysis still required.",
        ["bound_root"] = "The root is bound and cannot stand as a word.",
        ["surface_mismatch"] = "The surface the parser built differs from the surface requested.",
        ["template_ineligible"] = "The template did not apply to this candidate.",
        ["optional_slot_skipped"] = "An optional template slot was skipped.",
        ["nonpartial_rule_prohibited_after_final_template"] = "A nonpartial rule is not allowed after a final template.",
        ["nonpartial_rule_required_after_nonfinal_template"] = "A nonpartial rule was required after a nonfinal template.",
        ["environment"] = "A required environment did not match.",
        ["pattern_no_output"] = "A rule's pattern matched but produced no output.",
        ["pattern_no_match"] = "A rule's pattern did not match.",
        ["equivalent_history"] = "This history merged into an equivalent one already kept.",
        ["applied"] = "The rule applied.",
        ["lexical_family_replacement"] = "A compatible lexical-family entry replaced the rule's result.",
        ["max_application_count"] = "A rule reached its maximum number of applications.",
        ["allomorph_cooccurrence"] = "An allomorph co-occurrence restriction excluded this candidate.",
        ["allomorph_not_applicable"] = "The allomorph did not apply to this candidate.",
        ["morpheme_cooccurrence"] = "A morpheme co-occurrence restriction excluded this candidate.",
        ["disjunctive_allomorph"] = "A disjunctive allomorph was already chosen.",
        ["head_pattern"] = "The compound head's pattern did not match.",
        ["nonhead_pattern"] = "The compound nonhead's pattern did not match.",
        ["head_productivity_features"] = "The compound head's productivity features blocked the rule.",
        ["nonhead_productivity_features"] = "The compound nonhead's productivity features blocked the rule.",
        ["head_required_syntactic_features"] = "The compound head lacked a required syntactic feature.",
        ["nonhead_required_syntactic_features"] = "The compound nonhead lacked a required syntactic feature.",
        ["required_syntactic_features"] = "A required syntactic feature was not present.",
        ["obligatory_syntactic_features"] = "An obligatory syntactic feature was not present.",
        ["required_pos"] = "The required category was not present.",
        ["realizational_syntactic_clash"] = "Realizational features clashed with the syntactic features.",
        ["no_generation_seed"] = "No root seed could start this generation.",
        ["prefilter_rejected"] = "A prefilter rejected the candidate.",
    };

    /// <summary>The sentence for a reason; an unlisted reason still reads as a stop, naming only its code.</summary>
    public static string For(string reason, bool rejection)
    {
        var text = ByReason.TryGetValue(reason, out var known)
            ? known
            : $"The parser recorded '{reason}', which Motif has no sentence for.";
        return rejection ? text : text + " This is an observation, not a stop.";
    }
}
