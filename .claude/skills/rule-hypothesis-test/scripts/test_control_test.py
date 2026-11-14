import os, tempfile, unittest
import control_test as ct


def write(d, name, words):
    p = os.path.join(d, name)
    open(p, "w", encoding="utf-8").write("\n".join(words))
    return p


class ControlTest(unittest.TestCase):
    def run_case(self, stems, heldout, discovery=()):
        with tempfile.TemporaryDirectory() as d:
            args = ["--stems", write(d, "s", stems), "--suffixes", "an", "--change", "d:r",
                    "--heldout", write(d, "h", heldout), "--discovery", write(d, "d", discovery),
                    "--min-hits", "3"]
            return ct.main(args)

    def test_pass_when_rule_beats_controls(self):
        stems = [f"ka{c}d" for c in "bcfghjk"]
        held = [f"ka{c}ran" for c in "bcfghjk"]
        self.assertEqual(self.run_case(stems, held), 0)

    def test_fail_when_controls_match(self):
        stems = [f"ka{c}d" for c in "bcfghjk"]
        held = [f"ka{c}fan" for c in "bcfghjk"] + [f"ka{c}ran" for c in "bc"]
        self.assertEqual(self.run_case(stems, held), 1)

    def test_discovery_forms_are_not_hits(self):
        stems = [f"ka{c}d" for c in "bcfg"]
        held = [f"ka{c}ran" for c in "bcfg"]
        self.assertEqual(self.run_case(stems, held, discovery=held), 1)


if __name__ == "__main__":
    unittest.main()
