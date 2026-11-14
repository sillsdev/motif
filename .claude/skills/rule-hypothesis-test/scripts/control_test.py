#!/usr/bin/env python3
"""Letter-swap control test for a boundary-change hypothesis.

Rule form: when SUFFIX attaches to a stem ending in FROM, the stem-final FROM surfaces as TO.
Predicted form = stem[:-len(FROM)] + TO + suffix. Controls replace TO by other letters.
A hit is a predicted form attested in the held-out types and absent from the discovery types.
Control letters come from the same class as TO (vowel for vowel, consonant for consonant), taken from the
letters the language uses (the held-out and discovery types), nearest by place or manner first, then by
frequency. Override with --control-letters. Exit codes: 0 PASS, 1 FAIL, 2 UNDECIDED.

Usage:
  control_test.py --stems stems.txt --suffixes an,un --change d:r \
      --heldout heldout_types.txt --discovery discovery_types.txt [--controls 5] [--json]
Files hold one word per line (a tab and a count after the word is ignored).
"""
import argparse, json, sys


def load(path):
    with open(path, encoding="utf-8") as f:
        return {ln.split("\t")[0].strip() for ln in f if ln.strip()}


VOWELS = set("aeiou")
# Place/manner groups, Latin orthography; override per language with --control-letters.
GROUPS = ["pbf", "td", "kgqc", "szxj", "mn", "lr", "wy", "h"]


def group_of(c):
    for g in GROUPS:
        if c in g:
            return g
    return ""


def pick_controls(frm, to, n, types, explicit=None):
    """n control letters of TO's class, from the inventory seen in types, same place/manner first."""
    if explicit:
        return explicit[:n]
    freq = {}
    for w in types:
        for ch in w:
            if ch.isalpha():
                freq[ch] = freq.get(ch, 0) + 1
    vowel = to in VOWELS
    pool = [c for c in freq if c not in (frm, to) and ((c in VOWELS) == vowel)]
    tg = group_of(to)
    pool.sort(key=lambda c: (0 if tg and c in tg else 1, -freq[c], c))
    return pool[:n]


def hits(stems, suffixes, frm, repl, heldout, discovery):
    n = found = 0
    forms = []
    for s in stems:
        if not s.endswith(frm):
            continue
        for suf in suffixes:
            f = s[: len(s) - len(frm)] + repl + suf
            n += 1
            if f in heldout and f not in discovery:
                found += 1
                forms.append(f)
    return n, found, forms


def verdict(rule_hits, ctrl_hits, min_hits, ratio):
    best = max(ctrl_hits) if ctrl_hits else 0
    if rule_hits <= best:
        # clearly beaten only when the control itself has enough hits; else too little data to say
        return "FAIL" if best >= min_hits else "UNDECIDED"
    if rule_hits < min_hits:
        return "UNDECIDED"
    return "PASS" if rule_hits >= ratio * max(best, 1) else "UNDECIDED"


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--stems", required=True)
    ap.add_argument("--suffixes", required=True, help="comma-separated")
    ap.add_argument("--change", required=True, help="FROM:TO, stem-final FROM becomes TO before the suffix")
    ap.add_argument("--heldout", required=True)
    ap.add_argument("--discovery", required=True)
    ap.add_argument("--controls", type=int, default=5)
    ap.add_argument("--control-letters", help="comma-separated override, same class as TO")
    ap.add_argument("--min-hits", type=int, default=20, help="provisional; calibrate on d>r and metathesis")
    ap.add_argument("--ratio", type=float, default=2.0, help="provisional; rule hits over best control")
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args(argv)
    frm, to = a.change.split(":")
    stems, heldout, discovery = load(a.stems), load(a.heldout), load(a.discovery)
    suffixes = a.suffixes.split(",")
    n, rule_hits, forms = hits(stems, suffixes, frm, to, heldout, discovery)
    ctrl = {}
    explicit = a.control_letters.split(",") if a.control_letters else None
    for c in pick_controls(frm, to, a.controls, heldout | discovery, explicit):
        ctrl[c] = hits(stems, suffixes, frm, c, heldout, discovery)[1]
    v = verdict(rule_hits, list(ctrl.values()), a.min_hits, a.ratio)
    out = {"predicted": n, "rule_hits": rule_hits, "controls": ctrl,
           "margin": rule_hits - max(ctrl.values(), default=0), "verdict": v,
           "hit_forms": sorted(forms)[:50]}
    if a.json:
        print(json.dumps(out, ensure_ascii=False, indent=2))
    else:
        print(f"predicted {n} forms; rule hits {rule_hits}; controls {ctrl}; margin {out['margin']}; {v}")
    return {"PASS": 0, "FAIL": 1, "UNDECIDED": 2}[v]


if __name__ == "__main__":
    sys.exit(main())
