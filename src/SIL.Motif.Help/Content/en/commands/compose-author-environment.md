# Author an environment

Stages an environment with explicitly declared left and right contexts in a Draft. Contexts use project phonemes and natural classes, plus the word boundary `#` at the outer edge. An environment cannot hold a morpheme boundary or any other boundary marker, because the parser ignores such an environment and the form would be unconstrained. To condition a change on a morpheme boundary, use a phonological rule ([author a phonological rule](cmd:compose-author-phonological-rule)). Only a person can Apply.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
