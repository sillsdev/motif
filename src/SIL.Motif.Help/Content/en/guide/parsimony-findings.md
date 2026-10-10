# Parsimony

The **Parsimony** page lists grammar-tidiness findings from the latest stored check of the grammar. Each finding names what was counted and how strong the evidence is. The page reads what the check stored. Opening it, or coming back to it, never starts a check, and it writes nothing to the project.

The page appears only while Advanced AI mode is on. Without a stored check it says so and names the next step: start a Parsimony check, then come back here.

The page has two tabs. **Active** lists the findings still to review. **Suppressed** lists the findings you kept or deferred, each with its reason, or with "No reason given" when you gave none. Each tab shows its count.

Findings are grouped by check. Each group names its grammar side: **Restrictiveness**, **Parsimony**, or both. A line under the tabs says once what each side means, and the page gives no combined score. A finding you have decided shows "staged, not yet applied" beside its decision until Apply writes it.

Select a finding to read its evidence in the detail column: what was counted, the items it names, why it was reported, the strength of the evidence, whether a paired parser check has run, and the limits of the count. A finding is a suggestion to look at, not a verdict on the grammar.

## Deciding a finding

Select an active finding. Its detail column has **Keep**, **Defer** and **Ask** buttons, and a note about **Fix**:

- **Keep** records that the finding is intended. You may write a reason. A kept finding moves to Suppressed once Apply and Refresh have run.
- **Defer** records that you will come back to the finding later. Like Keep, it takes an optional reason and moves to Suppressed once applied.
- **Ask** records a question about the finding. It needs the question you write. It stays active, because a question is not a decision to suppress.
- **Fix** is not a button. A note under the buttons names the Guide page that describes the update Motif supports for the check, and says you make the fix itself in FieldWorks. It records nothing. Where a check has no supported update yet, the note says so, and you can ask or defer instead.

When there are several Notebook record types, choose the one the decision goes under. The page never picks one by its English name.

Each action is staged in pending changes, which the Review changes page shows. Nothing is written to FieldWorks until Apply. A staged decision shows as staged, not as suppressed. Refresh after Apply to see the decision in Suppressed.

## Suppressed decisions

A Suppressed decision stays in its own tab with its reason. Select it to read its details; that only shows them. **Return to Active** stages the withdrawal of the decision, so the finding comes back to the Active tab once Apply and Refresh have run.

A decision returns to Active on its own when the evidence it was made on changes. It then shows "Changed since it was kept" or "Changed since it was deferred" with the reason you gave before, so you can decide again.

A decision that conflicts with another, or that no longer matches any finding, stays visible and is not hidden. A conflict stays in Active and says what conflicts. A stale decision in Suppressed says that no current finding matches it.

If the stored check could not measure some of its checks, the page names them under **Not measured** and explains why. Those checks' findings are not listed. If the check was made from an earlier [Baseline](term:baseline), the page says so. Measure the grammar again to see current findings.
