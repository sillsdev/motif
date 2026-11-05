# Delete an affix process

`compose-delete-affix-process` stages deletion of one affix process with every input, output and other object it owns. Supply `entry` and `form`, with optional `replacement`: an existing plain affix allomorph of the same entry and morph type. A plain replacement can be authored earlier in this Draft with [Add an alternate form](cmd:compose-author-alternate-form). MCP offers `motif_delete_affix_process`; Apply stays with a person.

If the process is the lexeme form, its plain replacement moves from the alternate list to the lexeme slot. If the process is an alternate, the plain replacement stays where it is. Without `replacement`, the process is deleted without selecting a survivor. Other forms, senses and grammatical analyses are kept.

Every incoming reference into the whole process graph must be moved or removed first in the same Draft. [Retarget prohibition references](cmd:compose-edit-adhoc-prohibition-references) or [delete each prohibition](cmd:compose-delete-adhoc-prohibition). Disabled and legacy references count too. A reference from a stored analysis or custom field also blocks deletion; the refusal names every remaining source, field and occurrence and explains how to clear it. No reference is silently cleared by deletion.

The Draft binds the complete process graph and declares dependencies on the changes clearing its references. Changes to the graph require recomposition; new references after a Dry Run require renewed review. The Dry Run lists the deleted graph identities, the plain replacement and the entry content kept. Run a Trial with unaffected and affected words before a person Applies.
