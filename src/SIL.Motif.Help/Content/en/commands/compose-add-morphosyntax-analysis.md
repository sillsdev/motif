# Add a grammatical analysis

Use this command when one existing form should work in two grammatical roles, such as a Verb suffix that must also attach to Copula stems. FieldWorks holds this as one entry with two grammatical infos (morphosyntactic analyses), and each sense points at one of them. The parser loads every analysis of an entry as its own rule, so the same form parses on both categories, and the glosses of the two senses tell the analyses apart.

```sh
motif compose-add-morphosyntax-analysis --project language.fwdata --draft copula-suffix \
  --intent '{"entry":"<suffix-entry-id>","msa":{"kind":"inflectional","category":"<copula-id>","slots":["<copula-slot-id>"]},"gloss":"not (copula)","analysisWs":"en"}'
```

`entry` is an existing entry, or one an earlier command added to the same Draft. `msa` has the same shape as in `compose-author-lexical-entry`: its `kind` must suit the entry's form (`stem` for a stem, `inflectional`, `derivational` or `unclassified` for an affix), `category` is the part of speech, a derivational analysis also needs `toCategory`, an inflectional one can name `slots`, and a stem can name an `inflectionClass`.

The new analysis is used by the senses you name. `senses` lists senses of this entry that should now point at it, and `gloss` with `analysisWs` (and an optional `definition`) adds a new sense that points at it. Give at least one of them: an analysis no sense uses is removed by FieldWorks the next time the project is checked or opened, so a call that gives neither is refused. Moving the last sense off an analysis drops that analysis, and the Dry Run says so, naming any slots the old analysis fills (they lose that membership); if an ad hoc prohibition or a morph bundle still refers to that analysis the command refuses instead, naming what refers to it. Then either leave one sense on the old analysis, or remove that prohibition first. An analysis identical to one the entry already has (same category, classes and slots) is refused.

A derivational affix is restricted to one inflection class per analysis. Give it as `fromInflectionClass`, a class of its From category or an ancestor, at the top level of the intent beside `entry` and `gloss`, not inside `msa` (inside `msa` the command refuses it with "unknown property 'fromInflectionClass'"). It applies only to a derivational analysis. This adds a class 2 analysis to an affix entry that already has a derivational analysis for class 1:

```sh
motif compose-add-morphosyntax-analysis --project language.fwdata --draft class-two \
  --intent '{"entry":"<affix-entry-id>","msa":{"kind":"derivational","category":"<verb-id>","toCategory":"<noun-id>"},"gloss":"agentive (class 2)","analysisWs":"en","fromInflectionClass":"<class-2-id>"}'
```

The Dry Run shows a `grammar/moDerivAffMsa/setFromInflectionClass` operation for the new analysis. Checked with the real parser: with the first analysis restricted to class 1 and this one to class 2, the affix parses stems of both classes and no stem of a third class. To let it attach to several classes, give the entry one derivational analysis per class: the parser loads each analysis as its own rule, so the classes add up, and a stem of any other class is not accepted. A class whose subclasses should all qualify can instead be the From class itself, because the parser includes a class's subclasses. Restrict the first analysis with `compose-edit-derivational-affix-restriction`.

The inflection classes already set on the entry's affix form apply to every inflectional analysis of the entry, whatever its category; if the new category has its own classes, check the form with `compose-edit-affix-inflection-classes`.

Dry Run and Trial the Draft on words in both roles and on words that should not gain an analysis, then Finalize it for a person to review and Apply.

## When not to use this

- To turn the one analysis an affix has into another affix kind, use `compose-change-affix-kind`.
- For an entry that does not exist yet, use `compose-author-lexical-entry`.
- For two separate entries with different forms, one per category, use two entries in their slots (the [suffix and prefix slots guide](guide:encoding/suffix-prefix-slots)).

An entry may hold two analyses with the same category, classes and slots when they differ by feature requirements. The command refuses a new analysis only when an existing one is identical and carries no feature requirement. To give one morpheme two analyses that differ only by features (a relative marker that requires one group in one analysis and another in the second), first give the existing analysis its requirement with [Author a feature structure](cmd:compose-author-feature-structure) and a feature value, then add the second analysis and give it its own.
