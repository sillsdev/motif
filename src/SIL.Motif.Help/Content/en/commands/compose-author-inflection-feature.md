# Author an inflection feature

Add a closed morphosyntactic feature and the values it can take, such as Number with Singular and Plural. One call stages the feature and all its named values in the project's morphosyntactic feature system.

```sh
motif compose-author-inflection-feature --project language.fwdata --draft number \
  --intent '{"name":"Number","ws":"en","abbreviation":"num","values":[{"name":"Singular","abbreviation":"sg"},{"name":"Plural","abbreviation":"pl"}]}'
```

`values` must contain at least one object with a `name` and an optional `abbreviation`. Value names must be distinct after Unicode normalization, and the feature name must be new in that writing system. The writing system must be known to the project.

The result names every created entity. The `createFeatures` operation supplies the feature id; each `createValues` operation supplies a value id. Existing feature-structure and feature-value composers attach these choices to a stem's grammatical info. This command makes no category or entry assignments.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

Dry Run and Trial the Draft on affected words, revise it, then Finalize for a person to review and Apply.
