# Stage a source-validated decision

`compose-record-source-validated-decision` stages a decision that a named program, not a person, made from cited sources. It has two forms.

To approve or disapprove an analysis, give the existing analysis as `target`, an `agent` (a `name` and the `version` of the source release it stands for) and a `member` of `approves` or `disapproves`. FieldWorks will show the decision under that non-human agent. A person's approvals and disapprovals are never touched, and a new release of the sources is a new agent. Agents are told apart by name and version, so if release 1 approved an analysis and release 2 disapproves it, `difference produce --reference source-validated` marks the word `conflicting-decisions` and counts neither; stage the later release only after you have decided what to do with the earlier one.

To record why a word form is wrong or right without deciding anything in FieldWorks, give the wordform or analysis as `target` and leave `agent` and `member` out. This never changes spelling status, which stays a person's.

Either way, `evidence` says why: a `tier` (for example `source-approved`, `misspelled-typo` or `ill-formed`), the cited `sources` (a dictionary, a grammar, a corpus or an example, each with an optional locator) and the facts the tier requires: a typo needs `intendedForm` and `editType`, an ill-formed word needs `violatedRule`, a nonstandard variant needs `standardForm`, and a source-approved analysis needs a dictionary, a grammar and a corpus source. Read the records back with `evidence show`.

Motif stages the change in a Draft for a person to review. Only a person can Apply.
