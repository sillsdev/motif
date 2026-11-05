# Add Required Features

Stages an empty Required Features structure on one ordinary affix allomorph in a Draft. FieldWorks shows this as **Required Features**, on the lexeme form or an alternate form. The closed input is `{ "allomorph": "<form ID>" }`; the form may already exist or have been staged earlier in the Draft. Only a person can Apply.

Use the returned structure ID with [Author feature value](cmd:compose-author-feature-value), supplying `featStruc`, a morphosyntactic `feature` and its `value`. Required Features select this allomorph by the grammatical features of the stem it attaches to. They work on allomorphs of derivational affixes too: two prefix forms requiring transitive and intransitive respectively select matching stems and reject stems bearing the other value (`RealParserAllomorphGateTests`). A stem without a value is compatible with a requirement; use explicit stem values when testing the contrast.

This command preserves an occupied structure, even if it is empty, and refuses to replace it. It accepts ordinary affix allomorphs; a stem form or an affix process is a different LibLCM class. It imposes no restriction on the entry's grammatical info kind. Required Features are distinct from the whole affix's [grammatical features](cmd:compose-author-feature-structure) and from [inflection classes on an affix form](cmd:compose-edit-affix-inflection-classes).

Run a Trial with expected and excluded combinations before Finalize. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the source on the staged operation.
