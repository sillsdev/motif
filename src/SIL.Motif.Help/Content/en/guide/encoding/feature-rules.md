# Feature changes and alpha variables

A feature change sets named features on the segment matched by the Input and preserves its other features. Use it for a regular alternation such as voicing, nasalisation or place assimilation. Follow Andy Black's section 6.3.2: an archiphoneme N takes its place from the following consonant across a morpheme boundary, `N → [α place] / _ + [α place]`.

First define the phonological features and their values with [Author a phonological feature](cmd:compose-author-phonological-feature), give the phonemes their features with [Edit a phoneme's features](cmd:compose-edit-phoneme-features), and create the feature natural classes with [Author a natural class](cmd:compose-author-natural-class). Each surface phoneme needs enough features to distinguish it from the others (Black section 6.2). An underlying archiphoneme may leave place unspecified. A separate feature can distinguish underlying N from surface nasals; the Output then sets that feature to its surface value as well as copying place.

## Feature change

An Output item uses `change`, an array of `{ "feature": "<feature-id>", "value": "<value-id>" }` pairs. These are feature and value identities from this project's phonological feature system. A plus or minus feature value is an ordinary value identity, distinct from the polarity of an alpha reference. For example:

```json
{
  "name": "voice the matched consonant",
  "direction": "simultaneous",
  "input": [{ "naturalClass": "<voiceless-consonant-class-id>" }],
  "output": [{ "change": [{ "feature": "<voice-id>", "value": "<plus-id>" }] }]
}
```

The Output changes voice and keeps place and manner. Motif creates a feature natural class (`PhNCFeatures`) for this partial description, as FieldWorks does. Input and Output must have the same number of items when an Output item uses `change`, so each change has a matched segment. An Output segment-list natural class does not choose a member: the engines apply it as the union of its members' features, which leaves an underspecified segment, so use a feature natural class at an aligned Output position to change the named features, or alternate forms for free variation.

## Alpha agreement and disagreement

A `variables` array adds alpha references to a feature natural class in Input or either context, or to an Output `change`. Each reference contains a `variable` name, a closed phonological `feature` identity, and `polarity` (`+` by default). Use the same variable name for the same feature everywhere in that rule. Plus requires agreement with the variable; minus requires disagreement. Either polarity can bind a variable, and a variable may have a single reference. A minus is not a fixed minus feature value. FieldWorks' loader can name at most 24 distinct alpha variables across the whole rule, including all right-hand sides; Motif refuses a larger rule before staging it.

Here is the Black section 6.3.2 shape. The archiphoneme class matches N, the following-consonant class supplies place, and the Output makes a surface nasal with that place:

```json
{
  "name": "nasal place assimilation",
  "direction": "simultaneous",
  "input": [{ "naturalClass": "<archiphoneme-class-id>" }],
  "output": [{
    "change": [{ "feature": "<archiphoneme-feature-id>", "value": "<surface-value-id>" }],
    "variables": [{ "variable": "alpha", "feature": "<place-id>", "polarity": "+" }]
  }],
  "right": [
    { "boundary": "morpheme" },
    { "naturalClass": "<following-consonant-class-id>",
      "variables": [{ "variable": "alpha", "feature": "<place-id>", "polarity": "+" }] }
  ]
}
```

With underlying N + pa, N + ta and N + ka, expect mpa, nta and ŋka; npa, mta and the other wrong nasals have no analysis. Check each place and negative controls with a Trial before Apply. For a binary feature, Output polarity `-` and context polarity `+` express dissimilation instead. An empty `change` is allowed when alpha supplies the change by itself.

Motif refuses one variable naming different features, duplicate or contradictory polarities on one item, and a fixed and variable value for the same feature on one item. A referenced feature must be a declared closed feature with values in this project; it need not occur on any phoneme. Alpha references belong to feature natural classes, not literal phoneme or boundary items. They are not available in allomorph environment strings. FieldWorks' current loader cannot discover a variable used only inside optional or repeated items in multi-item contexts, so Motif explains this limit before staging such a rule. The same variable is discoverable when it also occurs on Input, Output, an ordinary context item, or a context containing only the repeated item.

## Read and edit

[Author a sound rule](cmd:compose-author-phonological-rule) and [Edit a sound rule](cmd:compose-edit-phonological-rule) accept these items through `--intent`. Their MCP tools stage the same intent in a Draft; a person Applies the Proposal.

Read `motif_grammar(kind="phonological_rules", detail="detailed")` before editing. Copy its `intent` into `expected`, and use its returned variable identifiers in changed parts to keep sharing with parts left in place. Read-back uses each stored feature constraint's identity as the variable name; author-supplied labels are local to authoring and are not stored by LibLCM. If every reference is in replaced Input, the constraint is recreated with a new identity while its sharing and polarity stay the same. Feature Output classes read back with their `naturalClass` identities and alpha references, preserving sharing with other users of the class. Multiple right-hand sides read back as an ordered `rightHandSides` sequence with per-case Output, contexts, rule features and input categories. Replace that complete sequence under `expected.rightHandSides` to add, remove or reorder cases; alpha scope and sharing span the whole rule. Only an unrepresentable context or applicability reference prevents editable read-back.

The default PanGloss engine supports feature changes and alpha agreement. Its FST engine may refuse alpha rules. Alpha on Input is representable and supported by the FieldWorks loader, but hc.dll parsing parity for that shape is unproven because the oracle hangs. A segment unspecified for a feature does not satisfy a fixed minus value in a synthesis rule or its environment. An Output segment-list class can supply lexical alternatives, while multiple right-hand sides are ordered cases: the first applicable case wins. Rule features restrict each case but do not make overlapping cases optional. Metathesis is a separate capability.
