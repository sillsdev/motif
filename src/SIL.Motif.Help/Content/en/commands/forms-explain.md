# Explain a morpheme's forms

`forms explain <root>` answers one question: what does this root become, and which rules make it so? It asks the resident PanGloss to generate the root, alone or together with the morphemes you list, and reports every surface it reaches, the allomorph it chose for each morpheme, and the rules that fired in order with the word before and after each step. It reads the project's current Baseline, or a Proposal's Trial copy with `--at <proposal-id>`, and changes nothing. Motif reasons about no grammar itself: every step is something PanGloss reported. The MCP tool `motif_forms_explain` takes the same fields, with `with` as a list.

```text
motif forms explain kaon --project <fwdata> --with -an --with mag- --json
motif forms explain --word kaonan --project <fwdata> --md
```

## Naming a morpheme

- An entry guid or Motif id names the entry; if it has one analysis of the right kind (a stem for the root, an affix for `--with`) that is the one used.
- `entryGuid:msaGuid` names one analysis exactly.
- A form in the lexicon, such as `kaon` or `-an`, names the entry whose headword or any allomorph is that form, ignoring the affix hyphen. When it matches more than one, Motif refuses and lists them; name one as `entry:msa`.
- `--with` repeats and keeps its order. The root and its `--with` morphemes are one context.

## A word

`--word <form>` parses the word with the same parser as `motif trace`, then generates each analysis from its own morphemes: the stem is the root and the other morphemes are added in surface order. Analyses that name the same morphemes are explained once; an analysis with a guessed morph, or none with a stem, is listed under "Not explained" with the reason.

## What you get

- **JSON `motif.forms-explain.v1`.** `morpheme` or `word`, then `contexts`, each with its `morphemes`, its `surfaces` and its own `complete` and `stopReason`; then `complete`, `stopReason`, `grammarHash`, `panglossVersion` and `tracedAgainst`. A surface is `surface`, `seed`, `allomorphs`, `derivations`, `steps` and `traceTruncated`; a step is `stratum`, `rule`, optional `subrule`, `input` and `output`, with `+` marking a morpheme boundary. Order is fixed: contexts as asked (a word's by their morpheme ids), surfaces by text then seed, steps in the order PanGloss applied them.
- **Markdown.** One section per context with a table of surface, allomorphs and rules fired, then a collapsed block per surface with every step's before and after.
- **Incomplete means incomplete.** When PanGloss hits a cap, a deadline or a limit it returns what it had found. The answer then has `complete: false`, the `stopReason`, and in Markdown an `incomplete: <reason>` banner. A form missing from an incomplete answer is not shown to be impossible.

## When it refuses

It refuses, each with its own code and a sentence: a request it cannot send; a morpheme that matches nothing or too much; a PanGloss whose `--describe` declares no `interfaces.serveOps.generate`, naming the release it needs; a request PanGloss itself rejects, such as an unknown entry; an answer it cannot read; and a resident parser that failed before it answered. It uses the machine parser queue and cancellation like the other parse questions.

## Related commands

- [Explain a root's forms](cmd:generate) is the same question for a root alone.
- [Trace a word](cmd:trace) returns the full diagnostic tree for one word.
- [Why a word did not parse](cmd:why-not) names the first failing constraint for a morph sequence you expected.
