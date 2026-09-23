# deep-optional-affix-nesting

Copied verbatim from the Machine repository (https://github.com/sillsdev/machine), branch
`conformance/correctness-fixtures` at commit `f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c`, path
`conformance/edge-cases/deep-optional-affix-nesting/`. The `fieldworks/` subfolder became this
folder's `project.fwdata`, `WritingSystemStore/` and `phonology-mutations.yaml`; `grammar.xml` and
`words.yaml` are the fixture's HermitCrab grammar and its oracle.

The project is synthetic: a grammar backed out into LibLCM, thirteen entries, no wordforms, no Texts.
Twelve independent, all-optional prefix slots each insert the letter `x`, so `k` and
`xxxxxxxxxxxxk` each have exactly one analysis and `xxxxxxk` has exactly C(12,6) = 924, listed one
per `parses:` entry in `words.yaml`. Machine marks the fixture pathological (15-second budget) and
never opens the `.fwdata` itself.

Re-copy from the same path to refresh; do not edit these files here.
