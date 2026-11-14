import os, tempfile, unittest
import control_test as ct


def write(d, name, words):
    p = os.path.join(d, name)
    with open(p, "w", encoding="utf-8") as f:
        f.write("\n".join(words))
    return p


def run(stems, held, discovery=(), change="d:r", extra=()):
    with tempfile.TemporaryDirectory() as d:
        args = ["--stems", write(d, "s", stems), "--suffixes", "an", "--change", change,
                "--heldout", write(d, "h", held), "--discovery", write(d, "d", discovery),
                "--min-hits", "3", *extra]
        return ct.main(args)


STEMS = [f"ka{c}d" for c in "bcfghjk"]


class ControlTest(unittest.TestCase):
    def test_pass_when_rule_beats_controls(self):
        self.assertEqual(run(STEMS, [f"ka{c}ran" for c in STEMS_C]), 0)

    def test_fail_when_a_control_matches(self):
        held = [f"ka{c}lan" for c in STEMS_C] + [f"ka{c}ran" for c in "bc"]
        self.assertEqual(run(STEMS, held), 1)

    def test_undecided_below_minimum_n(self):
        self.assertEqual(run(STEMS, ["kabran", "kacran"]), 2)

    def test_undecided_when_inside_margin(self):
        held = [f"ka{c}ran" for c in STEMS_C] + [f"ka{c}lan" for c in "bcfgh"]
        self.assertEqual(run(STEMS, held), 2)

    def test_discovery_forms_are_not_hits(self):
        held = [f"ka{c}ran" for c in STEMS_C]
        self.assertEqual(run(STEMS, held, discovery=held), 2)

    def test_count_column_is_ignored(self):
        self.assertEqual(run(STEMS, [f"ka{c}ran\t12" for c in STEMS_C]), 0)

    def test_multi_letter_change(self):
        stems = [f"ka{c}nd" for c in "bcfghjk"]
        held = [f"ka{c}nran" for c in "bcfghjk"]
        self.assertEqual(run(stems, held, change="nd:nr"), 0)

    def test_controls_match_target_class(self):
        types = {"karan", "kalan", "kanan", "katan", "kagan", "kasan", "kabin", "kodan", "kuman", "kemen", "kaan"}
        for to, vowel, n in (("r", False, 5), ("a", True, 4)):
            ctrl = ct.pick_controls("d", to, n, types)
            self.assertEqual(len(ctrl), n)
            self.assertTrue(all((c in ct.VOWELS) == vowel for c in ctrl), ctrl)
            self.assertNotIn(to, ctrl)
        # same place/manner first: liquids and stops before the rest
        self.assertEqual(ct.pick_controls("d", "r", 1, types), ["l"])

    def test_explicit_control_letters(self):
        self.assertEqual(ct.pick_controls("d", "r", 3, set(), ["l", "n", "t", "k"]), ["l", "n", "t"])


STEMS_C = "bcfghjk"

if __name__ == "__main__":
    unittest.main()
