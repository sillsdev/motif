# Unused grammar statements

This check finds environments and natural classes that the parser never applies. A statement is used when some grammar object applies it: an affix process, a rule context, an infix position, or an allomorph environment. A statement that no reference the parser applies is reported as unused, and its finding names the referrers that do not apply it.

## Analyse

Read `statement-usage` for each statement's references and their parser effect. Every reference in the grammar facts counts, so a reference from an affix process, a rule context or an infix position is seen the same way as one from an allomorph environment. A statement is used for parsing when one reference is `applied`. A statement whose references are all not applied, such as `ignored`, `unresolved` or `owner_not_loaded`, is unused for parsing. A statement with no reference at all is unused. The finding names each referrer that does not apply it.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

A statement with an applied reference is used for parsing and is never reported here. An unused statement that only references the parser does not apply may have a broken reference rather than an unused statement, so check those references before deciding.

## Ask the linguist

Ask whether an unused statement is reserved for planned work or can be retired. Include its identity, its referrers, and their parser effects.

## Update

No typed retirement action is available for this finding. After confirming that a statement is unused, record a `defer` with the referrer evidence until a semantic removal action can name the supported deletion scope. A statement that a reference the parser does not apply still names needs that reference fixed first, not removal.

## Verify

Re-run the view and confirm the retired statement is absent while every applied reference and Approved reading remains intact.

## Grounding

Engineering check: exact redundancy in the authored grammar; no linguistic claim.

- **[bender-poulson-drellishak-evans-2007]** — Emily M. Bender, Laurie Poulson, Scott Drellishak, and Chris Evans. 2007. “Validation and Regression Testing for a Cross-linguistic Grammar Resource.” In *ACL 2007 Workshop on Deep Linguistic Processing*, 136–143. Association for Computational Linguistics. [Source](https://aclanthology.org/W07-1218/). Grounds the use of regression checks when changing grammar resources.
