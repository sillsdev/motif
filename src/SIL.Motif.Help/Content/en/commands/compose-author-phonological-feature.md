# Author a phonological feature

Stages a closed phonological feature and its values in the project's phonological feature system, such as `place` with `labial`, `coronal` and `velar`. Phonemes then carry the values (see [Set a phoneme's features](cmd:compose-edit-phoneme-features)) and a natural class can be defined by them ([Author a natural class](cmd:compose-author-natural-class), [Edit a natural class's features](cmd:compose-edit-natural-class-features)). Only a person can Apply.

```sh
motif compose-author-phonological-feature --project language.fwdata --draft features \
  --intent '{"name":"place","abbreviation":"plc","values":[{"name":"labial","abbreviation":"lab"},{"name":"coronal","abbreviation":"cor"}]}'
```

The intent has `name`, optional `abbreviation` and `values`, a nonempty list of `{ "name", "abbreviation" }` objects (the abbreviation is optional). Names are written in the project's first analysis writing system. A feature name or abbreviation that another phonological feature already has is refused, and so is a repeated value name or abbreviation inside the feature.

**Defining the first feature changes how segment classes behave.** In a project with no phonological features, a rewrite rule that names a segment class is read one way; in a project with features it is read another (PanGloss issue 120). Before a featureless project gets its first feature, try the rules in a Trial and compare the readings.

Use [Add a phonological feature value](cmd:compose-add-phonological-feature-value) to extend a feature that exists. A feature is not a part of speech, an inflection feature or an exception feature; those use other commands. The command stages operations in a Draft and does not change the saved FieldWorks project.
