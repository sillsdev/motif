# Hypothesis record

One record per hypothesis; one hypothesis per brief. The record is a row of the hypothesis ledger in the Motif store
(ADR 0117); `motif hypothesis add --file <record.json>` reads these fields as snake_case JSON (`host_class`,
`objects_added`, `attested_types_gained`, `questions`). Only the CLI writes it; MCP never does.

| Field | Content |
|---|---|
| id | `H-<n>`, permanent |
| pattern | the regularity, stated over forms, not a list of words |
| statement | the broadest statement that fits |
| mechanism | environment, then class, then feature, then exception, ad hoc last ([mechanism-ladder.md](mechanism-ladder.md)) |
| neighbour | the ledger and rule query, and its result; the rule widened or merged, or "none" |
| predictions | 20 or more forms beyond the forms that suggested it |
| falsifier | the observation that would refute it (a control matching, an attested loss, a speaker rejecting a predicted form) |
| exceptions | expected, with the polarity: which side is default, and the number of objects tagged; a FieldWorks limitation (positive-only restriction forcing the majority to carry the tag) is recorded here |
| test | `rule-hypothesis-test` verdict, counts, controls, Trial losses and new readings |
| change, environment, host_class, slot | the shape the neighbour check looks up (`change` is required, e.g. `d>r`) |
| size_delta | objects added (entries, allomorphs, variants, MSAs, tags, features, rules, environments), attested held-out types gained, ratio (`objects_added`, `attested_types_gained`) |
| word_questions | the batch asked or queued, and answers (`questions`; `hypothesis answer` records an answer) |
| status | proposed, passed, failed, superseded |
| gdr | the GDR id and section changed, or "none: reason" (`hypothesis link --gdr GDR-n --gdr-section …` or `--gdr-none …`) |
| proposal | Proposal id(s) (`hypothesis link --proposal …`) |
| superseded_by | id, when a later hypothesis replaces this one (`hypothesis decide --status superseded --superseded-by H-n`) |

After the answer key is known, `hypothesis grade` adds the four numbers of #185: whether the sealed loop reached the
verdict, word questions needed, objects and attested types before and after, and agent hours against the list alternative.

## When a list is right

A list is correct when a rule attempt is on record and FAILED its test, or the fact is lexical. Worked case:
CEB's mu- class (TAKESMU, 11,338 verbs) was not predictable from stem initial, final, last two letters, length,
hyphen or reduplication: best accuracy 0.826 against a 0.824 majority baseline. The class lives in the lexicon,
so a tag list is the principled answer. Contrast 25 hyphen variants of one shape added with no rule retry; the
earlier broad contraction rule had been measured and rejected, and the second 25 carried no new attempt.

## Contradiction guard

Before status `passed`, search the ledger for a hypothesis with opposite polarity or a ruling on the same
shape. A prior ruling that says "do not build X as an alternate; build the ordered rule" binds until a
ledger row supersedes it. `hypothesis decide --status passed` enforces it: it refuses while another hypothesis has passed on
the same shape with a different mechanism, unless `--supersedes H-n` names it.
