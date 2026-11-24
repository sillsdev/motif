# Lessons from real grammar work

Each lesson holds for any language. Check them when a grammar edit does nothing, loses words or doubles
readings. The examples are single cases, not the rule.

## Alternates

- **The earliest matching alternate wins.** An edit to a shadowed alternate is inert: the Trial gains
  nothing, even with no environment. Before adding an alternate, list the entry's alternates in order and
  check that no earlier one already matches the same context. Example: an unconstrained `w` passive
  alternate shadows `lew`/`liw`, so generation produced only one form. Motif does not warn yet
  (sillsdev/PanGloss#469).
- **An alternate with an environment blocks the unconditioned form where that environment holds.** To keep
  the original form there, give it its own alternate carrying the same environment. Example: adding an
  `e` alternate with environment `w _ po #` lost every `-wapo` word until the original form got the same
  environment.

## Entries and classes

- **A stem with no inflection class parses only through derivation.** A derivation can't follow an
  inflectional slot, so such a stem can't take a derived form after, say, a passive or auxiliary slot.
  Give the stem a class, or build the route inside the template.
- **One morpheme, one entry.** Add alternates or extra MSAs to it. A class-free twin of a class-restricted
  entry makes every word with that morpheme parse twice. Example: a duplicate class-free future
  nominalizer doubled 146 words.
- **Moving a stem to another class can strand its features.** Check that its inflection features (gender,
  for example) still fit the new class's affixes.

## Retire or block

- **Retire a lexicalised derived stem** only when both hold:
  1. the derivation is semantically right;
  2. the grammar can build every attested form of it.
- **Repeated suffixes can't be built yet**: derivational affixes apply once and templates refuse a repeated
  slot (sillsdev/PanGloss#468, sillsdev/motif#436). Example: causative applied twice (`-ish-ish-`) needs the
  lexicalised `-ish` stem.
- **Before retiring, scan the corpus forms of the root** for chains the grammar can't build: repeated
  suffixes, forms after a passive or auxiliary, stems with their own tense alternates.
- **Otherwise keep the stem and block the nonsense chain** with an ad hoc prohibition.

## Measuring a change

- **Run a sibling check, not just the gold words.** Parse corpus words that share the touched morphemes and
  fail on any that stop parsing. Example: a retirement that passed on gold words lost 57 siblings.
- **Count words that gain extra readings**, not only words that change. Example: a change reported 74
  changed words and had doubled 421.
- The gate that does this lives outside Motif for now; its upstreaming is sillsdev/motif#435.
- **Don't impose an affix order untested.** Try both orders against the corpus first. Example: Swahili
  applicative and stative both occur in either order.

## Running

- **One parse per project at a time.** A transient `database is locked` (`StoreInconsistent`, SQLite error
  5) means retry the command, not that the grammar failed (sillsdev/motif#434).
