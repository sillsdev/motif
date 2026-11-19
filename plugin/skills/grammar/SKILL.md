---
name: grammar
description: >-
  Start here for any work on a language's grammar in FieldWorks through Motif: starting a grammar, adding
  entries or variants, fixing parses, adding an affix, rule or tag. Routes to the skill for each step (discovery
  loop, hypothesis test, Proposal, Apply) and says why a rule should come before a list of forms.
---

# Grammar

Grammar work is a loop of rules, not a pile of forms. A grammar that grows by listing the words that failed to
parse passes its gate and teaches the parser nothing: the next word of the same shape fails again. The default
path is to write the rule, test the hypothesis, check parsimony, and only then encode.

## The default path

1. **Discover.** Load `grammar-discovery-loop`. You should mine patterns, check for a neighbouring rule, and
   state a hypothesis (a rule plus the forms it predicts) before you touch the grammar.
2. **Test.** Load `rule-hypothesis-test`. You should get PASS, FAIL or UNDECIDED, with numbers, before you encode
   the rule.
3. **Propose.** Load `motif-workflow`. Draft the rule change and its Grammar Guide section in the same Proposal,
   Dry Run, Trial and Finalize it. Check parsimony with `parsimony-review` before you Finalize.
4. **Apply.** A person Applies. You stop at Finalize.

## Where the next rule comes from

Group the words the grammar does not parse yet: the unparsed and Open types. Those clusters show the next
pattern worth a rule. Words that already parse are the place to check a rule for overgeneration, not to mine.

## Pick the skill by the request

| The request | Start with |
|---|---|
| Start a grammar, or add a batch of entries | `grammar-discovery-loop` |
| Add variants, allomorphs or tags | `grammar-discovery-loop` (a rule that implies them removes the need) |
| Fix a wrong, missing or ambiguous parse | `fieldworks-parsing-expert` to diagnose, then `grammar-discovery-loop` if the cause is a missing rule |
| Add or change an affix, rule, process or feature | `grammar-discovery-loop`, then `rule-hypothesis-test` |
| Is this rule real? | `rule-hypothesis-test` |
| Change the project | `motif-workflow` |
| Does the grammar need simplifying? | `parsimony-review` |
| A linguistic judgement or a question for speakers | `linguistic-consultant` |
| Where something lives in FLEx | `fieldworks-expert` |

## Anti-pattern: listing forms

Entering the unparsed words one by one, as entries, variants, allomorphs or tags, is the anti-pattern. It is
the cheapest thing to pass a gate, and it grows the grammar without understanding it. Before you add a batch of five or more same-shape
objects, you should have a recorded attempt at the broader rule, with its `rule-hypothesis-test` result and why
it failed. A list is the right answer after a FAIL for that shape, or for a lexical fact.

Guidance here is advisory. The reviewer's check is the Proposal's parsimony delta: did the objects per attested
word get worse? A Proposal that makes it worse should say why. Refusal is kept for unambiguous cheats, such as
editing audit items.

Ask of every package: what broader rule was tried, and was a neighbour widened or merged?

## Hypothesis commands

`grammar-discovery-loop` records hypotheses with `motif hypothesis` verbs (`add`, `neighbours`, `test`,
`decide`, `link`). They are released CLI commands and need no switch. `motif guide check` is an Autonomous command: set `MOTIF_AUTONOMOUS=1` or pass `--autonomous`, or it refuses with `command.autonomous-required`.
