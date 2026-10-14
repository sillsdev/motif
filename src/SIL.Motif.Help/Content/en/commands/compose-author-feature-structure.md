# Author grammatical features

Stages an empty feature structure on a stem or inflectional affix MSA in a Draft. The MSA may already exist in the project or have been authored earlier in the same Draft. Only a person can Apply.

Use the closed input `{ "msa": "<canonical ID>" }` to name the exact MSA. A stem receives an MsFeatures structure; an inflectional affix receives an InflFeats structure for its word requirements. Other MSA types and an occupied owning slot are refused. A present structure occupies the slot even when it has no selections.

Use the returned structure identity when [authoring a feature selection](cmd:compose-author-feature-value). An empty structure is valid, and a feature can be selected before choosing its value. Selections must fit the current domain of the named structure.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
