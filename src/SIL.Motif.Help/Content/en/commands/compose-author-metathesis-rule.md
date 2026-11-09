# Author a metathesis rule

Use this command to stage one metathesis rule in a Draft: two neighbouring sounds that trade places, such as Cebuano *dahm-a* becoming *damh-a* (Wolff 5.14; Black §6.1.2.3). It writes the rule the way FieldWorks does, as a Metathesis Rule whose Rule Formula has a **Left environment**, the two **Switch these items** cells, an optional middle and a **Right environment**. FieldWorks shows the result of the switch below the formula; Motif computes it.

```sh
motif compose-author-metathesis-rule --project language.fwdata --draft h-c-metathesis --intent '{"name":"h and a consonant switch","direction":"simultaneous","left":[{"phoneme":"<a-id>"}],"leftSwitch":{"naturalClass":"<h-class-id>"},"rightSwitch":{"naturalClass":"<consonant-class-id>"},"right":[{"phoneme":"<a-id>"}],"placement":{"after":"<syncope-rule-id>"}}'
```

With the left switch an `h` class and the right switch a consonant class, the sequence h + consonant is rewritten as consonant + h, so a stem *dahma* is found for the word *damha* and *dahma* itself is rejected.

## The intent

- `name` and `direction` are the same as in [Author a sound rule](cmd:compose-author-phonological-rule).
- `leftSwitch` and `rightSwitch` each name one `phoneme` or one `naturalClass`. The left switch is the first item in the formula and moves to the right switch's place, and the right switch moves to the left one. Both are required.
- `middle` is an optional single `phoneme` or `naturalClass` between the switches that stays where it is. `middleWith` is `left` (the default) or `right` and says which switch's cell it belongs to, as in FieldWorks.
- `left` and `right` are the environments, each up to eight items in linguistic order, or left out when empty. An item names one phoneme, natural class, `word` or `morpheme` boundary, or `boundaryMarker`. Metathesis environment items cannot be optional, repeated or carry alpha variables.
- `placement` names the existing `after` or `before` rule that bounds the insertion gap. Rule order matters: put a metathesis rule after the rule whose output it reorders, such as a syncope rule.

## What Motif writes

Motif creates the rule, its name and direction, one structural item for each environment and switch in order, and the structural-change positions that FieldWorks stores (`grammar/phMetathesisRule/setStrucChange`). The positions name where each part sits in the rule's structure; the stored text is left to FieldWorks' own writer. A rule with an unset position is silently dropped by the parser, so Motif refuses to enable a metathesis rule without a left switch and a right switch.

The rule is staged enabled once its parts are complete. Finalize the Proposal, inspect its Dry Run, then use Trial to confirm the switched words parse and the unswitched ones do not before a person Applies it.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

## When not to use this

- For a sound that changes without moving, use [Author a sound rule](cmd:compose-author-phonological-rule).
- For one irregular word, use `compose-author-alternate-form`; a metathesis rule reorders every form that matches.
- Do not name a natural class wider than the evidence supports: the rule switches every matching pair.
