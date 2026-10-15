# Author grammatical features

Stages an empty feature structure on a stem or inflectional affix MSA in a Draft. The MSA may already exist in the project or have been authored earlier in the same Draft. Only a person can Apply.

Use the closed input `{ "msa": "<canonical ID>" }` to name the exact MSA. A stem receives an MsFeatures structure; an inflectional affix receives an InflFeats structure for its word requirements. The stem's MsFeatures is the only structure a word reads out: put a noun's class there (for example nc7 on *kitabu*) so an affix that requires nc7 can match it and a mismatched one cannot. An inflectional affix's InflFeats only requires; it never gives the word a feature. For singular and plural nouns, put the gender pair (for example 7/8) on the stem and the number on each class prefix: every reading shows its prefix's number, but a number requirement never rejects a stem that has none. Other MSA types and an occupied owning slot are refused. A present structure occupies the slot even when it has no selections.

Use the returned structure identity when [authoring a feature selection](cmd:compose-author-feature-value). An empty structure is valid, and a feature can be selected before choosing its value. Selections must fit the current domain of the named structure.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
