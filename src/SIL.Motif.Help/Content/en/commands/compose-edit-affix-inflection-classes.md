# Edit affix class restrictions

Replace the exact inflection class restriction set on an existing ordinary inflectional affix form.

```sh
motif compose-edit-affix-inflection-classes --project language.fwdata --draft numerals \
  --intent '{"target":"<affix-form-id>","expectedInflectionClasses":[],"inflectionClasses":["<class-id>"]}'
```

All three intent fields are required. Both class lists are unordered sets of canonical ids; `expectedInflectionClasses` must match the form's current memberships. An empty `inflectionClasses` list removes every restriction, leaving the form unrestricted rather than prohibiting attachment.

The target must be a usable ordinary inflectional prefix, suffix or interfix form. Circumfix entries are refused. The entry must have at least one inflectional grammatical-info record, and each record must have a usable category. Class ids returned by earlier composition calls in the same Draft can be used directly, and their dependencies are declared from the references.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
