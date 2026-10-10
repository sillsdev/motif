# FieldWorks 9.3.11 released configuration and parser loaders

**Source ID:** F07. **Accessed:** 2026-10-09. **Version/date:** undated; released tag FieldWorks9.3.11.

**Citation:** SIL Global. FieldWorks 9.3.11 released configuration and parser loaders. Tag FieldWorks9.3.11, commit 96da794961d20d82d53719824f3049aa19572533. [Primary source](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/Common/Controls/DetailControls/DataTree.cs).

**Licence:** LGPL-2.1-or-later, source-file headers. No manual or original topic prose is stored here.

## Summary

Released detail views bypass part visibility when Show Hidden Fields is on, while other class and ownership checks still apply. Browse generates reusable layout columns. The parser loader constructs explicit MPR groups and accepts name-based Strata configuration.

## Short excerpt

From DataTree part visibility around lines 2432–2467; Configuration area/tool/Parts files; HCLoader LoadLanguage, CreateStrata, template and affix loading; M3ToXAmpleTransformer:

> if (!m_fShowAllFields)

## Use and limits

Pin factual UI and projection claims to the shipped WinForms release; distinguish engine, loader and UI reach.

Source inspection, not an interactive Windows smoke test. Model vocabulary comes from Motif’s newer pinned package and is not claimed to be the exact package bundled with this FieldWorks installer.
