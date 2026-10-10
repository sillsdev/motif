# Help corpus lookup

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

## Search locally, fetch one topic

`help-index.tsv` has **1,600 topic rows**, one physical line per topic, plus a header:
`topic`, `title`, `breadcrumb`, `keywords`, `related`, `source_url`. Tabs separate fields.
`related` contains semicolon-separated in-corpus link targets (all local topic links, not only a
“Related Topics” footer). Breadcrumbs use the official contents hierarchy, with a folder-derived
fallback for topics omitted from the contents. Keywords are the topics' own RoboHelp metadata.

For example, from this skill's folder:

```sh
rg -i 'show hidden fields|custom fields|stem allomorph|parser parameters' references/help-index.tsv
```

Match the area and record type, not just a common label. Fetch the selected `source_url`; follow an
indexed related topic only if needed. The official live HTML is not content-pinned, so verify its
9.3 banner and compare a sensitive claim with the released configuration. Do not fetch the entire
corpus into model context. The skill contains metadata and authored explanations, never help prose.
If web fetching is unavailable, the local references still answer the core mechanism questions;
state when an exact procedure remains unverified.

## Source and reproducible refresh

The canonical proposed markdown home remains `sillsdev/FwHelps:markdown-export`. That branch is
**absent at authoring time**; the skill does not claim its frontmatter exists upstream or depend on a
personal fork. Instead the index is built from the official 9.3 CHM at
`d468f9ca501f421616f622965e674f5f4678c9ce`, which contains equivalent title, keyword, contents and
link metadata. This fallback makes current Help lookup usable; merging the markdown export is
still an upstream follow-up, not work performed by this lane.

Download [the pinned official CHM](https://raw.githubusercontent.com/sillsdev/FwHelps/d468f9ca501f421616f622965e674f5f4678c9ce/FieldWorks_Language_Explorer_Help.chm)
into temporary storage, verify its SHA-256 against [sources](sources.md), and extract it with a CHM
reader such as 7-Zip. Then regenerate using the included standard-library Python script:

```sh
python3 scripts/index_help.py /temporary/extracted-help references/help-index.tsv
```

The extractor and full help are not shipped. Rebuilding with another corpus requires updating the
source pin, digest and scope before publishing, not silently treating a new UI as 9.3.11.
F08 documents this deliberate departure from the September frontmatter plan.
