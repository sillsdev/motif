"""Grade profiles must agree with the tool lists the MCP server actually exposes."""
from pathlib import Path
import json
import os
import re
import unittest

REPO = Path(__file__).resolve().parents[2]
PROFILES = Path(os.environ.get('MOTIF_GRADE_PROFILES', REPO / 'evals/profiles'))
GOLDEN = REPO / 'tests/SIL.Motif.Tests.Mcp/Golden'
SHIPPED = REPO / 'profiles'


def golden_tools(profile):
    """Ordered (name, class) pairs; the class is None where the description states none."""
    tools = json.loads((GOLDEN / f'tools-list.{profile}.json').read_text(encoding='utf-8'))
    result = []
    for tool in tools:
        found = re.search(r'Agent class: (\w+)\.', tool['description'])
        result.append((tool['name'], found.group(1) if found else None))
    return result


def lean_tools():
    """The lean profile has no golden, so derive its list from the shipped profile and the default golden."""
    shipped = json.loads((SHIPPED / 'lean.json').read_text(encoding='utf-8'))
    classes = dict(golden_tools('default'))
    return [(selection.get('as', selection['tool']), classes[selection['tool']]) for selection in shipped['tools']], \
        {selection['as']: selection['tool'] for selection in shipped['tools'] if 'as' in selection}


class GradeProfileTests(unittest.TestCase):
    def test_every_grade_profile_matches_its_server_tool_list(self):
        files = sorted(PROFILES.glob('*-grade.json'))
        self.assertTrue(files, 'no grade profiles found')
        for path in files:
            with self.subTest(profile=path.name):
                grade = json.loads(path.read_text(encoding='utf-8'))
                server = path.name.removeprefix('mcp-').removesuffix('-grade.json')
                renames = {}
                if (GOLDEN / f'tools-list.{server}.json').exists():
                    expected = golden_tools(server)
                elif (SHIPPED / f'{server}.json').exists():
                    expected, renames = lean_tools()
                else:
                    self.fail(f'{path.name} names server profile {server!r}, which has no golden or shipped profile')
                actual = [(tool['name'], tool['class']) for tool in grade['tools']]
                self.assertEqual([name for name, _ in expected], [name for name, _ in actual],
                                 'tool names or order differ from the server')
                for (name, stated), (_, graded) in zip(expected, actual):
                    if stated is not None:
                        self.assertEqual(stated, graded, f'{name}: server says {stated}, grade profile says {graded}')
                    else:
                        self.assertEqual('Read', graded, f'{name} states no agent class and must be graded Read')
                hidden = set(grade.get('hiddenTools', []))
                self.assertFalse(hidden & {name for name, _ in expected}, 'a hidden tool is exposed by the server')
                canonical = set(grade.get('canonicalTools', []))
                self.assertEqual(set(renames.values()), canonical, 'canonicalTools must be the renamed tools\' real names')


    def test_every_server_golden_has_a_grade_profile(self):
        goldens = sorted(path.name.removeprefix('tools-list.').removesuffix('.json')
                         for path in GOLDEN.glob('tools-list.*.json'))
        missing = [name for name in goldens if not (PROFILES / f'mcp-{name}-grade.json').exists()]
        self.assertEqual([], missing, 'an Arm on these server profiles would be graded with no tool classes')

if __name__ == '__main__':
    unittest.main()
